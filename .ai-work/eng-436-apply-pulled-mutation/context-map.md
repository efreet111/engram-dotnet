# Context Map — ENG-436: ApplyPulledMutationAsync stub fix

**Feature slug:** `eng-436-apply-pulled-mutation`
**Work item:** ENG-436 (P0 Bug)
**Created:** 2026-06-23 (OSS Launch Audit)
**Updated:** 2026-06-24 (Discovery)

---

## ⚠️ Critical Discovery: The fix ALREADY EXISTS in code

**The implementation is NOT a stub.** `SqliteStore.ApplyPulledMutationAsync` (line 2145) has a full implementation with dispatch to 5 private methods, committed in `c9dd8808` (2026-06-09, ENG-425).

However, **NO tests exist** for any of these methods — the `SqliteStoreApplyPulledTests.cs` file was never committed. The rework ticket for FK insert issue was left open.

---

## Current State

### SqliteStore.cs — `ApplyPulledMutationAsync` (line 2145-2166)

```csharp
public Task ApplyPulledMutationAsync(string targetKey, SyncMutation mutation, CancellationToken ct = default)
{
    switch (mutation.Entity)
    {
        case "session" when mutation.Op == "upsert":
            ApplySessionUpsert(mutation);       // line 2168
            break;
        case "observation" when mutation.Op == "upsert":
            ApplyObservationUpsert(mutation);   // line 2193
            break;
        case "observation" when mutation.Op == "delete":
            ApplyObservationDelete(mutation);   // line 2257
            break;
        case "prompt" when mutation.Op == "upsert":
            ApplyPromptUpsert(mutation);        // line 2272
            break;
        case "prompt" when mutation.Op == "delete":
            ApplyPromptDelete(mutation);        // line 2318
            break;
    }
    return Task.CompletedTask;
}
```

The method is synchronous — calls sync private methods and returns `Task.CompletedTask`. This matches the pattern used across SqliteStore (14 occurrences, TD-014).

### Private method implementations

| Method | Lines | Entity | Op | Description |
|--------|-------|--------|----|-------------|
| `ApplySessionUpsert` | 2168-2191 | session | upsert | INSERT ... ON CONFLICT(id) DO UPDATE |
| `ApplyObservationUpsert` | 2193-2255 | observation | upsert | Lookup by sync_id → UPDATE existing or INSERT new. FK deferral on SQLITE_CONSTRAINT. |
| `ApplyObservationDelete` | 2257-2270 | observation | delete | Soft-delete (SET deleted_at) by sync_id |
| `ApplyPromptUpsert` | 2272-2316 | prompt | upsert | Lookup by sync_id → UPDATE existing or INSERT new. FK deferral on SQLITE_CONSTRAINT. |
| `ApplyPromptDelete` | 2318-2330 | prompt | delete | Soft-delete (SET deleted_at) by sync_id |
| `InsertDeferred` | 2332-2341 | any | any | INSERT into `sync_apply_deferred` for FK retry |
| `ReplayDeferredAsync` | 2350+ | any | any | Replay deferred rows (up to 5 retries) |

### Payload records (line 2344-2348)

```csharp
private record SessionPullPayload(string Id, string? Project, string? Directory, string? EndedAt, string? Summary, string? StartedAt);
private record ObservationPullPayload(string SyncId, string? SessionId, string? Type, string? Title, string? Content, string? ToolName, string? Project, string? Scope, string? TopicKey, string? OccurredAt);
private record ObservationDeletePayload(string SyncId);
private record PromptPullPayload(string SyncId, string? SessionId, string? Content, string? Project, string? OccurredAt);
private record PromptDeletePayload(string SyncId);
```

### JsonPullOpts (line 35-39)

```csharp
private static readonly JsonSerializerOptions JsonPullOpts = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true
};
```

**Note:** `SnakeCaseLower` was added in ENG-428 (commit `628be52`, after `c9dd8808`). Before the fix, payloads with snake_case fields would deserialize to `null`, causing silent returns in all `Apply*` methods.

---

## SyncManager Flow

### `SyncManager.PullAsync` (line 257-286)

```
foreach (var mutation in pullResult.Mutations)
    syncMutation = new SyncMutation(seq, targetKey, entity, entityKey, op, payload, "pull", project, occurredAt, null)
    store.ApplyPulledMutationAsync(targetKey, syncMutation)   ← line 271
    sinceSeq = mutation.Seq
    totalPulled++

UpdateSyncStateAsync(targetKey, sinceSeq)   ← cursor advances
```

**Critical detail**: The cursor (`last_pulled_seq`) advances AFTER all mutations in a batch are applied. If a mutation fails mid-batch, the cursor still advances because `ApplyPulledMutationAsync` does not report errors back. The method always returns `Task.CompletedTask` regardless of success/failure.

### `SyncMutation` record (ILocalSyncStore.cs:96-106)

```csharp
public sealed record SyncMutation(
    long Seq,
    string TargetKey,
    string Entity,       // "session" | "observation" | "prompt"
    string EntityKey,    // sync_id (canonical ID from server)
    string Op,           // "upsert" | "delete"
    string Payload,      // JSON payload matching the private record types
    string Source,       // "pull" for pulled mutations
    string Project,
    DateTime OccurredAt,
    string? AckedAt);
```

---

## PostgresStore Reference Implementation

PostgresStore has the same logic pattern but as async methods (line 2337-2558):

| Method | Lines | Notes |
|--------|-------|-------|
| `ApplyMutationsToDataStoreAsync` | 2337-2371 | Groups ops by entity and applies in order (sessions → observations → prompts) |
| `ApplySessionUpsertAsync` | 2373-2410 | Uses `NULLIF` + `COALESCE` for Postgres compatibility |
| `ApplyObservationUpsertAsync` | 2412-2481 | Same lookup-by-sync_id pattern, async |
| `ApplyObservationDeleteAsync` | 2483-2495 | Soft delete with timestamp formatting |
| `ApplyPromptUpsertAsync` | 2497-2545 | Same lookup pattern, async |
| `ApplyPromptDeleteAsync` | 2547-2558 | Soft delete |

**Key difference**: PostgresStore applies mutations as a GROUP (all sessions, then all observations, then all prompts) respecting FK order within a single transaction. SqliteStore applies them INDIVIDUALLY (each mutation in its own `WithTx`), relying on FK deferral for ordering issues.

**Key difference**: PostgresStore logs each apply with `_logger?.LogInformation(...)`. SqliteStore's `Apply*` methods have NO logging — failures are invisible unless they throw.

---

## What Entities Need Upsert

| Entity | Table | FK | Upsert key | Upsert strategy |
|--------|-------|----|-----------|-----------------|
| session | `sessions` | None (root) | `id` (EntityKey) | INSERT ON CONFLICT(id) DO UPDATE |
| observation | `observations` | `session_id` → sessions.id | `sync_id` | Lookup by sync_id → UPDATE or INSERT. FK miss → defer to `sync_apply_deferred` |
| prompt | `user_prompts` | `session_id` → sessions.id | `sync_id` | Lookup by sync_id → UPDATE or INSERT. FK miss → defer to `sync_apply_deferred` |

---

## Reusable Patterns Found

- `src/Engram.Store/SqliteStore.cs:2145-2348` — Full `ApplyPulledMutationAsync` with 5 dispatch methods. **The implementation is already done.**
- `src/Engram.Store/PostgresStore.cs:2337-2558` — Reference async implementation of the same pattern. **SqliteStore version is structurally near-identical** (only difference: synchronous SQLite vs async Npgsql).
- `src/Engram.Store/SqliteStore.cs:2706-2714` — `WithTx(Action<SqliteTransaction> fn)` pattern used by all apply methods.

---

## FlowDoc Context

- No `.flowforge.json` found — project uses `.ai-work/` only
- BACKLOG.md references TD-013 (stale — references old line numbers 1910-1916)
- `docs/TECHNICAL-DEBT.md` TD-013 also stale

---

## Open Issues & Risks

### 1. Known FK insert issue (UNRESOLVED — open rework ticket)

From `.ai-work/eng-421-apply-pulled-mutation/rework_ticket.md` (OPEN status):

- Tests 3.3 (Observation upsert — new) and 3.7 (Prompt upsert — new) fail
- INSERT with existing `session_id` fails silently — no exception, no deferral
- FK deferral works when `session_id` does NOT exist (test 3.9 passes)
- **Hypothesis**: The `JsonPullOpts` deserialization fix (ENG-428, `SnakeCaseLower`) may have resolved this — was applied AFTER the implementation. Verification needed.

### 2. No tests exist

- **0 tests** for any `Apply*` method in the repo
- `SqliteStoreApplyPulledTests.cs` (~11 tests) was planned in ENG-421 but never committed
- The 63 tests in `SqliteStoreTests.cs` do NOT cover pulled mutation apply
- The 2-client pull Docker test (`test-2client-pull.sh`) is lenient — passes if data is on server OR client

### 3. Silent failures

- `Apply*` methods return silently if payload deserialization fails (`if (payload is null) return;`)
- No logging in any SqliteStore `Apply*` method (contrast with PostgresStore which has `_logger?.LogInformation(...)`)
- `ApplyPulledMutationAsync` always returns `Task.CompletedTask` — no way for `SyncManager.PullAsync` to detect failures
- Cursor advances regardless of success/failure → data loss is invisible

### 4. No error handling for non-FK failures

- The catch clause only handles `SqliteException` with error code 19 (FK constraint)
- Other `SqliteException` codes (e.g., NOT NULL constraint, unique constraint, CHECK constraint) would propagate and crash the sync cycle

### 5. BACKLOG entry is stale

- Line references (1910-1916) don't match current code (2145-2166)
- Description claims method is a stub — it's not
- The real issue is: **untested code with possible deserialization edge cases**

---

## Recommended Focus for ENG-436

Given the code already exists, ENG-436 should shift from "implement the stub" to:

1. **Write the 11 unit tests** from the ENG-421 plan (verify all 5 apply methods work)
2. **Verify the FK insert issue** (re-run tests 3.3/3.7 with current `JsonPullOpts` — may now pass)
3. **Add logging** to SqliteStore `Apply*` methods (parity with PostgresStore)
4. **Add test for 2-client pull with SQLite** — ensure `test-2client-pull.sh` validates Client-B has the data locally (not just on server)
5. **Update BACKLOG.md** and **TECHNICAL-DEBT.md** TD-013 to reflect current state

---

## Verdict

The implementation IS present. The gap is:
- **No tests** (45% of the work)
- **Possible deserialization edge case** (15%)
- **No logging / silent failure** (25%)
- **Stale documentation** (15%)

This is **M effort** as estimated — focused on test writing and verification, not implementation.

---

**CLEAR**
