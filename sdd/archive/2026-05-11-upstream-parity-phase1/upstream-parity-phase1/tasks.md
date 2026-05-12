# Tasks: Upstream Parity — Phase 1 (Foundation)

## Phase 1: Project Detection 5-Case

- [x] 1.1 Create `DetectionResult` record in `src/Engram.Store/ProjectDetector.cs`
- [x] 1.2 Create `ProjectSources` constants class in `src/Engram.Store/ProjectDetector.cs`
- [x] 1.3 Implement `ScanChildren(string dir)` in `ProjectDetector.cs` — scan depth=1 for git repos, skip noise dirs, 200ms timeout, 20-entry cap
- [x] 1.4 Implement `DetectProjectFull(string? workingDir = null)` — 5-case algorithm (git_remote → git_root → git_child → ambiguous → dir_basename)
- [x] 1.5 Update `DetectProject(string? workingDir = null)` — wrapper over `DetectProjectFull`, returns `.project` string
- [x] 1.6 Implement `NormalizeProject(string name)` — lowercase, trim, collapse whitespace, underscores → hyphens (in `Normalizers.cs`)
- [x] 1.7 Write unit tests for all 5 detection cases (`ProjectDetectorTests.cs`)
- [x] 1.8 Write unit tests for `NormalizeProject` edge cases (`NormalizersTests.cs`)
- [ ] 1.9 Write unit tests for `ScanChildren` (timeout, noise dirs, cap) — **NOT FOUND**

## Phase 2: Schema Columns

- [x] 2.1 Add new fields to `Observation` record in `src/Engram.Store/Models.cs` (ReviewAfter, ExpiresAt, Embedding, EmbeddingModel, EmbeddingCreatedAt)
- [x] 2.2 Update `CREATE TABLE observations` in SqliteStore to include new columns
- [x] 2.3 Update `CREATE TABLE observations` in PostgresStore to include new columns
- [x] 2.4 Implement SQLite migration: `AddColumnIfNotExists` for each missing column (idempotent)
- [x] 2.5 Implement PostgreSQL migration: `ALTER TABLE observations ADD COLUMN ...` with `ColumnExists` check (idempotent)
- [ ] 2.6 Write migration tests: existing DB gets columns, new DB has columns from start — **NOT FOUND**
- [ ] 2.7 Verify existing tests pass with new nullable columns — **NOT VERIFIED**

## Phase 3: Write Queue

- [x] 3.1 Create `src/Engram.Mcp/WriteQueue.cs` — `WriteQueue` implementation (concrete class, no interface)
- [x] 3.2 Implement `EnqueueAsync<T>` using `Channel<Func<Task>>` with capacity 32, `BoundedChannelFullMode.Wait`
- [x] 3.3 Implement single-consumer worker task that processes jobs sequentially (`WorkerLoopAsync`)
- [x] 3.4 Implement `Dispose()` — signal stop, wait for in-progress (5s timeout), cancel pending
- [x] 3.5 Implement error handling — propagate exceptions to caller via TaskCompletionSource, continue processing
- [ ] 3.6 Register `WriteQueue` as singleton in DI container — **NOT FOUND** (used directly, not via DI)
- [x] 3.7 Update all 7 write MCP tools to use `writeQueue.EnqueueAsync()` instead of direct store calls (9 usages in EngramTools.cs)
- [x] 3.8 Verify read tools still call store directly (no queue)
- [x] 3.9 Write unit tests: concurrent writes serialize correctly (`WriteQueueTests.cs` exists)
- [x] 3.10 Write unit tests: backpressure when channel full
- [x] 3.11 Write unit tests: graceful shutdown cancels pending jobs
- [ ] 3.12 Write integration test: two concurrent mem_save calls don't cause SQLITE_BUSY — **NOT VERIFIED**

## Phase 4: Session Activity Tracker (Delta Spec — Go Upstream Behavior)

**Reference**: See `specs/04-session-activity-delta.md` for corrected behavior (8 deviations from original spec §4)

### 4.A: Core Implementation

- [x] 4.1 **DELTA** Create `src/Engram.Mcp/SessionActivity.cs` — concrete class (NOT interface-first), lock-based thread safety
- [x] 4.2 **DELTA** Constructor: `TimeSpan? nudgeAfter = null` (default 10 min), `Func<DateTimeOffset>? nowFunc = null` (injectable for tests)
- [x] 4.3 **DELTA** `RecordToolCall(string sessionId)` — thread-safe increment with `lock`, update `StartedAt` on first call
- [x] 4.4 **DELTA** `RecordSave(string sessionId)` — thread-safe increment, update `LastSaveAt`, reset nudge timer
- [x] 4.5 **DELTA** `ClearSession(string sessionId)` — safe no-op for unknown sessions (no panic)
- [x] 4.6 **DELTA** `NudgeIfNeeded(string sessionId)` — time-based logic (10 min), idle detection (≤5 tool calls + 0 saves = no nudge), returns `""` or nudge message
- [x] 4.7 **DELTA** `ActivityScore(string sessionId)` — format: `"Session activity: N tool call(s), M save(s)"` + optional warning, `""` for unknown sessions
- [x] 4.8 **DELTA** Internal `SessionStats` record: `ToolCallCount`, `SaveCount`, `LastSaveAt`, `StartedAt`

### 4.B: DI Registration

- [x] 4.9 Register `SessionActivity` as singleton in `src/Engram.Cli/Program.cs` (MCP command): `services.AddSingleton<SessionActivity>(new SessionActivity(TimeSpan.FromMinutes(10)))`
- [x] 4.10 **NO ENV VAR** — threshold is hardcoded to 10 minutes (Go upstream parity)

### 4.C: Integration into EngramTools.cs

- [x] 4.11 Add `SessionActivity` parameter to `EngramTools` constructor
- [x] 4.12 `MemSearch()`: call `_activity.RecordToolCall(sessionId)` at start, append nudge to response if present
- [x] 4.13 `MemContext()`: call `_activity.RecordToolCall(sessionId)` at start, append nudge to response if present
- [x] 4.14 `MemSave()`: call `_activity.RecordSave(defaultSessionId(project))` AFTER successful write (not RecordToolCall)
- [x] 4.15 `MemSessionSummary()`: include `_activity.ActivityScore(defaultSessionId(project))` in response message
- [x] 4.16 `MemSessionStart()`: call `_activity.RecordToolCall(defaultSessionId(project))`
- [x] 4.17 `MemSessionEnd()`: call `_activity.ClearSession(defaultSessionId(project))`
- [x] 4.18 `MemCapturePassive()`: call `_activity.RecordToolCall(defaultSessionId(project))`
- [x] 4.19 `MemUpdate()` and `MemDelete()`: NO activity tracking (Go upstream parity)

### 4.D: Unit Tests (port from `activity_test.go`)

- [x] 4.20 **RED** `TestRecordAndNudge` — nudge fires after 10 min without save
- [x] 4.21 **RED** `TestRecordSave_ResetsNudge` — save resets nudge timer
- [x] 4.22 **RED** `TestActivityScore` — correct format with pluralization, warning when `saves==0 && toolCalls>5`
- [x] 4.23 **RED** `TestNoNudgeForIdleSessions` — no nudge when `toolCallCount<=5 && saveCount==0`
- [x] 4.24 **RED** `TestClearSession` — removes session, unknown session is no-op
- [x] 4.25 **RED** `TestPluralization` — "1 tool call" vs "2 tool calls", "1 save" vs "2 saves"
- [x] 4.26 **RED** `TestConcurrentAccess` — 100 concurrent calls don't crash, counters are correct
- [x] 4.27 **GREEN** Create `tests/Engram.Mcp.Tests/SessionActivityTests.cs` with all 7 tests
- [x] 4.28 Update `EngramToolsTests.cs` constructor to pass `SessionActivity` instance (or null for tests that don't need it)

## Phase 5: Integration & Verification

- [ ] 5.1 Run full test suite — all existing tests pass
- [ ] 5.2 Run SQLite tests — all pass
- [ ] 5.3 Run PostgreSQL tests — all pass
- [ ] 5.4 Manual test: `engram serve` + `engram mcp` — basic functionality works
- [ ] 5.5 Manual test: concurrent mem_save calls — no SQLITE_BUSY errors
- [ ] 5.6 Manual test: `mem_session_end` includes activity score
- [ ] 5.7 Manual test: project detection in monorepo with single child repo
- [ ] 5.8 Manual test: project detection in monorepo with multiple child repos (ambiguous)
- [ ] 5.9 Verify no breaking changes to HTTP API
- [ ] 5.10 Verify no breaking changes to MCP tool signatures
- [ ] 5.11 Update ROADMAP.md — mark Phase 1 as completed
- [ ] 5.12 Create PR with descriptive title and body
