using System.Linq;
using System.Text;
using System.Text.Json;
using Polly;
using Polly.Retry;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Engram.Store;

/// <summary>
/// SQLite-backed implementation of IStore.
/// Schema, dedup logic, and FTS5 behaviour are intentionally identical to the Go original.
/// Implements ILocalSyncStore for offline-first-sync (Phase 1.4).
/// </summary>
public sealed class SqliteStore : IStore, ILocalSyncStore
{
    private readonly SqliteConnection _db;
    private readonly StoreConfig _cfg;
    private readonly ILogger<SqliteStore>? _logger;

    // Retry policy for SQLITE_BUSY (error code 5).
    // Works alongside PRAGMA busy_timeout=5000 — catches cases where
    // the internal SQLite retry window expires.
    private static readonly ResiliencePipeline SqliteRetry = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<SqliteException>(e => e.SqliteErrorCode == 5),
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(100),
            BackoffType = DelayBackoffType.Exponential,
        })
        .Build();

    // JSON options with snake_case matching (mutation payloads use snake_case)
    private static readonly JsonSerializerOptions JsonPullOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    // ─── Construction ──────────────────────────────────────────────────────────

    public SqliteStore(StoreConfig cfg) : this(cfg, null) { }

    public SqliteStore(StoreConfig cfg, ILogger<SqliteStore>? logger)
    {
        _cfg = cfg;
        _logger = logger;

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
    public string BackendName => "sqlite";

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
                review_after   TEXT,
                expires_at    TEXT,
                embedding     BLOB,
                embedding_model TEXT,
                embedding_created_at TEXT,
                md_path        TEXT,
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
                created_by TEXT,
                created_at TEXT    NOT NULL DEFAULT (datetime('now')),
                FOREIGN KEY (session_id) REFERENCES sessions(id)
            );

            CREATE INDEX IF NOT EXISTS idx_prompts_session ON user_prompts(session_id);
            CREATE INDEX IF NOT EXISTS idx_prompts_project ON user_prompts(project);
            CREATE INDEX IF NOT EXISTS idx_prompts_created_by ON user_prompts(created_by);
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
        // Upstream parity v1.14: reserved for future decay, expiration, and vector search
        AddColumnIfNotExists("observations", "review_after",          "TEXT");
        AddColumnIfNotExists("observations", "expires_at",            "TEXT");
        AddColumnIfNotExists("observations", "embedding",             "BLOB");
        AddColumnIfNotExists("observations", "embedding_model",       "TEXT");
        AddColumnIfNotExists("observations", "embedding_created_at",  "TEXT");
        AddColumnIfNotExists("observations", "md_path",               "TEXT");
        AddColumnIfNotExists("user_prompts", "sync_id",         "TEXT");
        AddColumnIfNotExists("user_prompts", "deleted_at",      "TEXT");
        AddColumnIfNotExists("user_prompts", "created_by",      "TEXT");

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

        Exec(@"
            CREATE TABLE IF NOT EXISTS project_migrations (
                from_project TEXT PRIMARY KEY,
                to_project   TEXT NOT NULL,
                migrated_at  TEXT NOT NULL DEFAULT (datetime('now'))
            );
        ");

        // ─── sync_apply_deferred (offline-first-sync Phase 1.4) ─────────────────
        // Note: CREATE TABLE IF NOT EXISTS fails if table exists with different schema.
        // We use a two-phase approach: first create with base columns, then add optional ones.

        // Phase 1: Create table if not exists (base columns only - these always existed)
        Exec(@"
            CREATE TABLE IF NOT EXISTS sync_apply_deferred (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                entity      TEXT NOT NULL,
                entity_key  TEXT NOT NULL,
                op          TEXT NOT NULL,
                payload     TEXT NOT NULL,
                source      TEXT NOT NULL DEFAULT 'pull',
                pulled_at   TEXT NOT NULL DEFAULT (datetime('now'))
            );
        ");

        // Phase 2: Add optional columns (for schema drift) + create index
        // These columns were added later and may be missing in old schemas
        AddColumnIfNotExists("sync_apply_deferred", "retry_count", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfNotExists("sync_apply_deferred", "last_error",  "TEXT");

        // Create index for retry tracking
        Exec("CREATE INDEX IF NOT EXISTS idx_sync_apply_deferred_retry ON sync_apply_deferred(retry_count)");

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

    // ─── Delete Session ────────────────────────────────────────────────────────

    public Task DeleteSessionAsync(string id)
    {
        Exec("PRAGMA foreign_keys = OFF");
        try
        {
            WithTx(tx =>
            {
                // 1. Verify session exists
                var sessionExists = QueryScalar<long?>(tx,
                    "SELECT 1 FROM sessions WHERE id = @id",
                    Param("@id", id));
                if (!sessionExists.HasValue)
                    throw new SessionNotFoundException(id);

                // 2. Count only active (non-soft-deleted) observations
                var totalObs = QueryScalar<long>(tx,
                    "SELECT COUNT(*) FROM observations WHERE session_id = @id AND deleted_at IS NULL",
                    Param("@id", id));
                if (totalObs > 0)
                    throw new SessionDeleteBlockedException(id, (int)totalObs);

                // 3. Soft-delete associated prompts so RecentPrompts ignores them
                Exec(tx,
                    "UPDATE user_prompts SET deleted_at = datetime('now') WHERE session_id = @id AND deleted_at IS NULL",
                    Param("@id", id));

                // 4. Hard-delete the session
                var rows = ExecRows(tx,
                    "DELETE FROM sessions WHERE id = @id",
                    Param("@id", id));

                // If FK constraint blocked the delete (race condition), treat as blocked
                if (rows == 0)
                    throw new SessionDeleteBlockedException(id, -1);
            });
        }
        finally
        {
            Exec("PRAGMA foreign_keys = ON");
        }

        return Task.CompletedTask;
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
                       scope, topic_key, revision_count, duplicate_count, last_seen_at, created_at, updated_at, deleted_at,
                       md_path
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
                   o.md_path, fts.rank
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
            var rank = ftsR.GetDouble(17);
            if (!seen.Contains(obs.Id))
                results.Add(new SearchResult { Observation = obs, Rank = rank });
        }

        if (results.Count > limit)
            results = results[..limit];

        // Phase 3: enrich results with project redirect info
        // if (results.Count > 0) { /* lookup project_migrations per result.Observation.Project */ }

        return Task.FromResult<IList<SearchResult>>(results);
    }

    /// <summary>
    /// Cross-namespace search: runs SearchAsync for each project namespace and merges results.
    /// Deduplicates by observation ID; ranks topic-key hits first, then FTS5 by rank.
    /// </summary>
    public async Task<IList<SearchResult>> SearchAsync(string query, IList<string> projects, SearchOptions opts)
    {
        if (projects.Count == 0) return await SearchAsync(query, opts);

        var seen    = new HashSet<long>();
        var merged  = new List<SearchResult>();
        var limit   = opts.Limit <= 0 ? 10 : opts.Limit;

        foreach (var proj in projects)
        {
            var perProjectOpts = opts with { Project = proj };
            var results = await SearchAsync(query, perProjectOpts);
            foreach (var r in results)
            {
                if (seen.Add(r.Observation.Id))
                    merged.Add(r);
            }
        }

        // Sort: topic-key hits (rank == -1000) first, then by FTS5 rank ascending (negative = better)
        merged.Sort((a, b) =>
        {
            if (a.Rank == -1000 && b.Rank != -1000) return -1;
            if (b.Rank == -1000 && a.Rank != -1000) return 1;
            return a.Rank.CompareTo(b.Rank);
        });

        if (merged.Count > limit) merged = merged[..limit];
        return merged;
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
                "INSERT INTO user_prompts (sync_id, session_id, content, project, created_by) VALUES (@sid, @sess, @content, @proj, @createdBy)",
                Param("@sid",     syncId),
                Param("@sess",    p.SessionId),
                Param("@content", content),
                Param("@proj",   NullableString(p.Project)),
                Param("@createdBy", NullableString(p.CreatedBy)));

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

    public Task<IList<Prompt>> RecentPromptsAsync(string? project, string? userId, int limit)
    {
        project = Normalizers.NormalizeProject(project);
        if (limit <= 0) limit = 20;

        var sql   = new StringBuilder("SELECT id, ifnull(sync_id,'') as sync_id, session_id, content, ifnull(project,'') as project, ifnull(created_by,'') as created_by, created_at FROM user_prompts WHERE deleted_at IS NULL");
        var parms = new List<SqliteParameter>();

        if (!string.IsNullOrEmpty(userId))
        {
            sql.Append(" AND created_by = @userId");
            parms.Add(Param("@userId", userId));
        }

        if (!string.IsNullOrEmpty(project))
        {
            sql.Append(" AND project = @proj");
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
            WHERE prompts_fts MATCH @fts AND p.deleted_at IS NULL");
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

    public Task DeletePromptAsync(long id)
    {
        WithTx(tx =>
        {
            // 1. Verify prompt exists
            var promptExists = QueryScalar<long?>(tx,
                "SELECT 1 FROM user_prompts WHERE id = @id",
                Param("@id", id));
            if (!promptExists.HasValue)
                throw new PromptNotFoundException(id);

            // 2. Soft-delete: set deleted_at
            Exec(tx,
                "UPDATE user_prompts SET deleted_at = datetime('now') WHERE id = @id AND deleted_at IS NULL",
                Param("@id", id));
        });

        return Task.CompletedTask;
    }

    // ─── Context & Stats ───────────────────────────────────────────────────────

    public async Task<string> FormatContextAsync(string? project, string? scope)
    {
        var sessions     = await RecentSessionsAsync(project, 5);
        var observations = await RecentObservationsAsync(project, scope, 20);
        var prompts      = await RecentPromptsAsync(project, null, 10);

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

    /// <summary>
    /// Wide-read overload: merges observations from all provided project namespaces.
    /// Used by mem_context to read both "team/{project}" and "{user}/{project}" simultaneously.
    /// Observations are labelled [team] or [personal] based on the project prefix.
    /// </summary>
    public async Task<string> FormatContextAsync(IList<string> projects, string? scope)
    {
        if (projects.Count == 0) return string.Empty;

        // Sessions: use first project (typically the team one) for recent sessions
        var sessions = await RecentSessionsAsync(projects[0], 5);

        // Observations: gather from all projects and merge by updated_at DESC
        var allObservations = new List<Observation>();
        foreach (var proj in projects)
        {
            var obs = await RecentObservationsAsync(proj, scope, 20);
            allObservations.AddRange(obs);
        }

        // Sort merged list by updated_at DESC, take top 20
        var mergedObservations = allObservations
            .OrderByDescending(o => o.UpdatedAt)
            .Take(20)
            .ToList();

        // Prompts: gather from all projects, merge and deduplicate
        var allPrompts = new List<Prompt>();
        foreach (var proj in projects)
        {
            var p = await RecentPromptsAsync(proj, null, 10);
            allPrompts.AddRange(p);
        }
        var mergedPrompts = allPrompts
            .OrderByDescending(p => p.CreatedAt)
            .Take(10)
            .ToList();

        if (sessions.Count == 0 && mergedObservations.Count == 0 && mergedPrompts.Count == 0)
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

        if (mergedPrompts.Count > 0)
        {
            sb.Append("### Recent User Prompts\n");
            foreach (var p in mergedPrompts)
                sb.AppendLine($"- {p.CreatedAt}: {Truncate(p.Content, 200)}");
            sb.AppendLine();
        }

        if (mergedObservations.Count > 0)
        {
            sb.Append("### Recent Observations\n");
            foreach (var o in mergedObservations)
            {
                var label = o.Project?.StartsWith("team/") == true ? "[team]" : "[personal]";
                sb.AppendLine($"- {label} [{o.Type}] **{o.Title}**: {Truncate(o.Content, 300)}");
            }
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
            cmd.CommandText = "SELECT COUNT(*) FROM user_prompts WHERE deleted_at IS NULL";
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

    // ─── Retention ─────────────────────────────────────────────────────

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
            new() { Label = "< 30 days", Count = CountObs("datetime(created_at) >= datetime('now', '-30 days')") },
            new() { Label = "30-90 days", Count = CountObs("datetime(created_at) >= datetime('now', '-90 days') AND datetime(created_at) < datetime('now', '-30 days')") },
            new() { Label = "90-180 days", Count = CountObs("datetime(created_at) >= datetime('now', '-180 days') AND datetime(created_at) < datetime('now', '-90 days')") },
            new() { Label = "180-365 days", Count = CountObs("datetime(created_at) >= datetime('now', '-365 days') AND datetime(created_at) < datetime('now', '-180 days')") },
            new() { Label = "> 365 days", Count = CountObs("datetime(created_at) < datetime('now', '-365 days')") },
        };

        // Count without topic_key in last 90d
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM observations WHERE deleted_at IS NULL AND (topic_key IS NULL OR topic_key = '') AND datetime(created_at) >= datetime('now', '-90 days')";
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
            countCmd.CommandText = "SELECT COUNT(*) FROM observations WHERE type = @type AND deleted_at IS NULL AND (topic_key IS NULL OR topic_key = '') AND datetime(created_at) < datetime(@cutoff)";
            countCmd.Parameters.AddWithValue("@type", type);
            countCmd.Parameters.AddWithValue("@cutoff", cutoffStr);
            var count = Convert.ToInt32(countCmd.ExecuteScalar());

            if (!p.DryRun && count > 0)
            {
                using var delCmd = _db.CreateCommand();
                delCmd.CommandText = "UPDATE observations SET deleted_at = datetime('now'), updated_at = datetime('now') WHERE type = @type AND deleted_at IS NULL AND (topic_key IS NULL OR topic_key = '') AND datetime(created_at) < datetime(@cutoff)";
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
        cmd.CommandText = "INSERT OR REPLACE INTO project_migrations (from_project, to_project, migrated_at) VALUES (@from, @to, datetime('now'))";
        cmd.Parameters.Add(Param("@from", fromProject));
        cmd.Parameters.Add(Param("@to", toProject));
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

    // ─── Export / Import ───────────────────────────────────────────────────────

    public Task<ExportData> ExportAsync()
    {
        var data = new ExportData
        {
            Version    = "1.1.0",
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
            cmd.CommandText = "SELECT id, ifnull(sync_id,'') as sync_id, session_id, content, ifnull(project,'') as project, created_at FROM user_prompts WHERE deleted_at IS NULL ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read()) data.Prompts.Add(ReadPrompt(r));
        }

        return Task.FromResult(data);
    }

    // ─── Export by Project (ENG-208 Phase 7) ────────────────────────────────

    public async Task<ExportData> ExportProjectAsync(string project)
    {
        var data = new ExportData
        {
            Version    = "1.1.0",
            ExportedAt = UtcNow(),
        };

        // Sessions filtered by project
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT id, project, directory, started_at, ended_at, summary FROM sessions WHERE project = @proj ORDER BY started_at";
            cmd.Parameters.AddWithValue("@proj", project);
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

        // Observations filtered by project - need full projection for ReadObservation
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT o.id, ifnull(o.sync_id,'') as sync_id, o.session_id, o.type, o.title, o.content,
                       ifnull(o.tool_name,'') as tool_name, ifnull(o.project,'') as project,
                       ifnull(o.scope,'project') as scope, ifnull(o.topic_key,'') as topic_key,
                       o.revision_count, o.duplicate_count, ifnull(o.last_seen_at,'') as last_seen_at,
                       o.created_at, o.updated_at, ifnull(o.deleted_at,'') as deleted_at,
                       ifnull(o.md_path,'') as md_path
                FROM observations o
                WHERE o.project = @proj AND o.deleted_at IS NULL
                ORDER BY o.id";
            cmd.Parameters.AddWithValue("@proj", project);
            using var r = cmd.ExecuteReader();
            while (r.Read()) data.Observations.Add(ReadObservation(r));
        }

        // Prompts filtered by project
        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = "SELECT id, ifnull(sync_id,'') as sync_id, session_id, content, ifnull(project,'') as project, created_at FROM user_prompts WHERE project = @proj AND deleted_at IS NULL ORDER BY id";
            cmd.Parameters.AddWithValue("@proj", project);
            using var r = cmd.ExecuteReader();
            while (r.Read()) data.Prompts.Add(ReadPrompt(r));
        }

        return data;
    }

    // ─── Incremental Export via mutation_seq (ENG-208 Phase 6) ─────────────
    // Note: SqliteStore uses observation/prompt IDs as cursor (not mutation_seq).
    // Sessions are excluded from incremental export (TEXT id, no numeric sequence).

    public async Task<ExportData> ExportSinceAsync(string? project, long afterSeq, int limit)
    {
        var data = new ExportData { Version = "1.1.0", ExportedAt = UtcNow() };

        // Query observations with id > afterSeq (filtered by project if provided)
        // Must include all 17 columns to match ReadObservation
        var obsSql = @"
            SELECT id, ifnull(sync_id,'') as sync_id, session_id, type, title, content,
                   ifnull(tool_name,'') as tool_name, ifnull(project,'') as project,
                   ifnull(scope,'project') as scope, ifnull(topic_key,'') as topic_key,
                   revision_count, duplicate_count, ifnull(last_seen_at,'') as last_seen_at,
                   created_at, updated_at, ifnull(deleted_at,'') as deleted_at,
                   ifnull(md_path,'') as md_path
            FROM observations
            WHERE id > @afterSeq";
        
        var prms = new List<SqliteParameter> { Param("@afterSeq", afterSeq) };
        if (!string.IsNullOrEmpty(project))
        {
            obsSql += " AND project = @project";
            prms.Add(Param("@project", project));
        }
        obsSql += " ORDER BY id LIMIT @limit";
        prms.Add(Param("@limit", limit + 1)); // Fetch extra to detect has_more

        using (var cmd = _db.CreateCommand())
        {
            cmd.CommandText = obsSql;
            foreach (var p in prms) cmd.Parameters.Add(p);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                data.Observations.Add(ReadObservation(r));
            }
        }

        // Determine has_more and next_seq from observations
        bool hasMore = data.Observations.Count > limit;
        if (hasMore)
        {
            data.Observations.RemoveAt(data.Observations.Count - 1);
        }
        data.NextSeq = data.Observations.Count > 0 ? data.Observations[^1].Id : afterSeq;
        data.HasMore = hasMore;

        // Query prompts (same pattern)
        if (data.Observations.Count < limit) // Only fetch prompts if we have room
        {
            var promptLimit = limit - data.Observations.Count + 1;
            var promptSql = @"
                SELECT id, ifnull(sync_id,'') as sync_id, session_id, content,
                       ifnull(project,'') as project, created_at
                FROM user_prompts
                WHERE id > @afterSeq";
            
            prms = [Param("@afterSeq", afterSeq)];
            if (!string.IsNullOrEmpty(project))
            {
                promptSql += " AND project = @project";
                prms.Add(Param("@project", project));
            }
            promptSql += " ORDER BY id LIMIT @limit";
            prms.Add(Param("@limit", promptLimit + 1));

            using (var cmd = _db.CreateCommand())
            {
                cmd.CommandText = promptSql;
                foreach (var p in prms) cmd.Parameters.Add(p);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    data.Prompts.Add(ReadPrompt(r));
                }
            }

            // Update has_more based on prompts too
            if (data.Prompts.Count > promptLimit - 1)
            {
                data.Prompts.RemoveAt(data.Prompts.Count - 1);
                data.HasMore = true;
            }
            
            // Update next_seq to be the max of obs and prompt cursors
            if (data.Prompts.Count > 0)
            {
                data.NextSeq = Math.Max(data.NextSeq, data.Prompts[^1].Id);
            }
        }

        // Sessions are excluded (TEXT id, no numeric sequence for cursor)
        data.Sessions = [];

        return data;
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

    // ─── MD Promotion ──────────────────────────────────────────────────────────

    public Task<long> PromoteToMdAsync(long observationId, string mdDir)
    {
        var obs = GetObservationDirect(observationId);
        if (obs is null) return Task.FromResult(0L);

        // Already promoted — skip
        if (!string.IsNullOrEmpty(obs.MdPath))
            return Task.FromResult(0L);

        // Parse the ISO date for the filename prefix
        var date = DateTime.TryParse(obs.CreatedAt, out var parsed)
            ? parsed
            : DateTime.UtcNow;

        var filename = ToFilename(obs.Title, date);
        var fullPath = Path.Combine(mdDir, filename);

        Directory.CreateDirectory(mdDir);
        File.WriteAllText(fullPath, RenderMdContent(obs));

        // Persist the relative path (filename only, not the full mdDir)
        using var upd = _db.CreateCommand();
        upd.CommandText = "UPDATE observations SET md_path = @path, updated_at = datetime('now') WHERE id = @id";
        upd.Parameters.Add(Param("@path", filename));
        upd.Parameters.Add(Param("@id", observationId));
        upd.ExecuteNonQuery();

        return Task.FromResult(observationId);
    }

    public async Task<int> SyncMdToRepoAsync(string mdDir, bool dryRun)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT id, title, content, type, created_at, topic_key
            FROM observations
            WHERE deleted_at IS NULL AND (md_path IS NULL OR md_path = '')
            ORDER BY created_at DESC LIMIT 100";
        using var r = cmd.ExecuteReader();
        var ids = new List<long>();
        while (r.Read()) ids.Add(r.GetInt64(0));
        r.Close();

        if (dryRun) return ids.Count;

        int promoted = 0;
        foreach (var id in ids)
        {
            var result = await PromoteToMdAsync(id, mdDir);
            if (result > 0) promoted++;
        }

        return promoted;
    }

    public Task<string> GenerateIndexAsync(string mdDir)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT o.id, ifnull(o.sync_id,''), o.session_id, o.type, o.title, o.content,
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

    public Task<MigrationResult> MigrateProjectAsync(string fromProject, string toProject)
    {
        fromProject = Normalizers.NormalizeProject(fromProject);
        toProject = Normalizers.NormalizeProject(toProject);
        if (string.IsNullOrEmpty(fromProject))
            throw new ArgumentException("from project name must not be empty");
        if (string.IsNullOrEmpty(toProject))
            throw new ArgumentException("to project name must not be empty");
        if (fromProject == toProject)
            throw new ArgumentException("from and to project names must be different");

        var result = new MigrationResult { FromProject = fromProject, ToProject = toProject };

        WithTx(tx =>
        {
            result.ObservationsMigrated = ExecRows(tx, "UPDATE observations SET project = @to WHERE project = @from",
                Param("@to", toProject), Param("@from", fromProject));
            result.SessionsMigrated = ExecRows(tx, "UPDATE sessions SET project = @to WHERE project = @from",
                Param("@to", toProject), Param("@from", fromProject));
            result.PromptsMigrated = ExecRows(tx, "UPDATE user_prompts SET project = @to WHERE project = @from",
                Param("@to", toProject), Param("@from", fromProject));
        });

        // Record the migration for audit trail
        AddProjectMigrationAsync(fromProject, toProject).GetAwaiter().GetResult();

        return Task.FromResult(result);
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

        // Directories per project (unique directory values from sessions)
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
                SELECT ifnull(project,'') as project, COUNT(*) as cnt
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
                // Don't add projects that only have prompts — unlikely and not useful
            }
        }

        var results = statsMap.Values
            .OrderByDescending(s => s.ObservationCount)
            .ToList();

        return Task.FromResult<IList<ProjectStats>>(results);
    }

    public Task<int> CountObservationsForProjectAsync(string project)
    {
        return Task.FromResult(CountObservationsForProjectCore(Normalizers.NormalizeProject(project)));
    }

    private int CountObservationsForProjectCore(string project)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM observations WHERE project = @proj AND deleted_at IS NULL";
        cmd.Parameters.AddWithValue("@proj", project);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public Task<PruneResult> PruneProjectAsync(string project)
    {
        project = Normalizers.NormalizeProject(project);

        // Safety check: refuse to prune if observations exist for this project
        var obsCount = CountObservationsForProjectCore(project);
        if (obsCount > 0)
            throw new InvalidOperationException(
                $"Project \"{project}\" still has {obsCount} observations — cannot prune. " +
                "Merge or delete observations first.");

        long sessionsDeleted = 0;
        long promptsDeleted = 0;

        WithTx(tx =>
        {
            // Delete prompts first (they have FK → sessions)
            using var delPrompts = _db.CreateCommand();
            delPrompts.Transaction = tx;
            delPrompts.CommandText = "DELETE FROM user_prompts WHERE project = @proj";
            delPrompts.Parameters.AddWithValue("@proj", project);
            promptsDeleted = delPrompts.ExecuteNonQuery();

            // Then delete sessions (FK → sessions from prompts already removed)
            using var delSessions = _db.CreateCommand();
            delSessions.Transaction = tx;
            delSessions.CommandText = "DELETE FROM sessions WHERE project = @proj";
            delSessions.Parameters.AddWithValue("@proj", project);
            sessionsDeleted = delSessions.ExecuteNonQuery();
        });

        return Task.FromResult(new PruneResult
        {
            Project = project,
            SessionsDeleted = sessionsDeleted,
            PromptsDeleted = promptsDeleted,
        });
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
    // ILocalSyncStore Implementation (offline-first-sync Phase 1.4)
    // ═══════════════════════════════════════════════════════════════════════════

    public Task<SyncState?> GetSyncStateAsync(string targetKey, CancellationToken ct = default)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT target_key, lifecycle, last_enqueued_seq, last_acked_seq, last_pulled_seq,
                   consecutive_failures, backoff_until, lease_owner, lease_until, last_error, updated_at
            FROM sync_state WHERE target_key = @target";
        cmd.Parameters.AddWithValue("@target", targetKey);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return Task.FromResult<SyncState?>(null);

        return Task.FromResult<SyncState?>(new SyncState(
            r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4),
            r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? null : r.GetString(7),
            r.IsDBNull(8) ? null : r.GetString(8),
            r.IsDBNull(9) ? null : r.GetString(9),
            DateTime.Parse(r.GetString(10))
        ));
    }

    public Task<List<SyncMutation>> ListPendingSyncMutationsAsync(string targetKey, int limit, CancellationToken ct = default)
    {
        var list = new List<SyncMutation>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT seq, target_key, entity, entity_key, op, payload, source, project, occurred_at, acked_at
            FROM sync_mutations
            WHERE target_key = @target AND acked_at IS NULL
            ORDER BY seq ASC LIMIT @limit";
        cmd.Parameters.AddWithValue("@target", targetKey);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new SyncMutation(
                r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
                DateTime.Parse(r.GetString(8)), r.IsDBNull(9) ? null : r.GetString(9)
            ));
        }
        return Task.FromResult(list);
    }

    public Task<List<PendingProjectCount>> CountPendingNonEnrolledAsync(string targetKey, CancellationToken ct = default)
    {
        var list = new List<PendingProjectCount>();
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT sm.project, COUNT(*) as count
            FROM sync_mutations sm
            LEFT JOIN sync_enrolled_projects ep ON sm.project = ep.project
            WHERE sm.target_key = @target AND sm.acked_at IS NULL AND ep.project IS NULL
            GROUP BY sm.project";
        cmd.Parameters.AddWithValue("@target", targetKey);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new PendingProjectCount(r.GetString(0), r.GetInt64(1)));
        }
        return Task.FromResult(list);
    }

    public Task EnrollProjectLocalAsync(string project)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO sync_enrolled_projects (project) VALUES (@project)";
        cmd.Parameters.AddWithValue("@project", project);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task UnenrollProjectLocalAsync(string project)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = "DELETE FROM sync_enrolled_projects WHERE project = @project";
        cmd.Parameters.AddWithValue("@project", project);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task AckSyncMutationSeqsAsync(string targetKey, IReadOnlyList<long> seqs, CancellationToken ct = default)
    {
        if (seqs.Count == 0) return Task.CompletedTask;

        using var cmd = _db.CreateCommand();
        var seqParams = string.Join(",", seqs.Select((s, i) => $"@seq{i}"));
        for (int i = 0; i < seqs.Count; i++)
            cmd.Parameters.AddWithValue($"@seq{i}", seqs[i]);

        cmd.CommandText = $"UPDATE sync_mutations SET acked_at = datetime('now') WHERE target_key = @target AND seq IN ({seqParams})";
        cmd.Parameters.AddWithValue("@target", targetKey);
        cmd.ExecuteNonQuery();

        // Update cursor: advance last_acked_seq to the max of accepted seqs
        var maxSeq = seqs.Max();
        using var updateCmd = _db.CreateCommand();
        updateCmd.CommandText = @"
            UPDATE sync_state SET last_acked_seq = MAX(last_acked_seq, @maxSeq), updated_at = datetime('now')
            WHERE target_key = @target";
        updateCmd.Parameters.AddWithValue("@target", targetKey);
        updateCmd.Parameters.AddWithValue("@maxSeq", maxSeq);
        updateCmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    public Task UpdateSyncStateAsync(string targetKey, long lastPulledSeq, CancellationToken ct = default)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            UPDATE sync_state SET last_pulled_seq = MAX(last_pulled_seq, @seq), updated_at = datetime('now')
            WHERE target_key = @target";
        cmd.Parameters.AddWithValue("@target", targetKey);
        cmd.Parameters.AddWithValue("@seq", lastPulledSeq);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task<bool> AcquireSyncLeaseAsync(string targetKey, string owner, TimeSpan ttl, CancellationToken ct = default)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            UPDATE sync_state SET
                lease_owner = @owner,
                lease_until = datetime('now', '+' || @seconds || ' seconds'),
                updated_at = datetime('now')
            WHERE target_key = @target AND (lease_until IS NULL OR lease_until < datetime('now'))";
        cmd.Parameters.AddWithValue("@owner", owner);
        cmd.Parameters.AddWithValue("@seconds", ttl.TotalSeconds);
        cmd.Parameters.AddWithValue("@target", targetKey);

        var rows = cmd.ExecuteNonQuery();
        return Task.FromResult(rows > 0);
    }

    public Task ReleaseSyncLeaseAsync(string targetKey, string owner, CancellationToken ct = default)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            UPDATE sync_state SET lease_owner = NULL, lease_until = NULL, updated_at = datetime('now')
            WHERE target_key = @target AND lease_owner = @owner";
        cmd.Parameters.AddWithValue("@target", targetKey);
        cmd.Parameters.AddWithValue("@owner", owner);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task ApplyPulledMutationAsync(string targetKey, SyncMutation mutation, CancellationToken ct = default)
    {
        switch (mutation.Entity)
        {
            case "session" when mutation.Op == "upsert":
                ApplySessionUpsert(mutation);
                break;
            case "observation" when mutation.Op == "upsert":
                ApplyObservationUpsert(mutation);
                break;
            case "observation" when mutation.Op == "delete":
                ApplyObservationDelete(mutation);
                break;
            case "prompt" when mutation.Op == "upsert":
                ApplyPromptUpsert(mutation);
                break;
            case "prompt" when mutation.Op == "delete":
                ApplyPromptDelete(mutation);
                break;
        }

        // Mark as acked in sync_mutations after applying
        if (mutation.Seq > 0)
        {
            using var cmd = _db.CreateCommand();
            cmd.CommandText = "UPDATE sync_mutations SET acked_at = datetime('now') WHERE target_key = @target AND seq = @seq AND acked_at IS NULL";
            cmd.Parameters.AddWithValue("@target", targetKey);
            cmd.Parameters.AddWithValue("@seq", mutation.Seq);
            cmd.ExecuteNonQuery();
        }

        return Task.CompletedTask;
    }

    public Task<long> InsertPulledMutationAsync(string targetKey, SyncMutation mutation, CancellationToken ct = default)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO sync_mutations (target_key, entity, entity_key, op, payload, source, project, occurred_at)
            VALUES (@target, @entity, @key, @op, @payload, 'pull', @project, @occurredAt)";
        cmd.Parameters.AddWithValue("@target", targetKey);
        cmd.Parameters.AddWithValue("@entity", mutation.Entity);
        cmd.Parameters.AddWithValue("@key", mutation.EntityKey);
        cmd.Parameters.AddWithValue("@op", mutation.Op);
        cmd.Parameters.AddWithValue("@payload", mutation.Payload);
        cmd.Parameters.AddWithValue("@project", mutation.Project);
        cmd.Parameters.AddWithValue("@occurredAt", mutation.OccurredAt.ToString("O"));
        cmd.ExecuteNonQuery();

        using var seqCmd = _db.CreateCommand();
        seqCmd.CommandText = "SELECT last_insert_rowid()";
        var seq = Convert.ToInt64(seqCmd.ExecuteScalar());
        return Task.FromResult(seq);
    }

    public Task<int> ReapplyPendingPulledMutationsAsync(string targetKey, CancellationToken ct = default)
    {
        int count = 0;
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            SELECT seq, target_key, entity, entity_key, op, payload, source, project, occurred_at, acked_at
            FROM sync_mutations
            WHERE target_key = @target AND source = 'pull' AND acked_at IS NULL
            ORDER BY seq ASC";
        cmd.Parameters.AddWithValue("@target", targetKey);

        using var r = cmd.ExecuteReader();
        var pending = new List<SyncMutation>();
        while (r.Read())
        {
            pending.Add(new SyncMutation(
                r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
                r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7),
                DateTime.Parse(r.GetString(8)), r.IsDBNull(9) ? null : r.GetString(9)
            ));
        }
        r.Close();

        foreach (var mutation in pending)
        {
            try
            {
                ApplyPulledMutationAsync(targetKey, mutation, ct).Wait(ct);
                count++;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to reapply pulled mutation seq={Seq}", mutation.Seq);
            }
        }

        return Task.FromResult(count);
    }

    private void ApplySessionUpsert(SyncMutation mutation)
    {
        SessionPullPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<SessionPullPayload>(mutation.Payload, JsonPullOpts);
        }
        catch (JsonException)
        {
            _logger?.LogWarning("Failed to deserialize session payload for entity_key={EntityKey}", mutation.EntityKey);
            return;
        }
        if (payload is null)
        {
            _logger?.LogWarning("Failed to deserialize session payload for entity_key={EntityKey}", mutation.EntityKey);
            return;
        }

        WithTx(tx =>
        {
            Exec(tx,
                @"INSERT INTO sessions (id, project, directory, started_at, ended_at, summary)
                  VALUES (@id, @project, @directory, COALESCE(@startedAt, datetime('now')), @endedAt, @summary)
                  ON CONFLICT(id) DO UPDATE SET
                      project    = COALESCE(@project, project),
                      directory  = COALESCE(@directory, directory),
                      ended_at   = COALESCE(@endedAt, ended_at),
                      summary   = COALESCE(@summary, summary),
                      started_at = COALESCE(@startedAt, started_at)",
                Param("@id",       mutation.EntityKey),
                Param("@project",  payload.Project ?? ""),
                Param("@directory", payload.Directory ?? ""),
                Param("@startedAt", payload.StartedAt),
                Param("@endedAt",  payload.EndedAt),
                Param("@summary",  payload.Summary));
        });

        _logger?.LogInformation("Applied mutation session/upsert for entity_key={EntityKey} in project={Project}",
            mutation.EntityKey, payload.Project ?? "");
    }

    private void ApplyObservationUpsert(SyncMutation mutation)
    {
        ObservationPullPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ObservationPullPayload>(mutation.Payload, JsonPullOpts);
        }
        catch (JsonException)
        {
            _logger?.LogWarning("Failed to deserialize observation payload for entity_key={EntityKey}", mutation.EntityKey);
            return;
        }
        if (payload is null)
        {
            _logger?.LogWarning("Failed to deserialize observation payload for entity_key={EntityKey}", mutation.EntityKey);
            return;
        }

        WithTx(tx =>
        {
            try
            {
                // Check if observation exists by sync_id
                var existingId = QueryId(tx,
                    "SELECT id FROM observations WHERE sync_id = @syncId AND deleted_at IS NULL",
                    Param("@syncId", mutation.EntityKey));

                if (existingId > 0)
                {
                    // Update existing
                    Exec(tx,
                        @"UPDATE observations
                          SET type = @type, title = @title, content = @content,
                              tool_name = @toolName, project = @project, scope = @scope,
                              topic_key = @topicKey, updated_at = datetime('now')
                          WHERE id = @id",
                        Param("@id",      existingId),
                        Param("@type",    payload.Type ?? ""),
                        Param("@title",   payload.Title ?? ""),
                        Param("@content", payload.Content ?? ""),
                        Param("@toolName", payload.ToolName ?? ""),
                        Param("@project", payload.Project ?? ""),
                        Param("@scope",  payload.Scope ?? "project"),
                        Param("@topicKey", payload.TopicKey ?? ""));
                }
                else
                {
                    // Insert new
                    var hash = payload.Content is { } c ? Normalizers.HashNormalized(c) : "";
                    Exec(tx,
                        @"INSERT INTO observations
                            (sync_id, session_id, type, title, content, tool_name, project,
                             scope, topic_key, normalized_hash, revision_count, duplicate_count,
                             last_seen_at, updated_at, created_at)
                          VALUES
                            (@syncId, @sessionId, @type, @title, @content, @toolName, @project,
                             @scope, @topicKey, @hash, 1, 1, datetime('now'), datetime('now'), COALESCE(@occurredAt, datetime('now')))",
                        Param("@syncId",   mutation.EntityKey),
                        Param("@sessionId", payload.SessionId ?? ""),
                        Param("@type",     payload.Type ?? ""),
                        Param("@title",    payload.Title ?? ""),
                        Param("@content",  payload.Content ?? ""),
                        Param("@toolName", payload.ToolName ?? ""),
                        Param("@project", payload.Project ?? ""),
                        Param("@scope",   payload.Scope ?? "project"),
                        Param("@topicKey",  payload.TopicKey ?? ""),
                        Param("@hash",     hash),
                        Param("@occurredAt", payload.OccurredAt));
                }
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT (FK)
            {
                InsertDeferred(tx, mutation);
            }
        });

        _logger?.LogInformation("Applied mutation observation/upsert for entity_key={EntityKey} in project={Project}",
            mutation.EntityKey, payload.Project ?? "");
    }

    private void ApplyObservationDelete(SyncMutation mutation)
    {
        ObservationDeletePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ObservationDeletePayload>(mutation.Payload, JsonPullOpts);
        }
        catch (JsonException)
        {
            _logger?.LogWarning("Failed to deserialize observation delete payload for entity_key={EntityKey}", mutation.EntityKey);
            return;
        }
        if (payload is null)
        {
            _logger?.LogWarning("Failed to deserialize observation delete payload for entity_key={EntityKey}", mutation.EntityKey);
            return;
        }

        WithTx(tx =>
        {
            Exec(tx,
                @"UPDATE observations
                  SET deleted_at = datetime('now'), updated_at = datetime('now')
                  WHERE sync_id = @syncId AND deleted_at IS NULL",
                Param("@syncId", mutation.EntityKey));
        });

        _logger?.LogInformation("Applied mutation observation/delete for entity_key={EntityKey}", mutation.EntityKey);
    }

    private void ApplyPromptUpsert(SyncMutation mutation)
    {
        PromptPullPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PromptPullPayload>(mutation.Payload, JsonPullOpts);
        }
        catch (JsonException)
        {
            _logger?.LogWarning("Failed to deserialize prompt payload for entity_key={EntityKey}", mutation.EntityKey);
            return;
        }
        if (payload is null)
        {
            _logger?.LogWarning("Failed to deserialize prompt payload for entity_key={EntityKey}", mutation.EntityKey);
            return;
        }

        WithTx(tx =>
        {
            try
            {
                // Check if prompt exists by sync_id
                var existingId = QueryId(tx,
                    "SELECT id FROM user_prompts WHERE sync_id = @syncId AND deleted_at IS NULL",
                    Param("@syncId", mutation.EntityKey));

                if (existingId > 0)
                {
                    // Update existing
                    Exec(tx,
                        @"UPDATE user_prompts
                          SET content = @content, project = @project
                          WHERE id = @id",
                        Param("@id",      existingId),
                        Param("@content", payload.Content ?? ""),
                        Param("@project", payload.Project ?? ""));
                }
                else
                {
                    // Insert new
                    Exec(tx,
                        @"INSERT INTO user_prompts (sync_id, session_id, content, project, created_by, created_at)
                          VALUES (@syncId, @sessionId, @content, @project, @createdBy, COALESCE(@occurredAt, datetime('now')))",
                        Param("@syncId",   mutation.EntityKey),
                        Param("@sessionId", payload.SessionId ?? ""),
                        Param("@content", payload.Content ?? ""),
                        Param("@project", payload.Project ?? ""),
                        Param("@createdBy", "sync"),
                        Param("@occurredAt", payload.OccurredAt));
                }
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT (FK)
            {
                InsertDeferred(tx, mutation);
            }
        });

        _logger?.LogInformation("Applied mutation prompt/upsert for entity_key={EntityKey} in project={Project}",
            mutation.EntityKey, payload.Project ?? "");
    }

    private void ApplyPromptDelete(SyncMutation mutation)
    {
        PromptDeletePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PromptDeletePayload>(mutation.Payload, JsonPullOpts);
        }
        catch (JsonException)
        {
            _logger?.LogWarning("Failed to deserialize prompt delete payload for entity_key={EntityKey}", mutation.EntityKey);
            return;
        }
        if (payload is null)
        {
            _logger?.LogWarning("Failed to deserialize prompt delete payload for entity_key={EntityKey}", mutation.EntityKey);
            return;
        }

        WithTx(tx =>
        {
            Exec(tx,
                @"UPDATE user_prompts SET deleted_at = datetime('now')
                  WHERE sync_id = @syncId AND deleted_at IS NULL",
                Param("@syncId", mutation.EntityKey));
        });

        _logger?.LogInformation("Applied mutation prompt/delete for entity_key={EntityKey}", mutation.EntityKey);
    }

    private void InsertDeferred(SqliteTransaction tx, SyncMutation mutation)
    {
        Exec(tx,
            @"INSERT INTO sync_apply_deferred (entity, entity_key, op, payload, source) 
              VALUES (@ent, @key, @op, @payload, 'pull')",
            Param("@ent",     mutation.Entity),
            Param("@key",     mutation.EntityKey),
            Param("@op",      mutation.Op),
            Param("@payload", mutation.Payload));
    }

    // Payload records for deserialization
    private record SessionPullPayload(string Id, string? Project, string? Directory, string? EndedAt, string? Summary, string? StartedAt);
    private record ObservationPullPayload(string SyncId, string? SessionId, string? Type, string? Title, string? Content, string? ToolName, string? Project, string? Scope, string? TopicKey, string? OccurredAt);
    private record ObservationDeletePayload(string SyncId);
    private record PromptPullPayload(string SyncId, string? SessionId, string? Content, string? Project, string? OccurredAt);
    private record PromptDeletePayload(string SyncId);

    public async Task<ReplayDeferredResult> ReplayDeferredAsync(CancellationToken ct = default)
    {
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = _cfg.DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync(ct);

        // Get deferred rows with retry_count < 5
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, entity, entity_key, op, payload, source, retry_count
            FROM sync_apply_deferred
            WHERE retry_count < 5
            ORDER BY pulled_at
            LIMIT 100";

        var deferredRows = new List<DeferredRow>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                deferredRows.Add(new DeferredRow
                {
                    Id = reader.GetInt64(0),
                    Entity = reader.GetString(1),
                    EntityKey = reader.GetString(2),
                    Op = reader.GetString(3),
                    Payload = reader.GetString(4),
                    Source = reader.GetString(5),
                    RetryCount = reader.GetInt32(6)
                });
            }
        }

        int applied = 0;
        int dead = 0;

        foreach (var row in deferredRows)
        {
            try
            {
                // Try to apply the deferred mutation by inserting into sync_mutations
                var applyCmd = conn.CreateCommand();
                applyCmd.CommandText = @"
                    INSERT INTO sync_mutations (target_key, entity, entity_key, op, payload, source, project, occurred_at)
                    VALUES ('cloud', @entity, @entity_key, @op, @payload, @source, '', datetime('now'))";
                applyCmd.Parameters.AddWithValue("@entity", row.Entity);
                applyCmd.Parameters.AddWithValue("@entity_key", row.EntityKey);
                applyCmd.Parameters.AddWithValue("@op", row.Op);
                applyCmd.Parameters.AddWithValue("@payload", row.Payload);
                applyCmd.Parameters.AddWithValue("@source", row.Source);
                await applyCmd.ExecuteNonQueryAsync(ct);

                // Delete from deferred table on success
                var deleteCmd = conn.CreateCommand();
                deleteCmd.CommandText = "DELETE FROM sync_apply_deferred WHERE id = @id";
                deleteCmd.Parameters.AddWithValue("@id", row.Id);
                await deleteCmd.ExecuteNonQueryAsync(ct);

                applied++;
            }
            catch (Exception)
            {
                // Increment retry_count
                var updateCmd = conn.CreateCommand();
                updateCmd.CommandText = @"
                    UPDATE sync_apply_deferred 
                    SET retry_count = retry_count + 1,
                        last_error = @error
                    WHERE id = @id";
                updateCmd.Parameters.AddWithValue("@id", row.Id);
                updateCmd.Parameters.AddWithValue("@error", "Replay failed");
                await updateCmd.ExecuteNonQueryAsync(ct);

                if (row.RetryCount >= 4)
                {
                    // This row will be dead after this increment (retry_count becomes 5)
                    dead++;
                }
            }
        }

        return new ReplayDeferredResult(applied, dead);
    }

    private sealed class DeferredRow
    {
        public long Id { get; set; }
        public string Entity { get; set; } = "";
        public string EntityKey { get; set; } = "";
        public string Op { get; set; } = "";
        public string Payload { get; set; } = "";
        public string Source { get; set; } = "";
        public int RetryCount { get; set; }
    }

    public Task MarkSyncFailureAsync(string targetKey, string message, DateTime backoffUntil, CancellationToken ct = default)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            UPDATE sync_state SET
                lifecycle = 'failed',
                consecutive_failures = consecutive_failures + 1,
                backoff_until = @backoff,
                last_error = @error,
                updated_at = datetime('now')
            WHERE target_key = @target";
        cmd.Parameters.AddWithValue("@backoff", backoffUntil.ToString("O"));
        cmd.Parameters.AddWithValue("@error", message);
        cmd.Parameters.AddWithValue("@target", targetKey);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task MarkSyncBlockedAsync(string targetKey, string reasonCode, string message, CancellationToken ct = default)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            UPDATE sync_state SET
                lifecycle = 'blocked',
                last_error = @error,
                updated_at = datetime('now')
            WHERE target_key = @target";
        cmd.Parameters.AddWithValue("@error", $"{reasonCode}: {message}");
        cmd.Parameters.AddWithValue("@target", targetKey);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    public Task MarkSyncHealthyAsync(string targetKey, CancellationToken ct = default)
    {
        using var cmd = _db.CreateCommand();
        cmd.CommandText = @"
            UPDATE sync_state SET
                lifecycle = 'healthy',
                consecutive_failures = 0,
                backoff_until = NULL,
                last_error = NULL,
                updated_at = datetime('now')
            WHERE target_key = @target";
        cmd.Parameters.AddWithValue("@target", targetKey);
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Internal helpers
    // ═══════════════════════════════════════════════════════════════════════════

    // ─── SQL constants ─────────────────────────────────────────────────────────

    private const string ObsSelect = @"
        SELECT o.id, ifnull(o.sync_id,'') as sync_id, o.session_id, o.type, o.title, o.content, o.tool_name, o.project,
               o.scope, o.topic_key, o.revision_count, o.duplicate_count, o.last_seen_at, o.created_at, o.updated_at, o.deleted_at,
               o.md_path
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
        MdPath         = r.IsDBNull(16) ? null : r.GetString(16),
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

    private long QueryId(SqliteTransaction tx, string sql, params SqliteParameter[] parms)
    {
        using var cmd = TxCmd(tx, sql, parms);
        var result = cmd.ExecuteScalar();
        if (result is null || result == DBNull.Value) return 0;
        return Convert.ToInt64(result);
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
        SqliteRetry.Execute(() => cmd.ExecuteNonQuery());
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
        SqliteRetry.Execute(() =>
        {
            using var tx = _db.BeginTransaction();
            fn(tx);
            tx.Commit();
        });
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
        if (v == Scopes.Team) return Scopes.Team;
        
        // If it already has a namespace (personal:user), keep it
        if (v.StartsWith("personal:") || v.StartsWith("project:"))
            return v;

        return Scopes.Personal;
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
