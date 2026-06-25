# Plan: ENG-436 — ApplyPulledMutationAsync Testing + Logging

## 1. Plan Metadata

| Field | Value |
|-------|-------|
| **Feature slug** | `eng-436-apply-pulled-mutation` |
| **Work item** | ENG-436 |
| **Branch** | `test/eng-436-apply-pulled-mutation-logging` |
| **Plan mode** | Retrospective (code already exists in c9dd8808) |
| **Effort** | M (3-4h) |

## 2. Gap Analysis

| FR | Description | Status | Gap |
|----|-------------|--------|-----|
| FR-001 | Unit tests for ApplySessionUpsert | **Missing** | No tests in `SqliteStoreTests.cs` or new file |
| FR-002 | Unit tests for ApplyObservationUpsert | **Missing** | No tests covering upsert/insert/FK deferral |
| FR-003 | Unit tests for ApplyObservationDelete | **Missing** | No tests for soft-delete |
| FR-004 | Unit tests for ApplyPromptUpsert | **Missing** | No tests for prompt upsert |
| FR-005 | Unit tests for ApplyPromptDelete | **Missing** | No tests for prompt delete |
| FR-006 | Logging parity with PostgresStore | **Missing** | `ILogger<SqliteStore>?` not present in constructor |
| FR-007 | FK insert issue verification | **Partial** | SnakeCaseLower exists (line 37), but tests needed to verify |
| FR-008 | End-to-end 2-client pull with SQLite | **Partial** | Script exists, needs verification with SQLite on Client-B |

### Implementation Status by File

| File | What exists | What needs work |
|------|-------------|---------------|
| `src/Engram.Store/SqliteStore.cs:2145-2330` | Full ApplyPulledMutationAsync + 5 private methods | Add ILogger injection |
| `src/Engram.Store/SqliteStore.cs:35-39` | JsonPullOpts with SnakeCaseLower | — (already done) |
| `tests/Engram.Store.Tests/SqliteStoreTests.cs` | ~1287 lines, existing test patterns | Add new test methods or new file |
| `scripts/test-2client-pull.sh` | Script exists | Verify SQLite usage on Client-B |

## 3. Implementation Checklist

### Code & Tests (FR-001 to FR-005)

- [x] 3.1 [FR-001] Create `tests/Engram.Store.Tests/SqliteStoreApplyPulledTests.cs`
- [x] 3.2 [FR-001] Test ApplySessionUpsert — happy path (new session)
- [x] 3.3 [FR-001] Test ApplySessionUpsert — upsert (existing session, updated summary)
- [x] 3.4 [FR-001] Test ApplySessionUpsert — malformed payload (no throw, no create)
- [x] 3.5 [FR-002] Test ApplyObservationUpsert — upsert existing observation
- [x] 3.6 [FR-002] Test ApplyObservationUpsert — upsert new observation
- [x] 3.7 [FR-002] Test ApplyObservationUpsert — FK deferral (missing session → deferred table)
- [x] 3.8 [FR-002] Test ApplyObservationUpsert — malformed payload (no throw)
- [x] 3.9 [FR-003] Test ApplyObservationDelete — soft-delete
- [x] 3.10 [FR-003] Test ApplyObservationDelete — idempotent (nonexistent)
- [x] 3.11 [FR-004] Test ApplyPromptUpsert — new prompt
- [x] 3.12 [FR-004] Test ApplyPromptUpsert — existing prompt update
- [x] 3.13 [FR-004] Test ApplyPromptUpsert — FK deferral
- [x] 3.14 [FR-005] Test ApplyPromptDelete — soft-delete
- [x] 3.15 [FR-005] Test ApplyPromptDelete — idempotent

### Logging Additions (FR-006)

- [x] 3.16 [FR-006] Add `ILogger<SqliteStore>? _logger` field to SqliteStore
- [x] 3.17 [FR-006] Add `ILogger<SqliteStore>?` constructor parameter (matches PostgresStore)
- [x] 3.18 [FR-006] Add INFO log to ApplySessionUpsert on success
- [x] 3.19 [FR-006] Add WARNING log on deserialization failure (ApplyObservationUpsert, ApplyPromptUpsert)
- [x] 3.20 [FR-006] Add INFO log to ApplyObservationUpsert on success
- [x] 3.21 [FR-006] Add INFO log to ApplyObservationDelete on success
- [x] 3.22 [FR-006] Add INFO log to ApplyPromptUpsert on success
- [x] 3.23 [FR-006] Add INFO log to ApplyPromptDelete on success

### FK Verification (FR-007)

- [x] 3.24 [FR-007] Test observation with existing session (no silent fail, no deferral)
- [x] 3.25 [FR-007] Test prompt with existing session (no silent fail, no deferral)

### End-to-End Test (FR-008)

 - [x] 3.26 [FR-008] Verify Client-B uses SQLite (not Postgres) in test-2client-pull.sh
- [x] 3.27 [FR-008] Run test-2client-pull.sh and verify data visible in Client-B SQLite DB
- [x] 3.28 [FR-008] Verify `./engram search` shows pulled data in Client-B

### BACKLOG / TD-013 Cleanup

- [x] 3.29 [OQ-3] Update BACKLOG.md ENG-436 status to Done
- [x] 3.30 [OQ-3] Mark TD-013 as resolved in docs/TECHNICAL-DEBT.md

### PM Manual Tests

- [ ] 3.31 [PM-1] Session upsert unit test — verify GetSessionAsync returns stored session
- [ ] 3.32 [PM-2] Observation upsert with FK present — verify row in DB
- [ ] 3.33 [PM-3] FK deferral — verify row in sync_apply_deferred
- [ ] 3.34 [PM-4] Observation soft-delete — verify excluded from active results
- [ ] 3.35 [PM-5] Prompt upsert with FK deferral — verify deferred
- [ ] 3.36 [PM-6] Logging output — verify each Apply* emits INFO log
- [x] 3.37 [PM-7] test-2client-pull.sh — ✅ PASS (Client-B encontró memoria de Client-A vía sync pull)

## 4. Identified Gaps

| Gap | Description | Not covered by FR |
|-----|-------------|------------------|
| G-001 | No existing tests for any Apply* methods | FR-001 to FR-005 (all missing) |
| G-002 | SqliteStore lacks ILogger injection | FR-006 (entirely missing) |
| G-003 | test-2client-pull.sh may use Postgres by default for Client-B | FR-008 (needs verification) |

## 5. Open Questions

| ID | Question | Resolution |
|----|----------|------------|
| OQ-1 | ILogger injection pattern — constructor parameter vs. static Log | Resolved: constructor param (matches PostgresStore) |
| OQ-2 | Test file location | Resolved: new file SqliteStoreApplyPulledTests.cs |
| OQ-3 | BACKLOG/TD-013 cleanup in same PR | Resolved: yes, one PR covers all |
| OQ-4 | Cursor-advance-on-failure behavior | Follow-up: separate ENG ticket, not in scope |

**No BLOCKERS found.** All OQs resolved with assumptions documented in spec.md.

---

## 6. Deployment & Rollback

- **Risk level:** MEDIUM (new tests + logging, no breaking changes)
- **Deploy strategy:** Rolling update (stateless, additive)
- **Rollback:** Revert commit — no DB changes, no schema changes