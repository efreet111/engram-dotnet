---
capability_matrix:
  ai_reasoning:
    - User scoping pattern for prompts (use GetUserId + NormalizeScope vs. filter at store level)
    - Session delete behavior change (Option B — allow deletion with soft-deleted observations only)
    - Project filter for retention/prune (optional parameter vs. breaking change)
  deterministic:
    - Bug #1: Null-check on body.Entries before accessing .Count
    - Bug #2: Age buckets, topic_key protection, and no project-level guard are all correct behavior
    - Bug #3: Change COUNT query to exclude soft-deleted: `deleted_at IS NULL`
    - Bug #4: Add `X-Engram-User` header handling to prompt handlers (must mirror observation handlers)
---

# Spec: Critical REST API Bugfix

## 1. Objective and Scope

Fix 4 bugs in the Engram REST API that were discovered via manual testing on `http://192.168.0.178:7437`. Three bugs have code-level fixes; one (Bug #2) requires only test improvements and a potential optional parameter.

### Bug Summary

| # | Endpoint | Severity | Root Cause | Fix Effort |
|---|----------|----------|------------|------------|
| 1 | `POST /sync/mutations/push` | **P0** 🔴 | NRE when `Entries` is null after JSON deserialization | 1 line |
| 2 | `POST /retention/prune` | **P2** 🟡 | No project-safety check; behavior otherwise correct | 0–10 lines |
| 3 | `DELETE /sessions/{id}` | **P2** 🟡 | Count includes soft-deleted observations, blocking valid deletes | 1 line |
| 4 | `GET /prompts/recent` + `GET /prompts/search` | **P1** 🟠 | Missing `X-Engram-User` scoping; strict project match misses data | ~10 lines |

### In Scope
- Fix NRE in mutation push endpoint (Bug #1)
- Improve retention prune with optional project filter; update tests (Bug #2)
- Fix session delete observation count to exclude soft-deleted (Bug #3)
- Add user scoping to prompt handlers, normalize project name in queries (Bug #4)
- Add or update tests for all four bugs
- Both SqliteStore and PostgresStore backends (no backend-specific fixes needed)

### Out of Scope
- Changes to observation handlers (they already have proper user scoping)
- Retention TTL changes (age buckets work correctly)
- FK constraint changes (triggers are correctly disabled during session delete)
- Changes to how prompts are stored (only how they're queried)
- Adding project isolation to observation queries (not broken, no need)

---

## 2. Functional Requirements (FR)

### Bug #1: POST /sync/mutations/push — NullReferenceException

**Endpoint:** `POST /sync/mutations/push`

**Root Cause:**
`CloudSyncEndpoints.cs` line 46 calls `body.Entries.Count` without checking if `Entries` is null. When a JSON payload omits `entries` entirely or sends `"entries": null`, `System.Text.Json` deserializes `Entries` as null (default for reference-type constructor parameters), causing a `NullReferenceException`.

**Fix Approach:**
File: `src/Engram.Server/CloudSyncEndpoints.cs` line 75

```csharp
// Before:
if (body is null || body.Entries.Count == 0)

// After:
if (body is null || body.Entries is null || body.Entries.Count == 0)
```

**Acceptance Criteria:**
- [ ] `{"created_by":"test"}` (no entries) → HTTP 400, not 500
- [ ] `{"entries":null,"created_by":"test"}` → HTTP 400, not 500
- [ ] `{"entries":[]}` → HTTP 400 (existing behavior, preserved)
- [ ] Valid `{"entries":[...]}` → HTTP 200 or 409 (existing behavior, preserved)
- [ ] No other endpoints affected

**PM-1: Null Entries Payload**
Steps:
1. Send: `curl -X POST http://localhost:7437/sync/mutations/push -H "Content-Type: application/json" -d '{"created_by":"test"}'`
2. Verify HTTP status is **400 Bad Request** (not 500 Internal Server Error)
3. Verify response body does NOT contain a stack trace

**PM-2: Explicit Null Entries Payload**
Steps:
1. Send: `curl -X POST http://localhost:7437/sync/mutations/push -H "Content-Type: application/json" -d '{"entries":null,"created_by":"test"}'`
2. Verify HTTP status is **400 Bad Request** (not 500)
3. Verify response body does NOT contain a stack trace

---

### Bug #2: POST /retention/prune — Project Safety Improvement

**Endpoint:** `POST /retention/prune`

**Root Cause:**
The prune behavior is correct (age buckets respected, topic_key protected). However, there is no project-level guard — pruning iterates across ALL projects. If a server hosts multiple projects, pruning `tool_use` older than 30d will delete from all projects simultaneously.

**Fix Approach:**
1. **Add optional `?project=` filter** to the prune endpoint — if omitted, prune all projects (current behavior, for backward compatibility)
2. **Improve test coverage** to verify project isolation works when project filter is provided

File: `src/Engram.Server/EngramServer.cs` lines 538-548 (handler) — add project parameter

**No code change required for age bucket behavior** — it works correctly.

**Acceptance Criteria:**
- [ ] Without `?project=`, prune works across all projects (current behavior, unchanged)
- [ ] With `?project=X`, prune only affects observations from project X
- [ ] Topic-key-protected observations are never pruned regardless of project filter
- [ ] Non-expiring types (decision, architecture, session_summary) are never pruned
- [ ] Dry-run mode (`?dry_run=true`) returns count without deleting

**PM-3: Prune With Project Filter**
Steps:
1. Create observations in project "test-project-a" and "test-project-b"
2. Backdate some `tool_use` observations in both projects to 40 days old
3. Run prune for type `tool_use` with `?project=test-project-a`
4. Verify observations in `test-project-a` older than 30d are pruned
5. Verify observations in `test-project-b` are untouched

**PM-4: Dry-Run Mode**
Steps:
1. Send: `curl -X POST http://localhost:7437/retention/prune?dry_run=true -H "Content-Type: application/json" -d '{"type":"tool_use"}'`
2. Verify response indicates how many would be pruned
3. Verify observations still exist in database after the call

---

### Bug #3: DELETE /sessions/{id} — Soft-Deleted Observations Blocking Delete

**Endpoint:** `DELETE /sessions/{id}`

**Root Cause:**
The observation count at `PostgresStore.cs` line 500 and `SqliteStore.cs` line 501 uses:
```sql
SELECT COUNT(*) FROM observations WHERE session_id = @id
```
This counts ALL observations including soft-deleted ones (`deleted_at IS NOT NULL`). Users who soft-delete all observations still cannot delete the session.

**Fix Approach:**
File: `src/Engram.Store/PostgresStore.cs` line 500 and `src/Engram.Store/SqliteStore.cs` line 501

Change the count query to:
```sql
SELECT COUNT(*) FROM observations WHERE session_id = @id AND deleted_at IS NULL
```

Apply the same fix in both stores.

**Acceptance Criteria:**
- [ ] Session with active (non-deleted) observations → HTTP 409 (unchanged)
- [ ] Session with ONLY soft-deleted observations → HTTP 200 (was 409, now fixed)
- [ ] Session with no observations → HTTP 200 (unchanged)
- [ ] Prompts associated with the session are soft-deleted before session hard-delete (unchanged)

**PM-5: Session Delete With Soft-Deleted Only**
Steps:
1. Create a session with ID `test-soft-del`
2. Create an observation with `session_id = test-soft-del`
3. Soft-delete the observation: `curl -X DELETE http://localhost:7437/observations/{id}`
4. Try to delete the session: `curl -X DELETE http://localhost:7437/sessions/test-soft-del`
5. Verify HTTP status is **200 OK** (not 409)
6. Verify the session is removed from the database

---

### Bug #4: GET /prompts/recent + GET /prompts/search — Empty Results

**Endpoints:** `GET /prompts/recent`, `GET /prompts/search`

**Root Cause:**
1. **Missing user scoping:** `HandleRecentPrompts` and `HandleSearchPrompts` never call `GetUserId(ctx)` and never apply `NormalizeScope`. Observation handlers already have this pattern. Prompt handlers are missing it.
2. **Strict project match:** PostgresStore query uses `AND project = @proj` (exact match). Prompts are stored with normalized project names. If the client sends a non-normalized project name, the query misses.

**Fix Approach:**
File: `src/Engram.Server/EngramServer.cs` lines 406-422 (HandleRecentPrompts) and 424-444 (HandleSearchPrompts)

1. Add `GetUserId(ctx)` call and `scope` query parameter handling to both handlers, mirroring the observation handlers pattern
2. Pass scope to store methods or filter results client-side (coordinate with store implementation)
3. Verify project normalization consistency between query and storage

Files to check:
- `src/Engram.Server/EngramServer.cs` lines 406-422 — prompt handlers
- `src/Engram.Store/PostgresStore.cs` lines 798-827 — Postgres prompt queries
- `src/Engram.Store/SqliteStore.cs` lines 960-1001 — Sqlite prompt queries

**Acceptance Criteria:**
- [ ] Without `X-Engram-User` header: return prompts for the given project across all users (or return 400 if project filter is missing — decide with PM-7)
- [ ] With `X-Engram-User: userA`: return only prompts created by or scoped to userA
- [ ] With `?project=` filter: filter by exact project match (normalized)
- [ ] Prompts with `deleted_at IS NOT NULL` are excluded (already working)
- [ ] `/prompts/search` mirrors the scoping behavior of `/prompts/recent`

**PM-6: Prompts With User Scoping**
Steps:
1. Create a prompt with `X-Engram-User: userA` for project "test-proj"
2. Create a prompt with `X-Engram-User: userB` for project "test-proj"
3. Query: `curl -H "X-Engram-User: userA" "http://localhost:7437/prompts/recent?project=test-proj"`
4. Verify only userA's prompt is returned (not userB's)
5. Query: `curl -H "X-Engram-User: userB" "http://localhost:7437/prompts/recent?project=test-proj"`
6. Verify only userB's prompt is returned

**PM-7: Prompts Without Project Filter**
Steps:
1. Create prompts in two different projects
2. Query: `curl "http://localhost:7437/prompts/recent"` (no project filter)
3. Verify either all prompts are returned OR HTTP 400 is returned with clear error message about missing project filter
4. Document the chosen behavior in the test

---

## 3. Non-Functional Requirements (NFR)

- NFR-001: Bug #1 fix (null-check) must not regress valid push behavior
- NFR-002: Bug #3 fix (count exclude deleted) must work identically in both PostgresStore and SqliteStore
- NFR-003: Bug #4 fix (user scoping) must be consistent with how observation handlers already implement scoping
- NFR-004: All endpoints must return JSON error responses (not HTML stack traces) for 4xx errors

---

## 4. Test Requirements

### 4.1 Existing Tests to Update

**File:** `tests/Engram.Server.Tests/CloudSyncEndpointsTests.cs`
- [ ] `Push_EmptyBatch_Returns400_EmptyBatch` — already covers empty array, verify no null-Entries test is needed separately

**File:** `tests/Engram.Store.Tests/SqliteStoreTests.cs`
- [ ] `DeleteSession_BlockedBySoftDeletedObservations` — update to expect success after fix (HTTP 200), not `SessionDeleteBlockedException`

**File:** `tests/Engram.Server.Tests/EngramServerTests.cs`
- [ ] `DELETE_sessions_has_observations_Returns409` — verify still returns 409 for active observations (not soft-deleted)

### 4.2 New Tests Required

**File:** `tests/Engram.Server.Tests/CloudSyncEndpointsTests.cs`
- [ ] `Push_NullEntries_Returns400` — send `{"created_by":"test"}` (no entries), expect 400
- [ ] `Push_EntriesFieldNull_Returns400` — send `{"entries":null,"created_by":"test"}`, expect 400

**File:** `tests/Engram.Store.Tests/RetentionStoreTests.cs`
- [ ] `PruneOldObservations_ProjectScoped_OnlyAffectsTargetProject` — prune with `project=X`, verify only project X is affected
- [ ] `PruneOldObservations_ProjectScoped_PreservesOtherProjects` — two projects, prune one, verify the other is untouched

**File:** `tests/Engram.Server.Tests/EngramServerTests.cs`
- [ ] `DeleteSession_SoftDeletedObservationsOnly_Succeeds` — soft-delete all observations, then delete session, expect 200
- [ ] `GET_prompts_recent_WithUserScope_ReturnsUserPrompts` — create prompts for userA and userB, query with `X-Engram-User: userA`, expect only userA's
- [ ] `GET_prompts_recent_WithoutProjectFilter_Returns400` — query without project, expect 400 or documented behavior
- [ ] `GET_prompts_search_WithUserScope_ReturnsUserPrompts` — same scoping for search endpoint

### 4.3 Manual Tests Checklist

| ID | Case | Steps | Expected Result | Done |
|----|------|-------|------------------|------|
| PM-1 | Null entries (missing field) | Push `{"created_by":"test"}` | 400, no stack trace | [x] |
| PM-2 | Null entries (explicit null) | Push `{"entries":null,"created_by":"test"}` | 400, no stack trace | [x] |
| PM-3 | Prune with project filter | Prune type=tool_use for project A | Only A's old obs pruned | [N/A] |
| PM-4 | Prune dry-run mode | Prune with `?dry_run=true` | Count returned, no deletes | [N/A] |
| PM-5 | Session delete (soft-del only) | Soft-del all obs, delete session | 200 OK | [x] |
| PM-6 | Prompts with user scoping | Create 2 user prompts, query each | Only matching user returned | [x] |
| PM-7 | Prompts without project filter | Query `/prompts/recent` without project | 400 or all (documented) | [x] |

> **PM-3 / PM-4**: Fuera de alcance de este cierre (`critical-rest-api-bugfix` = bugs push/sessions/prompts). Retention prune queda para feature aparte.
> **PM-7**: Comportamiento actual — HTTP 200 + `[]` sin `?project=` (con `X-Engram-User`); documentado en `summary.md`.

---

## 5. Risks

| # | Risk | Likelihood | Impact | Mitigation |
|---|------|-----------|--------|------------|
| R1 | Bug #1 fix changes valid empty-array behavior | Low | High | Ensure `{"entries":[]}` still returns 400 (existing test) |
| R2 | Bug #3 fix breaks FK constraints in Postgres | Low | High | Verify `deleted_at IS NULL` filter is applied before the FK-disabled session delete; run Postgres tests |
| R3 | Bug #4 scoping changes existing prompt behavior for single-user setups | Medium | Medium | Add integration test that verifies single-user (no `X-Engram-User`) still works |
| R4 | Bug #4 project normalization mismatch | Medium | High | Verify store uses `Normalizers.NormalizeProject` on both write (prompt creation) and read (queries); add test |
| R5 | Missing test coverage for new behavior | Medium | Medium | Ensure all new tests in section 4.2 are implemented before CKP-4 |

---

## Memory Signal

- type: none
- significance: low
- summary: "Routine bugfix spec with 4 bugs across 2 stores, no contested architectural decisions."