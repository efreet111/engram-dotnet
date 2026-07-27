---
capability_matrix:
  ai_reasoning:
    - Wording of actionable stderr / doctor messages when Migrate fails or remote /health is unreachable (tone, localization ES/EN, exact copy)
    - Whether doctor surfaces B2 (remote down) as WARN vs FAIL once store opens successfully
    - How much detail to show about deleted duplicate row counts after Migrate cleanup (log vs silent)
  deterministic:
    - Migrate MUST delete duplicate pull rows BEFORE CREATE UNIQUE INDEX idx_sync_mutations_pull_dedup
    - Dedupe invariant: exactly one row per (target_key, entity_key) WHERE source='pull'; survivor is the row with MIN(seq) (aligns with InsertPulledMutationAsync ORDER BY seq ASC LIMIT 1)
    - Dedupe step MUST be idempotent (safe on clean DBs and on re-open after index exists)
    - Local source='local' (or non-pull) mutations MUST NOT be deleted or constrained by this cleanup
    - OpenStore / SqliteStore construction MUST NOT throw SqliteException 19 due to pre-existing pull duplicates after this fix
    - MUST NOT wipe or recreate the user data directory without explicit human opt-in
    - MUST NOT switch sync mode to ENGRAM_URL / HttpStore as a workaround
    - PostgresStore is out of scope (no equivalent unique index today)
    - Restoring remote reachability for ENGRAM_SERVER_URL is ops (B2), not a product reconnect feature in v1
---
# Spec: verify-mcp-server-connection

## 1. Objective and scope

**Problem:** Cursor `user-engram` fails live tool discovery because `engram.exe mcp` (and `engram doctor`) crash during `SqliteStore.Migrate()` when creating `idx_sync_mutations_pull_dedup` against a local DB that already has duplicate `sync_mutations` rows with `source='pull'` (gap left by ENG-457). Independently, the configured remote `ENGRAM_SERVER_URL` (`http://192.168.0.178:7437`) is unreachable, so sync cannot complete even after MCP starts.

**Goal (v1):** Unblock MCP/doctor/OpenStore on existing SQLite client DBs by completing the ENG-457 migration (dedupe then unique index). Optionally improve diagnostics when the remote sync server is down. Do not wipe user DBs; do not change sync architecture.

**In scope**
1. **B1 fix (required):** In `SqliteStore.Migrate()`, run an idempotent pull-row dedupe **before** `CREATE UNIQUE INDEX IF NOT EXISTS idx_sync_mutations_pull_dedup`.
2. **Regression test (required):** Opening a store against a DB pre-seeded with duplicate pull rows succeeds; leaves one row per `(target_key, entity_key)` for `source='pull'`; index exists afterward.
3. **B2 messaging (optional XS):** Once the store can open, `engram doctor` (and/or MCP stderr if already in that path) reports clearly that remote `/health` failed / host unreachable — without inventing reconnect logic.

**Out of scope**
- Bringing up TrueNAS / VPN / firewall to `192.168.0.178` (human/ops).
- Rewriting SyncManager, MCP tool surface, or installer.
- Using `ENGRAM_URL` / HttpStore as a “fix”.
- Automatic wipe of `ENGRAM_DATA_DIR` / `engram.db`.
- PostgresStore unique index / dedupe.
- Resolving AssemblyInformationalVersion (`1.0.0+…`) vs installer tag (`v1.3.0`) mismatch (ops note only).

**Evidence baseline (discovery, 2026-07-19):** local `~/.engram/engram.db` has 609 pull rows, 6 duplicate groups (worst key ×421), index absent, 0 observations; `engram doctor` reproduces SqliteException 19; remote TCP/HTTP to `:7437` times out.

> No HU source — FlowDoc context: none referenced.

---

## 2. Functional requirements (FR)

- FR-001: Migrate pre-dedupe before unique index — Before creating the partial unique index on pull mutations, Migrate removes duplicate `source='pull'` rows so the index can be created on dirty historical DBs.
  * Scenario A: Given a SQLite `engram.db` with multiple `sync_mutations` rows sharing the same `(target_key, entity_key)` and `source='pull'`, When `SqliteStore` is constructed (Migrate runs), Then construction succeeds, `idx_sync_mutations_pull_dedup` exists, and exactly one pull row remains per `(target_key, entity_key)`.
  * Scenario B: Given a SQLite DB with no pull duplicates (or an empty `sync_mutations` table), When Migrate runs (first open or re-open), Then no error occurs, the unique index exists (or remains), and no non-pull rows are removed.

- FR-002: Survivor selection aligns with runtime dedup — When duplicates are collapsed, the kept row is the one with the lowest `seq` for that `(target_key, entity_key, source='pull')`, matching `InsertPulledMutationAsync` which returns `ORDER BY seq ASC LIMIT 1`.
  * Scenario A: Given three pull rows for the same `(target_key, entity_key)` with distinct `seq` values (e.g. 10, 20, 30), When Migrate dedupes, Then only the row with `seq=10` remains and later `InsertPulledMutationAsync` for that key returns `10`.
  * Scenario B: Given duplicate pull rows plus one or more `source` values that are not `'pull'` for the same `entity_key`, When Migrate dedupes, Then all non-pull rows remain untouched and only pull duplicates are collapsed.

- FR-003: Idempotent reopen after cleanup — After a successful Migrate on a formerly dirty DB, subsequent OpenStore/Migrate cycles do not fail and do not further delete the sole remaining pull row per key.
  * Scenario A: Given a DB that already completed dedupe + unique index creation, When the process opens the store again, Then Migrate completes without SqliteException 19 and pull row counts per key stay at 1.
  * Scenario B: Given a DB where the unique index already exists and new pulls arrive only via `INSERT OR IGNORE`, When Migrate runs, Then no destructive mass delete of unique pull rows occurs (cleanup is a no-op or equivalent).

- FR-004: MCP / CLI startup unblocked — Any entrypoint that calls OpenStore on a local SQLite store (including `engram mcp` and `engram doctor`) must not crash solely due to pre-existing pull duplicates.
  * Scenario A: Given the user’s dirty local DB shape (duplicates, index missing), When `engram doctor` runs against that data dir after the fix, Then the process does not abort with UNIQUE constraint on `sync_mutations.(target_key, entity_key)` during Migrate.
  * Scenario B: Given the same fixed binary and dirty DB, When Cursor spawns `engram.exe mcp`, Then stdio MCP live tool discovery can complete store open (tools become available); sync may still report remote failure if B2 persists.

- FR-005: No silent data-dir wipe — The product fix must operate in-place on `sync_mutations` pull duplicates; it must not delete the database file or reset `ENGRAM_DATA_DIR` unless the human explicitly chooses an ops workaround outside this feature.
  * Scenario A: Given a local DB with observations and/or sync state, When Migrate dedupes pull rows, Then tables other than duplicate pull rows in `sync_mutations` are preserved (e.g. `observations`, `sync_state` content not wiped by this step).
  * Scenario B: Given the human has not opted into a wipe, When the fixed binary starts, Then no code path in this feature recreates an empty data dir as the default recovery.

- FR-006: Remote-down diagnostics (optional XS) — When sync is enabled and `ENGRAM_SERVER_URL` is set but the remote `/health` (or equivalent reachability check already used by diagnostics) fails, doctor (and MCP only if it already surfaces sync/diag errors) should state that local store is OK but remote sync is unreachable — without changing sync mode.
  * Scenario A: Given OpenStore succeeds and `ENGRAM_SERVER_URL` points to an unreachable host, When the user runs `engram doctor`, Then output includes a clear remote-unreachable / health-failed indication and does not recommend switching to `ENGRAM_URL`.
  * Scenario B: Given OpenStore succeeds and the remote `/health` returns success, When the user runs `engram doctor`, Then the remote check is reported healthy (or equivalent pass) and no false “server down” warning is shown.

- FR-007: Regression coverage for dirty-DB open — Automated tests must cover the historical dirty-DB path that ENG-457 missed (index creation without prior cleanup).
  * Scenario A: Given a test that builds a SQLite file with duplicate pull rows **without** going through the fixed Migrate (e.g. raw SQL insert before open, or open with index creation deferred in the fixture), When `new SqliteStore(cfg)` runs with the fix, Then no exception is thrown and counts assert one row per pull key.
  * Scenario B: Given existing `SqliteStorePullDedupTests` for `INSERT OR IGNORE` behavior, When the suite runs, Then prior ENG-457 cases still pass and the new dirty-DB open case is included in the same area of coverage.

---

## 3. Non-functional requirements (NFR)

- NFR-001: **Performance** — Dedupe on open must be acceptable for worst known client sizes (millions of pull rows historically). Prefer a single SQL cleanup statement (or minimal batch) over per-row loops; should complete in seconds on multi‑GB DBs typical of the ENG-457 incident, not hang indefinitely.
- NFR-002: **Safety / durability** — Cleanup runs in the same migration path as index creation; after success, the unique index prevents re-accumulation via normal `InsertPulledMutationAsync`. No force-delete of the DB file.
- NFR-003: **Architecture lean** — Change limited to `SqliteStore.Migrate` (+ tests; optional small doctor/diagnostic message). No MediatR/CQRS/new layers.
- NFR-004: **Compatibility** — Behavior remains SQLite-client-only for this index; server Postgres unchanged. Config mode Local + sync (`ENGRAM_SERVER_URL`, not `ENGRAM_URL`) remains the supported path.
- NFR-005: **Operability** — Failure modes after the fix should distinguish: (1) local store/Migrate errors vs (2) remote unreachable, so Cursor/MCP “tools unavailable” is not conflated with sync-only outages.
- NFR-006: **Comments** — Follow ADR-003 / DEVELOPMENT.xml-doc policy; no extra markdown docs unless the human asks.

---

## 4. Developer manual tests (PM-*) — required for CKP-4

## Developer manual tests (required — mark [x] before /flow-close)

| ID | Case / flow | Steps (summary) | Expected result | [x] |
|----|-------------|-----------------|-----------------|-----|
| PM-1 | Happy path: dirty local DB opens + MCP | 1. Backup `C:\Users\efree\.engram\engram.db` (copy file).<br>2. Run fixed `engram doctor` with same `ENGRAM_DATA_DIR`.<br>3. Confirm store opens; optionally inspect pull row count (~1 per key).<br>4. Restart Cursor MCP / reload `user-engram`. | Doctor no longer crashes on UNIQUE; MCP tools discoverable. Sync may still warn if remote down. | [ ] |
| PM-2 | Error path: remote unreachable (B2) | 1. With store opening successfully and `ENGRAM_SYNC_ENABLED=true` + `ENGRAM_SERVER_URL=http://192.168.0.178:7437`.<br>2. Run `engram doctor` (and note MCP behavior if sync errors surface).<br>3. Confirm host still unreachable (e.g. Test-NetConnection / browser `/health`). | Local OK; clear remote/health failure message; no recommendation to set `ENGRAM_URL`; no process crash. | [ ] |
| PM-3 | Edge: clean DB / idempotent reopen | 1. Open store twice in a row on the same data dir after PM-1.<br>2. Run doctor again.<br>3. Spot-check that pull keys remain at one row each (no progressive deletion). | Second open succeeds; index present; counts stable. | [ ] |
| PM-4 | Safety: no wipe without opt-in | 1. After PM-1, verify `engram.db` path unchanged and file was not replaced by an empty new DB (size/mtime sensible; observations/sync_state not zeroed by wipe).<br>2. Confirm feature did not require deleting the data dir. | In-place dedupe only; data dir intact. | [ ] |

---

## 5. Open questions for human (OQ-*)

| ID | Tag | Question | Default / assumption |
|----|-----|---------|---------------------|
| OQ-1 | [OPTIONAL] | ¿Phase 1 incluye solo el fix Migrate (B1) o también el endurecimiento XS de mensajes `doctor`/MCP para B2? | Assumed: **B1 required + B2 messaging XS in scope** (orchestrator preference). Plan may drop messaging if timeboxed, but design includes FR-006. |
| OQ-2 | [OPTIONAL] | ¿Dedupe in-place de `engram.db` (609→~8 pull rows) es aceptable, o preferís backup + wipe del data dir? | Assumed: **in-place Migrate dedupe only**; **never wipe** without explicit human opt-in. Manual backup before PM-1 is recommended ops hygiene, not product wipe. |
| OQ-3 | [FOLLOW-UP] | ¿El servidor en `192.168.0.178:7437` debería estar UP ahora (TrueNAS/VPN), o está apagado a propósito? | — (ops; does not change B1 design). Sync validation against a live remote is post-fix. |
| OQ-4 | [FOLLOW-UP] | ¿Hay que alinear `engram.exe --version` (`1.0.0+…`) con el tag de installer `v1.3.0`? | — (versioning/release hygiene; not the crash root cause). |

---

## Traceability

| Item | Notes |
|------|--------|
| ENG-457 | Closes migration gap: unique index without pre-cleanup |
| ENG-451 / ADR-007 | Same pull mutation paths; survivor `MIN(seq)` keeps reapply semantics coherent |
| ENG-459 | Sync failure feedback useful only after OpenStore works |
| Config | MCP Local + sync already correct per `docs/MCP-CONFIG.md` — no config rewrite in v1 |
