using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Engram.Store;

/// <summary>
/// SQLite-backed implementation of IStore.
/// Schema, dedup logic, and FTS5 behaviour are intentionally identical to the Go original.
/// </summary>
public sealed class SqliteStore : IStore
{
    private readonly SqliteConnection _db;
    private readonly StoreConfig _cfg;

    // ─── Construction ──────────────────────────────────────────────────────────

    public SqliteStore(StoreConfig cfg)
    {
        _cfg = cfg;

        if (!Path.IsPathRooted(cfg.DataDir))
            throw new ArgumentException(
                $"engram: data directory must be an absolute path, got \"{cfg.DataDir}\" — set ENGRAM_DATA_DIR or ensure your home directory is resolvable");

        Directory.CreateDirectory(cfg.DataDir);

        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = cfg.DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        _db = new SqliteConnection(connStr);
        _db.Open();

        ApplyPragmas();
        Migrate();
    }

    public int MaxObservationLength => _cfg.MaxObservationLength;

    public void Dispose() => _db.Dispose();

    // ─── Pragmas ───────────────────────────────────────────────────────────────

    private void ApplyPragmas()
    {
        foreach (var pragma in new[]
        {
            "PRAGMA journal_mode = WAL",
            "PRAGMA busy_timeout = 5000",
            "PRAGMA synchronous = NORMAL",
            "PRAGMA foreign_keys = ON",
        })
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = pragma;
            cmd.ExecuteNonQuery();
        }
    }

    // ─── Migrations ────────────────────────────────────────────────────────────

    private void Migrate()
    {
        Exec(@"
            CREATE TABLE IF NOT EXISTS sessions (
                id         TEXT PRIMARY KEY,
                project    TEXT NOT NULL,
                directory  TEXT NOT NULL,
                started_at TEXT NOT NULL DEFAULT (datetime('now')),
                ended_at   TEXT,
                summary    TEXT
            );

            CREATE TABLE IF NOT EXISTS observations (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                sync_id         TEXT,
                session_id      TEXT    NOT NULL,
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
                created_at      TEXT    NOT NULL DEFAULT (datetime('now')),
                updated_at      TEXT    NOT NULL DEFAULT (datetime('now')),
                deleted_at      TEXT,
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );

            CREATE INDEX IF NOT EXISTS idx_obs_session  ON observations(session_id);
            CREATE INDEX IF NOT EXISTS idx_obs_type     ON observations(type);
            CREATE INDEX IF NOT EXISTS idx_obs_project  ON observations(project);
            CREATE INDEX IF NOT EXISTS idx_obs_created  ON observations(created_at DESC);

            CREATE VIRTUAL TABLE IF NOT EXISTS observations_fts USING fts5(
                title,
                content,
                tool_name,
                type,
                project,
                topic_key,
                content='observations',
                content_rowid='id'
            );

            CREATE TABLE IF NOT EXISTS user_prompts (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                sync_id    TEXT,
                session_id TEXT    NOT NULL,
                content    TEXT    NOT NULL,
                project    TEXT,
                created_at TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );

            CREATE INDEX IF NOT EXISTS idx_prompts_session ON user_prompts(session_id);
            CREATE INDEX IF NOT EXISTS idx_prompts_project ON user_prompts(project);
            CREATE INDEX IF NOT EXISTS idx_prompts_created ON user_prompts(created_at DESC);

            CREATE VIRTUAL TABLE IF NOT EXISTS prompts_fts USING fts5(
                content,
                project,
                content='user_prompts',
                content_rowid='id'
            );

            CREATE TABLE IF NOT EXISTS sync_chunks (
                chunk_id    TEXT PRIMARY KEY,
                imported_at TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS sync_state (
                target_key           TEXT PRIMARY KEY,
                lifecycle            TEXT NOT NULL DEFAULT 'idle',
                last_enqueued_seq    INTEGER NOT NULL DEFAULT 0,
                last_acked_seq       INTEGER NOT NULL DEFAULT 0,
                last_pulled_seq      INTEGER NOT NULL DEFAULT 0,
                consecutive_failures INTEGER NOT NULL DEFAULT 0,
                backoff_until        TEXT,
                lease_owner          TEXT,
                lease_until          TEXT,
                last_error           TEXT,
                updated_at           TEXT NOT NULL DEFAULT (datetime('now'))
            );

            CREATE TABLE IF NOT EXISTS sync_mutations (
                seq         INTEGER PRIMARY KEY AUTOINCREMENT,
                target_key  TEXT NOT NULL,
                entity      TEXT NOT NULL,
                entity_key  TEXT NOT NULL,
                op          TEXT NOT NULL,
                payload     TEXT NOT NULL,
                source      TEXT NOT NULL DEFAULT 'local',
                occurred_at TEXT NOT NULL DEFAULT (datetime('now')),
                acked_at    TEXT,
                FOREIGN KEY (target_key) REFERENCES sync_state(target_key)
            );
        ");

        // Additive column migrations (idempotent)
        AddColumnIfNotExists("observations", "sync_id",         "TEXT");
        AddColumnIfNotExists("observations", "scope",           "TEXT NOT NULL DEFAULT 'project'");
        AddColumnIfNotExists("observations", "topic_key",       "TEXT");
        AddColumnIfNotExists("observations", "normalized_hash", "TEXT");
        AddColumnIfNotExists("observations", "revision_count",  "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfNotExists("observations", "duplicate_count", "INTEGER NOT NULL DEFAULT 1");
        AddColumnIfNotExists("observations", "last_seen_at",    "TEXT");
        AddColumnIfNotExists("observations", "updated_at",      "TEXT NOT NULL DEFAULT ''");
        AddColumnIfNotExists("observations", "deleted_at",      "TEXT");
        AddColumnIfNotExists("user_prompts", "sync_id",         "TEXT");

        Exec(@"
            CREATE INDEX IF NOT EXISTS idx_obs_scope         ON observations(scope);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_obs_sync_id ON observations(sync_id);
            CREATE INDEX IF NOT EXISTS idx_obs_topic         ON observations(topic_key, project, scope, updated_at DESC);
            CREATE INDEX IF NOT EXISTS idx_obs_deleted       ON observations(deleted_at);
            CREATE INDEX IF NOT EXISTS idx_obs_dedupe        ON observations(normalized_hash, project, scope, type, title, created_at DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_prompts_sync_id ON user_prompts(sync_id);
            CREATE INDEX IF NOT EXISTS idx_sync_mutations_target_seq ON sync_mutations(target_key, seq);
            CREATE INDEX IF NOT EXISTS idx_sync_mutations_pending    ON sync_mutations(target_key, acked_at, seq);
        ");

        AddColumnIfNotExists("sync_mutations", "project", "TEXT NOT NULL DEFAULT ''");

        Exec(@"
            CREATE TABLE IF NOT EXISTS sync_enrolled_projects (
                project     TEXT PRIMARY KEY,
                enrolled_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            CREATE INDEX IF NOT EXISTS idx_sync_mutations_project ON sync_mutations(project);
        ");

        // Backfill project column in sync_mutations from JSON payload
        Exec(@"
            UPDATE sync_mutations
            SET project = COALESCE(json_extract(payload, '$.project'), '')
            WHERE project = '' AND payload != ''
        ");

        // Normalisation fixes
        Exec("UPDATE observations SET scope = 'project' WHERE scope IS NULL OR scope = ''");
        Exec("UPDATE observations SET topic_key = NULL WHERE topic_key = ''");
        Exec("UPDATE observations SET revision_count = 1 WHERE revision_count IS NULL OR revision_count < 1");
        Exec("UPDATE observations SET duplicate_count = 1 WHERE duplicate_count IS NULL OR duplicate_count < 1");
        Exec("UPDATE observations SET updated_at = created_at WHERE updated_at IS NULL OR updated_at = ''");
        Exec("UPDATE observations SET sync_id = 'obs-' || lower(hex(randomblob(16))) WHERE sync_id IS NULL OR sync_id = ''");
        Exec("UPDATE user_prompts SET project = '' WHERE project IS NULL");
        Exec("UPDATE user_prompts SET sync_id = 'prompt-' || lower(hex(randomblob(16))) WHERE sync_id IS NULL OR sync_id = ''");
        Exec("INSERT OR IGNORE INTO sync_state (target_key, lifecycle, updated_at) VALUES ('cloud', 'idle', datetime('now'))");

        // FTS triggers (idempotent check)
        if (!TriggerExists("obs_fts_insert"))
        {
            Exec(@"
                CREATE TRIGGER obs_fts_insert AFTER INSERT ON observations BEGIN
                    INSERT INTO observations_fts(rowid, title, content, tool_name, type, project, topic_key)
                    VALUES (new.id, new.title, new.content, new.tool_name, new.type, new.project, new.topic_key);
                END;

                CREATE TRIGGER obs_fts_delete AFTER DELETE ON observations BEGIN
                    INSERT INTO observations_fts(observations_fts, rowid, title, content, tool_name, type, project, topic_key)
                    VALUES ('delete', old.id, old.title, old.content, old.tool_name, old.type, old.project, old.topic_key);
                END;

                CREATE TRIGGER obs_fts_update AFTER UPDATE ON observations BEGIN
                    INSERT INTO observations_fts(observations_fts, rowid, title, content, tool_name, type, project, topic_key)
                    VALUES ('delete', old.id, old.title, old.content, old.tool_name, old.type, old.project, old.topic_key);
                    INSERT INTO observations_fts(rowid, title, content, tool_name, type, project, topic_key)
                    VALUES (new.id, new.title, new.content, new.tool_name, new.type, new.project, new.topic_key);
                END;
            ");
        }

        MigrateFtsTopicKey();

        if (!TriggerExists("prompt_fts_insert"))
        {
            Exec(@"
                CREATE TRIGGER prompt_fts_insert AFTER INSERT ON user_prompts BEGIN
                    INSERT INTO prompts_fts(rowid, content, project)
                    VALUES (new.id, new.content, new.project);
                END;

                CREATE TRIGGER prompt_fts_delete AFTER DELETE ON user_prompts BEGIN
                    INSERT INTO prompts_fts(prompts_fts, rowid, content, project)
                    VALUES ('delete', old.id, old.content, old.project);
                END;

                CREATE TRIGGER prompt_fts_update AFTER UPDATE ON user_prompts BEGIN
                    INSERT INTO prompts_fts(prompts_fts, rowid, content, project)
                    VALUES ('delete', old.id, old.content, old.project);
                    INSERT INTO prompts_fts(rowid, content, project)
                    VALUES (new.id, new.content, new.project);
                END;
            ");
        }
    }

    private void MigrateFtsTopicKey()
    {
        // Check whether observations_fts already has a topic_key column
        int colCount = 0;
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_xinfo('observations_fts') WHERE name = 'topic_key'";
            var result = cmd.ExecuteScalar();
            colCount = Convert.ToInt32(result);
        }
        if (colCount > 0) return;

        Exec(@"
            DROP TRIGGER IF EXISTS obs_fts_insert;
            DROP TRIGGER IF EXISTS obs_fts_update;
            DROP TRIGGER IF EXISTS obs_fts_delete;
            DROP TABLE IF EXISTS observations_fts;
            CREATE VIRTUAL TABLE observations_fts USING fts5(
                title,
                content,
                tool_name,
                type,
                project,
                topic_key,
                content='observations',
                content_rowid='id'
            );
            INSERT INTO observations_fts(rowid, title, content, tool_name, type, project, topic_key)
            SELECT id, title, content, tool_name, type, project, topic_key
            FROM observations
            WHERE deleted_at IS NULL;

            CREATE TRIGGER obs_fts_insert AFTER INSERT ON observations BEGIN
                INSERT INTO observations_fts(rowid, title, content, tool_name, type, project, topic_key)
                VALUES (new.id, new.title, new.content, new.tool_name, new.type, new.project, new.topic_key);
            END;

            CREATE TRIGGER obs_fts_delete AFTER DELETE ON observations BEGIN
                INSERT INTO observations_fts(observations_fts, rowid, title, content, tool_name, type, project, topic_key)
                VALUES ('delete', old.id, old.title, old.content, old.tool_name, old.type, old.project, old.topic_key);
            END;

            CREATE TRIGGER obs_fts_update AFTER UPDATE ON observations BEGIN
                INSERT INTO observations_fts(observations_fts, rowid, title, content, tool_name, type, project, topic_key)
                VALUES ('delete', old.id, old.title, old.content, old.tool_name, old.type, old.project, old.topic_key);
                INSERT INTO observations_fts(rowid, title, content, tool_name, type, project, topic_key)
                VALUES (new.id, new.title, new.content, new.tool_name, new.type, new.project, new.topic_key);
            END;
        ");
    }

    // ─── Sessions ──────────────────────────────────────────────────────────────

    public Task CreateSessionAsync(string id, string project, string directory)
    {
        project = Normalizers.NormalizeProject(project);

        WithTx(tx =>
        {
            Exec(tx, "INSERT OR IGNORE INTO sessions (id, project, directory) VALUES (@id, @proj, @dir)",
                Param("@id", id), Param("@proj", project), Param("@dir", directory));

            EnqueueSyncMutation(tx, "session", id, "upsert", new
            {
                id, project, directory,
                ended_at = (string?)null,
                summary  = (string?)null,
            });
        });

        return Task.CompletedTask;
    }

    public Task EndSessionAsync(string id, string summary)
    {
        WithTx(tx =>
        {
            var rows = ExecRows(tx,
                "UPDATE sessions SET ended_at = datetime('now'), summary = @sum WHERE id = @id",
                Param("@sum", NullableString(summary)),
                Param("@id", id));

            if (rows == 0) return;

            string project = "", directory = "", endedAt = "";
            string? storedSummary = null;

            using var cmd = TxCmd(tx,
                "SELECT project, directory, ended_at, summary FROM sessions WHERE id = @id",
                Param("@id", id));
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                project      = reader.GetString(0);
                directory    = reader.GetString(1);
                endedAt      = reader.GetString(2);
                storedSummary = reader.IsDBNull(3) ? null : reader.GetString(3);
            }

            EnqueueSyncMutation(tx, "session", id, "upsert", new
            {
                id, project, directory,
                ended_at = endedAt,
                summary  = storedSummary,
            });
        });

        return Task.CompletedTask;
    }

    public Task<Session?> GetSessionAsync(string id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT id, project, directory, started_at, ended_at, summary FROM sessions WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return Task.FromResult<Session?>(null);

        return Task.FromResult<Session?>(new Session
        {
            Id        = r.GetString(0),
            Project   = r.GetString(1),
            Directory = r.GetString(2),
            StartedAt = r.GetString(3),
            EndedAt   = r.IsDBNull(4) ? null : r.GetString(4),
            Summary   = r.IsDBNull(5) ? null : r.GetString(5),
        });
    }

    public Task<IList<SessionSummary>> RecentSessionsAsync(string? project, int limit)
    {
        project = Normalizers.NormalizeProject(project);
        if (limit <= 0) limit = 5;

        var sql = new StringBuilder(@"
            SELECT s.id, s.project, s.started_at, s.ended_at, s.summary,
                   COUNT(o.id) as observation_count
            FROM sessions s
            LEFT JOIN observations o ON o.session_id = s.id AND o.deleted_at IS NULL
            WHERE 1=1
        ");
        var parms = new List<SqliteParameter>();

        if (!string.IsNullOrEmpty(project))
        {
            sql.Append(" AND s.project = @proj");
            parms.Add(Param("@proj", project));
        }

        sql.Append(" GROUP BY s.id ORDER BY MAX(COALESCE(o.created_at, s.started_at)) DESC LIMIT @limit");
        parms.Add(Param("@limit", limit));

        var results = new List<SessionSummary>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql.ToString();
        foreach (var p in parms) cmd.Parameters.Add(p);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new SessionSummary
            {
                Id               = r.GetString(0),
                Project          = r.GetString(1),
                StartedAt        = r.GetString(2),
                EndedAt          = r.IsDBNull(3) ? null : r.GetString(3),
                Summary          = r.IsDBNull(4) ? null : r.GetString(4),
                ObservationCount = r.GetInt32(5),
            });
        }

        return Task.FromResult<IList<SessionSummary>>(results);
    }

    // ─── Observations ──────────────────────────────────────────────────────────

    public Task<long> AddObservationAsync(AddObservationParams p)
    {
        p = p with { Project = Normalizers.NormalizeProject(p.Project) };

        var title   = StripPrivateTags(p.Title);
        var content = StripPrivateTags(p.Content);
        if (content.Length > _cfg.MaxObservationLength)
            content = content[.._cfg.MaxObservationLength] + "... [truncated]";

        var scope    = NormalizeScope(p.Scope);
        var normHash = Normalizers.HashNormalized(content);
        var topicKey = Normalizers.NormalizeTopicKey(p.TopicKey);

        long observationId = 0;

        WithTx(tx =>
        {
            // ── Path 1: topic_key upsert ──────────────────────────────────────
            if (!string.IsNullOrEmpty(topicKey))
            {
                var existingId = QueryScalar<long?>(tx,
                    @"SELECT id FROM observations
                      WHERE topic_key = @tk
                        AND ifnull(project,'') = ifnull(@proj,'')
                        AND scope = @scope
                        AND deleted_at IS NULL
                      ORDER BY datetime(updated_at) DESC, datetime(created_at) DESC
                      LIMIT 1",
                    Param("@tk",    topicKey),
                    Param("@proj",  NullableString(p.Project)),
                    Param("@scope", scope));

                if (existingId.HasValue)
                {
                    Exec(tx,
                        @"UPDATE observations
                          SET type = @type, title = @title, content = @content,
                              tool_name = @tool, topic_key = @tk,
                              normalized_hash = @hash,
                              revision_count = revision_count + 1,
                              last_seen_at = datetime('now'),
                              updated_at   = datetime('now')
                          WHERE id = @id",
                        Param("@type",  p.Type),
                        Param("@title", title),
                        Param("@content", content),
                        Param("@tool", NullableString(p.ToolName)),
                        Param("@tk",   NullableString(topicKey)),
                        Param("@hash", normHash),
                        Param("@id",   existingId.Value));

                    var obs = GetObservationTx(tx, existingId.Value)!;
                    observationId = existingId.Value;
                    EnqueueSyncMutation(tx, "observation", obs.SyncId, "upsert", ObsPayload(obs));
                    return;
                }
            }

            // ── Path 2: hash dedup window ─────────────────────────────────────
            var window = Normalizers.DedupeWindowExpression(_cfg.DedupeWindow);
            var dupId  = QueryScalar<long?>(tx,
                @"SELECT id FROM observations
                  WHERE normalized_hash = @hash
                    AND ifnull(project,'') = ifnull(@proj,'')
                    AND scope = @scope
                    AND type  = @type
                    AND title = @title
                    AND deleted_at IS NULL
                    AND datetime(created_at) >= datetime('now', @window)
                  ORDER BY created_at DESC
                  LIMIT 1",
                Param("@hash",   normHash),
                Param("@proj",   NullableString(p.Project)),
                Param("@scope",  scope),
                Param("@type",   p.Type),
                Param("@title",  title),
                Param("@window", window));

            if (dupId.HasValue)
            {
                Exec(tx,
                    @"UPDATE observations
                      SET duplicate_count = duplicate_count + 1,
                          last_seen_at = datetime('now'),
                          updated_at   = datetime('now')
                      WHERE id = @id",
                    Param("@id", dupId.Value));

                var obs = GetObservationTx(tx, dupId.Value)!;
                observationId = dupId.Value;
                EnqueueSyncMutation(tx, "observation", obs.SyncId, "upsert", ObsPayload(obs));
                return;
            }

            // ── Path 3: fresh insert ──────────────────────────────────────────
            var syncId = NewSyncId("obs");
            Exec(tx,
                @"INSERT INTO observations
                    (sync_id, session_id, type, title, content, tool_name, project,
                     scope, topic_key, normalized_hash, revision_count, duplicate_count,
                     last_seen_at, updated_at)
                  VALUES
                    (@sid, @sess, @type, @title, @content, @tool, @proj,
                     @scope, @tk, @hash, 1, 1, datetime('now'), datetime('now'))",
                Param("@sid",     syncId),
                Param("@sess",    p.SessionId),
                Param("@type",    p.Type),
                Param("@title",   title),
                Param("@content", content),
                Param("@tool",    NullableString(p.ToolName)),
                Param("@proj",    NullableString(p.Project)),
                Param("@scope",   scope),
                Param("@tk",      NullableString(topicKey)),
                Param("@hash",    normHash));

            observationId = LastInsertRowId(tx);
            var newObs = GetObservationTx(tx, observationId)!;
            EnqueueSyncMutation(tx, "observation", newObs.SyncId, "upsert", ObsPayload(newObs));
        });

        return Task.FromResult(observationId);
    }

    public Task<Observation?> GetObservationAsync(long id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = ObsSelect + " WHERE o.id = @id AND o.deleted_at IS NULL";
        cmd.Parameters.AddWithValue("@id", id);
        using var r = cmd.ExecuteReader();
        return Task.FromResult(r.Read() ? ReadObservation(r) : (Observation?)null);
    }

    public Task<IList<Observation>> RecentObservationsAsync(string? project, string? scope, int limit)
    {
        project = Normalizers.NormalizeProject(project);
        if (limit <= 0) limit = 20;

        var sql   = new StringBuilder(ObsSelect + " WHERE o.deleted_at IS NULL");
        var parms = new List<SqliteParameter>();

        if (!string.IsNullOrEmpty(project))
        {
            sql.Append(" AND o.project = @proj");
            parms.Add(Param("@proj", project));
        }
        if (!string.IsNullOrEmpty(scope))
        {
            sql.Append(" AND o.scope = @scope");
            parms.Add(Param("@scope", NormalizeScope(scope)));
        }

        sql.Append(" ORDER BY o.created_at DESC LIMIT @limit");
        parms.Add(Param("@limit", limit));

        return Task.FromResult<IList<Observation>>(QueryObservations(sql.ToString(), parms));
    }

    public Task<bool> UpdateObservationAsync(long id, UpdateObservationParams p)
    {
        bool updated = false;

        WithTx(tx =>
        {
            var obs = GetObservationTx(tx, id);
            if (obs is null) return;

            var typ      = p.Type    ?? obs.Type;
            var title    = p.Title   != null ? StripPrivateTags(p.Title) : obs.Title;
            var content  = p.Content != null ? StripPrivateTags(p.Content) : obs.Content;
            if (content.Length > _cfg.MaxObservationLength)
                content = content[.._cfg.MaxObservationLength] + "... [truncated]";

            var project  = p.Project  != null ? Normalizers.NormalizeProject(p.Project) : (obs.Project ?? "");
            var scope    = p.Scope    != null ? NormalizeScope(p.Scope) : obs.Scope;
            var topicKey = p.TopicKey != null ? Normalizers.NormalizeTopicKey(p.TopicKey) : (obs.TopicKey ?? "");

            Exec(tx,
                @"UPDATE observations
                  SET type = @type, title = @title, content = @content,
                      project = @proj, scope = @scope, topic_key = @tk,
                      normalized_hash = @hash,
                      revision_count = revision_count + 1,
                      updated_at = datetime('now')
                  WHERE id = @id AND deleted_at IS NULL",
                Param("@type",    typ),
                Param("@title",   title),
                Param("@content", content),
                Param("@proj",    NullableString(project)),
                Param("@scope",   scope),
                Param("@tk",      NullableString(topicKey)),
                Param("@hash",    Normalizers.HashNormalized(content)),
                Param("@id",      id));

            var fresh = GetObservationTx(tx, id);
            if (fresh is null) return;
            updated = true;
            EnqueueSyncMutation(tx, "observation", fresh.SyncId, "upsert", ObsPayload(fresh));
        });

        return Task.FromResult(updated);
    }

    public Task<bool> DeleteObservationAsync(long id)
    {
        bool deleted = false;

        WithTx(tx =>
        {
            var obs = GetObservationTx(tx, id);
            if (obs is null) return;

            Exec(tx,
                "UPDATE observations SET deleted_at = datetime('now'), updated_at = datetime('now') WHERE id = @id AND deleted_at IS NULL",
                Param("@id", id));

            deleted = true;
            EnqueueSyncMutation(tx, "observation", obs.SyncId, "delete", new
            {
                sync_id     = obs.SyncId,
                deleted     = true,
                deleted_at  = (string?)null,
                hard_delete = false,
            });
        });

        return Task.FromResult(deleted);
    }

    // ─── Search ────────────────────────────────────────────────────────────────

    public Task<IList<SearchResult>> SearchAsync(string query, SearchOptions opts)
    {
        opts = opts with { Project = Normalizers.NormalizeProject(opts.Project) };
        var limit = opts.Limit <= 0 ? 10 : opts.Limit;

        var results  = new List<SearchResult>();
        var seen     = new HashSet<long>();

        // ── Topic-key shortcut (when query contains '/') ──────────────────────
        if (query.Contains('/'))
        {
            var tkSql   = new StringBuilder(@"
                SELECT id, ifnull(sync_id,'') as sync_id, session_id, type, title, content, tool_name, project,
                       scope, topic_key, revision_count, duplicate_count, last_seen_at, created_at, updated_at, deleted_at
                FROM observations
                WHERE topic_key = @tk AND deleted_at IS NULL");
            var tkParms = new List<SqliteParameter> { Param("@tk", query) };

            AppendFilter(tkSql, tkParms, opts);
            tkSql.Append(" ORDER BY updated_at DESC LIMIT @limit");
            tkParms.Add(Param("@limit", limit));

            using var tkCmd = _db.CreateCommand();
            tkCmd.CommandText = tkSql.ToString();
            foreach (var pr in tkParms) tkCmd.Parameters.Add(pr);
            using var tkR = tkCmd.ExecuteReader();
            while (tkR.Read())
            {
                var obs = ReadObservation(tkR);
                results.Add(new SearchResult { Observation = obs, Rank = -1000 });
                seen.Add(obs.Id);
            }
        }

        // ── FTS5 ──────────────────────────────────────────────────────────────
        var ftsQuery = Normalizers.SanitizeFts5Query(query);
        var ftsSql   = new StringBuilder(@"
            SELECT o.id, ifnull(o.sync_id,'') as sync_id, o.session_id, o.type, o.title, o.content, o.tool_name, o.project,
                   o.scope, o.topic_key, o.revision_count, o.duplicate_count, o.last_seen_at, o.created_at, o.updated_at, o.deleted_at,
                   fts.rank
            FROM observations_fts fts
            JOIN observations o ON o.id = fts.rowid
            WHERE observations_fts MATCH @fts AND o.deleted_at IS NULL");
        var ftsParms = new List<SqliteParameter> { Param("@fts", ftsQuery) };

        AppendFilter(ftsSql, ftsParms, opts);
        ftsSql.Append(" ORDER BY fts.rank LIMIT @limit");
        ftsParms.Add(Param("@limit", limit));

        using var ftsCmd = _db.CreateCommand();
        ftsCmd.CommandText = ftsSql.ToString();
        foreach (var pr in ftsParms) ftsCmd.Parameters.Add(pr);

        using var ftsR = ftsCmd.ExecuteReader();
        while (ftsR.Read())
        {
            var obs  = ReadObservation(ftsR);
            var rank = ftsR.GetDouble(16);
            if (!seen.Contains(obs.Id))
                results.Add(new SearchResult { Observation = obs, Rank = rank });
        }

        if (results.Count > limit)
            results = results[..limit];

        return Task.FromResult<IList<SearchResult>>(results);
    }

    public Task<TimelineResult?> TimelineAsync(long observationId, int before, int after)
    {
        if (before <= 0) before = 5;
        if (after  <= 0) after  = 5;

        var focusObs = GetObservationDirect(observationId);
        if (focusObs is null) return Task.FromResult<TimelineResult?>(null);

        var session = GetSessionDirect(focusObs.SessionId);

        // Before entries (reverse order → flip to chronological)
        var beforeEntries = new List<TimelineEntry>();
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = TimelineEntrySelect +
                " WHERE session_id = @sess AND id < @anchor AND deleted_at IS NULL ORDER BY id DESC LIMIT @n";
            cmd.Parameters.AddWithValue("@sess",   focusObs.SessionId);
            cmd.Parameters.AddWithValue("@anchor", observationId);
            cmd.Parameters.AddWithValue("@n",      before);
            using var r = cmd.ExecuteReader();
            while (r.Read()) beforeEntries.Add(ReadTimelineEntry(r));
        }
        beforeEntries.Reverse();

        // After entries (chronological)
        var afterEntries = new List<TimelineEntry>();
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = TimelineEntrySelect +
                " WHERE session_id = @sess AND id > @anchor AND deleted_at IS NULL ORDER BY id ASC LIMIT @n";
            cmd.Parameters.AddWithValue("@sess",   focusObs.SessionId);
            cmd.Parameters.AddWithValue("@anchor", observationId);
            cmd.Parameters.AddWithValue("@n",      after);
            using var r = cmd.ExecuteReader();
            while (r.Read()) afterEntries.Add(ReadTimelineEntry(r));
        }

        int total = 0;
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM observations WHERE session_id = @sess AND deleted_at IS NULL";
            cmd.Parameters.AddWithValue("@sess", focusObs.SessionId);
            total = Convert.ToInt32(cmd.ExecuteScalar());
        }

        return Task.FromResult<TimelineResult?>(new TimelineResult
        {
            Focus        = focusObs,
            Before       = beforeEntries,
            After        = afterEntries,
            SessionInfo  = session,
            TotalInRange = total,
        });
    }

    // ─── Prompts ───────────────────────────────────────────────────────────────

    public Task<long> AddPromptAsync(AddPromptParams p)
    {
        p = p with { Project = Normalizers.NormalizeProject(p.Project) };

        var content = StripPrivateTags(p.Content);
        if (content.Length > _cfg.MaxObservationLength)
            content = content[.._cfg.MaxObservationLength] + "... [truncated]";

        long promptId = 0;

        WithTx(tx =>
        {
            var syncId = NewSyncId("prompt");
            Exec(tx,
                "INSERT INTO user_prompts (sync_id, session_id, content, project) VALUES (@sid, @sess, @content, @proj)",
                Param("@sid",     syncId),
                Param("@sess",    p.SessionId),
                Param("@content", content),
                Param("@proj",    NullableString(p.Project)));

            promptId = LastInsertRowId(tx);

            EnqueueSyncMutation(tx, "prompt", syncId, "upsert", new
            {
                sync_id    = syncId,
                session_id = p.SessionId,
                content,
                project    = NullableString(p.Project),
            });
        });

        return Task.FromResult(promptId);
    }

    public Task<IList<Prompt>> RecentPromptsAsync(string? project, int limit)
    {
        project = Normalizers.NormalizeProject(project);
        if (limit <= 0) limit = 20;

        var sql   = new StringBuilder("SELECT id, ifnull(sync_id,'') as sync_id, session_id, content, ifnull(project,'') as project, created_at FROM user_prompts");
        var parms = new List<SqliteParameter>();

        if (!string.IsNullOrEmpty(project))
        {
            sql.Append(" WHERE project = @proj");
            parms.Add(Param("@proj", project));
        }

        sql.Append(" ORDER BY created_at DESC LIMIT @limit");
        parms.Add(Param("@limit", limit));

        return Task.FromResult<IList<Prompt>>(QueryPrompts(sql.ToString(), parms));
    }

    public Task<IList<Prompt>> SearchPromptsAsync(string query, string? project, int limit)
    {
        if (limit <= 0) limit = 10;
        var ftsQuery = Normalizers.SanitizeFts5Query(query);

        var sql = new StringBuilder(@"
            SELECT p.id, ifnull(p.sync_id,'') as sync_id, p.session_id, p.content, ifnull(p.project,'') as project, p.created_at
            FROM prompts_fts fts
            JOIN user_prompts p ON p.id = fts.rowid
            WHERE prompts_fts MATCH @fts");
        var parms = new List<SqliteParameter> { Param("@fts", ftsQuery) };

        if (!string.IsNullOrEmpty(project))
        {
            sql.Append(" AND p.project = @proj");
            parms.Add(Param("@proj", project));
        }
        sql.Append(" ORDER BY fts.rank LIMIT @limit");
        parms.Add(Param("@limit", limit));

        return Task.FromResult<IList<Prompt>>(QueryPrompts(sql.ToString(), parms));
    }

    // ─── Context & Stats ───────────────────────────────────────────────────────

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

    public Task<Stats> StatsAsync()
    {
        var stats = new Stats();

        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM sessions";
            stats.TotalSessions = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM observations WHERE deleted_at IS NULL";
            stats.TotalObservations = Convert.ToInt32(cmd.ExecuteScalar());
        }
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM user_prompts";
            stats.TotalPrompts = Convert.ToInt32(cmd.ExecuteScalar());
        }

        var projects = new List<string>();
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT project FROM observations WHERE project IS NOT NULL AND deleted_at IS NULL GROUP BY project ORDER BY MAX(created_at) DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read()) projects.Add(r.GetString(0));
        }
        stats.Projects = projects;

        return Task.FromResult(stats);
    }

    // ─── Export / Import ───────────────────────────────────────────────────────

    public Task<ExportData> ExportAsync()
    {
        var data = new ExportData
        {
            Version    = "0.1.0",
            ExportedAt = UtcNow(),
        };

        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT id, project, directory, started_at, ended_at, summary FROM sessions ORDER BY started_at";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                data.Sessions.Add(new Session
                {
                    Id        = r.GetString(0),
                    Project   = r.GetString(1),
                    Directory = r.GetString(2),
                    StartedAt = r.GetString(3),
                    EndedAt   = r.IsDBNull(4) ? null : r.GetString(4),
                    Summary   = r.IsDBNull(5) ? null : r.GetString(5),
                });
        }

        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = ObsSelect + " ORDER BY o.id";
            using var r = cmd.ExecuteReader();
            while (r.Read()) data.Observations.Add(ReadObservation(r));
        }

        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT id, ifnull(sync_id,'') as sync_id, session_id, content, ifnull(project,'') as project, created_at FROM user_prompts ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read()) data.Prompts.Add(ReadPrompt(r));
        }

        return Task.FromResult(data);
    }

    public Task<ImportResult> ImportAsync(ExportData data)
    {
        var result = new ImportResult();

        WithTx(tx =>
        {
            foreach (var sess in data.Sessions)
            {
                var rows = ExecRows(tx,
                    "INSERT OR IGNORE INTO sessions (id, project, directory, started_at, ended_at, summary) VALUES (@id,@proj,@dir,@sat,@eat,@sum)",
                    Param("@id",  sess.Id),
                    Param("@proj", sess.Project),
                    Param("@dir",  sess.Directory),
                    Param("@sat",  sess.StartedAt),
                    Param("@eat",  (object?)sess.EndedAt ?? DBNull.Value),
                    Param("@sum",  (object?)sess.Summary ?? DBNull.Value));
                result.SessionsImported += (int)rows;
            }

            foreach (var obs in data.Observations)
            {
                var topicKey = Normalizers.NormalizeTopicKey(obs.TopicKey);
                var rows = ExecRows(tx,
                    @"INSERT OR IGNORE INTO observations
                        (sync_id, session_id, type, title, content, tool_name, project, scope, topic_key,
                         normalized_hash, revision_count, duplicate_count, last_seen_at, created_at, updated_at, deleted_at)
                      VALUES (@sid,@sess,@type,@title,@content,@tool,@proj,@scope,@tk,@hash,@rc,@dc,@lsa,@cat,@uat,@dat)",
                    Param("@sid",   NormalizeExistingSyncId(obs.SyncId, "obs")),
                    Param("@sess",  obs.SessionId),
                    Param("@type",  obs.Type),
                    Param("@title", obs.Title),
                    Param("@content", obs.Content),
                    Param("@tool",  (object?)obs.ToolName ?? DBNull.Value),
                    Param("@proj",  (object?)obs.Project  ?? DBNull.Value),
                    Param("@scope", NormalizeScope(obs.Scope)),
                    Param("@tk",    NullableString(topicKey) ?? (object)DBNull.Value),
                    Param("@hash",  Normalizers.HashNormalized(obs.Content)),
                    Param("@rc",    Math.Max(obs.RevisionCount,  1)),
                    Param("@dc",    Math.Max(obs.DuplicateCount, 1)),
                    Param("@lsa",   (object?)obs.LastSeenAt ?? DBNull.Value),
                    Param("@cat",   obs.CreatedAt),
                    Param("@uat",   obs.UpdatedAt),
                    Param("@dat",   (object?)obs.DeletedAt ?? DBNull.Value));
                result.ObservationsImported += (int)rows;
            }

            foreach (var p in data.Prompts)
            {
                var rows = ExecRows(tx,
                    "INSERT OR IGNORE INTO user_prompts (sync_id, session_id, content, project, created_at) VALUES (@sid,@sess,@content,@proj,@cat)",
                    Param("@sid",     NormalizeExistingSyncId(p.SyncId, "prompt")),
                    Param("@sess",    p.SessionId),
                    Param("@content", p.Content),
                    Param("@proj",    p.Project),
                    Param("@cat",     p.CreatedAt));
                result.PromptsImported += (int)rows;
            }
        });

        return Task.FromResult(result);
    }

    // ─── Projects ──────────────────────────────────────────────────────────────

    public Task<MergeResult> MergeProjectsAsync(IList<string> sources, string canonical)
    {
        canonical = Normalizers.NormalizeProject(canonical);
        if (string.IsNullOrEmpty(canonical))
            throw new ArgumentException("canonical project name must not be empty");

        var result = new MergeResult { Canonical = canonical };

        WithTx(tx =>
        {
            foreach (var rawSrc in sources)
            {
                var src = Normalizers.NormalizeProject(rawSrc);
                if (string.IsNullOrEmpty(src) || src == canonical) continue;

                result.ObservationsUpdated += ExecRows(tx, "UPDATE observations SET project = @can WHERE project = @src",
                    Param("@can", canonical), Param("@src", src));
                result.SessionsUpdated += ExecRows(tx, "UPDATE sessions SET project = @can WHERE project = @src",
                    Param("@can", canonical), Param("@src", src));
                result.PromptsUpdated += ExecRows(tx, "UPDATE user_prompts SET project = @can WHERE project = @src",
                    Param("@can", canonical), Param("@src", src));

                result.SourcesMerged.Add(src);
            }
        });

        return Task.FromResult(result);
    }

    // ─── Sync Chunks ───────────────────────────────────────────────────────────

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
        cmd.CommandText = "INSERT OR IGNORE INTO sync_chunks (chunk_id) VALUES (@id)";
        cmd.Parameters.AddWithValue("@id", chunkId);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Internal helpers
    // ═══════════════════════════════════════════════════════════════════════════

    // ─── SQL constants ─────────────────────────────────────────────────────────

    private const string ObsSelect = @"
        SELECT o.id, ifnull(o.sync_id,'') as sync_id, o.session_id, o.type, o.title, o.content, o.tool_name, o.project,
               o.scope, o.topic_key, o.revision_count, o.duplicate_count, o.last_seen_at, o.created_at, o.updated_at, o.deleted_at
        FROM observations o";

    private const string TimelineEntrySelect = @"
        SELECT id, session_id, type, title, content, tool_name, project,
               scope, topic_key, revision_count, duplicate_count, last_seen_at, created_at, updated_at, deleted_at
        FROM observations";

    // ─── Readers ───────────────────────────────────────────────────────────────

    private static Observation ReadObservation(SqliteDataReader r) => new()
    {
        Id             = r.GetInt64(0),
        SyncId         = r.GetString(1),
        SessionId      = r.GetString(2),
        Type           = r.GetString(3),
        Title          = r.GetString(4),
        Content        = r.GetString(5),
        ToolName       = r.IsDBNull(6)  ? null : r.GetString(6),
        Project        = r.IsDBNull(7)  ? null : r.GetString(7),
        Scope          = r.GetString(8),
        TopicKey       = r.IsDBNull(9)  ? null : r.GetString(9),
        RevisionCount  = r.GetInt32(10),
        DuplicateCount = r.GetInt32(11),
        LastSeenAt     = r.IsDBNull(12) ? null : r.GetString(12),
        CreatedAt      = r.GetString(13),
        UpdatedAt      = r.GetString(14),
        DeletedAt      = r.IsDBNull(15) ? null : r.GetString(15),
    };

    private static TimelineEntry ReadTimelineEntry(SqliteDataReader r) => new()
    {
        Id             = r.GetInt64(0),
        SessionId      = r.GetString(1),
        Type           = r.GetString(2),
        Title          = r.GetString(3),
        Content        = r.GetString(4),
        ToolName       = r.IsDBNull(5)  ? null : r.GetString(5),
        Project        = r.IsDBNull(6)  ? null : r.GetString(6),
        Scope          = r.GetString(7),
        TopicKey       = r.IsDBNull(8)  ? null : r.GetString(8),
        RevisionCount  = r.GetInt32(9),
        DuplicateCount = r.GetInt32(10),
        LastSeenAt     = r.IsDBNull(11) ? null : r.GetString(11),
        CreatedAt      = r.GetString(12),
        UpdatedAt      = r.GetString(13),
        DeletedAt      = r.IsDBNull(14) ? null : r.GetString(14),
    };

    private static Prompt ReadPrompt(SqliteDataReader r) => new()
    {
        Id        = r.GetInt64(0),
        SyncId    = r.GetString(1),
        SessionId = r.GetString(2),
        Content   = r.GetString(3),
        Project   = r.GetString(4),
        CreatedAt = r.GetString(5),
    };

    // ─── Query helpers ─────────────────────────────────────────────────────────

    private List<Observation> QueryObservations(string sql, IEnumerable<SqliteParameter> parms)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parms) cmd.Parameters.Add(p);
        using var r = cmd.ExecuteReader();
        var list = new List<Observation>();
        while (r.Read()) list.Add(ReadObservation(r));
        return list;
    }

    private List<Prompt> QueryPrompts(string sql, IEnumerable<SqliteParameter> parms)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var p in parms) cmd.Parameters.Add(p);
        using var r = cmd.ExecuteReader();
        var list = new List<Prompt>();
        while (r.Read()) list.Add(ReadPrompt(r));
        return list;
    }

    private Observation? GetObservationTx(SqliteTransaction tx, long id)
    {
        using var cmd = TxCmd(tx,
            ObsSelect + " WHERE o.id = @id AND o.deleted_at IS NULL",
            Param("@id", id));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadObservation(r) : null;
    }

    private Observation? GetObservationDirect(long id)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = ObsSelect + " WHERE o.id = @id AND o.deleted_at IS NULL";
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
        if (!r.Read()) return null;
        return new Session
        {
            Id        = r.GetString(0),
            Project   = r.GetString(1),
            Directory = r.GetString(2),
            StartedAt = r.GetString(3),
            EndedAt   = r.IsDBNull(4) ? null : r.GetString(4),
            Summary   = r.IsDBNull(5) ? null : r.GetString(5),
        };
    }

    private T? QueryScalar<T>(SqliteTransaction tx, string sql, params SqliteParameter[] parms)
    {
        using var cmd = TxCmd(tx, sql, parms);
        var result = cmd.ExecuteScalar();
        if (result is null || result == DBNull.Value) return default;
        return (T)Convert.ChangeType(result, typeof(T).IsGenericType
            ? Nullable.GetUnderlyingType(typeof(T))! : typeof(T));
    }

    private static void AppendFilter(StringBuilder sql, List<SqliteParameter> parms, SearchOptions opts)
    {
        if (!string.IsNullOrEmpty(opts.Type))
        {
            sql.Append(" AND o.type = @type");
            parms.Add(Param("@type", opts.Type));
        }
        if (!string.IsNullOrEmpty(opts.Project))
        {
            sql.Append(" AND o.project = @proj");
            parms.Add(Param("@proj", opts.Project));
        }
        if (!string.IsNullOrEmpty(opts.Scope))
        {
            sql.Append(" AND o.scope = @scope");
            parms.Add(Param("@scope", NormalizeScope(opts.Scope)));
        }
    }

    // ─── Exec helpers ──────────────────────────────────────────────────────────

    private void Exec(string sql)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void Exec(SqliteTransaction tx, string sql, params SqliteParameter[] parms)
    {
        using var cmd = TxCmd(tx, sql, parms);
        cmd.ExecuteNonQuery();
    }

    private long ExecRows(SqliteTransaction tx, string sql, params SqliteParameter[] parms)
    {
        using var cmd = TxCmd(tx, sql, parms);
        cmd.ExecuteNonQuery();
        using var rowCmd = _db.CreateCommand();
        rowCmd.Transaction = tx;
        rowCmd.CommandText = "SELECT changes()";
        return Convert.ToInt64(rowCmd.ExecuteScalar());
    }

    private long LastInsertRowId(SqliteTransaction tx)
    {
        using var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private SqliteCommand TxCmd(SqliteTransaction tx, string sql, params SqliteParameter[] parms)
    {
        var cmd = _db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var p in parms) cmd.Parameters.Add(p);
        return cmd;
    }

    private void WithTx(Action<SqliteTransaction> fn)
    {
        using var tx = _db.BeginTransaction();
        fn(tx);
        tx.Commit();
    }

    // ─── Sync mutation journal ─────────────────────────────────────────────────

    private void EnqueueSyncMutation(SqliteTransaction tx, string entity, string entityKey, string op, object payload)
    {
        var encoded = JsonSerializer.Serialize(payload);
        var project = ExtractProjectFromPayload(payload);

        Exec(tx,
            "INSERT OR IGNORE INTO sync_state (target_key, lifecycle, updated_at) VALUES ('cloud', 'idle', datetime('now'))");

        Exec(tx,
            "INSERT INTO sync_mutations (target_key, entity, entity_key, op, payload, source, project) VALUES ('cloud', @ent, @key, @op, @payload, 'local', @proj)",
            Param("@ent",     entity),
            Param("@key",     entityKey),
            Param("@op",      op),
            Param("@payload", encoded),
            Param("@proj",    project));

        var seq = LastInsertRowId(tx);

        Exec(tx,
            "UPDATE sync_state SET lifecycle = 'pending', last_enqueued_seq = @seq, updated_at = datetime('now') WHERE target_key = 'cloud'",
            Param("@seq", seq));
    }

    private static string ExtractProjectFromPayload(object payload)
    {
        if (payload is null) return "";
        // Serialize and extract $.project from JSON
        var json = JsonSerializer.Serialize(payload);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("project", out var proj))
        {
            if (proj.ValueKind == JsonValueKind.String)
                return proj.GetString() ?? "";
        }
        return "";
    }

    private static object ObsPayload(Observation obs) => new
    {
        sync_id    = obs.SyncId,
        session_id = obs.SessionId,
        type       = obs.Type,
        title      = obs.Title,
        content    = obs.Content,
        tool_name  = obs.ToolName,
        project    = obs.Project,
        scope      = obs.Scope,
        topic_key  = obs.TopicKey,
    };

    // ─── Migration helpers ─────────────────────────────────────────────────────

    private void AddColumnIfNotExists(string table, string column, string definition)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            if (r.GetString(1) == column) return; // col 1 = name
        }
        using var alter = _db.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    private bool TriggerExists(string name)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='trigger' AND name=@n";
        cmd.Parameters.AddWithValue("@n", name);
        return cmd.ExecuteScalar() is not null;
    }

    // ─── Static helpers ────────────────────────────────────────────────────────

    private static SqliteParameter Param(string name, object? value) =>
        new(name, value ?? DBNull.Value);

    private static string? NullableString(string? s) =>
        string.IsNullOrEmpty(s) ? null : s;

    private static string NormalizeScope(string? scope)
    {
        var v = (scope ?? "").Trim().ToLowerInvariant();
        return v == "personal" ? "personal" : "project";
    }

    private static string NewSyncId(string prefix)
    {
        var bytes = new byte[8];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return prefix + "-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string NormalizeExistingSyncId(string? existing, string prefix) =>
        !string.IsNullOrWhiteSpace(existing) ? existing : NewSyncId(prefix);

    // Matches <private>...</private> (multiline, case-insensitive)
    private static readonly Regex PrivateTagRegex =
        new(@"(?is)<private>.*?</private>", RegexOptions.Compiled);

    private static string StripPrivateTags(string s)
    {
        var result = PrivateTagRegex.Replace(s, "[REDACTED]");
        return result.Trim();
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max) return s;
        return s[..max] + "...";
    }

    private static string UtcNow() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
}
