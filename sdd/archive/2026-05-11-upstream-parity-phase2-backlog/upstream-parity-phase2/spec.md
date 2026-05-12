# Spec: Upstream Parity — Phase 2 (API Parity)

## Delta from prior state

This spec adds 7 new capabilities to engram-dotnet that bring parity with Go upstream v1.14.8:
1. `DELETE /sessions/{id}` — Hard-delete session with FK guard
2. `DELETE /prompts/{id}` — Soft-delete prompt by ID
3. `mem_current_project` — MCP discovery tool (never errors)
4. Structured error responses — `error_code`, `available_projects`, `hint`
5. Obsidian `--watch` — Daemon mode with periodic timer
6. Obsidian `--since` — Filter by date (ISO 8601 + relative)
7. Export by project — `GET /export?project=X`

All changes are additive — no breaking changes to existing API contracts.

---

# 1. DELETE /sessions/{id}

## Purpose

Allow deletion of empty sessions via HTTP API, matching Go upstream behavior.

## Requirements

### REQ-DEL-001: DeleteSession Store Method

The store MUST implement:
```csharp
Task DeleteSessionAsync(string id);
```

### REQ-DEL-002: Deletion Preconditions

The deletion MUST follow this order:
1. Verify session exists → throw `SessionNotFoundException` if not
2. Verify session has NO active (non-deleted) observations → throw `SessionDeleteBlockedException` if it does
3. Soft-delete all prompts associated with the session (set `deleted_at` in `prompt_tombstones`)
4. Hard-delete the session row

### REQ-DEL-003: HTTP Endpoint

```
DELETE /sessions/{id}
→ 200 { "id": "sess-123", "status": "deleted" }
→ 400 { "error": "session id is required" }
→ 404 { "error": "session not found: sess-123" }
→ 409 { "error": "session has active observations, cannot delete" }
→ 500 { "error": "internal server error" }
```

### REQ-DEL-004: SQLite FK Constraint Fallback

If a SQLite FK constraint error occurs during the DELETE FROM sessions step (race condition where an observation was added between check and delete), the error MUST be caught and returned as a 409 Conflict.

## Scenarios

### Scenario: Delete empty session

**Given** a session "sess-empty" exists with no observations
**When** `DELETE /sessions/sess-empty` is called
**Then** returns `200 { "id": "sess-empty", "status": "deleted" }`
**And** the session is removed from the database

### Scenario: Delete session with observations fails

**Given** a session "sess-with-obs" exists with 5 active observations
**When** `DELETE /sessions/sess-with-obs` is called
**Then** returns `409 { "error": "session has active observations, cannot delete" }`
**And** the session is NOT deleted

### Scenario: Delete non-existent session

**Given** no session with ID "does-not-exist"
**When** `DELETE /sessions/does-not-exist` is called
**Then** returns `404 { "error": "session not found: does-not-exist" }`

### Scenario: Delete session also soft-deletes prompts

**Given** a session "sess-with-prompts" exists with 3 prompts
**When** `DELETE /sessions/sess-with-prompts` succeeds
**Then** the session is hard-deleted
**And** all 3 prompts have `deleted_at` set (soft-delete)

---

# 2. DELETE /prompts/{id}

## Purpose

Allow deletion of individual prompts via HTTP API.

## Requirements

### REQ-DELP-001: DeletePrompt Store Method

The store MUST implement:
```csharp
Task DeletePromptAsync(long id);
```

### REQ-DELP-002: Soft-Delete Behavior

The prompt MUST be soft-deleted:
- Set `deleted_at` in the `prompt_tombstones` table
- The prompt row in `user_prompts` is NOT removed

### REQ-DELP-003: HTTP Endpoint

```
DELETE /prompts/{id}
→ 200 { "id": 42, "status": "deleted" }
→ 400 { "error": "invalid prompt id" }
→ 404 { "error": "prompt not found: 42" }
→ 500 { "error": "internal server error" }
```

## Scenarios

### Scenario: Delete existing prompt

**Given** a prompt with ID 42 exists
**When** `DELETE /prompts/42` is called
**Then** returns `200 { "id": 42, "status": "deleted" }`
**And** the prompt has `deleted_at` set

### Scenario: Delete non-existent prompt

**Given** no prompt with ID 999
**When** `DELETE /prompts/999` is called
**Then** returns `404 { "error": "prompt not found: 999" }`

---

# 3. mem_current_project MCP Tool

## Purpose

Discovery tool that tells the agent which project would be used for the current working directory. **Never errors** — always returns success.

## Requirements

### REQ-CUR-001: Tool Registration

The tool MUST be registered as `mem_current_project` with no required arguments.

### REQ-CUR-002: Response Format

The tool MUST return JSON with:
```json
{
  "project": "my-project",
  "project_source": "git_remote",
  "project_path": "/path/to/repo",
  "cwd": "/path/to/repo",
  "available_projects": [],
  "warning": null
}
```

### REQ-CUR-003: Never Errors

The tool MUST NOT return `IsError = true`, even when:
- The cwd is ambiguous (multiple git repos)
- No git repo exists
- Any detection error

For ambiguous cwd: `project` is empty string, `available_projects` is populated, `warning` or `error_hint` explains the situation.

### REQ-CUR-004: Uses DetectProjectFull

The tool MUST call `DetectProjectFull(cwd)` from Phase 1 and map the result:
- `DetectionResult.Project` → `project`
- `DetectionResult.Source` → `project_source`
- `DetectionResult.ProjectPath` → `project_path`
- `os.Getwd()` → `cwd`
- `DetectionResult.AvailableProjects` → `available_projects`
- `DetectionResult.Warning` → `warning` (omit if null)
- `DetectionResult.Error` → `error_hint` (omit if null)

## Scenarios

### Scenario: Normal git remote detection

**Given** cwd is inside a git repo with origin `git@github.com:user/auth-service.git`
**When** `mem_current_project()` is called
**Then** returns `{ project: "auth-service", project_source: "git_remote", ... }`

### Scenario: Ambiguous cwd returns success with metadata

**Given** cwd contains 3 child git repos: `frontend`, `backend`, `shared`
**When** `mem_current_project()` is called
**Then** returns `IsError = false`
**And** `project` is empty string
**And** `available_projects` contains `["frontend", "backend", "shared"]`
**And** `error_hint` explains the ambiguity

### Scenario: Warning case (git_child)

**Given** cwd contains exactly 1 child git repo `my-app`
**When** `mem_current_project()` is called
**Then** `project_source` is `"git_child"`
**And** `warning` contains `"Auto-promoted single child repo"`

---

# 4. Structured Error Responses

## Purpose

Return machine-readable error metadata from MCP tools instead of plain text.

## Requirements

### REQ-ERR-001: Error Format

Structured errors MUST use this JSON format:
```json
{
  "error": true,
  "error_code": "ambiguous_project",
  "message": "Multiple git repositories found in working directory",
  "available_projects": ["auth-service", "frontend"],
  "hint": "Navigate to one of the project directories before writing"
}
```

### REQ-ERR-002: Error Codes

| Code | Description |
|------|-------------|
| `ambiguous_project` | Multiple git repos in cwd |
| `unknown_project` | Project override not found in store |
| `project_not_found` | Project doesn't exist |
| `session_not_found` | Session doesn't exist |
| `prompt_not_found` | Prompt doesn't exist |
| `validation_error` | Invalid parameter |

### REQ-ERR-003: Helper Class

A `McpErrors` helper class MUST provide:
```csharp
public static class McpErrors
{
    public static CallToolResult Structured(string code, string message,
        IReadOnlyList<string>? availableProjects = null, string? hint = null);
}
```

### REQ-ERR-004: Usage in Tools

Tools that can fail with project resolution MUST use structured errors:
- Write tools: when cwd is ambiguous → `ambiguous_project`
- Read tools: when project override is unknown → `unknown_project`

## Scenarios

### Scenario: Ambiguous project in write tool

**Given** cwd has multiple git repos
**When** `mem_save` is called
**Then** returns structured error with `error_code: "ambiguous_project"`
**And** `available_projects` is populated
**And** `hint` suggests navigating to a project directory
**And** no data is written

### Scenario: Unknown project override in read tool

**Given** `mem_search` is called with `project: "nonexistent"`
**When** the project doesn't exist in the store
**Then** returns structured error with `error_code: "unknown_project"`
**And** `available_projects` lists known projects

---

# 5. Obsidian Export — Watch Mode

## Purpose

Run obsidian-export as a daemon that exports continuously at configurable intervals.

## Requirements

### REQ-WATCH-001: CLI Flags

```bash
engram obsidian-export --watch                    # Default 60s interval
engram obsidian-export --watch --interval 30s     # 30 seconds
engram obsidian-export --watch --interval 5m      # 5 minutes
```

### REQ-WATCH-002: Watch Loop Behavior

The watch loop MUST:
1. Execute an initial export immediately
2. Wait for the configured interval
3. Export only new observations since last export (using state file)
4. Log each cycle to stderr: `[watch] exported 3 observations at 14:32:01`
5. Repeat until cancellation (Ctrl+C)

### REQ-WATCH-003: Interval Parsing

The `--interval` flag MUST accept:
- Duration strings: `30s`, `5m`, `1h`
- Default: `60s` when `--watch` is used without `--interval`

### REQ-WATCH-004: Graceful Shutdown

On `Ctrl+C` or cancellation token:
- Complete the current export cycle (if in progress)
- Exit cleanly with code 0

## Scenarios

### Scenario: Watch runs immediately then ticks

**Given** `--watch --interval 1s` is used
**When** the command starts
**Then** an export runs immediately
**And** another export runs after ~1 second
**And** another after ~2 seconds total

### Scenario: Watch continues after error

**Given** `--watch` is running
**And** cycle 1 fails with an error
**When** cycle 2 starts
**Then** cycle 2 runs normally (error doesn't stop the loop)
**And** the error is logged to stderr

### Scenario: Watch graceful shutdown

**Given** `--watch` is running with a 60s interval
**When** Ctrl+C is pressed during the sleep period
**Then** the command exits within 1 second
**And** no partial export is left

---

# 6. Obsidian Export — Since Filter

## Purpose

Filter exported observations by creation date.

## Requirements

### REQ-SINCE-001: CLI Flag

```bash
engram obsidian-export --since 2025-01-01     # ISO 8601 date
engram obsidian-export --since 30d             # Relative: last 30 days
engram obsidian-export --since 7d              # Relative: last 7 days
engram obsidian-export --since 24h             # Relative: last 24 hours
```

### REQ-SINCE-002: Relative Date Parsing

The `--since` flag MUST accept:
- ISO 8601 dates: `2025-01-01`, `2025-01-01T00:00:00Z`
- Relative durations: `Nd` (days), `Nh` (hours), `Nm` (minutes)

### REQ-SINCE-003: Filter Behavior

When `--since` is provided:
- Only observations with `created_at >= since` are exported
- Compatible with `--watch` (watch uses `--since` internally for subsequent cycles)
- Compatible with `--project` filter

## Scenarios

### Scenario: Export since specific date

**Given** 10 observations exist, 5 created before 2025-01-01, 5 after
**When** `--since 2025-01-01` is used
**Then** only the 5 observations created on or after 2025-01-01 are exported

### Scenario: Export since relative duration

**Given** observations exist from the last 7 days
**When** `--since 3d` is used
**Then** only observations from the last 3 days are exported

---

# 7. Export by Project

## Purpose

Export observations from a single project instead of all projects.

## Requirements

### REQ-PROJEXP-001: CLI Flag

```bash
engram obsidian-export --project my-project
```

### REQ-PROJEXP-002: HTTP Endpoint

```
GET /export?project=my-project
→ 200 (JSON export of only that project's observations)
→ 400 { "error": "project parameter must not be blank" }
```

### REQ-PROJEXP-003: Store Method

The store MUST implement:
```csharp
Task<ExportData> ExportProjectAsync(string project);
```

### REQ-PROJEXP-004: Compatibility

Project filter MUST be compatible with:
- `--watch` (watch cycles filter by project)
- `--since` (filter by project AND date)
- `--limit` (limit within the project)
- Incremental state (state file tracks per-project exports)

## Scenarios

### Scenario: Export single project

**Given** 3 projects exist with 10, 20, and 30 observations
**When** `--project project-b` is used
**Then** only the 20 observations from project-b are exported
**And** the vault structure contains only project-b's files

### Scenario: Export with project and since

**Given** project-b has 20 observations, 10 from last week, 10 older
**When** `--project project-b --since 7d` is used
**Then** only the 10 recent observations from project-b are exported

---

# Non-Functional Requirements

### REQ-NF-001: Performance

- `DeleteSessionAsync` completes in < 100ms for sessions with < 100 prompts
- `DeletePromptAsync` completes in < 10ms
- Watch mode adds < 50ms overhead per cycle beyond the export itself

### REQ-NF-002: Compatibility

- All changes work with both SqliteStore and PostgresStore
- No changes to existing HTTP API response formats (only new endpoints added)
- No changes to existing MCP tool signatures (only new tool added)

### REQ-NF-003: Test Coverage

- Each requirement has at least one unit test
- Integration tests cover SQLite and PostgreSQL for delete endpoints
- Watch mode tests use fake timer or short intervals
