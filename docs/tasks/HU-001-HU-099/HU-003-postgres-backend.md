# HU-003: postgres-backend

**Status**: 🟡 In Progress
**Owner**: @owner
**Created**: 2026-06-01
**Priority**: Should

---

## 🎯 Intent

Add `PostgresStore` as a third `IStore` implementation, enabling `engram serve` to use PostgreSQL as a persistence backend instead of SQLite. This addresses the concurrency limitations of SQLite when 10+ developers write to the shared server simultaneously and enables enterprise-grade backup, HA, and observability.

---

## 📋 Scope

### In Scope
- `PostgresStore.cs` implementing all 22 `IStore` methods
- `StoreConfig` extended with `DbType` and `PgConnectionString`
- Switch in `Program.cs` to select backend via `ENGRAM_DB_TYPE` / `ENGRAM_PG_CONNECTION`
- PostgreSQL schema (idempotent migrations in code)
- FTS via `tsvector` stored generated column + GIN index
- Parity test suite (same tests run against SqliteStore and PostgresStore)
- Docker Compose with PostgreSQL companion
- Documentation: `docs/POSTGRES-SETUP.md`, updated `ARCHITECTURE.md`

### Out of Scope
- pgvector / semantic search (future change)
- Multi-tenant at PG schema level (overkill)
- Replication / HA orchestration (operator responsibility)
- EF Core or any ORM (ADR-001)
- Removing SQLite (it remains the default)

---

## 🔗 Origin

Migrated from `sdd/postgres-backend/`

Original artifacts:
- Proposal: `sdd/postgres-backend/propose/proposal.md`
- Spec: `sdd/postgres-backend/spec/spec.md`

---

---

## As a user...

**As**: Developer or IT admin
**I want**: run `engram serve` with PostgreSQL as the persistence backend
**To**: handle 10+ concurrent writers, enable enterprise-grade backup, HA, and observability

---

## Acceptance Criteria

- [ ] `PostgresStore.cs` implements all 22 `IStore` methods
- [ ] `StoreConfig` extended with `DbType` and `PgConnectionString`
- [ ] `Program.cs` switches backend via `ENGRAM_DB_TYPE` / `ENGRAM_PG_CONNECTION` env vars
- [ ] PostgreSQL schema created via idempotent in-code migrations
- [ ] Full-text search implemented via `tsvector` stored generated column + GIN index
- [ ] Parity test suite runs the same tests against `SqliteStore` and `PostgresStore`
- [ ] Docker Compose includes PostgreSQL companion service
- [ ] `docs/POSTGRES-SETUP.md` documentation created/updated
- [ ] `docs/ARCHITECTURE.md` updated to reflect the new backend option

---

## Tasks (Implementation)

- [ ] Implement `PostgresStore.cs` — all 22 `IStore` methods
- [ ] Extend `StoreConfig` with `DbType` and `PgConnectionString`
- [ ] Update `Program.cs` with backend selection via env vars
- [ ] Implement PostgreSQL schema migrations (idempotent, in-code)
- [ ] Implement FTS via `tsvector` stored generated column + GIN index
- [ ] Write parity test suite (same tests for SqliteStore and PostgresStore)
- [ ] Create Docker Compose with PostgreSQL companion
- [ ] Write `docs/POSTGRES-SETUP.md`
- [ ] Update `docs/ARCHITECTURE.md`
- [ ] Run T3 (Docker + Postgres integration tests)

---

## 📝 Notes

This HU was created during FlowDoc adoption (2026-06-01) to consolidate documentation into the FlowDoc structure.

Original location: `sdd/postgres-backend/`
Current status: Migrated to FlowDoc

See `sdd/README.md` for full migration mapping.