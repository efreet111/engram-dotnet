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

## 📝 Notes

This HU was created during FlowDoc adoption (2026-06-01) to consolidate documentation into the FlowDoc structure.

---

## 🔄 Migration Reference

Original location: `sdd/postgres-backend/`
Current status: Migrated to FlowDoc

See `sdd/README.md` for full migration mapping.