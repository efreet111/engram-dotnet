# HU-027 — Fix PostgreSQL session_id constraint on sync push

**As**: Sync-enabled client (offline-first, desktop)
**I want**: Push observations to a remote PostgreSQL server without failing due to missing session_id
**To**: Sync works reliably when observations are created without a git-backed project context

---

## Acceptance Criteria

- [x] Push batch with empty/null `session_id` applies on PostgreSQL without constraint error
- [x] Retry loop stops after fix — batch is acked and `consecutive_failures` resets
- [x] Normal sync path unaffected — sessions-first batch makes ensure-session insert a no-op
- [x] Both `ApplyObservationUpsertAsync` and `ApplyPromptUpsertAsync` handle missing session correctly
- [x] Placeholder sessions are namespaced (`obs-{syncId}` / `prompt-{syncId}`) to avoid collisions

---

## Tasks (Implementation)

- [x] Add ensure-session upsert in `ApplyObservationUpsertAsync` (PostgresStore.cs)
- [x] Add ensure-session upsert in `ApplyPromptUpsertAsync` (PostgresStore.cs)
- [x] Add unit test: observation mutation with empty/null `session_id` → inserts successfully
- [x] Add unit test: prompt mutation with empty/null `session_id` → inserts successfully
- [x] Verify T2 tests pass: `dotnet test -c Release --filter "FullyQualifiedName!~Engram.Postgres.Tests"`

---

## Notes

- **Root cause**: `PostgresStore.cs:2714` binds `(object?)payload.SessionId ?? DBNull.Value` — `DBNull` violates `NOT NULL REFERENCES sessions(id)` constraint. SQLite uses `?? ""` + `InsertDeferred` on FK failure; PostgreSQL has no deferral.
- **Ensure-session SQL**: `INSERT INTO sessions (id, project, directory) VALUES (@sid, COALESCE(NULLIF(@proj, ''), 'unknown'), '/') ON CONFLICT (id) DO NOTHING`
- **Fallback session id**: `string.IsNullOrEmpty(sessionId) ? $"obs-{entityKey}" : sessionId` for observations; `prompt-{entityKey}` for prompts
- **Why `COALESCE(NULLIF(@proj, ''), 'unknown')`**: Distinguishes null from empty-string project values; both would otherwise violate the `sessions.project NOT NULL` constraint
- **Affected code paths**:
  - `src/Engram.Store/PostgresStore.cs` — `ApplyObservationUpsertAsync` (EnsureSessionAsync call in insert branch)
  - `src/Engram.Store/PostgresStore.cs` — `ApplyPromptUpsertAsync` (EnsureSessionAsync call in insert branch)
- **Sync flow**: offline-first (SQLite) → HTTP push → remote-server (PostgreSQL) → batch atómico → whole batch rolls back on constraint violation → never acked → infinite retry loop
- **Tests**: `PostgresStoreTests.cs` — `ApplyObservationUpsert_NullSessionId_CreatesPlaceholderAndSucceeds`, `ApplyPromptUpsert_NullSessionId_CreatesPlaceholderAndSucceeds`, `PushMutation_MixedNullAndValidSession_BatchCommitsAtomically`, `PushMutation_NullSessionId_ReapplyCreatesNoDuplicatePlaceholder`

---

## Implementation Results

**Commit**: `6c451fc` — `fix: add EnsureSessionAsync for null session_id in PostgreSQL sync push (HU-027)`

**Testing**:
- Postgres suite: 59 passed / 0 failed (`PostgresStoreTests.cs`)
- T2 suite: all green
- T3 integration (scripts/sync-integration-test.sh): offline-first → remote-server sync verified — null session observation pushed to PostgreSQL server, placeholder session `obs-{EntityKey}` auto-created

**Key behaviors verified**:
- Null session_id mutation accepted with `accepted_seqs` returned
- Placeholder session `obs-test-null-session-obs` created in PostgreSQL
- Observation stored with FK reference to placeholder session
- Existing sessions cause no-op (ON CONFLICT DO NOTHING)
- Retry loop terminates when batch succeeds
