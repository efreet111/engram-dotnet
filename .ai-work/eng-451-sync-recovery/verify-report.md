# Verify Report — ENG-451 Sync Recovery

**Date**: 2026-07-01 | **Verdict**: **REWORK** | **Spec**: [ADR-007](docs/architecture/adr/ADR-007-sync-blocked-recovery.md)

---

## Verdict

| Check | Status |
|-------|--------|
| Build (static) | ✅ Compiles — no new compiler errors |
| Logic flow (BUG-1) | ✅ Correct |
| Logic flow (BUG-2) | ✅ Correct |
| SQL correctness | ✅ Correct |
| Store compatibility (Postgres/Http) | ✅ Not affected — only SqliteStore implements `ILocalSyncStore` |
| Existing tests pass | ❌ `SyncManager_CycleCompletesSuccessfully_PhaseTransitionsToHealthy` **breaks** |
| Required tests (spec) | ❌ None implemented (3 tests specified in ADR-007, all unchecked) |
| `Counts ?? metrics` fallback | ✅ Correct fallback chain |

**Decision**: REWORK — the happy-path test is broken and required test coverage from the spec is missing.

---

## 1. BUG-1 Analysis (commit `6ba2674`)

### 1.1 Interface changes (`ILocalSyncStore.cs`)

```csharp
Task<long> InsertPulledMutationAsync(string targetKey, SyncMutation mutation, CancellationToken ct = default);
Task<int>  ReapplyPendingPulledMutationsAsync(string targetKey, CancellationToken ct = default);
```

- **Verdict**: ✅ Correct. Both methods are well-documented and semantically clear.

### 1.2 `ApplyPulledMutationAsync` — acked_at marking (SqliteStore.cs:2192-2200)

```csharp
if (mutation.Seq > 0)
{
    using var cmd = _db.CreateCommand();
    cmd.CommandText = "UPDATE sync_mutations SET acked_at = datetime('now') ... WHERE seq = @seq AND acked_at IS NULL";
    ...
}
```

- **Verdict**: ✅ Correct. Marks mutation as acked ONLY after successful application. The `acked_at IS NULL` guard prevents overwriting an already-acked mutation (e.g., during reapply).

### 1.3 `InsertPulledMutationAsync` (SqliteStore.cs:2205-2224)

```sql
INSERT INTO sync_mutations (target_key, entity, entity_key, op, payload, source, project, occurred_at)
VALUES (@target, @entity, @key, @op, @payload, 'pull', @project, @occurredAt)
```

- **Verdict**: ✅ Correct. Inserts with `source='pull'` and NO `acked_at` (defaults to NULL). Returns `last_insert_rowid()` — thread-safe on WAL-mode SQLite.

- ⚠️ **Minor**: Two separate commands (INSERT + SELECT) without explicit transaction. SQLite WAL guarantees connection-level isolation, so this is safe in practice, but an explicit transaction would be more idiomatic.

### 1.4 `ReapplyPendingPulledMutationsAsync` (SqliteStore.cs:2226-2263)

```sql
SELECT ... FROM sync_mutations
WHERE target_key = @target AND source = 'pull' AND acked_at IS NULL
ORDER BY seq ASC
```

- **Verdict**: ✅ Correct. Reads all orphaned mutations, applies them sequentially, catches and logs individual failures. `ORDER BY seq ASC` ensures chronological order.

- ⚠️ **Minor**: Uses `.Wait(ct)` instead of `await`. Since `ApplyPulledMutationAsync` returns `Task.CompletedTask` synchronously, this won't deadlock, but it's not idiomatic and could become a problem if `ApplyPulledMutationAsync` is ever made truly async.

- ✅ **No-duplication**: Since `ApplyObservationUpsert` uses `ON CONFLICT(id) DO UPDATE SET`, re-applying an already-applied observation is idempotent — no duplication occurs.

### 1.5 SyncManager integration (SyncManager.cs:188-191)

```csharp
// Re-apply any orphaned pulled mutations from a previous interrupted sync
var reapplyCount = await _store.ReapplyPendingPulledMutationsAsync(_cfg.TargetKey, ct);
if (reapplyCount > 0)
    _logger.LogInformation("SyncManager recovered {Count} orphaned pulled mutations", reapplyCount);
```

- **Verdict**: ✅ Correct placement — runs between `ReplayDeferredAsync` (post-push) and `PullAsync` (pre-pull). This ensures orphaned mutations from a previous interrupted pull are applied before the next pull cycle fetches newer ones.

### 1.6 PullAsync flow change (SyncManager.cs:273-279)

```csharp
// Insert into sync_mutations first to get local seq, then apply
var tempMutation = new SyncMutation(0, ...);
var localSeq = await _store.InsertPulledMutationAsync(_cfg.TargetKey, tempMutation, ct);
var syncMutation = new SyncMutation(localSeq, ...);
await _store.ApplyPulledMutationAsync(_cfg.TargetKey, syncMutation, ct);
```

- **Verdict**: ✅ Correct. The two-step approach (insert → get local seq → apply → mark acked) ensures crash recovery. If the process dies between insert and apply, `ReapplyPendingPulledMutationsAsync` will recover.

### 1.7 ❌ Test breakage

The new `ReapplyPendingPulledMutationsAsync` call at line 189 is **NOT mocked** in any existing test. Moq's default `MockBehavior.Loose` with `DefaultValue.Empty` returns `null` for unconfigured `Task<int>` methods.

**Impact analysis of all 16 existing tests**:

| Test | Reaches line 189? | Still passes? | Why |
|------|--------------------|---------------|-----|
| `InitialPhase_IsIdle` | No | ✅ | No cycle call |
| `Config_HasCorrectBackoffSettings` | No | ✅ | No cycle call |
| `ExponentialBackoff_CalculatesCorrectly` | No | ✅ | No cycle call |
| `PushFailure_PhaseTransitionsToPushFailed` | No (push throws before) | ✅ | Exception at PushAsync |
| `CycleCompletesSuccessfully_PhaseTransitionsToHealthy` | **Yes** | ❌ | NRE → caught → PullFailed, but expects Healthy |
| `PullFailure_PhaseTransitionsToPullFailed` | Yes | ✅ | NRE → PullFailed = expected PullFailed |
| `FailureCeilingReached_PhaseTransitionsToDisabled` | Yes (3×) | ✅ | NRE increments failures, expected Disabled |
| `FailureCeilingReached_StopsExecuting` | Yes (2×) | ✅ | Verify counts match |
| `Cycle_CallsReplayDeferredAsync` | Yes | ✅ | ReplayDeferredAsync called before NRE |
| `ReplayDeferred_SuccessfulReplay_LogsReplayCount` | Yes | ✅ | Same reason |
| `ReplayDeferred_WithDeadRows_ReturnsDeadCount` | Yes | ✅ | Same reason |
| `ReplayDeferred_DeadRowsAreLogged` | Yes | ✅ | Same reason |
| `CountPendingNonEnrolledAsync_DetectsNonEnrolledProjects` | Yes | ✅ | CountPendingNonEnrolledAsync called in PushAsync before NRE |
| `NonEnrolledDetected_CallsMarkSyncBlockedAsync` | Yes | ✅ | MarkSyncBlockedAsync called in PushAsync before NRE |
| `PushCycle_BlocksWhenSyncPaused` | Yes | ✅ | AckSyncMutationSeqsAsync verify still correct |
| `SuccessfulPush_CallsAckSyncMutationSeqsAsync` | Yes | ✅ | AckSyncMutationSeqsAsync called in PushAsync before NRE |

**Only 1 test definitively breaks**, but it's the most important happy-path test.

---

## 2. BUG-2 Analysis (commit `12b97a9`)

### 2.1 `SyncMutationCounts` record (ILocalSyncStore.cs:137-140)

```csharp
public sealed record SyncMutationCounts(long TotalPushed, long TotalPulled);
```

- **Verdict**: ✅ Correct. Clean record type.

### 2.2 `GetSyncMutationCountsAsync` (SqliteStore.cs:2065-2084)

```sql
SELECT
    SUM(CASE WHEN source = 'local' AND acked_at IS NOT NULL THEN 1 ELSE 0 END) AS total_pushed,
    SUM(CASE WHEN source = 'pull'  AND acked_at IS NOT NULL THEN 1 ELSE 0 END) AS total_pulled
FROM sync_mutations
WHERE target_key = @target
```

- **Verdict**: ✅ Correct. Counts acked mutations only. Push mutations are `source='local'`, pulled mutations are `source='pull'`. 
- ✅ `IsDBNull` guard handles the case where the table is empty (SUM returns NULL) → defaults to 0.
- ✅ Returns `new SyncMutationCounts(0, 0)` when the reader has no rows (empty table).

### 2.3 `HandleSyncStatusAsync` (CloudSyncEndpoints.cs:403-465)

The fallback chain for `TotalPushed` and `TotalPulled`:

```csharp
// Cursor section:
LastPushedSeq: state?.LastAckedSeq ?? counts?.TotalPushed ?? 0,
LastPulledSeq: state?.LastPulledSeq ?? counts?.TotalPulled ?? 0,

// Counts section:
TotalPushed: counts?.TotalPushed ?? metrics?.TotalPushed ?? 0,
TotalPulled: counts?.TotalPulled ?? metrics?.TotalPulled ?? 0,
```

- **Verdict**: ✅ Correct. DB counts are primary source, metrics are fallback. When `counts` is null (non-ILocalSyncStore like PostgresStore or HttpStore), the `?? metrics?.TotalPushed ?? 0` chain kicks in. On cloud-relay mode (no local SyncManager), `provider` is null, so `metrics` is also null, falling back to 0. This is correct behavior.

- ⚠️ **Minor inconsistency in Cursor section**: The Cursor section uses `counts?.TotalPushed` as fallback (after `state?.LastAckedSeq`) but does NOT fall back to `metrics?.TotalPushed`. The Counts section adds `metrics` as third fallback. This is fine — the Cursor represents the "latest checkpoint" from `sync_state`, not total counts, so falling back to total counts from the DB is appropriate. Metrics would add noise in the cursor context.

### 2.4 Route registration (CloudSyncEndpoints.cs:120-123)

```csharp
app.MapGet("/sync/status", async (HttpContext ctx, IStore store) =>
{
    return await HandleSyncStatusAsync(ctx, store);
});
```

- **Verdict**: ✅ Correct. Uses existing pattern.

---

## 3. Structural Analysis

### 3.1 Store compatibility matrix

| Store | Implements `ILocalSyncStore`? | New methods needed? | Status |
|-------|-------------------------------|---------------------|--------|
| `SqliteStore` | ✅ | ✅ | Implemented |
| `PostgresStore` | ❌ (`IStore, ICloudMutationStore, ICloudChunkStore`) | N/A | ✅ Not affected |
| `HttpStore` | ❌ (`IStore`) | N/A | ✅ Not affected |

`ILocalSyncStore` is intentionally only for local (SQLite) sync. The cloud and proxy stores don't need these methods. The `store is ILocalSyncStore localStore` pattern in CloudSyncEndpoints handles this correctly.

### 3.2 Test coverage gaps (from ADR-007 §Tests requeridos)

| Required test | Status |
|---------------|--------|
| Fixture: DB with `source='pull' AND acked_at IS NULL` → cycle → verify observations populated | ❌ Not implemented |
| `engram sync status` from fresh CLI → same result as SQLite direct | ❌ Not implemented |
| No-duplication: re-apply existing observation doesn't duplicate | ❌ Not implemented |

---

## 4. Summary of Issues

| # | Severity | Issue |
|---|----------|-------|
| 1 | 🔴 HIGH | `SyncManager_CycleCompletesSuccessfully_PhaseTransitionsToHealthy` test breaks — `ReapplyPendingPulledMutationsAsync` not mocked |
| 2 | 🟡 MEDIUM | Three required tests from ADR-007 spec are not implemented |
| 3 | 🟢 LOW | `ReapplyPendingPulledMutationsAsync` uses `.Wait(ct)` instead of `await` (non-idiomatic but functional) |
| 4 | 🟢 LOW | `InsertPulledMutationAsync` uses two commands without explicit transaction (safe due to WAL, but less explicit) |

---

## 5. Fix Instructions

The rework is straightforward:

1. **Add mock setup** for `ReapplyPendingPulledMutationsAsync` in all tests that execute cycles:
   ```csharp
   _storeMock.Setup(s => s.ReapplyPendingPulledMutationsAsync(_config.TargetKey, It.IsAny<CancellationToken>()))
       .ReturnsAsync(0);
   ```

2. **Add at minimum** one test for BUG-1: create a fixture with orphaned pulled mutations, run a cycle, verify observations are populated.

3. **Optionally** convert `.Wait(ct)` to `await` in `ReapplyPendingPulledMutationsAsync`.

---

## Traceability Matrix

| RF/FR | Requirement | Implementation file | Status |
|-------|------------|---------------------|--------|
| FR-007 (BUG-1) | Re-apply orphaned pulled mutations | `SyncManager.cs:189`, `SqliteStore.cs:2226` | ✅ |
| FR-007 | Track pulled mutations for recovery | `SyncManager.cs:277`, `SqliteStore.cs:2205` | ✅ |
| FR-007 | Mark pulled mutations as acked after apply | `SqliteStore.cs:2192-2200` | ✅ |
| FR-008 (BUG-2) | Read counts from DB (not memory) | `SqliteStore.cs:2065`, `CloudSyncEndpoints.cs:414` | ✅ |
| FR-007 | Interface definitions | `ILocalSyncStore.cs:59-71` | ✅ |
| FR-008 | Interface + record definitions | `ILocalSyncStore.cs:26-30,137-140` | ✅ |
| FR-007 | Tests | — | ❌ |
| FR-008 | Tests | — | ❌ |

---

**Generated by forge-verify (Phase 3)** | **Cycle count: 0 → 1** | **Next agent: forge-dev**
