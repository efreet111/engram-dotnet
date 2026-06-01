# FlowForge Context Map — Critical REST API Bugfix

**Feature Slug:** `critical-rest-api-bugfix`  
**Source:** Manual testing on `http://192.168.0.178:7437`  
**Date:** 2026-05-31  
**Backend:** PostgresStore (implements `IStore`, `ICloudMutationStore`, `ICloudChunkStore`)

---

## Summary Table

| # | Endpoint | Root Cause | Severity | Fix Approach | Has Test? |
|---|----------|-----------|----------|-------------|-----------|
| 1 | `POST /sync/mutations/push` | NRE on `body.Entries.Count` when `Entries` is null after JSON deser | **P0** | Null-check `Entries` before `.Count` | Partial (no null-Entries test) |
| 2 | `POST /retention/prune` | Behavior verified OK — TTL/age buckets respected; no project guard | **P2** | Add project-safety check; improve tests | Yes (RetentionStoreTests) |
| 3 | `DELETE /sessions/{id}` | Blocked by soft-deleted observations (counts ALL obs, incl. deleted) | **P2** | Exclude soft-deleted from count: `AND deleted_at IS NULL` | Yes (tests exist) |
| 4 | `GET /prompts/recent` + `GET /prompts/search` | No user-scoping; strict project match — data exists but filter misses | **P1** | Add `X-Engram-User` scoping; normalize project; match observations pattern | Partial (no scope test) |

---

## Bug 1: POST /sync/mutations/push — NullReferenceException

### Files
- `src/Engram.Server/CloudSyncEndpoints.cs` — route + handler
- `src/Engram.Server/Dtos/MutationDtos.cs` — data contract

### Root Cause

Route mapping at line 30 injects `IStore store` and checks cast:

```csharp
// Line 30-36
app.MapPost("/sync/mutations/push", async (HttpContext ctx, IStore store) =>
{
    if (store is not ICloudMutationStore cloudStore)
        return Results.StatusCode(501);
    return await HandleMutationPushAsync(ctx, cloudStore);
});
```

`PostgresStore` implements `ICloudMutationStore`✔ so the cast succeeds. Inside `HandleMutationPushAsync`:

```csharp
// Line 140-141
var body = await ReadJsonAsync<PushRequestBody>(ctx, 8 * 1024 * 1024);
if (body is null || body.Entries.Count == 0)   // ← NRE here
```

`PushRequestBody` is a positional record:

```csharp
public sealed record PushRequestBody(
    [property: JsonPropertyName("entries")] IReadOnlyList<MutationEntryBody> Entries,
    [property: JsonPropertyName("created_by")] string? CreatedBy = null);
```

`Entries` is typed as `IReadOnlyList<...>` (reference type, default `null`). When a JSON payload omits `entries` entirely, or sends `"entries": null`, `System.Text.Json` deserialization sets `Entries = null`. Then `body.Entries.Count` throws `NullReferenceException`.

**The "no active enrollment" association:** The NRE is triggered when the push client sends a malformed payload (missing entries) — unrelated to enrollment state, though the test scenario may have coincided with a non-enrolled project.

### Severity: P0 🔴
- Crashes the endpoint with HTTP 500 for any client that omits the `entries` field
- The global error middleware catches it and returns a 500 with stack trace in the response body
- ONLY affects the push endpoint; pull and other sync endpoints are fine

### Fix

**`CloudSyncEndpoints.cs` line 141 — null-safe guard:**

```csharp
// Before:
if (body is null || body.Entries.Count == 0)

// After:
if (body is null || body.Entries is null || body.Entries.Count == 0)
```

Alternatively use pattern: `body?.Entries?.Count ?? 0 == 0`.

### Existing Test Coverage

**File:** `tests/Engram.Server.Tests/CloudSyncEndpointsTests.cs`

| Test | Covers | Missing |
|------|--------|---------|
| `Push_EmptyBatch_Returns400_EmptyBatch` | `entries: []` → 400 | Does NOT test `entries: null` or omitted |
| `Push_BatchTooLarge_Returns400` | 101 entries → 400 | — |
| `Push_EntryWithoutProject_Returns400` | Missing project field → 400 | — |
| `Push_RelationMissingRequiredFields_Returns400` | Bad relation payload → 400 | — |
| `Push_PauseGate_Returns409` | Paused project → 409 | — |
| `Push_Success_Returns200` | Happy path → 200 | — |

**Missing test:** A test sending `{"created_by": "test"}` (no `entries`) or `{"entries": null}` should expect 400, not 500.

---

## Bug 2: POST /retention/prune — Prune Behavior Verification

### Files
- `src/Engram.Server/EngramServer.cs` lines 538-548 — endpoint handler
- `src/Engram.Store/SqliteStore.cs` lines 1206-1249 — prune logic (Sqlite)
- `src/Engram.Store/PostgresStore.cs` lines 1016-1061 — prune logic (Postgres)
- `src/Engram.Store/RetentionConfig.cs` — TTL definitions
- `src/Engram.Store/Models.cs` lines 203-237 — data models

### Findings

**Age buckets:** ✅ RESPECTED. Each type has its own TTL:

| Type | TTL |
|------|-----|
| `tool_use`, `file_change`, `command` | 30 days |
| `learning`, `discovery` | 60 days |
| `bugfix`, `pattern` | 90 days |
| `decision`, `architecture`, `session_summary` | **Never expire** |

The cut-off is computed as `DateTime.UtcNow - ttl`. Observations older than the cut-off are pruned.

**Topic key protection:** Observations with a topic_key (`topic_key IS NOT NULL AND topic_key != ''`) are **never pruned**, regardless of age. This is intentional — topic-anchored observations are treated as reference material.

**Project safety:** ⚠️ NO project-level guard. The prune iterates by type and deletes eligible observations across ALL projects. If you have two projects (e.g., "engram-dotnet" and "some-old-project"), pruning `tool_use` older than 30d will delete from both. There is no `project` filter parameter in the endpoint.

**Prune SQL (Postgres):**
```sql
SELECT COUNT(*) FROM observations
WHERE type = @type AND deleted_at IS NULL
  AND (topic_key IS NULL OR topic_key = '')
  AND created_at::timestamptz < @cutoff::timestamptz
```

**Prune SQL (Sqlite):**
```sql
SELECT COUNT(*) FROM observations
WHERE type = @type AND deleted_at IS NULL
  AND (topic_key IS NULL OR topic_key = '')
  AND datetime(created_at) < datetime(@cutoff)
```

### Severity: P2 🟡
- Behavior is correct for age buckets and topic key protection
- Missing project-level safety (could prune from wrong project if running multi-project)
- The 47 items pruned in the test were likely valid tool_use/file_change observations older than 30d

### Recommendations

1. **Add `?project=` filter** to the retention/prune endpoint (optional, default to all projects)
2. **Dry-run before real prune** — existing behavior is correct, but test revealed no dry-run was used before the 47-item prune
3. **No code fix needed for age buckets** — they work correctly

### Existing Test Coverage

**File:** `tests/Engram.Store.Tests/RetentionStoreTests.cs`

| Test | Verifies |
|------|----------|
| `GetRetentionStats_ReturnsStats` | Stats after seeding 3 observations |
| `PruneOldObservations_DryRun_DoesNotDelete` | Dry-run preserves data |
| `PruneOldObservations_PreservesNonExpiringTypes` | decision/architecture never pruned |
| `PruneOldObservations_RealRun_BackdatedObservations` | Old tool_use (60d) is pruned, fresh is kept |

**Missing:** Multi-project prune test, project-scoped prune, topic_key preservation test with non-expiring types.

---

## Bug 3: DELETE /sessions/{id} — Cascade Behavior

### Files
- `src/Engram.Server/EngramServer.cs` lines 235-253 — handler
- `src/Engram.Store/SqliteStore.cs` lines 486-528 — Sqlite implementation
- `src/Engram.Store/PostgresStore.cs` lines 392-449 — Postgres implementation

### Findings

The delete-session flow is identical in both stores:

```
1. Verify session exists          → 404 if not found
2. COUNT observations for session → 409 if count > 0 (includes SOFT-deleted!)
3. Soft-delete associated prompts → UPDATE user_prompts SET deleted_at = NOW()
4. Disable FK triggers            → PRAGMA foreign_keys = OFF / SET session_replication_role = replica
5. DELETE FROM sessions WHERE id  → hard-delete
6. Re-enable FK triggers
```

**Prompts:** ✅ **Soft-deleted** — `SET deleted_at = datetime('now') WHERE session_id = @id AND deleted_at IS NULL`. They remain in the database with `deleted_at` set. They still reference the (now deleted) session via `session_id`. Not orphaned because they keep their FK reference, and FK triggers were disabled during the delete.

**Observations:** ❌ **Block deletion entirely** — even if ALL observations are already soft-deleted. The count at step 2 uses:

```sql
SELECT COUNT(*) FROM observations WHERE session_id = @id
```

This counts ALL rows, including soft-deleted ones (`deleted_at IS NOT NULL`). The test `DeleteSession_BlockedBySoftDeletedObservations` at `SqliteStoreTests.cs:858` explicitly expects this behavior and passes — it was ported from Go parity. Go's original behavior was to block on any observations (soft-deleted included) because the FK constraint requires the row to exist.

**However:** The FK constraint is subsequently disabled in step 4, so technically the session could be deleted even with soft-deleted observations. The guard in step 2 is a separate application-level check, not an FK check.

**Orphaned records:** ✅ **No orphaned prompts or observations** — prompts are explicitly soft-deleted before the session delete; observations block the delete entirely. No orphaned data.

### Severity: P2 🟡
- The behavior is intentional (Go parity) but may be too strict
- Users who soft-delete all observations still can't delete the session
- Not strictly a bug, but a usability limitation

### Fix Options

**Option A (Stricter parity):** Keep as-is — matches Go behavior.

**Option B (Looser):** Change the count to exclude soft-deleted observations:

```csharp
// Line 501-503 in SqliteStore.cs
var totalObs = QueryScalar<long>(tx,
    "SELECT COUNT(*) FROM observations WHERE session_id = @id AND deleted_at IS NULL",
    Param("@id", id));
```

This frees sessions that only have soft-deleted observations. Requires updating the test at `SqliteStoreTests.cs:858` to expect success instead of `SessionDeleteBlockedException`.

### Existing Test Coverage

**File:** `tests/Engram.Store.Tests/SqliteStoreTests.cs`

| Test | Verifies |
|------|----------|
| `DeleteSession_EmptySession_Succeeds` | Empty session → 200 |
| `DeleteSession_NotFound_Throws` | Ghost session → 404 |
| `DeleteSession_HasActiveObservations_Throws` | Active observations → 409 |
| `DeleteSession_DeletesAssociatedPrompts` | Prompts soft-deleted after session delete |
| `DeleteSession_BlockedBySoftDeletedObservations` | Soft-deleted obs still block (intentional) |

**File:** `tests/Engram.Server.Tests/EngramServerTests.cs`

| Test | Verifies |
|------|----------|
| `DELETE_sessions_success_Returns200` | Full HTTP roundtrip |
| `DELETE_sessions_nonexistent_Returns404` | HTTP 404 |
| `DELETE_sessions_has_observations_Returns409` | HTTP 409 |

**File:** `tests/Engram.Postgres.Tests/PostgresStoreTests.cs` — mirrors Sqlite tests for Postgres.

---

## Bug 4: GET /prompts/recent + /prompts/search — Empty Results

### Files
- `src/Engram.Server/EngramServer.cs` lines 406-422 — handlers
- `src/Engram.Store/SqliteStore.cs` lines 960-1001 — Sqlite queries
- `src/Engram.Store/PostgresStore.cs` lines 798-827 — Postgres queries
- `src/Engram.Store/HttpStore.cs` lines 207-221 — HTTP client proxy

### Root Cause

**Missing user-scoping (primary issue).**

Compare prompt handlers with observation handlers:

```csharp
// HandleRecentPrompts (line 406) — NO user scoping
var project = ctx.Request.Query["project"].FirstOrDefault();
var limit   = QueryInt(ctx, "limit", 20);
var result  = await store.RecentPromptsAsync(project, limit);

// HandleRecentObservations (line 299) — HAS user scoping
var project = ctx.Request.Query["project"].FirstOrDefault();
var scope   = ctx.Request.Query["scope"].FirstOrDefault();
var userId  = GetUserId(ctx);  // ← reads X-Engram-User
var result  = await store.RecentObservationsAsync(project, NormalizeScope(scope, userId), limit);
```

`HandleRecentPrompts` and `HandleSearchPrompts` never call `GetUserId(ctx)` and never apply `NormalizeScope`. The store queries filter only by `deleted_at IS NULL` and optionally `project = @proj`. There is **no user isolation** for prompts.

**The empty response scenario:** When testing on a multi-user server (`192.168.0.178:7437`), if the client sends prompts and then queries `/prompts/recent`, the results depend solely on the `?project=` parameter:

- **Without `?project=`:** Returns ALL prompts across all projects/users (may be empty if no prompts exist in the DB at all)
- **With `?project=X`:** Returns prompts for that project, but only if the project name **exactly matches** the stored (normalized) project name

**Secondary issue — strict project match:** PostgresStore query uses `AND project = @proj` (exact match). However, prompts are stored with normalized project names (`Normalizers.NormalizeProject` lowercases and replaces underscores/hyphens). If the client sends a project name that normalizes differently, the query misses.

### Severity: P1 🟠
- Returning empty when data exists is a functionality break
- Affects any client relying on prompt retrieval
- No workaround via `X-Engram-User` since the handler ignores it entirely

### Fix

1. **Add user scoping to prompt handlers** — mirror observations pattern:

```csharp
// In HandleRecentPrompts:
var userId = GetUserId(ctx);
var scope  = ctx.Request.Query["scope"].FirstOrDefault();
// ... pass scope to store or filter by personal scope

// In HandleSearchPrompts:
var userId = GetUserId(ctx);
```

2. **Verify project normalization consistency** — ensure the client always sends normalized project names in queries

3. **Consider adding a `deleted_at` check** — ensure prompts soft-deleted via session deletion are excluded (they already are, per the `WHERE deleted_at IS NULL` clause)

### Existing Test Coverage

**File:** `tests/Engram.Server.Tests/EngramServerTests.cs`

| Test | Verifies |
|------|----------|
| `POST_prompts_Creates_And_GET_Returns` | Creates prompt, queries `/prompts/recent?project=test-proj` |

This test uses `SqliteStore` (single-user) and always passes a `project` filter. It does NOT test:
- Prompts without a project filter
- Prompts with `X-Engram-User` header
- Prompts across different users
- `/prompts/search` endpoint at the HTTP level

**File:** `tests/Engram.Store.Tests/SqliteStoreTests.cs`

| Test | Verifies |
|------|----------|
| `SearchPrompts_FindsMatchingPrompts` | FTS5 search by content |
| `AddPromptAsync_StoresAndReturnsPrompt` | Basic create + recent |

No multi-user or scope tests for prompts.

---

## Manual Testing Steps

### Bug 1 — Push NRE
```
# Should return 400, not 500:
curl -X POST http://192.168.0.178:7437/sync/mutations/push \
  -H "Content-Type: application/json" \
  -d '{"created_by":"test"}'                          # missing entries → expect 400

curl -X POST http://192.168.0.178:7437/sync/mutations/push \
  -H "Content-Type: application/json" \
  -d '{"entries":null,"created_by":"test"}'            # null entries → expect 400

curl -X POST http://192.168.0.178:7437/sync/mutations/push \
  -H "Content-Type: application/json" \
  -d '{"entries":[],"created_by":"test"}'              # empty entries → expect 400 (already works)
```

### Bug 2 — Retention Prune
```
# Verify age buckets with dry-run:
curl -X POST http://192.168.0.178:7437/retention/prune?dry_run=true \
  -H "Content-Type: application/json" \
  -d '{"type":"tool_use"}'

# Verify project isolation:
# First check how many observations exist per project via GET /stats
# Then prune with no type filter and verify only the expected types are affected

# Verify topic_key protection:
# Create an observation with topic_key, backdate it, run prune, verify it survives
```

### Bug 3 — Session Delete Cascade
```
# Create session + soft-delete all observations, then try to delete:
curl -X POST http://192.168.0.178:7437/sessions \
  -H "Content-Type: application/json" \
  -d '{"id":"test-cascade","project":"bugfix-test"}'

# Add + soft-delete an observation
curl -X POST .../observations -d '{"session_id":"test-cascade","title":"del","content":"x","type":"manual"}'
curl -X DELETE .../observations/{id}

# Try session delete — currently returns 409 even though observation is soft-deleted
curl -X DELETE http://192.168.0.178:7437/sessions/test-cascade
# Expect: 409 Conflict "session has 1 active observations, cannot delete"
# After fix (Option B): 200 OK

# Verify prompts are soft-deleted (not orphaned):
curl -X GET http://192.168.0.178:7437/prompts/recent?project=bugfix-test
# Expect: empty after session delete
```

### Bug 4 — Prompts Empty Results
```
# Create a prompt:
curl -X POST http://192.168.0.178:7437/prompts \
  -H "Content-Type: application/json" \
  -d '{"session_id":"some-session","content":"test prompt","project":"bugfix-test"}'

# Query WITHOUT project filter:
curl http://192.168.0.178:7437/prompts/recent
# Expect: all prompts across all projects

# Query WITH project filter:
curl "http://192.168.0.178:7437/prompts/recent?project=bugfix-test"
# Expect: the prompt just created

# Query WITH X-Engram-User (after fix):
curl -H "X-Engram-User: tester" "http://192.168.0.178:7437/prompts/recent?project=bugfix-test"
# Expect: scoped results matching the user

# Search:
curl "http://192.168.0.178:7437/prompts/search?q=test&project=bugfix-test"
# Expect: matching prompts
```

---

## Additional Observations

### Shared Root Cause (Bug 1 + Bug 4)
Bug 1 and Bug 4 both relate to the same architectural gap: the `ICloudMutationStore` push endpoint and the prompt endpoints both lack proper null-safety / scoping. However, they are in separate code paths and require distinct fixes.

### Postgres vs Sqlite Differences
| Aspect | SqliteStore | PostgresStore |
|--------|------------|---------------|
| Prompt search | FTS5 (`prompts_fts MATCH`) | ILIKE (`content ILIKE @q`) |
| Session delete FK bypass | `PRAGMA foreign_keys = OFF` | `SET session_replication_role = replica` |
| Retention prune date compare | `datetime(created_at) < datetime(@cutoff)` | `created_at::timestamptz < @cutoff::timestamptz` |

All four bugs affect both backends equally — no backend-specific fixes needed.

### Estimated Fix Effort
| Bug | Lines Changed | Test Updates | Risk |
|-----|--------------|--------------|------|
| 1 | 1 line (CloudSyncEndpoints.cs:141) | +1 test | Low |
| 2 | 0 (verification only) or +10 (project filter) | +2 tests | Low |
| 3 | 1 line (count SQL) | Update 1 test expectation | Low |
| 4 | ~10 lines (add user scoping to handlers) | +3 tests | Medium |

**Total estimate:** ~12-15 lines of production code, ~7 new/modified tests.

---

*Context Map prepared by FlowForge Discovery Agent — ready for CKP-1 review.*
