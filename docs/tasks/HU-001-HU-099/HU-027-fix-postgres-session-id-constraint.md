# HU-027 — Fix PostgreSQL session_id constraint on sync push

**As**: Sync-enabled client (offline-first, desktop)
**I want**: Push observations to a remote PostgreSQL server without failing due to missing session_id
**To**: Sync works reliably when observations are created without a git-backed project context

---

## Acceptance Criteria

- [ ] Push batch with empty/null `session_id` applies on PostgreSQL without constraint error
- [ ] Retry loop stops after fix — batch is acked and `consecutive_failures` resets
- [ ] Normal sync path unaffected — sessions-first batch makes ensure-session insert a no-op
- [ ] Both `ApplyObservationUpsertAsync` and `ApplyPromptUpsertAsync` handle missing session correctly
- [ ] Placeholder sessions are namedpaced (`obs-{syncId}`) to avoid collisions

---

## Tasks (Implementation)

- [ ] Add ensure-session upsert in `ApplyObservationUpsertAsync` (PostgresStore.cs)
- [ ] Add ensure-session upsert in `ApplyPromptUpsertAsync` (PostgresStore.cs)
- [ ] Add unit test: observation mutation with empty/null `session_id` → inserts successfully
- [ ] Add unit test: prompt mutation with empty/null `session_id` → inserts successfully
- [ ] Verify T2 tests pass: `dotnet test -c Release --filter "FullyQualifiedName!~Engram.Postgres.Tests"`

---

## Notes

- **Root cause**: `PostgresStore.cs:2714` binds `(object?)payload.SessionId ?? DBNull.Value` — `DBNull` violates `NOT NULL REFERENCES sessions(id)` constraint. SQLite uses `?? ""` + `InsertDeferred` on FK failure; PostgreSQL has no deferral.
- **Ensure-session SQL**: `INSERT INTO sessions (id, project, directory) VALUES (@sid, COALESCE(@proj, 'unknown'), '/') ON CONFLICT (id) DO NOTHING`
- **Fallback session id**: `string.IsNullOrEmpty(payload.SessionId) ? $"obs-{entry.EntityKey}" : payload.SessionId`
- **Why `COALESCE(@proj, 'unknown')`**: `sessions.project TEXT NOT NULL` — without it, a null project in the payload would fail the ensure-session insert too
- **Affected code paths**:
  - `src/Engram.Store/PostgresStore.cs:2714` — `ApplyObservationUpsertAsync`
  - `src/Engram.Store/PostgresStore.cs:2788` — `ApplyPromptUpsertAsync`
- **Sync flow**: offline-first (SQLite) → HTTP push → remote-server (PostgreSQL) → batch atómico → whole batch rolls back on constraint violation → never acked → infinite retry loop
- **Existing tests**: `SyncBehaviorPostgresTests` (RequiresDocker) covers sync push — should add sessionless mutation case
