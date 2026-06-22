# Plan: deep-audit-fixes

## 1. Impact and dependencies

5 fixes from forge-verify audit (2026-06-22):
- FR-001: Deadlock risk — `.Result` blocks sync context (SEC-001/BP-001)
- FR-002: Documentation-only — remove non-existent endpoint
- FR-003: Documentation-only — fix version mismatch
- FR-004: Documentation-only — tool count mismatch (26 → 28)
- FR-005: Documentation-only — remove misleading JWT claim

No new dependencies. FR-001 requires T3 (Postgres) before merge per NFR-003.

## 2. File changes

| Batch | File | Change |
|-------|------|--------|
| 1.1 | `src/Stores/SqliteStore.cs:1639` | `.Result` → `await` |
| 1.2 | `src/Stores/PostgresStore.cs:1445` | `.Result` → `await` |
| 2.1 | `docs/API-REFERENCE.md` | Remove `POST /debug-test` section |
| 2.2 | `docs/API-REFERENCE.md` | `"0.3.0"` → `"1.1.0"` in /health example |
| 3.1 | `docs/README.md` | Tool count 26 → 28 |
| 3.2 | `docs/README.es.md` | Tool count 26 → 28 |
| 3.3 | `docs/ARCHITECTURE.md` | Tool count 26 → 28 |
| 3.4 | `src/Mcp/EngramMcpServer.cs:10` | Comment: 19 → 28 |
| 4.1 | `docs/ARCHITECTURE.md` | JWT `[planned]` instead of claimed implemented |

## 3. Implementation checklist

- [x] 1.1 SqliteStore.cs:1639 — `.Result` → `await`
- [x] 1.2 PostgresStore.cs:1445 — `.Result` → `await`
- [x] 2.1 API-REFERENCE.md — remove `POST /debug-test` endpoint
- [x] 2.2 API-REFERENCE.md — fix version `"0.3.0"` → `"1.1.0"`
- [x] 3.1 README.md: 26 → 28
- [x] 3.2 README.es.md: 26 → 28
- [x] 3.3 ARCHITECTURE.md: 26 → 28
- [x] 3.4 EngramMcpServer.cs:10 — comment 19 → 28
- [x] 4.1 ARCHITECTURE.md — mark JWT `[planned]`
- [x] 5.1 Build passes (`dotnet build -c Release`)
- [x] 5.2 T2 tests pass (`dotnet test --filter "Category!=RequiresDocker"`)
- [x] 5.3 Verify no `.Result` in sync paths (`grep -r "\.Result" src/Stores/`)