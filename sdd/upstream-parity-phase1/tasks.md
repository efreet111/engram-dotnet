# Tasks: Upstream Parity — Phase 1 (Foundation)

## Phase 1: Project Detection 5-Case

- [ ] 1.1 Create `DetectionResult` record in `src/Engram.Project/Models.cs`
- [ ] 1.2 Create `ProjectSources` constants class in `src/Engram.Project/Constants.cs`
- [ ] 1.3 Implement `scanChildren(string dir)` in `ProjectDetector.cs` — scan depth=1 for git repos, skip noise dirs, 200ms timeout, 20-entry cap
- [ ] 1.4 Implement `DetectProjectFull(string? workingDir = null)` — 5-case algorithm (git_remote → git_root → git_child → ambiguous → dir_basename)
- [ ] 1.5 Update `DetectProject(string? workingDir = null)` — wrapper over `DetectProjectFull`, returns `.project` string
- [ ] 1.6 Implement `NormalizeProject(string name)` — lowercase, trim, collapse whitespace, underscores → hyphens
- [ ] 1.7 Write unit tests for all 5 detection cases
- [ ] 1.8 Write unit tests for `NormalizeProject` edge cases
- [ ] 1.9 Write unit tests for `scanChildren` (timeout, noise dirs, cap)

## Phase 2: Schema Columns

- [ ] 2.1 Add new fields to `Observation` record in `src/Engram.Store/Models.cs` (ReviewAfter, ExpiresAt, Embedding, EmbeddingModel, EmbeddingCreatedAt)
- [ ] 2.2 Update `CREATE TABLE observations` in SqliteStore to include new columns
- [ ] 2.3 Update `CREATE TABLE observations` in PostgresStore to include new columns
- [ ] 2.4 Implement SQLite migration: `ALTER TABLE observations ADD COLUMN ...` for each missing column (idempotent)
- [ ] 2.5 Implement PostgreSQL migration: `ALTER TABLE observations ADD COLUMN ...` for each missing column (idempotent)
- [ ] 2.6 Write migration tests: existing DB gets columns, new DB has columns from start
- [ ] 2.7 Verify existing tests pass with new nullable columns

## Phase 3: Write Queue

- [ ] 3.1 Create `src/Engram.Mcp/WriteQueue.cs` — `IWriteQueue` interface + `WriteQueue` implementation
- [ ] 3.2 Implement `EnqueueAsync<T>` using `Channel<WriteJob>` with capacity 32, `BoundedChannelFullMode.Wait`
- [ ] 3.3 Implement single-consumer worker task that processes jobs sequentially
- [ ] 3.4 Implement `Dispose()` — signal stop, wait for in-progress (5s timeout), cancel pending
- [ ] 3.5 Implement error handling — propagate exceptions to caller, continue processing
- [ ] 3.6 Register `IWriteQueue` as singleton in DI container
- [ ] 3.7 Update all 7 write MCP tools to use `writeQueue.EnqueueAsync()` instead of direct store calls
- [ ] 3.8 Verify read tools still call store directly (no queue)
- [ ] 3.9 Write unit tests: concurrent writes serialize correctly
- [ ] 3.10 Write unit tests: backpressure when channel full
- [ ] 3.11 Write unit tests: graceful shutdown cancels pending jobs
- [ ] 3.12 Write integration test: two concurrent mem_save calls don't cause SQLITE_BUSY

## Phase 4: Session Activity Tracker

- [ ] 4.1 Create `src/Engram.Mcp/SessionActivity.cs` — `ISessionActivity` interface + `SessionActivity` implementation
- [ ] 4.2 Implement `RecordToolCall(string sessionId)` — increment counter, update timestamp
- [ ] 4.3 Implement `RecordSave(string sessionId)` — increment save counter, update timestamp
- [ ] 4.4 Implement `ClearSession(string sessionId)` — remove from dictionary
- [ ] 4.5 Implement `NudgeIfNeeded(string sessionId, TimeSpan? threshold)` — nudge logic per spec
- [ ] 4.6 Implement `ActivityScore(string sessionId)` — formatted string
- [ ] 4.7 Register `ISessionActivity` as singleton in DI container
- [ ] 4.8 Add `ENGRAM_ACTIVITY_NUDGE_THRESHOLD` env var parsing (default: 10)
- [ ] 4.9 Update all MCP tools to call `activity.RecordToolCall(sessionId)` before execution
- [ ] 4.10 Update all write MCP tools to call `activity.RecordSave(sessionId)` after successful write
- [ ] 4.11 Update `mem_session_end` response to include activity score and nudge message
- [ ] 4.12 Write unit tests: nudge triggers on zero-save session
- [ ] 4.13 Write unit tests: no nudge on healthy session (good save ratio)
- [ ] 4.14 Write unit tests: activity score format
- [ ] 4.15 Write unit tests: thread safety (concurrent RecordToolCall from multiple threads)

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
