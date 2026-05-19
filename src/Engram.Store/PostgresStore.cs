using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;

namespace Engram.Store;

/// <summary>
/// PostgreSQL-backed implementation of IStore.
/// Schema, dedup logic, and FTS (tsvector) behaviour are intentionally equivalent to SqliteStore.
/// Implements ICloudMutationStore and ICloudChunkStore for offline-first-sync (Phase 1).
/// </summary>
public sealed class PostgresStore : IStore, ICloudMutationStore, ICloudChunkStore
{
    private readonly NpgsqlConnection _db;
    private readonly StoreConfig _cfg;

    // ─── Construction ──────────────────────────────────────────────────────────

    public PostgresStore(StoreConfig cfg)
    {
        _cfg = cfg;

        if (string.IsNullOrWhiteSpace(cfg.PgConnectionString))
            throw new ArgumentException(
                "ENGRAM_PG_CONNECTION is required when ENGRAM_DB_TYPE=postgres");

        _db = new NpgsqlConnection(cfg.PgConnectionString);
        _db.Open();

        Migrate();
    }

    public int MaxObservationLength => _cfg.MaxObservationLength;
    public string BackendName => "postgres";

    public void Dispose() => _db.Dispose();

    // ─── Migrations ────────────────────────────────────────────────────────────

    private void Migrate()
    {
        Exec(@"
            CREATE TABLE IF NOT EXISTS sessions (
                id         TEXT PRIMARY KEY,
                project    TEXT NOT NULL,
                directory  TEXT NOT NULL,
                started_at TEXT NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
                ended_at   TEXT,
                summary    TEXT
            );

            CREATE TABLE IF NOT EXISTS observations (
                id              BIGSERIAL PRIMARY KEY,
                sync_id         TEXT,
                session_id      TEXT    NOT NULL REFERENCES sessions(id),
                type            TEXT    NOT NULL,
                title           TEXT    NOT NULL,
                content         TEXT    NOT NULL,
                tool_name       TEXT,
                project         TEXT,
                scope           TEXT    NOT NULL DEFAULT 'project',
                topic_key       TEXT,
                normalized_hash TEXT,
                revision_count  INTEGER NOT NULL DEFAULT 1,
                duplicate_count INTEGER NOT NULL DEFAULT 1,
                last_seen_at    TEXT,
                created_at      TEXT    NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
                updated_at      TEXT    NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
                deleted_at      TEXT,
                review_after    TEXT,
                expires_at      TEXT,
                embedding       BYTEA,
                embedding_model TEXT,
                embedding_created_at TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_obs_session  ON observations(session_id);
            CREATE INDEX IF NOT EXISTS idx_obs_type     ON observations(type);
            CREATE INDEX IF NOT EXISTS idx_obs_project  ON observations(project);
            CREATE INDEX IF NOT EXISTS idx_obs_created  ON observations(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_obs_scope    ON observations(scope);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_obs_sync_id ON observations(sync_id) WHERE sync_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_obs_topic    ON observations(topic_key, project, scope, updated_at DESC) WHERE topic_key IS NOT NULL;
            CREATE INDEX IF NOT EXISTS idx_obs_deleted  ON observations(deleted_at);
            CREATE INDEX IF NOT EXISTS idx_obs_dedupe   ON observations(normalized_hash, project, scope, type, title, created_at DESC) WHERE normalized_hash IS NOT NULL;

            CREATE TABLE IF NOT EXISTS user_prompts (
                id         BIGSERIAL PRIMARY KEY,
                sync_id    TEXT,
                session_id TEXT    NOT NULL REFERENCES sessions(id),
                content    TEXT    NOT NULL,
                project    TEXT,
                created_at TEXT    NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc')
            );

            CREATE INDEX IF NOT EXISTS idx_prompts_session ON user_prompts(session_id);
            CREATE INDEX IF NOT EXISTS idx_prompts_project ON user_prompts(project);
            CREATE INDEX IF NOT EXISTS idx_prompts_created ON user_prompts(created_at DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_prompts_sync_id ON user_prompts(sync_id) WHERE sync_id IS NOT NULL;

            CREATE TABLE IF NOT EXISTS sync_chunks (
                chunk_id    TEXT PRIMARY KEY,
                imported_at TEXT NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc')
            );

            CREATE TABLE IF NOT EXISTS sync_state (
                target_key           TEXT PRIMARY KEY,
                lifecycle            TEXT NOT NULL DEFAULT 'idle',
                last_enqueued_seq    BIGINT NOT NULL DEFAULT 0,
                last_acked_seq       BIGINT NOT NULL DEFAULT 0,
                last_pulled_seq      BIGINT NOT NULL DEFAULT 0,
                consecutive_failures INTEGER NOT NULL DEFAULT 0,
                backoff_until        TEXT,
                lease_owner          TEXT,
                lease_until          TEXT,
                last_error           TEXT,
                updated_at           TEXT NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc')
            );

            CREATE TABLE IF NOT EXISTS sync_mutations (
                seq         BIGSERIAL PRIMARY KEY,
                target_key  TEXT NOT NULL REFERENCES sync_state(target_key),
                entity      TEXT NOT NULL,
                entity_key  TEXT NOT NULL,
                op          TEXT NOT NULL,
                payload     TEXT NOT NULL,
                source      TEXT NOT NULL DEFAULT 'local',
                project     TEXT NOT NULL DEFAULT '',
                occurred_at TEXT NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
                acked_at    TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_sync_mutations_target_seq ON sync_mutations(target_key, seq);
            CREATE INDEX IF NOT EXISTS idx_sync_mutations_pending    ON sync_mutations(target_key, seq) WHERE acked_at IS NULL;
            CREATE INDEX IF NOT EXISTS idx_sync_mutations_project    ON sync_mutations(project);

            CREATE TABLE IF NOT EXISTS sync_enrolled_projects (
                project     TEXT NOT NULL,
                ""user""    TEXT NOT NULL DEFAULT '',
                enrolled_at TEXT NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc'),
                enrolled_by TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (project, ""user"")
            );
        ");

        // ─── Cloud sync tables (offline-first-sync Phase 1) ────────────────────

        Exec(@"
            CREATE TABLE IF NOT EXISTS cloud_mutations (
                seq           BIGSERIAL PRIMARY KEY,
                project       TEXT NOT NULL,
                entity        TEXT NOT NULL,
                entity_key    TEXT NOT NULL,
                op            TEXT NOT NULL,
                payload       JSONB NOT NULL,
                created_by    TEXT DEFAULT '',
                occurred_at   TIMESTAMPTZ DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_cm_project_seq ON cloud_mutations(project, seq);
        ");

        Exec(@"
            CREATE TABLE IF NOT EXISTS cloud_sync_audit_log (
                id            BIGSERIAL PRIMARY KEY,
                project       TEXT NOT NULL,
                action        TEXT NOT NULL,
                outcome       TEXT NOT NULL,
                contributor   TEXT DEFAULT '',
                entry_count   INT DEFAULT 0,
                reason_code   TEXT DEFAULT '',
                created_at    TIMESTAMPTZ DEFAULT NOW()
            );
            CREATE INDEX IF NOT EXISTS idx_csal_project ON cloud_sync_audit_log(project, created_at);
        ");

        Exec(@"
            CREATE TABLE IF NOT EXISTS cloud_project_controls (
                project       TEXT PRIMARY KEY,
                sync_enabled  BOOL DEFAULT true,
                pause_reason  TEXT DEFAULT '',
                paused_at     TIMESTAMPTZ DEFAULT NULL,
                paused_by     TEXT DEFAULT '',
                resumed_at    TIMESTAMPTZ DEFAULT NULL,
                resumed_by    TEXT DEFAULT '',
                updated_at    TIMESTAMPTZ DEFAULT NOW()
            );
        ");

        Exec(@"
            CREATE TABLE IF NOT EXISTS cloud_chunks (
                chunk_id          TEXT PRIMARY KEY,
                project           TEXT NOT NULL,
                payload           JSONB NOT NULL,
                created_by        TEXT DEFAULT '',
                client_created_at TIMESTAMPTZ,
                sessions_count    INT DEFAULT 0,
                observations_count INT DEFAULT 0,
                prompts_count     INT DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_cc_project ON cloud_chunks(project, chunk_id);
        ");

        // ─── Project migrations ────────────────────────────────────────────────

        Exec(@"
            CREATE TABLE IF NOT EXISTS project_migrations (
                from_project TEXT PRIMARY KEY,
                to_project   TEXT NOT NULL,
                migrated_at  TEXT NOT NULL DEFAULT (NOW() AT TIME ZONE 'utc')
            );
        ");

        // ─── FTS: tsvector GENERATED ALWAYS AS STORED ──────────────────────────

        // Check if search_vector column exists
        if (!ColumnExists("observations", "search_vector"))
        {
            Exec(@"
                ALTER TABLE observations
                ADD COLUMN search_vector tsvector
                    GENERATED ALWAYS AS (
                        to_tsvector('simple',
                            coalesce(title, '') || ' ' ||
                            coalesce(content, '') || ' ' ||
                            coalesce(tool_name, '') || ' ' ||
                            coalesce(type, '') || ' ' ||
                            coalesce(project, '') || ' ' ||
                            coalesce(topic_key, '')
                        )
                    ) STORED
            ");
        }

        Exec(@"
            CREATE INDEX IF NOT EXISTS idx_obs_fts ON observations USING GIN(search_vector);
            CREATE INDEX IF NOT EXISTS idx_obs_fts_active ON observations USING GIN(search_vector) WHERE deleted_at IS NULL;
        ");

        // ─── Upstream parity v1.14: reserved columns ───────────────────────────

        if (!ColumnExists("observations", "review_after"))
            Exec("ALTER TABLE observations ADD COLUMN review_after TEXT");
        if (!ColumnExists("observations", "expires_at"))
            Exec("ALTER TABLE observations ADD COLUMN expires_at TEXT");
        if (!ColumnExists("observations", "embedding"))
            Exec("ALTER TABLE observations ADD COLUMN embedding BYTEA");
        if (!ColumnExists("observations", "embedding_model"))
            Exec("ALTER TABLE observations ADD COLUMN embedding_model TEXT");
        if (!ColumnExists("observations", "embedding_created_at"))
            Exec("ALTER TABLE observations ADD COLUMN embedding_created_at TEXT");
        if (!ColumnExists("observations", "md_path"))
            Exec("ALTER TABLE observations ADD COLUMN md_path TEXT");
        if (!ColumnExists("user_prompts", "deleted_at"))
            Exec("ALTER TABLE user_prompts ADD COLUMN deleted_at TIMESTAMPTZ");

        // ─── Normalisation (idempotent) ────────────────────────────────────────

        Exec("UPDATE observations SET scope = 'project' WHERE scope IS NULL OR scope = ''");
        Exec("UPDATE observations SET topic_key = NULL WHERE topic_key = ''");
        Exec("UPDATE observations SET revision_count = 1 WHERE revision_count IS NULL OR revision_count < 1");
        Exec("UPDATE observations SET duplicate_count = 1 WHERE duplicate_count IS NULL OR duplicate_count < 1");
        Exec("UPDATE observations SET updated_at = created_at WHERE updated_at IS NULL OR updated_at = ''");
        Exec("UPDATE user_prompts SET project = '' WHERE project IS NULL");
        Exec(@"
            INSERT INTO sync_state (target_key, lifecycle, updated_at)
            VALUES ('cloud', 'idle', NOW() AT TIME ZONE 'utc')
            ON CONFLICT (target_key) DO NOTHING
        ");
    }

    private bool ColumnExists(string table, string column)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_name = @table AND column_name = @column";
        cmd.Parameters.AddWithValue("@table", table);
        cmd.Parameters.AddWithValue("@column", column);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ─── Dedupe window expression (PostgreSQL dialect) ────────────────────────

    internal string DedupeWindowExpression =>
        $"created_at::timestamptz >= NOW() - INTERVAL '{_cfg.DedupeWindow.TotalMinutes} minutes'";

    // ═══════════════════════════════════════════════════════════════════════════
    // Sessions
    // ═══════════════════════════════════════════════════════════════════════════

    public Task CreateSessionAsync(string id, string project, string directory)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sessions (id, project, directory, started_at)
            VALUES (@id, @project, @directory, NOW() AT TIME ZONE 'utc')
            ON CONFLICT (id) DO NOTHING";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@directory", directory);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task EndSessionAsync(string id, string summary)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            UPDATE sessions SET ended_at = NOW() AT TIME ZONE 'utc', summary = @summary
            WHERE id = @id";
        cmd.Parameters.AddWithValue("@summary", summary);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<Session?> GetSessionAsync(string id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id, project, directory, started_at, ended_at, summary FROM sessions WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return Task.FromResult<Session?>(r.Read() ? ReadSession(r) : null);
    }

    public Task<IList<SessionSummary>> RecentSessionsAsync(string? project, int limit)
    {
        var sql = new StringBuilder(@"
            SELECT s.id, s.project, s.started_at, s.ended_at, s.summary,
                   (SELECT COUNT(*) FROM observations o WHERE o.session_id = s.id AND o.deleted_at IS NULL) as obs_count
            FROM sessions s");
        var parms = new List<NpgsqlParameter>();
        if (!string.IsNullOrEmpty(project))
        {
            sql.Append(" WHERE s.project = @proj");
            parms.Add(new NpgsqlParameter("@proj", project));
        }
        sql.Append(" ORDER BY s.started_at DESC LIMIT @limit");
        parms.Add(new NpgsqlParameter("@limit", limit));

        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql.ToString();
        foreach (var p in parms) cmd.Parameters.Add(p);
        using var r = cmd.ExecuteReader();
        var list = new List<SessionSummary>();
        while (r.Read())
        {
            list.Add(new SessionSummary
            {
                Id = r.GetString(0),
                Project = r.GetString(1),
                StartedAt = r.GetString(2),
                EndedAt = r.IsDBNull(3) ? null : r.GetString(3),
                Summary = r.IsDBNull(4) ? null : r.GetString(4),
                ObservationCount = r.GetInt32(5),
            });
        }
        return Task.FromResult<IList<SessionSummary>>(list);
    }

    public Task DeleteSessionAsync(string id)
    {
        using var tx = _db.BeginTransaction();
        try
        {
            // 1. Verify session exists
            using (var cmd = _db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT project FROM sessions WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                var result = cmd.ExecuteScalar();
                if (result == null)
                    throw new SessionNotFoundException(id);
            }

            // 2. Count ALL observations (including soft-deleted) — FK constraint has no ON DELETE CASCADE
            using (var cmd = _db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT COUNT(*) FROM observations WHERE session_id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                var totalObs = Convert.ToInt64(cmd.ExecuteScalar());
                if (totalObs > 0)
                    throw new SessionDeleteBlockedException(id, (int)totalObs);
            }

            // 3. Soft-delete associated prompts so RecentPrompts excludes them
            using (var cmd = _db.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE user_prompts SET deleted_at = NOW() AT TIME ZONE 'utc' WHERE session_id = @id AND deleted_at IS NULL";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }

            // 4. Hard-delete the session. Temporarily disable FK triggers so soft-deleted prompts can keep their session_id.
            using var disable = _db.CreateCommand();
            disable.CommandText = "SET session_replication_role = replica";
            disable.ExecuteNonQuery();
            try
            {
                using var cmd = _db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM sessions WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                var rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                    throw new SessionDeleteBlockedException(id, -1);
            }
            finally
            {
                using var enable = _db.CreateCommand();
                enable.CommandText = "SET session_replication_role = origin";
                enable.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            try
            {
                tx.Rollback();
            }
            catch
            {
                // Ignore rollback failures
            }
            throw;
        }

        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Observations
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<long> AddObservationAsync(AddObservationParams p)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        // Path 1: topic_key upsert
        if (!string.IsNullOrWhiteSpace(p.TopicKey))
        {
            var existing = await GetObservationByTopicKeyAsync(p.TopicKey, p.Project, p.Scope);
            if (existing != null)
            {
                using var cmd = _db.CreateCommand();
                cmd.CommandText = @"
                    UPDATE observations
                    SET content = @content, title = @title, type = @type,
                        tool_name = @tool, project = @project, scope = @scope,
                        topic_key = @topic, last_seen_at = @last_seen,
                        updated_at = @updated, revision_count = revision_count + 1
                    WHERE id = @id";
                cmd.Parameters.AddWithValue("@content", p.Content);
                cmd.Parameters.AddWithValue("@title", p.Title);
                cmd.Parameters.AddWithValue("@type", p.Type);
                cmd.Parameters.AddWithValue("@tool", (object?)p.ToolName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@project", (object?)p.Project ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@scope", p.Scope ?? "project");
                cmd.Parameters.AddWithValue("@topic", (object?)p.TopicKey ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@last_seen", (object?)now ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@updated", now);
                cmd.Parameters.AddWithValue("@id", existing.Id);
                cmd.ExecuteNonQuery();
                return existing.Id;
            }
        }

        // Path 2: hash dedup within window
        var hash = Normalizers.HashNormalized(p.Content);
        var dedupeWindow = DedupeWindowExpression;
        using var dedupCmd = _db.CreateCommand();
        dedupCmd.CommandText = $@"
            SELECT id, duplicate_count FROM observations
            WHERE normalized_hash = @hash AND project = @project AND scope = @scope
              AND type = @type AND title = @title AND {dedupeWindow}
              AND deleted_at IS NULL
            ORDER BY created_at DESC LIMIT 1";
        dedupCmd.Parameters.AddWithValue("@hash", (object?)hash ?? DBNull.Value);
        dedupCmd.Parameters.AddWithValue("@project", (object?)p.Project ?? DBNull.Value);
        dedupCmd.Parameters.AddWithValue("@scope", p.Scope ?? "project");
        dedupCmd.Parameters.AddWithValue("@type", p.Type);
        dedupCmd.Parameters.AddWithValue("@title", p.Title);
        using var dedupR = dedupCmd.ExecuteReader();
        if (dedupR.Read())
        {
            var existingId = dedupR.GetInt64(0);
            var dupCount = dedupR.GetInt32(1);
            dedupR.Close();

            using var upd = _db.CreateCommand();
            upd.CommandText = @"
                UPDATE observations SET duplicate_count = @dc, last_seen_at = @ls, updated_at = @up
                WHERE id = @id";
            upd.Parameters.AddWithValue("@dc", dupCount + 1);
            upd.Parameters.AddWithValue("@ls", (object?)now ?? DBNull.Value);
            upd.Parameters.AddWithValue("@up", now);
            upd.Parameters.AddWithValue("@id", existingId);
            upd.ExecuteNonQuery();
            return existingId;
        }
        dedupR.Close();

        // Path 3: fresh insert
        using var ins = _db.CreateCommand();
        ins.CommandText = @"
            INSERT INTO observations
                (sync_id, session_id, type, title, content, tool_name, project, scope,
                 topic_key, normalized_hash, revision_count, duplicate_count, last_seen_at,
                 created_at, updated_at)
            VALUES
                (@sync_id, @session_id, @type, @title, @content, @tool, @project, @scope,
                 @topic, @hash, 1, 1, @last_seen, @created, @updated)
            RETURNING id";
        ins.Parameters.AddWithValue("@sync_id", (object?)NewSyncId("obs") ?? DBNull.Value);
        ins.Parameters.AddWithValue("@session_id", p.SessionId);
        ins.Parameters.AddWithValue("@type", p.Type);
        ins.Parameters.AddWithValue("@title", p.Title);
        ins.Parameters.AddWithValue("@content", p.Content);
        ins.Parameters.AddWithValue("@tool", (object?)p.ToolName ?? DBNull.Value);
        ins.Parameters.AddWithValue("@project", (object?)p.Project ?? DBNull.Value);
        ins.Parameters.AddWithValue("@scope", p.Scope ?? "project");
        ins.Parameters.AddWithValue("@topic", (object?)p.TopicKey ?? DBNull.Value);
        ins.Parameters.AddWithValue("@hash", (object?)hash ?? DBNull.Value);
        ins.Parameters.AddWithValue("@last_seen", (object?)now ?? DBNull.Value);
        ins.Parameters.AddWithValue("@created", now);
        ins.Parameters.AddWithValue("@updated", now);
        return (long)ins.ExecuteScalar()!;
    }

    private async Task<Observation?> GetObservationByTopicKeyAsync(string topicKey, string? project, string? scope)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT o.id, o.sync_id, o.session_id, o.type, o.title, o.content, o.tool_name, o.project,
                   o.scope, o.topic_key, o.revision_count, o.duplicate_count, o.last_seen_at,
                   o.created_at, o.updated_at, o.deleted_at
            FROM observations o
            WHERE o.topic_key = @topic AND o.project = @project AND o.scope = @scope AND o.deleted_at IS NULL";
        cmd.Parameters.AddWithValue("@topic", topicKey);
        cmd.Parameters.AddWithValue("@project", (object?)project ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@scope", scope ?? "project");
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadObservation(r) : null;
    }

    public Task<Observation?> GetObservationAsync(long id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT o.id, o.sync_id, o.session_id, o.type, o.title, o.content, o.tool_name, o.project,
                   o.scope, o.topic_key, o.revision_count, o.duplicate_count, o.last_seen_at,
                   o.created_at, o.updated_at, o.deleted_at
            FROM observations o WHERE o.id = @id AND o.deleted_at IS NULL";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return Task.FromResult(r.Read() ? ReadObservation(r) : null);
    }

    public Task<IList<Observation>> RecentObservationsAsync(string? project, string? scope, int limit)
    {
        var sql = new StringBuilder(@"
            SELECT o.id, o.sync_id, o.session_id, o.type, o.title, o.content, o.tool_name, o.project,
                   o.scope, o.topic_key, o.revision_count, o.duplicate_count, o.last_seen_at,
                   o.created_at, o.updated_at, o.deleted_at
            FROM observations o WHERE o.deleted_at IS NULL");
        var parms = new List<NpgsqlParameter>();
        if (!string.IsNullOrEmpty(project))
        {
            sql.Append(" AND o.project = @proj");
            parms.Add(new NpgsqlParameter("@proj", project));
        }
        if (!string.IsNullOrEmpty(scope))
        {
            sql.Append(" AND o.scope = @scope");
            parms.Add(new NpgsqlParameter("@scope", scope));
        }
        sql.Append(" ORDER BY o.created_at DESC LIMIT @limit");
        parms.Add(new NpgsqlParameter("@limit", limit));

        return Task.FromResult<IList<Observation>>(QueryObservations(sql.ToString(), parms));
    }

    public Task<bool> UpdateObservationAsync(long id, UpdateObservationParams p)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var sql = new StringBuilder("UPDATE observations SET updated_at = @updated");
        var parms = new List<NpgsqlParameter>
        {
            new("@updated", now),
            new("@id", id),
        };

        if (p.Type != null) { sql.Append(", type = @type"); parms.Add(new NpgsqlParameter("@type", p.Type)); }
        if (p.Title != null) { sql.Append(", title = @title"); parms.Add(new NpgsqlParameter("@title", p.Title)); }
        if (p.Content != null) { sql.Append(", content = @content"); parms.Add(new NpgsqlParameter("@content", p.Content)); }
        if (p.Project != null) { sql.Append(", project = @project"); parms.Add(new NpgsqlParameter("@project", p.Project)); }
        if (p.Scope != null) { sql.Append(", scope = @scope"); parms.Add(new NpgsqlParameter("@scope", p.Scope)); }
        if (p.TopicKey != null) { sql.Append(", topic_key = @topic"); parms.Add(new NpgsqlParameter("@topic", (object?)p.TopicKey ?? DBNull.Value)); }

        sql.Append(" WHERE id = @id AND deleted_at IS NULL");

        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql.ToString();
        foreach (var par in parms) cmd.Parameters.Add(par);
        var rows = cmd.ExecuteNonQuery();
        return Task.FromResult(rows > 0);
    }

    public Task<bool> DeleteObservationAsync(long id)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "UPDATE observations SET deleted_at = @now, updated_at = @now WHERE id = @id AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@id", id);
        var rows = cmd.ExecuteNonQuery();
        return Task.FromResult(rows > 0);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Search
    // ═══════════════════════════════════════════════════════════════════════════

    public Task<IList<SearchResult>> SearchAsync(string query, SearchOptions opts)
    {
        var sql = new StringBuilder();
        var parms = new List<NpgsqlParameter>();

        // Topic-key shortcut
        sql.Append(@"
            SELECT o.id, o.sync_id, o.session_id, o.type, o.title, o.content, o.tool_name, o.project,
                   o.scope, o.topic_key, o.revision_count, o.duplicate_count, o.last_seen_at,
                   o.created_at, o.updated_at, o.deleted_at,
                   -1000.0 as rank
            FROM observations o
            WHERE o.topic_key = @query AND o.deleted_at IS NULL");
        parms.Add(new NpgsqlParameter("@query", query));
        AppendSearchFilter(sql, parms, opts);

        sql.Append(@"
            UNION ALL
            SELECT o.id, o.sync_id, o.session_id, o.type, o.title, o.content, o.tool_name, o.project,
                   o.scope, o.topic_key, o.revision_count, o.duplicate_count, o.last_seen_at,
                   o.created_at, o.updated_at, o.deleted_at,
                   ts_rank(o.search_vector, plainto_tsquery('simple', @q2)) as rank
            FROM observations o
            WHERE o.search_vector @@ plainto_tsquery('simple', @q2) AND o.deleted_at IS NULL");
        parms.Add(new NpgsqlParameter("@q2", query));
        AppendSearchFilter(sql, parms, opts);

        sql.Append(@"
            ORDER BY rank DESC, created_at DESC
            LIMIT @limit");
        parms.Add(new NpgsqlParameter("@limit", opts.Limit > 0 ? opts.Limit : 10));

        // Phase 3: enrich results with project redirect info
        // if (results.Count > 0) { /* lookup project_migrations per result.Observation.Project */ }

        return Task.FromResult<IList<SearchResult>>(QuerySearchResults(sql.ToString(), parms));
    }

    public Task<IList<SearchResult>> SearchAsync(string query, IList<string> projects, SearchOptions opts)
    {
        var sql = new StringBuilder();
        var parms = new List<NpgsqlParameter>();

        // Topic-key shortcut across all projects
        sql.Append(@"
            SELECT o.id, o.sync_id, o.session_id, o.type, o.title, o.content, o.tool_name, o.project,
                   o.scope, o.topic_key, o.revision_count, o.duplicate_count, o.last_seen_at,
                   o.created_at, o.updated_at, o.deleted_at,
                   -1000.0 as rank
            FROM observations o
            WHERE o.topic_key = @query AND o.deleted_at IS NULL");
        parms.Add(new NpgsqlParameter("@query", query));
        AppendProjectInClause(sql, parms, projects);

        sql.Append(@"
            UNION ALL
            SELECT o.id, o.sync_id, o.session_id, o.type, o.title, o.content, o.tool_name, o.project,
                   o.scope, o.topic_key, o.revision_count, o.duplicate_count, o.last_seen_at,
                   o.created_at, o.updated_at, o.deleted_at,
                   ts_rank(o.search_vector, plainto_tsquery('simple', @q2)) as rank
            FROM observations o
            WHERE o.search_vector @@ plainto_tsquery('simple', @q2) AND o.deleted_at IS NULL");
        parms.Add(new NpgsqlParameter("@q2", query));
        AppendProjectInClause(sql, parms, projects);

        sql.Append(@"
            ORDER BY rank DESC, created_at DESC
            LIMIT @limit");
        parms.Add(new NpgsqlParameter("@limit", opts.Limit > 0 ? opts.Limit : 10));

        return Task.FromResult<IList<SearchResult>>(QuerySearchResults(sql.ToString(), parms));
    }

    public Task<TimelineResult?> TimelineAsync(long observationId, int before, int after)
    {
        var focus = GetObservationDirect(observationId);
        if (focus == null) return Task.FromResult<TimelineResult?>(null);

        var session = GetSessionDirect(focus.SessionId);

        var beforeEntries = QueryObservations(
            @"SELECT o.id, o.sync_id, o.session_id, o.type, o.title, o.content, o.tool_name, o.project,
                     o.scope, o.topic_key, o.revision_count, o.duplicate_count, o.last_seen_at,
                     o.created_at, o.updated_at, o.deleted_at
              FROM observations o
              WHERE o.session_id = @sid AND o.created_at < @created AND o.deleted_at IS NULL
              ORDER BY o.created_at DESC LIMIT @limit",
            new[]
            {
                new NpgsqlParameter("@sid", focus.SessionId),
                new NpgsqlParameter("@created", focus.CreatedAt),
                new NpgsqlParameter("@limit", before),
            });
        beforeEntries.Reverse();

        var afterEntries = QueryObservations(
            @"SELECT o.id, o.sync_id, o.session_id, o.type, o.title, o.content, o.tool_name, o.project,
                     o.scope, o.topic_key, o.revision_count, o.duplicate_count, o.last_seen_at,
                     o.created_at, o.updated_at, o.deleted_at
              FROM observations o
              WHERE o.session_id = @sid AND o.created_at > @created AND o.deleted_at IS NULL
              ORDER BY o.created_at ASC LIMIT @limit",
            new[]
            {
                new NpgsqlParameter("@sid", focus.SessionId),
                new NpgsqlParameter("@created", focus.CreatedAt),
                new NpgsqlParameter("@limit", after),
            });

        return Task.FromResult<TimelineResult?>(new TimelineResult
        {
            Focus = focus,
            Before = beforeEntries.Select(ToTimelineEntry).ToList(),
            After = afterEntries.Select(ToTimelineEntry).ToList(),
            SessionInfo = session,
            TotalInRange = beforeEntries.Count + afterEntries.Count + 1,
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Prompts
    // ═══════════════════════════════════════════════════════════════════════════

    public Task<long> AddPromptAsync(AddPromptParams p)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO user_prompts (sync_id, session_id, content, project, created_at)
            VALUES (@sync_id, @session_id, @content, @project, NOW() AT TIME ZONE 'utc')
            RETURNING id";
        cmd.Parameters.AddWithValue("@sync_id", (object?)NewSyncId("prompt") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@session_id", p.SessionId);
        cmd.Parameters.AddWithValue("@content", p.Content);
        cmd.Parameters.AddWithValue("@project", (object?)p.Project ?? DBNull.Value);
        return Task.FromResult((long)cmd.ExecuteScalar()!);
    }

    public Task<IList<Prompt>> RecentPromptsAsync(string? project, int limit)
    {
        var sql = new StringBuilder("SELECT id, sync_id, session_id, content, project, created_at FROM user_prompts WHERE deleted_at IS NULL");
        var parms = new List<NpgsqlParameter>();
        if (!string.IsNullOrEmpty(project))
        {
            sql.Append(" AND project = @proj");
            parms.Add(new NpgsqlParameter("@proj", project));
        }
        sql.Append(" ORDER BY created_at DESC LIMIT @limit");
        parms.Add(new NpgsqlParameter("@limit", limit));
        return Task.FromResult<IList<Prompt>>(QueryPrompts(sql.ToString(), parms));
    }

    public Task<IList<Prompt>> SearchPromptsAsync(string query, string? project, int limit)
    {
        var sql = new StringBuilder(@"
            SELECT id, sync_id, session_id, content, project, created_at
            FROM user_prompts
            WHERE content ILIKE @q AND deleted_at IS NULL");
        var parms = new List<NpgsqlParameter> { new("@q", $"%{query}%") };
        if (!string.IsNullOrEmpty(project))
        {
            sql.Append(" AND project = @proj");
            parms.Add(new NpgsqlParameter("@proj", project));
        }
        sql.Append(" ORDER BY created_at DESC LIMIT @limit");
        parms.Add(new NpgsqlParameter("@limit", limit));
        return Task.FromResult<IList<Prompt>>(QueryPrompts(sql.ToString(), parms));
    }

    public Task DeletePromptAsync(long id)
    {
        // 1. Verify prompt exists
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT 1 FROM user_prompts WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            var result = cmd.ExecuteScalar();
            if (result == null)
                throw new PromptNotFoundException(id);
        }

        // 2. Soft-delete
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "UPDATE user_prompts SET deleted_at = NOW() WHERE id = @id AND deleted_at IS NULL";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Context & Stats
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<string> FormatContextAsync(string? project, string? scope)
    {
        var sessions     = await RecentSessionsAsync(project, 5);
        var observations = await RecentObservationsAsync(project, scope, 20);
        var prompts      = await RecentPromptsAsync(project, 10);

        if (sessions.Count == 0 && observations.Count == 0 && prompts.Count == 0)
            return string.Empty;

        var sb = new StringBuilder("## Memory from Previous Sessions\n\n");

        if (sessions.Count > 0)
        {
            sb.Append("### Recent Sessions\n");
            foreach (var s in sessions)
            {
                var sum = s.Summary != null ? $": {Truncate(s.Summary, 200)}" : "";
                sb.AppendLine($"- **{s.Project}** ({s.StartedAt}){sum} [{s.ObservationCount} observations]");
            }
            sb.AppendLine();
        }

        if (prompts.Count > 0)
        {
            sb.Append("### Recent User Prompts\n");
            foreach (var p in prompts)
                sb.AppendLine($"- {p.CreatedAt}: {Truncate(p.Content, 200)}");
            sb.AppendLine();
        }

        if (observations.Count > 0)
        {
            sb.Append("### Recent Observations\n");
            foreach (var o in observations)
                sb.AppendLine($"- [{o.Type}] **{o.Title}**: {Truncate(o.Content, 300)}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task<string> FormatContextAsync(IList<string> projects, string? scope)
    {
        if (projects.Count == 0) return string.Empty;

        var sessions = await RecentSessionsAsync(projects[0], 5);

        var allObservations = new List<Observation>();
        foreach (var proj in projects)
        {
            var obs = await RecentObservationsAsync(proj, scope, 20);
            allObservations.AddRange(obs);
        }

        var merged = allObservations
            .OrderByDescending(o => o.UpdatedAt)
            .Take(20)
            .ToList();

        var prompts = await RecentPromptsAsync(projects[0], 10);

        if (sessions.Count == 0 && merged.Count == 0 && prompts.Count == 0)
            return string.Empty;

        var sb = new StringBuilder("## Memory from Previous Sessions\n\n");

        if (sessions.Count > 0)
        {
            sb.Append("### Recent Sessions\n");
            foreach (var s in sessions)
            {
                var sum = s.Summary != null ? $": {Truncate(s.Summary, 200)}" : "";
                sb.AppendLine($"- **{s.Project}** ({s.StartedAt}){sum} [{s.ObservationCount} observations]");
            }
            sb.AppendLine();
        }

        if (prompts.Count > 0)
        {
            sb.Append("### Recent User Prompts\n");
            foreach (var p in prompts)
                sb.AppendLine($"- {p.CreatedAt}: {Truncate(p.Content, 200)}");
            sb.AppendLine();
        }

        if (merged.Count > 0)
        {
            sb.Append("### Recent Observations\n");
            foreach (var o in merged)
                sb.AppendLine($"- [{o.Type}] **{o.Title}**: {Truncate(o.Content, 300)}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task<Stats> StatsAsync()
    {
        long totalSessions, totalObservations, totalPrompts;

        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM sessions";
            totalSessions = Convert.ToInt64(cmd.ExecuteScalar());
        }
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM observations WHERE deleted_at IS NULL";
            totalObservations = Convert.ToInt64(cmd.ExecuteScalar());
        }
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM user_prompts WHERE deleted_at IS NULL";
            totalPrompts = Convert.ToInt64(cmd.ExecuteScalar());
        }

        var names = await ListProjectNamesAsync();
        return new Stats
        {
            TotalSessions = (int)totalSessions,
            TotalObservations = (int)totalObservations,
            TotalPrompts = (int)totalPrompts,
            Projects = names.ToList(),
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Retention
    // ═══════════════════════════════════════════════════════════════════════════

    public Task<RetentionStats> GetRetentionStatsAsync()
    {
        var stats = new RetentionStats();

        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM observations WHERE deleted_at IS NULL";
            stats.TotalObservations = Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Age buckets
        stats.AgeBuckets = new List<AgeBucket>
        {
            new() { Label = "< 30 days", Count = CountObs("created_at::timestamptz >= NOW() - INTERVAL '30 days'") },
            new() { Label = "30-90 days", Count = CountObs("created_at::timestamptz >= NOW() - INTERVAL '90 days' AND created_at::timestamptz < NOW() - INTERVAL '30 days'") },
            new() { Label = "90-180 days", Count = CountObs("created_at::timestamptz >= NOW() - INTERVAL '180 days' AND created_at::timestamptz < NOW() - INTERVAL '90 days'") },
            new() { Label = "180-365 days", Count = CountObs("created_at::timestamptz >= NOW() - INTERVAL '365 days' AND created_at::timestamptz < NOW() - INTERVAL '180 days'") },
            new() { Label = "> 365 days", Count = CountObs("created_at::timestamptz < NOW() - INTERVAL '365 days'") },
        };

        // Count without topic_key in last 90d
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM observations WHERE deleted_at IS NULL AND (topic_key IS NULL OR topic_key = '') AND created_at::timestamptz >= NOW() - INTERVAL '90 days'";
            stats.WithoutTopicKey90d = Convert.ToInt32(cmd.ExecuteScalar());
        }

        return Task.FromResult(stats);
    }

    public Task<RetentionPruneResult> PruneOldObservationsAsync(RetentionPruneParams p)
    {
        var config = new RetentionConfig();
        var result = new RetentionPruneResult { DryRun = p.DryRun };
        var details = new Dictionary<string, int>();

        var typesToPrune = string.IsNullOrEmpty(p.Type)
            ? config.TtlByType.Keys.ToList()
            : [p.Type];

        foreach (var type in typesToPrune)
        {
            if (!config.ShouldExpire(type)) continue;
            var ttl = config.GetTtl(type);
            if (ttl is null) continue;

            var cutoff = DateTime.UtcNow - ttl.Value;
            var cutoffStr = cutoff.ToString("yyyy-MM-dd HH:mm:ss");

            using var countCmd = _db.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM observations WHERE type = @type AND deleted_at IS NULL AND (topic_key IS NULL OR topic_key = '') AND created_at::timestamptz < @cutoff::timestamptz";
            countCmd.Parameters.AddWithValue("@type", type);
            countCmd.Parameters.AddWithValue("@cutoff", cutoffStr);
            var count = Convert.ToInt32(countCmd.ExecuteScalar());

            if (!p.DryRun && count > 0)
            {
                var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                using var delCmd = _db.CreateCommand();
                delCmd.CommandText = "UPDATE observations SET deleted_at = @now, updated_at = @now WHERE type = @type AND deleted_at IS NULL AND (topic_key IS NULL OR topic_key = '') AND created_at::timestamptz < @cutoff::timestamptz";
                delCmd.Parameters.AddWithValue("@now", now);
                delCmd.Parameters.AddWithValue("@type", type);
                delCmd.Parameters.AddWithValue("@cutoff", cutoffStr);
                delCmd.ExecuteNonQuery();
            }

            if (count > 0) details[type] = count;
        }

        return Task.FromResult(new RetentionPruneResult
        {
            Pruned = details.Values.Sum(),
            DryRun = p.DryRun,
            Details = details
        });
    }

    public Task AddProjectMigrationAsync(string fromProject, string toProject)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "INSERT INTO project_migrations (from_project, to_project, migrated_at) VALUES (@from, @to, NOW() AT TIME ZONE 'utc') ON CONFLICT (from_project) DO UPDATE SET to_project = @to2, migrated_at = NOW() AT TIME ZONE 'utc'";
        cmd.Parameters.AddWithValue("@from", fromProject);
        cmd.Parameters.AddWithValue("@to", toProject);
        cmd.Parameters.AddWithValue("@to2", toProject);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<IList<ProjectMigration>> GetProjectMigrationsAsync()
    {
        var list = new List<ProjectMigration>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT from_project, to_project, migrated_at FROM project_migrations ORDER BY migrated_at DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new ProjectMigration
            {
                FromProject = r.GetString(0),
                ToProject = r.GetString(1),
                MigratedAt = r.GetString(2)
            });
        }
        return Task.FromResult<IList<ProjectMigration>>(list);
    }

    private int CountObs(string whereClause)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM observations WHERE deleted_at IS NULL AND {whereClause}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Export / Import
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<ExportData> ExportAsync()
    {
        var sessions = new List<Session>();
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT id, project, directory, started_at, ended_at, summary FROM sessions ORDER BY started_at";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                sessions.Add(new Session
                {
                    Id = r.GetString(0),
                    Project = r.GetString(1),
                    Directory = r.GetString(2),
                    StartedAt = r.GetString(3),
                    EndedAt = r.IsDBNull(4) ? null : r.GetString(4),
                    Summary = r.IsDBNull(5) ? null : r.GetString(5),
                });
        }

        var observations = QueryObservations(
            @"SELECT o.id, o.sync_id, o.session_id, o.type, o.title, o.content, o.tool_name, o.project,
                     o.scope, o.topic_key, o.revision_count, o.duplicate_count, o.last_seen_at,
                     o.created_at, o.updated_at, o.deleted_at
              FROM observations o WHERE o.deleted_at IS NULL ORDER BY o.created_at",
            Array.Empty<NpgsqlParameter>());

        var prompts = QueryPrompts(
            "SELECT id, sync_id, session_id, content, project, created_at FROM user_prompts WHERE deleted_at IS NULL ORDER BY created_at",
            Array.Empty<NpgsqlParameter>());

        return new ExportData
        {
            ExportedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            Sessions = sessions,
            Observations = observations,
            Prompts = prompts,
        };
    }

    public async Task<ImportResult> ImportAsync(ExportData data)
    {
        long sessionsImported = 0, observationsImported = 0, promptsImported = 0;

        var existingSessions = new HashSet<string>();
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM sessions";
            using var r = cmd.ExecuteReader();
            while (r.Read()) existingSessions.Add(r.GetString(0));
        }

        foreach (var s in data.Sessions)
        {
            if (existingSessions.Contains(s.Id)) continue;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT INTO sessions (id, project, directory, started_at, ended_at, summary) VALUES (@id, @proj, @dir, @started, @ended, @summary)";
            cmd.Parameters.AddWithValue("@id", s.Id);
            cmd.Parameters.AddWithValue("@proj", s.Project);
            cmd.Parameters.AddWithValue("@dir", s.Directory);
            cmd.Parameters.AddWithValue("@started", s.StartedAt);
            cmd.Parameters.AddWithValue("@ended", (object?)s.EndedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@summary", (object?)s.Summary ?? DBNull.Value);
            cmd.ExecuteNonQuery();
            sessionsImported++;
        }

        var existingObs = new HashSet<string>();
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT sync_id FROM observations WHERE sync_id IS NOT NULL";
            using var r = cmd.ExecuteReader();
            while (r.Read()) existingObs.Add(r.GetString(0));
        }

        foreach (var o in data.Observations)
        {
            if (!string.IsNullOrEmpty(o.SyncId) && existingObs.Contains(o.SyncId)) continue;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO observations
                    (sync_id, session_id, type, title, content, tool_name, project, scope,
                     topic_key, normalized_hash, revision_count, duplicate_count, last_seen_at,
                     created_at, updated_at, deleted_at)
                VALUES
                    (@sync_id, @session_id, @type, @title, @content, @tool, @project, @scope,
                     @topic, @hash, @rev, @dup, @last_seen, @created, @updated, @deleted)";
            cmd.Parameters.AddWithValue("@sync_id", (object?)o.SyncId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@session_id", o.SessionId);
            cmd.Parameters.AddWithValue("@type", o.Type);
            cmd.Parameters.AddWithValue("@title", o.Title);
            cmd.Parameters.AddWithValue("@content", o.Content);
            cmd.Parameters.AddWithValue("@tool", (object?)o.ToolName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@project", (object?)o.Project ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@scope", o.Scope);
            cmd.Parameters.AddWithValue("@topic", (object?)o.TopicKey ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@hash", (object?)null ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@rev", o.RevisionCount);
            cmd.Parameters.AddWithValue("@dup", o.DuplicateCount);
            cmd.Parameters.AddWithValue("@last_seen", (object?)o.LastSeenAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@created", o.CreatedAt);
            cmd.Parameters.AddWithValue("@updated", o.UpdatedAt);
            cmd.Parameters.AddWithValue("@deleted", (object?)o.DeletedAt ?? DBNull.Value);
            cmd.ExecuteNonQuery();
            observationsImported++;
        }

        var existingPrompts = new HashSet<string>();
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT sync_id FROM user_prompts WHERE sync_id IS NOT NULL";
            using var r = cmd.ExecuteReader();
            while (r.Read()) existingPrompts.Add(r.GetString(0));
        }

        foreach (var p in data.Prompts)
        {
            if (!string.IsNullOrEmpty(p.SyncId) && existingPrompts.Contains(p.SyncId)) continue;
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "INSERT INTO user_prompts (sync_id, session_id, content, project, created_at) VALUES (@sync_id, @session_id, @content, @project, @created)";
            cmd.Parameters.AddWithValue("@sync_id", (object?)p.SyncId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@session_id", p.SessionId);
            cmd.Parameters.AddWithValue("@content", p.Content);
            cmd.Parameters.AddWithValue("@project", p.Project);
            cmd.Parameters.AddWithValue("@created", p.CreatedAt);
            cmd.ExecuteNonQuery();
            promptsImported++;
        }

        return new ImportResult
        {
            SessionsImported = (int)sessionsImported,
            ObservationsImported = (int)observationsImported,
            PromptsImported = (int)promptsImported,
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MD Promotion
    // ═══════════════════════════════════════════════════════════════════════════

    public Task<long> PromoteToMdAsync(long observationId, string mdDir)
    {
        var obs = GetObservationDirect(observationId);
        if (obs is null) return Task.FromResult(0L);

        var date = DateTime.TryParse(obs.CreatedAt, out var parsed)
            ? parsed
            : DateTime.UtcNow;

        var filename = ToFilename(obs.Title, date);
        var fullPath = Path.Combine(mdDir, filename);

        Directory.CreateDirectory(mdDir);
        File.WriteAllText(fullPath, RenderMdContent(obs));

        using var cmd = _db.CreateCommand();
        cmd.CommandText = "UPDATE observations SET md_path = @path, updated_at = @now WHERE id = @id";
        cmd.Parameters.AddWithValue("@path", filename);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@id", observationId);
        cmd.ExecuteNonQuery();

        return Task.FromResult(observationId);
    }

    public Task<int> SyncMdToRepoAsync(string mdDir, bool dryRun)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT id FROM observations
            WHERE deleted_at IS NULL AND (md_path IS NULL OR md_path = '')
            ORDER BY created_at DESC LIMIT 100";
        using var r = cmd.ExecuteReader();
        var ids = new List<long>();
        while (r.Read()) ids.Add(r.GetInt64(0));
        r.Close();

        if (dryRun) return Task.FromResult(ids.Count);

        int promoted = 0;
        foreach (var id in ids)
        {
            var result = PromoteToMdAsync(id, mdDir).Result;
            if (result > 0) promoted++;
        }

        return Task.FromResult(promoted);
    }

    public Task<string> GenerateIndexAsync(string mdDir)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT o.id, o.sync_id, o.session_id, o.type, o.title, o.content,
                   o.tool_name, o.project, o.scope, o.topic_key, o.revision_count,
                   o.duplicate_count, o.last_seen_at, o.created_at, o.updated_at,
                   o.deleted_at, o.md_path
            FROM observations o
            WHERE o.md_path IS NOT NULL AND o.md_path != '' AND o.deleted_at IS NULL
            ORDER BY o.created_at DESC";
        using var r = cmd.ExecuteReader();
        var list = new List<Observation>();
        while (r.Read())
        {
            var obs = ReadObservation(r);
            obs.MdPath = r.IsDBNull(16) ? null : r.GetString(16);
            list.Add(obs);
        }

        var content = RenderIndex(list);

        if (!string.IsNullOrEmpty(mdDir))
        {
            Directory.CreateDirectory(mdDir);
            File.WriteAllText(Path.Combine(mdDir, "index.md"), content);
        }

        return Task.FromResult(content);
    }

    // ─── MD helpers ─────────────────────────────────────────────────────────────

    private static string RenderMdContent(Observation obs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"observation_id: {obs.Id}");
        sb.AppendLine($"type: \"{EscapeYamlValue(obs.Type)}\"");
        sb.AppendLine($"title: \"{EscapeYamlValue(obs.Title)}\"");
        sb.AppendLine($"created_at: \"{obs.CreatedAt}\"");
        if (!string.IsNullOrEmpty(obs.TopicKey))
            sb.AppendLine($"topic_key: \"{obs.TopicKey}\"");
        if (!string.IsNullOrEmpty(obs.Project))
            sb.AppendLine($"project: \"{EscapeYamlValue(obs.Project)}\"");
        if (!string.IsNullOrEmpty(obs.Scope))
            sb.AppendLine($"scope: \"{EscapeYamlValue(obs.Scope)}\"");
        sb.AppendLine($"generated_at: \"{DateTime.UtcNow:O}\"");
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# {obs.Title}");
        sb.AppendLine();
        sb.AppendLine(obs.Content);
        return sb.ToString();
    }

    private static string EscapeYamlValue(string value)
    {
        return value.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n");
    }

    private static string Slugify(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "untitled";
        var slug = Regex.Replace(title.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 60) slug = slug[..60].Trim('-');
        if (string.IsNullOrEmpty(slug)) slug = "untitled";
        return slug;
    }

    private static string ToFilename(string title, DateTime date)
    {
        return $"{date:yyyy-MM-dd}-{Slugify(title)}.md";
    }

    private static string RenderIndex(IReadOnlyList<Observation> promoted)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Decision Records");
        sb.AppendLine();
        sb.AppendLine($"Total: {promoted.Count} records");
        sb.AppendLine();
        var grouped = promoted.GroupBy(o => o.Type).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            sb.AppendLine($"## {group.Key}");
            sb.AppendLine();
            foreach (var obs in group.OrderByDescending(o => o.CreatedAt))
            {
                var mdLink = !string.IsNullOrEmpty(obs.MdPath)
                    ? $"[{obs.Title}]({obs.MdPath})"
                    : obs.Title;
                var date = obs.CreatedAt.Length >= 10 ? obs.CreatedAt[..10] : obs.CreatedAt;
                sb.AppendLine($"- {date} — {mdLink}");
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Projects
    // ═══════════════════════════════════════════════════════════════════════════

    public Task<MergeResult> MergeProjectsAsync(IList<string> sources, string canonical)
    {
        long observationsUpdated = 0, sessionsUpdated = 0, promptsUpdated = 0;

        foreach (var src in sources)
        {
            using var cmdObs = _db.CreateCommand();
            cmdObs.CommandText = "UPDATE observations SET project = @canonical WHERE project = @src AND deleted_at IS NULL";
            cmdObs.Parameters.AddWithValue("@canonical", canonical);
            cmdObs.Parameters.AddWithValue("@src", src);
            observationsUpdated += cmdObs.ExecuteNonQuery();

            using var cmdSess = _db.CreateCommand();
            cmdSess.CommandText = "UPDATE sessions SET project = @canonical WHERE project = @src";
            cmdSess.Parameters.AddWithValue("@canonical", canonical);
            cmdSess.Parameters.AddWithValue("@src", src);
            sessionsUpdated += cmdSess.ExecuteNonQuery();

            using var cmdPrompt = _db.CreateCommand();
            cmdPrompt.CommandText = "UPDATE user_prompts SET project = @canonical WHERE project = @src";
            cmdPrompt.Parameters.AddWithValue("@canonical", canonical);
            cmdPrompt.Parameters.AddWithValue("@src", src);
            promptsUpdated += cmdPrompt.ExecuteNonQuery();
        }

        return Task.FromResult(new MergeResult
        {
            Canonical = canonical,
            SourcesMerged = sources.ToList(),
            ObservationsUpdated = observationsUpdated,
            SessionsUpdated = sessionsUpdated,
            PromptsUpdated = promptsUpdated,
        });
    }

    public Task<IList<string>> ListProjectNamesAsync()
    {
        var results = new List<string>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT project FROM observations
            WHERE project IS NOT NULL AND project != '' AND deleted_at IS NULL
            ORDER BY project";
        using var r = cmd.ExecuteReader();
        while (r.Read()) results.Add(r.GetString(0));
        return Task.FromResult<IList<string>>(results);
    }

    public Task<IList<ProjectStats>> ListProjectsWithStatsAsync()
    {
        var statsMap = new Dictionary<string, ProjectStats>();

        // Observation counts
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT project, COUNT(*) as cnt
                FROM observations
                WHERE project IS NOT NULL AND project != '' AND deleted_at IS NULL
                GROUP BY project";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(0);
                var cnt = r.GetInt32(1);
                statsMap[name] = new ProjectStats { Name = name, ObservationCount = cnt };
            }
        }

        // Session counts + directories
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT project, COUNT(*) as cnt
                FROM sessions
                WHERE project IS NOT NULL AND project != ''
                GROUP BY project";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(0);
                var cnt = r.GetInt32(1);
                if (statsMap.TryGetValue(name, out var stats))
                    stats.SessionCount = cnt;
                else
                    statsMap[name] = new ProjectStats { Name = name, SessionCount = cnt };
            }
        }

        // Directories per project
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT project, directory
                FROM sessions
                WHERE project IS NOT NULL AND project != '' AND directory IS NOT NULL AND directory != ''
                ORDER BY project, directory";
            using var r = cmd.ExecuteReader();
            var dirSet = new Dictionary<string, HashSet<string>>();
            while (r.Read())
            {
                var name = r.GetString(0);
                var dir = r.GetString(1);
                if (!dirSet.TryGetValue(name, out var set))
                {
                    set = new HashSet<string>();
                    dirSet[name] = set;
                }
                set.Add(dir);
            }
            foreach (var (name, dirs) in dirSet)
            {
                if (statsMap.TryGetValue(name, out var stats))
                    stats.Directories = dirs.OrderBy(d => d).ToList();
            }
        }

        // Prompt counts
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT COALESCE(project,'') as project, COUNT(*) as cnt
                FROM user_prompts
                WHERE deleted_at IS NULL
                GROUP BY project";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var name = r.GetString(0);
                if (string.IsNullOrEmpty(name)) continue;
                var cnt = r.GetInt32(1);
                if (statsMap.TryGetValue(name, out var stats))
                    stats.PromptCount = cnt;
            }
        }

        var results = statsMap.Values.OrderByDescending(s => s.ObservationCount).ToList();
        return Task.FromResult<IList<ProjectStats>>(results);
    }

    public Task<int> CountObservationsForProjectAsync(string project)
    {
        project = Normalizers.NormalizeProject(project);
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM observations WHERE project = @proj AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("@proj", project);
        return Task.FromResult(Convert.ToInt32(cmd.ExecuteScalar()));
    }

    public Task<PruneResult> PruneProjectAsync(string project)
    {
        project = Normalizers.NormalizeProject(project);

        var obsCount = Convert.ToInt32(_db.CreateCommand().ExecuteScalarWithParams(
            "SELECT COUNT(*) FROM observations WHERE project = @proj AND deleted_at IS NULL",
            new NpgsqlParameter("@proj", project)));
        if (obsCount > 0)
            throw new InvalidOperationException(
                $"Project \"{project}\" still has {obsCount} observations — cannot prune. " +
                "Merge or delete observations first.");

        long sessionsDeleted = 0, promptsDeleted = 0;

        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM user_prompts WHERE project = @proj";
            cmd.Parameters.AddWithValue("@proj", project);
            promptsDeleted = cmd.ExecuteNonQuery();
        }
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM sessions WHERE project = @proj";
            cmd.Parameters.AddWithValue("@proj", project);
            sessionsDeleted = cmd.ExecuteNonQuery();
        }

        return Task.FromResult(new PruneResult
        {
            Project = project,
            SessionsDeleted = sessionsDeleted,
            PromptsDeleted = promptsDeleted,
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Sync Chunks
    // ═══════════════════════════════════════════════════════════════════════════

    public Task<ISet<string>> GetSyncedChunksAsync()
    {
        var chunks = new HashSet<string>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT chunk_id FROM sync_chunks";
        using var r = cmd.ExecuteReader();
        while (r.Read()) chunks.Add(r.GetString(0));
        return Task.FromResult<ISet<string>>(chunks);
    }

    public Task RecordSyncedChunkAsync(string chunkId)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "INSERT INTO sync_chunks (chunk_id, imported_at) VALUES (@id, NOW() AT TIME ZONE 'utc') ON CONFLICT (chunk_id) DO NOTHING";
        cmd.Parameters.AddWithValue("@id", chunkId);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ICloudMutationStore Implementation (offline-first-sync Phase 1)
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<List<long>> InsertMutationBatchAsync(
        IReadOnlyList<MutationEntry> entries,
        string? createdBy = null,
        CancellationToken ct = default)
    {
        if (entries.Count == 0) return [];

        var seqs = new List<long>(entries.Count);
        using var tx = _db.BeginTransaction();

        try
        {
            foreach (var entry in entries)
            {
                await using var cmd = _db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO cloud_mutations (project, entity, entity_key, op, payload, created_by, occurred_at)
                    VALUES (@project, @entity, @entity_key, @op, @payload, @created_by, NOW())
                    RETURNING seq";

                cmd.Parameters.AddWithValue("@project", entry.Project);
                cmd.Parameters.AddWithValue("@entity", entry.Entity);
                cmd.Parameters.AddWithValue("@entity_key", entry.EntityKey);
                cmd.Parameters.AddWithValue("@op", entry.Op);
                cmd.Parameters.AddWithValue("@payload", entry.Payload);
                cmd.Parameters.AddWithValue("@created_by", (object?)createdBy ?? DBNull.Value);

                var seq = (long)(await cmd.ExecuteScalarAsync(ct))!;
                seqs.Add(seq);
            }

            tx.Commit();
            return seqs;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<(List<StoredMutation> Mutations, bool HasMore, long LatestSeq)> ListMutationsSinceAsync(
        long sinceSeq,
        int limit,
        List<string>? allowedProjects,
        CancellationToken ct = default)
    {
        var mutations = new List<StoredMutation>();
        await using var cmd = _db.CreateCommand();

        var sql = @"
            SELECT seq, project, entity, entity_key, op, payload, occurred_at
            FROM cloud_mutations
            WHERE seq > @sinceSeq";

        if (allowedProjects is not null && allowedProjects.Count > 0)
        {
            var projectParams = new List<string>();
            for (int i = 0; i < allowedProjects.Count; i++)
            {
                var paramName = $"@proj{i}";
                projectParams.Add(paramName);
                cmd.Parameters.AddWithValue(paramName, allowedProjects[i]);
            }
            sql += $" AND project = ANY({{{string.Join(",", projectParams)}}})";
        }

        sql += " ORDER BY seq ASC LIMIT @limit";

        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@sinceSeq", sinceSeq);
        cmd.Parameters.AddWithValue("@limit", limit + 1); // Fetch one extra to detect has_more

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            mutations.Add(new StoredMutation(
                r.GetInt64(0), // seq
                r.GetString(1), // project
                r.GetString(2), // entity
                r.GetString(3), // entity_key
                r.GetString(4), // op
                r.GetString(5), // payload
                r.GetDateTime(6).ToString("O") // occurred_at
            ));
        }

        bool hasMore = mutations.Count > limit;
        long latestSeq = mutations.Count > 0 ? mutations[^1].Seq : sinceSeq;

        if (hasMore)
            mutations.RemoveAt(mutations.Count - 1); // Remove the extra item

        return (mutations, hasMore, latestSeq);
    }

    public async Task<bool> IsProjectSyncEnabledAsync(string project, CancellationToken ct = default)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT COALESCE(sync_enabled, true)
            FROM cloud_project_controls
            WHERE project = @project";
        cmd.Parameters.AddWithValue("@project", project);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null || (bool)result;
    }

    public async Task InsertAuditEntryAsync(AuditEntry entry, CancellationToken ct = default)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO cloud_sync_audit_log (project, action, outcome, contributor, entry_count, reason_code, created_at)
            VALUES (@project, @action, @outcome, @contributor, @entry_count, @reason_code, NOW())";

        cmd.Parameters.AddWithValue("@project", entry.Project);
        cmd.Parameters.AddWithValue("@action", entry.Action);
        cmd.Parameters.AddWithValue("@outcome", entry.Outcome);
        cmd.Parameters.AddWithValue("@contributor", (object?)entry.Contributor ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@entry_count", entry.EntryCount);
        cmd.Parameters.AddWithValue("@reason_code", (object?)entry.ReasonCode ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<EnrolledProject>> GetEnrolledProjectsAsync(string user, CancellationToken ct = default)
    {
        var projects = new List<EnrolledProject>();
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT project, enrolled_at, enrolled_by FROM sync_enrolled_projects
            WHERE user = @user
            ORDER BY enrolled_at DESC";
        cmd.Parameters.AddWithValue("@user", user);

        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            var project = r.GetString(0);
            var enrolledAt = r.IsDBNull(1) ? "" : r.GetString(1);
            var enrolledBy = r.IsDBNull(2) ? "" : r.GetString(2);
            projects.Add(new EnrolledProject(project, enrolledAt, enrolledBy));
        }

        return projects;
    }

    public async Task<EnrollmentResult> EnrollProjectAsync(string project, string user, CancellationToken ct = default)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sync_enrolled_projects (project, user, enrolled_by, enrolled_at)
            VALUES (@project, @user, @user, NOW() AT TIME ZONE 'utc')
            ON CONFLICT (project, user) DO NOTHING
            RETURNING enrolled_at";
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@user", user);

        var result = await cmd.ExecuteScalarAsync(ct);
        var enrolledAt = result?.ToString();

        return new EnrollmentResult(
            Project: project,
            EnrolledAt: enrolledAt,
            EnrolledBy: user,
            Status: enrolledAt != null ? "enrolled" : "already_enrolled"
        );
    }

    public async Task<EnrollmentResult> UnenrollProjectAsync(string project, string user, CancellationToken ct = default)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM sync_enrolled_projects
            WHERE project = @project AND user = @user
            RETURNING enrolled_at";
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@user", user);

        var result = await cmd.ExecuteScalarAsync(ct);
        
        if (result == null)
        {
            return new EnrollmentResult(
                Project: project,
                Status: "not_found"
            );
        }

        return new EnrollmentResult(
            Project: project,
            UnenrolledAt: DateTime.UtcNow.ToString("O"),
            Status: "unenrolled"
        );
    }

    public async Task<PauseResult> PauseProjectAsync(string project, string reason, string pausedBy, CancellationToken ct = default)
    {
        var pausedAt = DateTime.UtcNow.ToString("O");
        
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO cloud_project_controls (project, sync_enabled, pause_reason, paused_at, paused_by)
            VALUES (@project, false, @reason, @paused_at, @paused_by)
            ON CONFLICT (project) DO UPDATE SET
                sync_enabled = false,
                pause_reason = EXCLUDED.pause_reason,
                paused_at = EXCLUDED.paused_at,
                paused_by = EXCLUDED.paused_by
            RETURNING paused_at";
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@reason", reason);
        cmd.Parameters.AddWithValue("@paused_at", pausedAt);
        cmd.Parameters.AddWithValue("@paused_by", pausedBy);

        await cmd.ExecuteScalarAsync(ct);

        // Log audit entry
        await InsertAuditEntryAsync(new AuditEntry(
            Project: project,
            Action: "pause",
            Outcome: "success",
            Contributor: pausedBy,
            EntryCount: 0,
            ReasonCode: reason
        ), ct);

        return new PauseResult(
            Project: project,
            Paused: true,
            PausedAt: pausedAt,
            PausedBy: pausedBy,
            Reason: reason
        );
    }

    public async Task<PauseResult> ResumeProjectAsync(string project, string resumedBy, CancellationToken ct = default)
    {
        var resumedAt = DateTime.UtcNow.ToString("O");
        
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            UPDATE cloud_project_controls
            SET sync_enabled = true, pause_reason = NULL, paused_at = NULL, paused_by = NULL,
                resumed_at = @resumed_at, resumed_by = @resumed_by
            WHERE project = @project
            RETURNING resumed_at";
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@resumed_at", resumedAt);
        cmd.Parameters.AddWithValue("@resumed_by", resumedBy);

        await cmd.ExecuteScalarAsync(ct);

        // Log audit entry
        await InsertAuditEntryAsync(new AuditEntry(
            Project: project,
            Action: "resume",
            Outcome: "success",
            Contributor: resumedBy,
            EntryCount: 0
        ), ct);

        return new PauseResult(
            Project: project,
            Paused: false,
            ResumedAt: resumedAt,
            ResumedBy: resumedBy
        );
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ICloudChunkStore Implementation (offline-first-sync Phase 1 - schema only)
    // ═══════════════════════════════════════════════════════════════════════════
    // ICloudChunkStore Implementation (offline-first-sync Phase 1 - schema only)
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task WriteChunkAsync(
        string project,
        string chunkId,
        string createdBy,
        DateTime clientCreatedAt,
        byte[] payload,
        CancellationToken ct = default)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO cloud_chunks (chunk_id, project, payload, created_by, client_created_at)
            VALUES (@chunk_id, @project, @payload, @created_by, @client_created_at)
            ON CONFLICT (chunk_id) DO UPDATE SET
                payload = EXCLUDED.payload";

        cmd.Parameters.AddWithValue("@chunk_id", chunkId);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@payload", payload);
        cmd.Parameters.AddWithValue("@created_by", createdBy);
        cmd.Parameters.AddWithValue("@client_created_at", clientCreatedAt);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<byte[]?> ReadChunkAsync(string project, string chunkId, CancellationToken ct = default)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT payload FROM cloud_chunks WHERE chunk_id = @chunk_id AND project = @project";
        cmd.Parameters.AddWithValue("@chunk_id", chunkId);
        cmd.Parameters.AddWithValue("@project", project);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is byte[] bytes ? bytes : null;
    }

    public async Task<bool> ChunkExistsAsync(string project, string chunkId, CancellationToken ct = default)
    {
        await using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM cloud_chunks WHERE chunk_id = @chunk_id AND project = @project";
        cmd.Parameters.AddWithValue("@chunk_id", chunkId);
        cmd.Parameters.AddWithValue("@project", project);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is not null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    private static Session ReadSession(NpgsqlDataReader r) => new()
    {
        Id = r.GetString(0),
        Project = r.GetString(1),
        Directory = r.GetString(2),
        StartedAt = r.GetString(3),
        EndedAt = r.IsDBNull(4) ? null : r.GetString(4),
        Summary = r.IsDBNull(5) ? null : r.GetString(5),
    };

    private static Session ReadSessionFromObs(Observation o) => new()
    {
        Id = o.SessionId,
        Project = o.Project ?? "",
        Directory = "",
        StartedAt = o.CreatedAt,
    };

    private static Observation ReadObservation(NpgsqlDataReader r) => new()
    {
        Id = r.GetInt64(0),
        SyncId = r.IsDBNull(1) ? "" : r.GetString(1),
        SessionId = r.GetString(2),
        Type = r.GetString(3),
        Title = r.GetString(4),
        Content = r.GetString(5),
        ToolName = r.IsDBNull(6) ? null : r.GetString(6),
        Project = r.IsDBNull(7) ? null : r.GetString(7),
        Scope = r.GetString(8),
        TopicKey = r.IsDBNull(9) ? null : r.GetString(9),
        RevisionCount = r.GetInt32(10),
        DuplicateCount = r.GetInt32(11),
        LastSeenAt = r.IsDBNull(12) ? null : r.GetString(12),
        CreatedAt = r.GetString(13),
        UpdatedAt = r.GetString(14),
        DeletedAt = r.IsDBNull(15) ? null : r.GetString(15),
    };

    private static TimelineEntry ToTimelineEntry(Observation o) => new()
    {
        Id = o.Id,
        SessionId = o.SessionId,
        Type = o.Type,
        Title = o.Title,
        Content = o.Content,
        ToolName = o.ToolName,
        Project = o.Project,
        Scope = o.Scope,
        TopicKey = o.TopicKey,
        RevisionCount = o.RevisionCount,
        DuplicateCount = o.DuplicateCount,
        LastSeenAt = o.LastSeenAt,
        CreatedAt = o.CreatedAt,
        UpdatedAt = o.UpdatedAt,
        DeletedAt = o.DeletedAt,
    };

    private static Prompt ReadPrompt(NpgsqlDataReader r) => new()
    {
        Id = r.GetInt64(0),
        SyncId = r.IsDBNull(1) ? "" : r.GetString(1),
        SessionId = r.GetString(2),
        Content = r.GetString(3),
        Project = r.GetString(4),
        CreatedAt = r.GetString(5),
    };

    private List<Observation> QueryObservations(string sql, IEnumerable<NpgsqlParameter> parms)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parms) cmd.Parameters.Add(p);
        using var r = cmd.ExecuteReader();
        var list = new List<Observation>();
        while (r.Read()) list.Add(ReadObservation(r));
        return list;
    }

    private List<Prompt> QueryPrompts(string sql, IEnumerable<NpgsqlParameter> parms)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parms) cmd.Parameters.Add(p);
        using var r = cmd.ExecuteReader();
        var list = new List<Prompt>();
        while (r.Read()) list.Add(ReadPrompt(r));
        return list;
    }

    private List<SearchResult> QuerySearchResults(string sql, IEnumerable<NpgsqlParameter> parms)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parms) cmd.Parameters.Add(p);

        var seen = new HashSet<long>();
        var list = new List<SearchResult>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.GetInt64(0);
            if (seen.Contains(id)) continue;
            seen.Add(id);

            var obs = ReadObservation(r);
            var rank = r.GetDouble(16);
            list.Add(new SearchResult { Observation = obs, Rank = rank });
        }
        return list;
    }

    private Observation? GetObservationDirect(long id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT o.id, o.sync_id, o.session_id, o.type, o.title, o.content, o.tool_name, o.project,
                   o.scope, o.topic_key, o.revision_count, o.duplicate_count, o.last_seen_at,
                   o.created_at, o.updated_at, o.deleted_at
            FROM observations o WHERE o.id = @id AND o.deleted_at IS NULL";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadObservation(r) : null;
    }

    private Session? GetSessionDirect(string id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id, project, directory, started_at, ended_at, summary FROM sessions WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadSession(r) : null;
    }

    private static void AppendSearchFilter(StringBuilder sql, List<NpgsqlParameter> parms, SearchOptions opts)
    {
        if (!string.IsNullOrEmpty(opts.Type))
        {
            sql.Append(" AND o.type = @type");
            parms.Add(new NpgsqlParameter("@type", opts.Type));
        }
        if (!string.IsNullOrEmpty(opts.Project))
        {
            sql.Append(" AND o.project = @proj");
            parms.Add(new NpgsqlParameter("@proj", opts.Project));
        }
        if (!string.IsNullOrEmpty(opts.Scope))
        {
            sql.Append(" AND o.scope = @scope");
            parms.Add(new NpgsqlParameter("@scope", opts.Scope));
        }
    }

    private static void AppendProjectInClause(StringBuilder sql, List<NpgsqlParameter> parms, IList<string> projects)
    {
        if (projects.Count == 0) return;
        sql.Append(" AND o.project IN (");
        for (int i = 0; i < projects.Count; i++)
        {
            var pname = $"@p{i}";
            sql.Append(pname);
            if (i < projects.Count - 1) sql.Append(", ");
            parms.Add(new NpgsqlParameter(pname, projects[i]));
        }
        sql.Append(")");
    }

    private static string NewSyncId(string prefix)
    {
        var bytes = new byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return prefix + "-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Truncate(string s, int max)
    {
        if (s == null) return "";
        if (s.Length <= max) return s;
        return s[..max] + "...";
    }
}

// Extension helper for one-off scalar queries
internal static class NpgsqlCommandExtensions
{
    public static object? ExecuteScalarWithParams(this NpgsqlCommand cmd, string sql, params NpgsqlParameter[] parms)
    {
        cmd.CommandText = sql;
        foreach (var p in parms) cmd.Parameters.Add(p);
        return cmd.ExecuteScalar();
    }
}
