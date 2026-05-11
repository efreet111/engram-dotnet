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

# 4. Session Activity Tracker (CORRECTED — Go Upstream Parity)

## Purpose

Track tool call and save activity per session to provide feedback to the agent about memory hygiene.

**Note**: This section was corrected by delta spec `specs/04-session-activity-delta.md` to match Go upstream (`internal/mcp/activity.go`). The original requirements had 8 deviations from upstream behavior.

## Requirements

### REQ-ACT-001: SessionActivity Class (CORRECTED)

```csharp
namespace Engram.Mcp;

public sealed class SessionActivity
{
    private readonly object _lock = new();
    private readonly Dictionary<string, SessionStats> _sessions = new();
    private readonly TimeSpan _nudgeAfter = TimeSpan.FromMinutes(10);
    private readonly Func<DateTimeOffset> _now;

    // Public constructor uses DateTimeOffset.Now
    public SessionActivity() : this(() => DateTimeOffset.Now) { }

    // Internal constructor for testability (injectable nowFunc)
    internal SessionActivity(Func<DateTimeOffset> nowFunc) => _now = nowFunc;

    public void RecordToolCall(string sessionId);
    public void RecordSave(string sessionId);
    public void ClearSession(string sessionId);
    public string? NudgeIfNeeded(string sessionId);
    public string ActivityScore(string sessionId);

    private sealed record SessionStats(
        DateTimeOffset StartedAt,
        int ToolCallCount,
        int SaveCount,
        DateTimeOffset LastSaveAt
    );
}
```

**Corrections from original spec:**
- ✅ Concrete class `SessionActivity` (NO `ISessionActivity` interface)
- ✅ Uses `lock` + `Dictionary` (port of Go's `sync.Mutex` + `map`)
- ✅ `nowFunc` injectable for testability
- ✅ NO `TimeSpan? threshold` parameter — hardcoded 10 minutes

---

### REQ-ACT-002: SessionStats Fields (CORRECTED)

Track per session:

| Field | Type | Description |
|-------|------|-------------|
| `StartedAt` | `DateTimeOffset` | When session was created (first tool call) |
| `ToolCallCount` | `int` | Total tool calls made |
| `SaveCount` | `int` | Total save operations |
| `LastSaveAt` | `DateTimeOffset` | Timestamp of last save (default if none) |

**Corrections from original spec:**
- ✅ NO `LastToolCall` field — Go doesn't track it
- ✅ `StartedAt` used as fallback when `LastSaveAt` is default

---

### REQ-ACT-003: Nudge Logic (CORRECTED — CRITICAL)

```csharp
public string? NudgeIfNeeded(string sessionId)
{
    lock (_lock)
    {
        // 1. Unknown session → no nudge
        if (!_sessions.TryGetValue(sessionId, out var s))
            return "";

        var now = _now();

        // 2. Session too young (< 10 min) → no nudge
        if (now - s.StartedAt < _nudgeAfter)
            return "";

        // 3. Idle session (≤5 tool calls, 0 saves) → no nudge
        if (s.SaveCount == 0 && s.ToolCallCount <= 5)
            return "";

        // 4. Check time since last save (or session start if no saves)
        var lastRef = s.LastSaveAt == default ? s.StartedAt : s.LastSaveAt;
        var elapsed = now - lastRef;

        if (elapsed < _nudgeAfter)
            return "";

        // 5. NUDGE: return message with minutes elapsed
        var minutes = (int)elapsed.TotalMinutes;
        return $"\n\n⚠️ No mem_save calls for this project in {minutes} minutes. Did you make any decisions, fix bugs, or discover something worth persisting?";
    }
}
```

**Decision tree:**

```
session exists? ──NO──> return ""
     │
    YES
     │
     v
now - startedAt < 10 min? ──YES──> return ""
     │
    NO
     │
     v
saveCount == 0 AND toolCallCount <= 5? ──YES──> return ""  (idle session)
     │
    NO
     │
     v
now - lastRef < 10 min? ──YES──> return ""
     │
    NO
     │
     v
return nudge message with minutes
```

Where `lastRef = lastSaveAt` if non-default, else `startedAt`.

**Corrections from original spec:**
- ✅ **Time-based only**: 10 minutes since last save (or session start)
- ✅ **Idle detection**: ≤5 tool calls + 0 saves = no nudge (avoids nagging new sessions)
- ❌ NO ratio calculation (`saves/tool_calls`)
- ❌ NO `tool_calls > 10` / `tool_calls > 20` thresholds
- ❌ NO "5 minutes ago" check

---

### REQ-ACT-004: ActivityScore Format (CORRECTED)

```csharp
public string ActivityScore(string sessionId)
{
    lock (_lock)
    {
        // Unknown session → empty string
        if (!_sessions.TryGetValue(sessionId, out var s))
            return "";

        // Pluralization
        var callLabel = s.ToolCallCount == 1 ? "tool call" : "tool calls";
        var saveLabel = s.SaveCount == 1 ? "save" : "saves";

        var score = $"Session activity: {s.ToolCallCount} {callLabel}, {s.SaveCount} {saveLabel}";

        // High activity warning
        if (s.SaveCount == 0 && s.ToolCallCount > 5)
            score += " — high activity with no saves, consider persisting important decisions";

        return score;
    }
}
```

**Examples:**

| Tool Calls | Saves | Output |
|------------|-------|--------|
| 0 | 0 | `""` (unknown session) |
| 1 | 1 | `"Session activity: 1 tool call, 1 save"` |
| 8 | 0 | `"Session activity: 8 tool calls, 0 saves — high activity with no saves, consider persisting important decisions"` |
| 12 | 3 | `"Session activity: 12 tool calls, 3 saves"` |

**Corrections from original spec:**
- ✅ Returns `""` for unknown sessions (NOT `"Activity: 0 tool calls, 0 saves (0.0% save rate)"`)
- ✅ Prefix: `"Session activity: "` (not `"Activity: "`)
- ✅ Proper singular/plural: `"1 tool call"` vs `"2 tool calls"`
- ✅ Warning text: `" — high activity with no saves, consider persisting important decisions"`
- ❌ NO save ratio percentage (`"(6.7% save rate)"`)

---

### REQ-ACT-005: Integration in MCP Tools (CORRECTED)

| Tool | Go Action | Implementation |
|------|-----------|----------------|
| `mem_search` | `RecordToolCall` + `NudgeIfNeeded` in response | Call both, append nudge to response text |
| `mem_context` | `RecordToolCall` + `NudgeIfNeeded` in response | Call both, append nudge to response text |
| `mem_session_start` | `RecordToolCall` | Call once |
| `mem_capture_passive` | `RecordToolCall` | Call once |
| `mem_save` | `RecordSave` | Call on successful save |
| `mem_session_summary` | `ActivityScore` in response | Append score to response text |
| `mem_session_end` | `ClearSession` | Call to free memory |

**Tools that do NOT register activity** (same as Go):
- `mem_update`, `mem_delete`, `mem_save_prompt`, `mem_stats`, `mem_timeline`, `mem_get_observation`, `mem_judge`

**Corrections from original spec:**
- ✅ Nudge in `mem_search` + `mem_context` (visible during work)
- ✅ Activity score in `mem_session_summary` (end-of-session summary)
- ❌ NO nudge in `mem_session_end`
- ❌ NO activity score in `mem_session_end`

---

### REQ-ACT-006: Configuration (CORRECTED)

**NO environment variables.**

The nudge threshold is **hardcoded**:

```csharp
private readonly TimeSpan _nudgeAfter = TimeSpan.FromMinutes(10);
```

**Corrections from original spec:**
- ❌ NO `ENGRAM_ACTIVITY_NUDGE_THRESHOLD` env var
- ❌ NO configurable threshold
- ✅ Always 10 minutes (matches Go upstream)

**Rationale:** Go upstream hardcodes this value. Consistency > configurability for this feature.

---

### REQ-ACT-007: Thread Safety (CORRECTED)

```csharp
public sealed class SessionActivity
{
    private readonly object _lock = new();
    private readonly Dictionary<string, SessionStats> _sessions = new();

    public void RecordToolCall(string sessionId)
    {
        lock (_lock)
        {
            var s = GetOrCreate(sessionId);
            s.ToolCallCount++;
        }
    }

    // ... all methods use the same lock
}
```

**Corrections from original spec:**
- ✅ `lock` + plain `Dictionary` (direct port of Go's `sync.Mutex` + `map`)
- ❌ NO `ConcurrentDictionary<string, SessionStats>`
- ❌ NO `Interlocked.Increment`

**Rationale:** Go uses `sync.Mutex` to protect the map. The C# equivalent is `lock` + `Dictionary`. This is simpler and matches the upstream pattern exactly.

## Scenarios

### Scenario: Nudge after 10 minutes without save

**Given** a session with 6 tool calls at T=0
**And** no saves recorded
**When** time advances 15 minutes
**And** `NudgeIfNeeded(sessionId)` is called
**Then** returns `"\n\n⚠️ No mem_save calls for this project in 15 minutes. Did you make any decisions, fix bugs, or discover something worth persisting?"`

### Scenario: Save resets nudge timer

**Given** a session with 6 tool calls at T=0
**When** time advances 15 minutes
**And** `RecordSave(sessionId)` is called
**And** `NudgeIfNeeded(sessionId)` is called immediately
**Then** returns `""` (save reset the timer)

### Scenario: No nudge for idle session

**Given** a session with only 3 tool calls (no saves)
**When** time advances 20 minutes
**And** `NudgeIfNeeded(sessionId)` is called
**Then** returns `""` (idle session: ≤5 tool calls + 0 saves)

### Scenario: Activity score with high activity warning

**Given** a session with 8 tool calls and 0 saves
**When** `ActivityScore(sessionId)` is called
**Then** returns `"Session activity: 8 tool calls, 0 saves — high activity with no saves, consider persisting important decisions"`

### Scenario: Activity score after save (warning disappears)

**Given** a session with 8 tool calls and 0 saves
**When** `RecordSave(sessionId)` is called
**And** `ActivityScore(sessionId)` is called
**Then** returns `"Session activity: 8 tool calls, 1 save"` (no warning)

### Scenario: Unknown session returns empty string

**Given** no activity recorded for session "xyz"
**When** `ActivityScore("xyz")` is called
**Then** returns `""`
**And** no exception is thrown

### Scenario: Activity score in mem_session_summary

**Given** a session with 12 tool calls and 3 saves
**When** `mem_session_summary` is called
**Then** response includes `"Session activity: 12 tool calls, 3 saves"`

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
