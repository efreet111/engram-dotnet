# Spec: Upstream Parity — Phase 1 (Foundation)

## Delta from prior state

This spec adds 4 new capabilities to engram-dotnet that bring parity with Go upstream v1.14.8:
1. Project detection 5-case algorithm (was 3-case)
2. New observation schema columns (review_after, expires_at, embedding)
3. Write queue for serialized SQLite access
4. Session activity tracker with nudge behavior

All changes are additive — no breaking changes to existing API contracts.

---

# 1. Project Detection — 5-Case Algorithm

## Purpose

Detect the current project name from the working directory using a 5-case algorithm that handles monorepos and nested git repositories correctly.

## Requirements

### REQ-PROJ-001: DetectProjectFull returns DetectionResult

The `DetectProjectFull` method MUST return a `DetectionResult` with the following fields:

| Field | Type | Description |
|-------|------|-------------|
| `project` | string | Detected project name (never empty) |
| `source` | string | Detection source: one of `git_remote`, `git_root`, `git_child`, `ambiguous`, `dir_basename` |
| `project_path` | string | Absolute path to the project root |
| `warning` | string? | Non-fatal warning message (e.g., "Multiple repos found, using 'X'") |
| `error` | string? | Fatal error message (e.g., "Ambiguous project") |
| `available_projects` | List\<string\> | List of project names when ambiguous |

### REQ-PROJ-002: 5-Case Detection Order

The algorithm MUST evaluate cases in this order:

| Priority | Case | Condition | Result |
|----------|------|-----------|--------|
| 1 | `git_remote` | cwd is inside a git repo with `origin` remote | Return remote repo name |
| 2 | `git_root` | cwd IS a git repo root (no parent git repo) | Return basename of cwd |
| 3 | `git_child` | cwd contains exactly ONE child git repo | Return child repo name, warning about auto-promotion |
| 4 | `ambiguous` | cwd contains TWO OR MORE child git repos | Return error with `available_projects` list |
| 5 | `dir_basename` | cwd is not inside any git repo | Return basename of cwd |

### REQ-PROJ-003: Git Remote Detection

When detecting from git remote:
- Parse the `origin` remote URL (SSH or HTTPS format)
- Extract the repository name (last path segment, without `.git` suffix)
- Normalize: lowercase, trim, collapse whitespace

Examples:
- `git@github.com:user/my-project.git` → `my-project`
- `https://github.com/user/repo-name.git` → `repo-name`
- `git@gitlab.com:org/team/service.git` → `service`

### REQ-PROJ-004: Git Child Scanning

When scanning for child git repos:
- Scan only depth=1 (immediate subdirectories)
- Skip noise directories: `.git`, `node_modules`, `vendor`, `.venv`, `__pycache__`
- Timeout: 200ms per scan
- Cap: maximum 20 entries
- If exactly 1 child repo found: return it with `source=git_child` and warning
- If 2+ child repos found: return `source=ambiguous` with error and `available_projects`

### REQ-PROJ-005: DetectProject Backward Compatibility

The existing `DetectProject(string? workingDir = null)` method MUST continue to work:
- Returns just the project name (string)
- Internally calls `DetectProjectFull` and extracts `.project`
- If `source=ambiguous`, returns the first project from `available_projects`

### REQ-PROJ-006: Project Normalization

Project names MUST be normalized:
- Lowercase
- Trim leading/trailing whitespace
- Collapse consecutive whitespace to single space
- Replace underscores with hyphens

### REQ-PROJ-007: Source Constants

Detection sources MUST be defined as constants:

```csharp
public static class ProjectSources
{
    public const string GitRemote = "git_remote";
    public const string GitRoot = "git_root";
    public const string GitChild = "git_child";
    public const string Ambiguous = "ambiguous";
    public const string DirBasename = "dir_basename";
    public const string ExplicitOverride = "explicit_override";
    public const string RequestBody = "request_body";
}
```

## Scenarios

### Scenario: Project detected from git remote

**Given** cwd is `/home/user/projects/auth-service/src`
**And** `/home/user/projects/auth-service` is a git repo with origin `git@github.com:user/auth-service.git`
**When** `DetectProjectFull()` is called
**Then** returns `{ project: "auth-service", source: "git_remote", project_path: "/home/user/projects/auth-service", warning: null, error: null, available_projects: [] }`

### Scenario: Ambiguous project (multiple child repos)

**Given** cwd is `/home/user/monorepo`
**And** `/home/user/monorepo/frontend` is a git repo
**And** `/home/user/monorepo/backend` is a git repo
**And** `/home/user/monorepo/shared` is a git repo
**When** `DetectProjectFull()` is called
**Then** returns `{ project: "", source: "ambiguous", project_path: "/home/user/monorepo", warning: null, error: "Ambiguous project: multiple git repositories found", available_projects: ["frontend", "backend", "shared"] }`

### Scenario: Single child repo auto-promoted

**Given** cwd is `/home/user/workspace`
**And** `/home/user/workspace/my-app` is the only git repo in immediate subdirectories
**When** `DetectProjectFull()` is called
**Then** returns `{ project: "my-app", source: "git_child", project_path: "/home/user/workspace/my-app", warning: "Auto-promoted single child repo 'my-app'", error: null, available_projects: [] }`

### Scenario: No git repo, fallback to basename

**Given** cwd is `/tmp/scratch`
**And** no git repo exists in cwd or any parent
**When** `DetectProjectFull()` is called
**Then** returns `{ project: "scratch", source: "dir_basename", project_path: "/tmp/scratch", warning: null, error: null, available_projects: [] }`

---

# 2. Schema Columns — Observation Extensions

## Purpose

Add new columns to the `observations` table to support future features (decay, vector search, expiration) without requiring schema migrations later.

## Requirements

### REQ-SCHEMA-001: New Columns on Observations

The `observations` table MUST have these additional columns:

| Column | Type (SQLite) | Type (PostgreSQL) | Nullable | Default | Description |
|--------|---------------|-------------------|----------|---------|-------------|
| `review_after` | TEXT | TEXT | YES | NULL | Suggested review date (ISO 8601) |
| `expires_at` | TEXT | TEXT | YES | NULL | Expiration date (ISO 8601) |
| `embedding` | BLOB | BYTEA | YES | NULL | Vector embedding (reserved) |
| `embedding_model` | TEXT | TEXT | YES | NULL | Model that generated the embedding |
| `embedding_created_at` | TEXT | TEXT | YES | NULL | Timestamp of embedding creation |

### REQ-SCHEMA-002: Migration on Startup

When the store initializes:
- If columns don't exist, run `ALTER TABLE observations ADD COLUMN ...` for each missing column
- Migration MUST be idempotent (safe to run multiple times)
- Migration MUST NOT fail if columns already exist

### REQ-SCHEMA-003: Observation Model Updated

The `Observation` record MUST include the new fields:

```csharp
public record Observation(
    // ... existing fields ...
    string? ReviewAfter = null,
    string? ExpiresAt = null,
    byte[]? Embedding = null,
    string? EmbeddingModel = null,
    string? EmbeddingCreatedAt = null
);
```

### REQ-SCHEMA-004: Backward Compatibility

- All new fields are nullable with null defaults
- Existing code that doesn't set these fields MUST continue to work
- Queries MUST NOT filter on these fields unless explicitly requested
- Search results MUST NOT include embedding data (too large)

## Scenarios

### Scenario: New database with all columns

**Given** a fresh SQLite database
**When** SqliteStore initializes
**Then** the `observations` table includes all 5 new columns
**And** no migration errors occur

### Scenario: Existing database migration

**Given** a SQLite database created before this change
**And** the `observations` table lacks the new columns
**When** SqliteStore initializes
**Then** 5 `ALTER TABLE` statements execute successfully
**And** existing observations are unaffected (new columns are NULL)

### Scenario: PostgreSQL migration

**Given** a PostgreSQL database created before this change
**When** PostgresStore initializes
**Then** 5 `ALTER TABLE` statements execute successfully
**And** existing observations are unaffected

---

# 3. Write Queue — Serialized SQLite Access

## Purpose

Serialize all MCP write operations through a queue to prevent SQLite concurrency issues (SQLITE_BUSY, database is locked).

## Requirements

### REQ-WQ-001: WriteQueue Interface

```csharp
public interface IWriteQueue : IDisposable
{
    Task<T> EnqueueAsync<T>(Func<IStore, CancellationToken, Task<T>> operation, CancellationToken ct);
    int PendingCount { get; }
}
```

### REQ-WQ-002: Channel-Based Implementation

The write queue MUST use `System.Threading.Channels`:
- Bounded channel with capacity 32
- `BoundedChannelFullMode.Wait` (backpressure — callers wait when full)
- Single consumer task processes jobs sequentially

### REQ-WQ-003: Write Serialization

- Only ONE write operation executes at a time
- Jobs are processed in FIFO order
- Read operations bypass the queue (call store directly)

### REQ-WQ-004: Graceful Shutdown

On `Dispose()`:
- Signal the worker to stop
- Wait for in-progress job to complete (max 5s timeout)
- Cancel pending jobs with `OperationCanceledException`
- Dispose the channel

### REQ-WQ-005: Error Handling

- If a write operation throws, the exception is propagated to the caller
- The queue continues processing subsequent jobs
- Panics/unhandled exceptions are logged but don't crash the queue

### REQ-WQ-006: Write Tool Integration

All MCP write tools MUST use the write queue:
- `mem_save`
- `mem_save_prompt`
- `mem_session_start`
- `mem_session_end`
- `mem_session_summary`
- `mem_capture_passive`
- `mem_update`

Read tools MUST NOT use the write queue:
- `mem_search`
- `mem_context`
- `mem_timeline`
- `mem_get_observation`
- `mem_stats`
- `mem_current_project` (Phase 2)

## Scenarios

### Scenario: Two concurrent saves

**Given** the write queue is idle
**And** `mem_save` is called with observation A
**And** `mem_save` is called with observation B (before A completes)
**When** both calls are enqueued
**Then** A executes first, B waits
**And** B executes after A completes
**And** no SQLITE_BUSY error occurs

### Scenario: Channel full (backpressure)

**Given** the write queue has 32 pending jobs
**And** `mem_save` is called
**When** the call tries to enqueue
**Then** the caller waits (async) until a slot is available
**And** no job is dropped

### Scenario: Graceful shutdown

**Given** the write queue has 3 pending jobs
**And** the MCP server receives SIGTERM
**When** `WriteQueue.Dispose()` is called
**Then** the in-progress job completes (or times out at 5s)
**And** pending jobs are cancelled
**And** the worker task exits cleanly

---

# 4. Session Activity Tracker

## Purpose

Track tool call and save activity per session to provide feedback to the agent about memory hygiene.

## Requirements

### REQ-ACT-001: SessionActivity Interface

```csharp
public interface ISessionActivity
{
    void RecordToolCall(string sessionId);
    void RecordSave(string sessionId);
    void ClearSession(string sessionId);
    string? NudgeIfNeeded(string sessionId, TimeSpan? threshold = null);
    string ActivityScore(string sessionId);
}
```

### REQ-ACT-002: SessionStats Tracking

Track per session:
- `ToolCalls`: total tool calls made
- `Saves`: total save operations (mem_save, mem_session_summary, etc.)
- `LastSave`: timestamp of last save
- `LastToolCall`: timestamp of last tool call

Stats are stored in-memory (`ConcurrentDictionary<string, SessionStats>`).

### REQ-ACT-003: Nudge Logic

A nudge is triggered when ALL conditions are met:
- `saves == 0` AND `tool_calls > 10`
- OR `saves > 0` AND `tool_calls > 20` AND `(saves / tool_calls) < 0.1` (save ratio < 10%)
- AND `last_save` is more than 5 minutes ago (or never)

Default threshold: `tool_calls > 10` for zero-save sessions.

Nudge message examples:
- `"Low save rate (3 saves in 45 tool calls). Consider saving key decisions with mem_save."`
- `"No saves recorded in this session (45 tool calls). Consider using mem_save for important observations."`

### REQ-ACT-004: Activity Score Format

`ActivityScore` returns a formatted string:
```
"Activity: 45 tool calls, 3 saves (6.7% save rate)"
```

### REQ-ACT-005: Integration in MCP Tools

- **All tools**: call `activity.RecordToolCall(sessionId)` before execution
- **Write tools**: call `activity.RecordSave(sessionId)` after successful write
- **mem_session_end**: include activity score and nudge message in response

### REQ-ACT-006: Configurable Threshold

The nudge threshold is configurable via environment variable:
- `ENGRAM_ACTIVITY_NUDGE_THRESHOLD=10` (default)
- If not set, default is 10 tool calls

### REQ-ACT-007: Thread Safety

All operations MUST be thread-safe:
- Use `ConcurrentDictionary` for session storage
- Use `Interlocked.Increment` for counters
- No locks required for read operations

## Scenarios

### Scenario: Nudge on zero-save session

**Given** a session with 45 tool calls and 0 saves
**And** last tool call was 6 minutes ago
**When** `NudgeIfNeeded(sessionId)` is called
**Then** returns `"No saves recorded in this session (45 tool calls). Consider using mem_save for important observations."`

### Scenario: No nudge on healthy session

**Given** a session with 45 tool calls and 12 saves (26.7% save rate)
**When** `NudgeIfNeeded(sessionId)` is called
**Then** returns `null`

### Scenario: Activity score in mem_session_end

**Given** a session with 45 tool calls and 3 saves
**When** `mem_session_end` is called
**Then** response includes:
```json
{
  "status": "session_ended",
  "session_id": "abc123",
  "activity": {
    "tool_calls": 45,
    "saves": 3,
    "save_ratio": "6.7%",
    "score": "Activity: 45 tool calls, 3 saves (6.7% save rate)",
    "nudge": "Low save rate (3 saves in 45 tool calls). Consider saving key decisions with mem_save."
  }
}
```

### Scenario: Session not found

**Given** no activity recorded for session "xyz"
**When** `ActivityScore("xyz")` is called
**Then** returns `"Activity: 0 tool calls, 0 saves (0.0% save rate)"`
**And** no exception is thrown

---

# Non-Functional Requirements

### REQ-NF-001: Performance

- `DetectProjectFull` completes in < 500ms (including git child scan)
- Write queue adds < 1ms overhead per operation (excluding wait time)
- Activity tracker operations are O(1)

### REQ-NF-002: Memory

- Session activity tracker uses < 1MB for 1000 active sessions
- Write queue buffer uses < 4MB (32 jobs × ~128KB each)

### REQ-NF-003: Compatibility

- All changes work with both SqliteStore and PostgresStore
- No changes to HTTP API contracts
- No changes to existing MCP tool signatures (only internal implementation)
