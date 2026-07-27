# Summary — ENG-459: Sync Failure Feedback

> **Feature**: Sync failure feedback — 4 notification channels
> **Commit**: `712242a8` — `feat(ENG-459): sync failure feedback — 4 notification channels`
> **Status**: ✅ Done (pushed to origin/main)
> **Duration**: 2026-07-16 → 2026-07-18 (spec+plan+dev+verify+close)

---

## 1. Problem

`SyncManager` runs as a `BackgroundService`. When sync fails repeatedly, errors are only logged to `ILogger` (stderr) and persisted in `sync_state.last_error` (DB) — both invisible to the end user. The user believes their memories are syncing, but they never reach the remote server. **Silent data loss.**

Real incident: user worked for days creating memories thinking they were syncing. Upon manual check, sync had been blocked since day one — 38 pending mutations.

## 2. What was implemented

4 notification channels covering the full interaction spectrum:

| # | Channel | Who sees it | When |
|---|---------|------------|------|
| 1 | **Notification file** (`~/.engram/sync-notifications.log`) | User (off-session diagnosis) | On threshold crossing (ConsecutiveFailures == 3) |
| 2 | **`/sync/status` endpoint** (`suggested_action` field) | CLI, scripts, tools | Every status query |
| 3 | **`engram sync status` CLI** (improved output) | User directly | Every command execution |
| 4 | **MCP diagnostics** (DiagnosticService `sync_health` + stderr warning) | LLM agent → user | Every MCP diagnosis / init |

### Files changed (16 files, +975 / -47 lines)

**Source (10 files)**:
- `SyncManager.cs` — notification writer, `LastError` property, threshold crossing logic
- `SyncManagerConfig.cs` — `NotificationThreshold`, `NotificationFileMaxEntries` config
- `SyncMetrics.cs` — `ClearError()` on recovery
- `ISyncStatusProvider.cs` — `LastError` property added
- `CloudSyncEndpoints.cs` — `suggested_action` generation (server-side via `HttpContext.Request`)
- `MutationDtos.cs` — `SuggestedAction` field in `StatusHealthBody`
- `Program.cs` (CLI) — `SyncStatusFormatter` extracted, warning + suggested action display
- `DiagnosticService.cs` — `sync_health` component
- `EngramTools.cs` — stderr warning on init if sync blocked

**Tests (6 files, 25 new tests)**:
- `SyncManagerTests.cs` — notification file, LastError, recovery, blocked scenarios
- `SyncStatusEndpointTests.cs` — suggested_action per health state
- `SyncStatusCliTests.cs` — CLI output formatting
- `DiagnosticServiceTests.cs` — sync_health component
- `EngramToolsTests.cs` — stderr warning on init

## 3. Test results

| Layer | Result |
|-------|--------|
| **T2 (unit tests)** | 712 passing (25 new) |
| **T3 (Postgres/Testcontainers)** | 45 passing |
| **Verify (Phase 3b)** | PASS |

## 4. Key decisions

| Decision | Rationale |
|----------|-----------|
| **Exact threshold crossing (`== 3`)** vs `>= 3` | Prevents notification spam on every backoff cycle. Write once when threshold is crossed, not on every subsequent failure. |
| **`LastError` delegates to `_metrics.LastError`** | No separate field needed — `SyncMetrics` already tracks this. Avoids state duplication. |
| **"blocked" detection via `state?.Lifecycle == "blocked"`** | `MarkSyncBlockedAsync` sets lifecycle but doesn't change `SyncPhase`, so phase alone is insufficient for blocked detection. |
| **`suggested_action` generated server-side** | The endpoint knows `server_url` via `HttpContext.Request`. SyncManager shouldn't know about URLs. Clean separation of concerns. |
| **`PushAsync` returns `bool`** | Prevents lifecycle overwrite during blocked cycles — caller can distinguish "push attempted and failed" from "push skipped because blocked". |
| **`SyncMetrics.ClearError()` on recovery** | Prevents stale error messages from persisting after sync recovers. Called when a successful cycle completes. |
| **Notification file: JSON Lines + rotation (max 10)** | Machine-parseable, consistent with project style. Rotation prevents unbounded growth. Best-effort write (never blocks sync cycle). |
| **`SyncStatusFormatter` extracted in CLI** | Testability — pure function that takes JSON and produces formatted string, no I/O dependencies. |

## 5. Lessons learned

1. **Exact crossing vs >= comparison for threshold notifications**: Using `== threshold` (exact crossing) instead of `>= threshold` prevents writing the same notification on every retry cycle. Combined with a `_notificationWritten` flag, this ensures exactly one notification per threshold crossing.

2. **Bool return for state machine transitions**: When a method like `PushAsync` can be "skipped" (blocked state) vs "attempted and failed", returning `bool` lets the caller distinguish these cases without inspecting internal state. This prevented a lifecycle overwrite bug.

3. **Server-side action generation**: Generating `suggested_action` in the HTTP endpoint (not in SyncManager) keeps the background service unaware of URLs and HTTP concerns. The endpoint constructs the server URL from `HttpContext.Request`, which is always correct.

4. **Stale error prevention**: Calling `ClearError()` on recovery (successful cycle) is critical — without it, `LastError` would show the old error even when sync is healthy, confusing users and diagnostics.

5. **Extract formatters for testability**: The CLI's `SyncStatusFormatter` is a pure function (JSON in → string out), making it trivially testable without HTTP mocks or console capture.

## 6. Follow-up recommendations

- **ENG-455** (`flowforge sync connect` with auto-enroll): The `suggested_action` for non-enrolled projects currently shows a raw `curl` command. Once ENG-455 is implemented, it could suggest `flowforge sync connect` instead for better UX.
- **Sanitize `last_error`**: Consider stripping absolute paths from `last_error` before exposing in `suggested_action` (noted in spec STRIDE analysis).
- **Notification file reader**: A future `engram sync notifications` command could parse and display the notification file in a human-friendly format.

---

## 7. Session Closure

> **Closed:** 2026-07-18 · **Commit:** `712242a` → `origin/main`

### Models used

| Phase | Model | Notes |
|-------|-------|-------|
| forge-plan (Phase 2) | mimo-v2.5-pro | User-requested; produced compact 7-task plan (~5h) from original 11-task (~9h) |
| forge-dev (Phase 3) | mimo-v2.5-pro | Implemented all 7 tasks, 25 tests |
| forge-verify (Phase 3b) | mimo-v2.5-pro | Verdict PASS, 3 post-verify recommendations applied |
| forge-memory (Phase 4) | qwen3.7-plus | Session closure |

### Workflow decisions

| Decision | Rationale |
|----------|-----------|
| **`plan-compact.md` as executive summary** | Original plan.md was 740 lines — too long for quick reference. Compact version (300 lines) used as working document during dev/verify phases. |
| **No `.ai-work/` artifacts in git** | These are process documentation, not product code. Kept in session memory / local filesystem only. Branch `docs/eng-459-session-artifacts` was not merged. |
| **Post-verify recommendations batched** | 3 verify recommendations + 2 additional tests applied in a single commit rather than separate commits, reducing git noise. |

### Reusable patterns (from this session)

1. **Bool return for skippable state transitions** — When a method can be "skipped" (blocked) vs "attempted and failed", return `bool` so callers can distinguish without inspecting internal state. Prevented a lifecycle overwrite bug in `PushAsync`.

2. **Exact threshold crossing (`== N`) for notifications** — Use `== threshold` with a flag, not `>= threshold`, to fire notifications exactly once per crossing. Avoids spam on every retry cycle.

3. **ClearError() on recovery** — Always clear stale error state when a system recovers. Without this, diagnostics show old errors even when healthy.

4. **Server-side action generation** — Generate user-facing suggestions (URLs, commands) in the HTTP layer where request context is available, not in background services. Keeps concerns separated.

5. **Extract formatters as pure functions** — CLI output formatting extracted to testable pure functions (JSON → string). No I/O dependencies, trivially testable.

6. **Plan compact for long specs** — When plan.md exceeds ~500 lines, create a `plan-compact.md` executive summary. Use compact version during dev/verify; keep full plan for reference.

### Final status

- **ENG-459**: ✅ Done (merged to main)
- **BACKLOG.md**: Updated (ENG-459 marked Done)
- **Tests**: 712 unit + 45 Postgres — all passing
- **Artifacts**: `.ai-work/eng-459-sync-failure-feedback/` (local only, not in git)
