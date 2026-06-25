---
capability_matrix:
  ai_reasoning:
    - Logger injection design (constructor parameter vs. static singleton)
    - Test structure (separate file vs. added to SqliteStoreTests.cs)
    - Whether to add error-reporting to ApplyPulledMutationAsync return type
  deterministic:
    - SnakeCaseLower deserialization is already applied (ENG-428)
    - FK constraint code is 19 (SQLITE_CONSTRAINT)
    - Session upsert uses ON CONFLICT DO UPDATE
    - Observation/Prompt upsert uses lookup-by-sync_id pattern
    - Deferred inserts go to sync_apply_deferred table
---

# Spec: ENG-436 — ApplyPulledMutationAsync Testing + Logging

## 1. Objective and Scope

**The implementation already exists.** `SqliteStore.ApplyPulledMutationAsync` (L2145) was fully implemented in commit `c9dd8808` (2026-06-09, ENG-425) with dispatch to 5 private methods covering session/observation/prompt upsert/delete.

**The gap is: zero committed tests, no logging, and unverified FK fix.**

### In Scope
- Unit tests for all 5 `Apply*` private methods in `SqliteStore`
- Logging parity with `PostgresStore` (`ILogger<PostgresStore>?` pattern)
- Verification that FK insert issue (from ENG-421 rework ticket) is resolved by ENG-428's `SnakeCaseLower` fix
- End-to-end 2-client pull test with `test-2client-pull.sh` using SQLite on Client-B

### Out of Scope
- Re-implementing `ApplyPulledMutationAsync` — code is done
- Modifying `SyncManager.PullAsync` cursor behavior (silent failure on partial batch apply)
- Adding error-reporting return types to `ApplyPulledMutationAsync`
- PostgreSQL store changes

> **HU source:** No HU referenced. This is an engineering task (test + logging) derived from the ENG-436 P0 bug entry in `docs/BACKLOG.md`.

---

## 2. Functional Requirements (FR)

### FR-001: Unit tests for ApplySessionUpsert

**Description:** `ApplySessionUpsert` deserializes `SessionPullPayload` from `SyncMutation.Payload` and executes `INSERT ... ON CONFLICT DO UPDATE` against the `sessions` table. Tests must cover happy path and malformed payload.

* Scenario A (happy path — new session): Given a `SyncMutation` for entity `"session"` with valid JSON payload containing `id`, `project`, `directory`, `startedAt`; When `ApplyPulledMutationAsync` is called; Then the session is stored and retrievable via `GetSessionAsync`.

* Scenario B (upsert — existing session): Given a session already exists in the store; When a `SyncMutation` for that session with updated `summary` is applied; Then `GetSessionAsync` returns the updated summary.

* Scenario C (malformed payload): Given a `SyncMutation` for entity `"session"` with invalid JSON payload; When `ApplyPulledMutationAsync` is called; Then the method returns without throwing and the session is not created.

### FR-002: Unit tests for ApplyObservationUpsert

**Description:** `ApplyObservationUpsert` looks up by `sync_id` to decide UPDATE vs INSERT. FK failures are deferred via `InsertDeferred`. Tests must cover: upsert existing, upsert new, missing session FK (deferral), malformed payload.

* Scenario A (upsert — existing observation): Given an observation with `sync_id = "obs-1"` exists; When a `SyncMutation` for `"observation"` upsert with `sync_id = "obs-1"` and new `title` is applied; Then `GetObservationAsync` returns the updated title.

* Scenario B (upsert — new observation): Given no observation with `sync_id = "obs-new"` exists; When a `SyncMutation` for `"observation"` upsert with `sync_id = "obs-new"` and valid payload is applied; Then the observation is stored and retrievable.

* Scenario C (FK deferral — missing session): Given no session with `id = "missing-session"` exists; When a `SyncMutation` for `"observation"` upsert with `session_id = "missing-session"` is applied; Then the mutation is inserted into `sync_apply_deferred` and `ReplayDeferredAsync` later applies it when the session exists.

* Scenario D (malformed payload): Given a `SyncMutation` for `"observation"` with invalid JSON payload; When `ApplyPulledMutationAsync` is called; Then the method returns without throwing and no observation is created.

### FR-003: Unit tests for ApplyObservationDelete

**Description:** `ApplyObservationDelete` performs a soft-delete (sets `deleted_at`) by `sync_id`.

* Scenario A (soft-delete): Given an observation with `sync_id = "obs-del"` exists and is not deleted; When a `SyncMutation` for `"observation"` delete with `sync_id = "obs-del"` is applied; Then `GetObservationAsync` returns `null` or filtered results exclude it.

* Scenario B (idempotent delete): Given no observation with `sync_id = "nonexistent"` exists; When a `SyncMutation` for `"observation"` delete with `sync_id = "nonexistent"` is applied; Then no error is thrown.

### FR-004: Unit tests for ApplyPromptUpsert

**Description:** `ApplyPromptUpsert` mirrors `ApplyObservationUpsert` — lookup by `sync_id`, update-or-insert, FK deferral. Must cover same patterns as FR-002.

* Scenario A (upsert — new prompt): Given no prompt with `sync_id = "prompt-new"` exists; When a `SyncMutation` for `"prompt"` upsert with valid payload is applied; Then the prompt is stored.

* Scenario B (upsert — existing prompt): Given a prompt with `sync_id = "prompt-1"` exists; When a `SyncMutation` for `"prompt"` upsert with new `content` is applied; Then the content is updated.

* Scenario C (FK deferral): Given no session exists; When a `SyncMutation` for `"prompt"` upsert referencing a missing session is applied; Then it is deferred to `sync_apply_deferred`.

### FR-005: Unit tests for ApplyPromptDelete

**Description:** `ApplyPromptDelete` performs a soft-delete by `sync_id`.

* Scenario A (soft-delete): Given a prompt with `sync_id = "prompt-del"` exists; When a `SyncMutation` for `"prompt"` delete is applied; Then the prompt is soft-deleted.

* Scenario B (idempotent): Given no prompt exists; When delete is applied; Then no error.

### FR-006: Logging parity with PostgresStore

**Description:** Add `ILogger<SqliteStore>? _logger` to `SqliteStore` following the same pattern as `PostgresStore`. Each `Apply*` method logs at INFO level on success and WARNING level on deserialization failure.

* Scenario A (session upsert logs): Given a valid session mutation; When `ApplyPulledMutationAsync` is called; Then `_logger?.LogInformation("Applied mutation session/upsert for entity_key={EntityKey} in project={Project}", ...)` is emitted.

* Scenario B (observation deserialization failure logs): Given an observation mutation with malformed JSON; When `ApplyPulledMutationAsync` is called; Then `_logger?.LogWarning("Failed to deserialize observation payload for entity_key={EntityKey}", ...)` is emitted and method returns cleanly.

* Scenario C (prompt upsert logs): Given a valid prompt mutation; When `ApplyPulledMutationAsync` is called; Then `_logger?.LogInformation("Applied mutation prompt/upsert ...")` is emitted.

* Scenario D (observation delete logs): Given a valid observation delete mutation; When `ApplyPulledMutationAsync` is called; Then `_logger?.LogInformation("Applied mutation observation/delete ...")` is emitted.

* Scenario E (prompt delete logs): Given a valid prompt delete mutation; When `ApplyPulledMutationAsync` is called; Then `_logger?.LogInformation("Applied mutation prompt/delete ...")` is emitted.

### FR-007: FK insert issue verification

**Description:** The FK insert issue (from ENG-421 rework ticket: tests 3.3/3.7 failing with silent INSERT failures) was hypothetically fixed by ENG-428's `SnakeCaseLower` change to `JsonPullOpts`. Verify by re-running the previously-failing test cases.

* Scenario A (observation with existing session — no longer silent fail): Given a session with `id = "session-fk"` exists; When a `SyncMutation` for `"observation"` upsert with `session_id = "session-fk"` is applied; Then the observation is stored without being deferred.

* Scenario B (prompt with existing session — no longer silent fail): Given a session with `id = "session-fk"` exists; When a `SyncMutation` for `"prompt"` upsert with `session_id = "session-fk"` is applied; Then the prompt is stored without being deferred.

### FR-008: End-to-end 2-client pull with SQLite Client-B

**Description:** The existing `test-2client-pull.sh` must pass with Client-B using SQLite (not Postgres). Currently Client-B runs with defaults — verify it uses SQLite and that data pulled from server is visible locally in Client-B (not just on server).

* Scenario A (Client-B sees pulled data locally): Given Client-A saves an observation on the server; When Client-B pulls via sync; Then `SELECT id FROM observations WHERE sync_id = ?` on Client-B's SQLite DB returns the observation (not just server).

* Scenario B (Client-B can search pulled data): Given Client-A creates memories; When Client-B runs `./engram search`; Then pulled data appears in search results.

---

## 3. Non-Functional Requirements (NFR)

- NFR-001: All new test methods must use the existing `SqliteStoreTests.cs` pattern — unique temp directory per test, `IDisposable` cleanup, seeded fixtures via `SeedSession`/`SeedObservation` helpers.
- NFR-002: Logging must not change method behavior — `ApplyPulledMutationAsync` remains `Task`-returning with no error reporting.
- NFR-003: FK constraint error code 19 (`SQLITE_CONSTRAINT`) is the only caught exception — other `SqliteException` codes still propagate (this is documented behavior, not a bug for ENG-436).
- NFR-004: New tests must pass in CI (GitHub Actions) with SQLite in-memory connection string.

---

## 4. Developer Manual Tests (Required — mark [x] before /flow-close)

| ID | Case / Flow | Steps (Summary) | Expected Result | [x] |
|----|-------------|-----------------|-----------------|-----|
| PM-1 | Session upsert unit test | 1. Call `ApplyPulledMutationAsync` with session upsert mutation<br>2. `GetSessionAsync` to verify | Session stored with correct fields | [x] |
| PM-2 | Observation upsert new (FK present) | 1. Seed session<br>2. Apply observation mutation via `ApplyPulledMutationAsync`<br>3. Query SQLite directly for `sync_id` | Observation row exists in DB | [x] |
| PM-3 | FK deferral — observation missing session | 1. Apply observation mutation with invalid `session_id` (no session seeded)<br>2. Query `sync_apply_deferred` table | Row inserted in deferred table | [x] |
| PM-4 | Observation soft-delete | 1. Seed observation<br>2. Apply observation delete mutation<br>3. Query `observations` where `deleted_at IS NULL` | Observation not in active results | [x] |
| PM-5 | Prompt upsert with FK deferral | 1. Apply prompt mutation with missing session<br>2. Query `sync_apply_deferred` | Row deferred | [x] |
| PM-6 | Logging output verification | 1. Enable verbose logging<br>2. Apply mutations (upsert + delete for session/obs/prompt)<br>3. Capture log output | Each apply emits `ILogger` INFO line | [x] |
| PM-7 | `test-2client-pull.sh` with SQLite | 1. Run script (Client-B uses SQLite by default)<br>2. Query Client-B's SQLite DB directly for synced observation | Observation found in Client-B's local SQLite | [x] |

---

## 5. Open Questions for Human (OQ-*)

| ID | Tag | Question | Default / Assumption |
|----|-----|---------|---------------------|
| OQ-1 | [OPTIONAL] | Should `SqliteStore` accept `ILogger<SqliteStore>?` in its constructor (matching `PostgresStore`), or use a static `Log` facade? | Assumed: `ILogger<SqliteStore>?` constructor parameter (matches PostgresStore pattern, allows test injection) |
| OQ-2 | [OPTIONAL] | Test file location: add to existing `SqliteStoreTests.cs` (~1287 lines) or create `SqliteStoreApplyPulledTests.cs`? | Assumed: Create new `SqliteStoreApplyPulledTests.cs` in `tests/Engram.Store.Tests/` (avoids further bloat of existing large file) |
| OQ-3 | [OPTIONAL] | Should the logging PR also update the stale BACKLOG.md line references and TD-013? | Assumed: Yes — one PR covers tests + logging + BACKLOG cleanup |
| OQ-4 | [FOLLOW-UP] | Cursor-advance-on-failure design: `SyncManager.PullAsync` advances cursor even if `ApplyPulledMutationAsync` silently fails. Should this be addressed separately? | Separate ENG ticket, not in ENG-436 scope |
