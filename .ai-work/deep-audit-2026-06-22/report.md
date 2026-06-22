# Deep Audit Report — engram-dotnet — 2026-06-22

## 1. Documentation vs Code

### Critical Issues

| # | Issue | Evidence |
|---|-------|----------|
| **DOC-001** | API-REFERENCE.md documents removed `/debug-test` endpoint | `TECHNICAL-DEBT.md` TD-015 confirms removal; `grep debug-test src/` returns zero matches in `src/` |
| **DOC-002** | API-REFERENCE.md `/health` example shows `"version":"0.3.0"`; code returns `"1.1.0"` | `EngramServer.cs:213`: `version = "1.1.0"`. Stale example. |
| **DOC-003** | MCP tools count inconsistent: README/ARCHITECTURE say 26; code has 28 | `EngramTools.cs` has 28 `[McpServerTool]` attributes (mem_relations, mem_lineage_obs added post-doc). `EngramMcpServer.cs:10` comment still says "19 tools". |

### Warnings

| # | Issue | Evidence |
|---|-------|----------|
| **DOC-004** | ARCHITECTURE.md lists JWT auth as tech stack: "Auth: JWT (optional, via `ENGRAM_JWT_SECRET`)". Not implemented — code comments say "future". | `HttpStore.cs:52-53`: `// JWT auth — future: when server validates tokens`. Misleading: `StoreConfig.JwtSecret` reads the env var but nothing enforces auth with it. |
| **DOC-005** | ARCHITECTURE.md says SqliteStore "~2400 lines" (code: 2840) and PostgresStore "~2100 lines" (code: 2570) | Both files grew ~440 lines since last doc update. |
| **DOC-006** | TECHNICAL-DEBT.md line counts for God Classes are stale: TD-001 says PostgresStore "2136 líneas" (now 2570), TD-002 says SqliteStore "2397 líneas" (now 2840) | Audit baseline drifted. |
| **DOC-007** | `docs/decisions/` directory doesn't exist — referenced as default MD promotion target in both API-REFERENCE.md and EngramTools.cs | `glob docs/decisions/**` returns no files. Created lazily at first promotion. |
| **DOC-008** | ARCHITECTURE.md MCP tools section lists 26 tools by name. Two new tools (`mem_relations`, `mem_lineage_obs`) are not listed. | Section "26 tools registered" is stale; should be 28. |

### OK

- REST endpoints: 41 total in code (33 EngramServer + 8 CloudSync) = 41 claimed in README ✅
- DEVELOPMENT.md structure + commands current ✅
- `scripts/dev-test.sh` exists ✅
- Referenced docs (`MCP-CONFIG.md`, `SYNC-SETUP.md`, `SETUP-WIZARD.md`, `MANUAL-TESTING-CHECKLIST.md`) all exist ✅
- `docs/architecture/adr/` contains ADR-002 and ADR-003 ✅
- `BodyDebugLoggingMiddleware` exists and is registered ✅
- IStore interface has 40+ method signatures — matches "35+ methods" claim ✅

---

## 2. Security

### Critical Issues

| # | Issue | Evidence |
|---|-------|----------|
| **SEC-001** | `.Result` blocking on Tasks — deadlock risk in synchronous contexts | `SqliteStore.cs:1639`: `var result = PromoteToMdAsync(id, mdDir).Result;` inside `SyncMdToRepoAsync`. `PostgresStore.cs:1445`: identical pattern. These methods are themselves called by handlers that expect async behavior. If the synchronization context is captured, this will deadlock. |

### Warnings

| # | Issue | Evidence |
|---|-------|----------|
| **SEC-002** | `Process.Start` with dynamic `path` argument in `DiagnosticService.cs:354` | `git check-ignore -q -- {path}` — while `UseShellExecute = false` prevents shell injection, the `path` variable comes from `ProjectIdentity.cs` detection logic and is not validated against traversal. Low risk (internal code only). |
| **SEC-003** | No authentication on any REST endpoint | All 41 routes are open. Only the `X-Engram-User` header provides identity but there is no JWT verification, API key check, or token validation. JWT auth is declared in ARCHITECTURE.md but code says "future". |
| **SEC-004** | `catch (Exception)` without logging in `SqliteStore.cs:2415` | Empty catch blocks mask underlying failures. |
| **SEC-005** | `catch (Exception)` in `DiagnosticService.cs:315` swallows identity check failures without logging | The catch inside `CheckProjectIdentityAsync` creates a fail-open diagnostic result — no error logged. |

### OK

- **No SQL injection**: All SQL queries use parameterized `@param` syntax across both SqliteStore and PostgresStore ✅
- **No hardcoded secrets or API keys**: Password/connection string handling goes through env vars and `StoreConfig` ✅
- **No `async void`**: Zero occurrences in `src/` ✅
- **No `Thread.Sleep`** in production code ✅
- **Path traversal**: `Path.GetFullPath` used appropriately in `ProjectDetector.cs` ✅
- CSPRNG usage: `randomblob(16)` for sync IDs in SQLite — acceptable ✅

---

## 3. Best Practices

### Violations

| # | Issue | Location | Severity |
|---|-------|----------|----------|
| **BP-001** | `.Result` blocking on async Task | `SqliteStore.cs:1639`, `PostgresStore.cs:1445` | 🔴 High |
| **BP-002** | Empty `catch (Exception)` — silently suppresses errors | `SqliteStore.cs:2415` | 🟡 Medium |
| **BP-003** | EngramMcpServer.cs comment claims "19 tools" — stale count | `EngramMcpServer.cs:10` | 🟢 Low |

### Warnings

| # | Issue | Evidence |
|---|-------|----------|
| **BP-004** | `catch (Exception)` without logging (22 instances total). Most do log/rethrow, but a few don't: | `DiagnosticService.cs:108,187,241,315`: creates fail-open diagnostic. `SqliteStore.cs:2415`: empty catch. `CloudSyncEndpoints.cs:95`: logs but then returns error with exception message (potential info leak). |
| **BP-005** | ADR-003 XML comment policy partially followed | `EngramTools.cs` has 21 `///<summary>` blocks — class-level ones are useful, helper method ones (`ResolveProject`, `AutoClassifyScope`) follow ADR-003 spirit. `EngramMcpServer.cs:17` has summary for `CreateBuilder` which is acceptable. However, old code in parsers (`SinceArgumentParser`, `WatchIntervalParser`) still has redundant `<param>`/`<returns>` as noted in TD-019. |
| **BP-006** | MD promotion `.Result` calls break async chain | Both `SyncMdToRepoAsync` implementations call `PromoteToMdAsync(id, mdDir).Result` in a loop, blocking the thread. Should use `await`. |

### God Classes (documented, unresolved)

- **SqliteStore.cs**: 2840 lines (TD-002, originally 2397)
- **PostgresStore.cs**: 2570 lines (TD-001, originally 2136)
- **EngramTools.cs**: 1220 lines (TD-009, originally 1034)

All three grew since last audit. TECHNICAL-DEBT.md line counts need updating.

---

## 4. Technical Debt

### Unresolved from TECHNICAL-DEBT.md

All 19 TD items were reviewed. Status:

| TD | Status | Notes |
|----|--------|-------|
| TD-001 | ❌ Open | PostgresStore now 2570 lines (was 2136) — getting worse |
| TD-002 | ❌ Open | SqliteStore now 2840 lines (was 2397) — getting worse |
| TD-003 | ❌ Open | "Async" methods still synchronous — 6× Postgres, 14× Sqlite |
| TD-004 | ❌ Open | `AddObservationAsync` 3 nested paths — not refactored |
| TD-005 | ❌ Open | `StatsAsync` 3 separate queries — not optimized |
| TD-006 | ❌ Open | `ImportAsync` N+1 inserts — not batched |
| TD-007 | ❌ Open | `ReadSessionFromOps` naming — not renamed |
| TD-008 | ❌ Open | `AddWithValue` null warnings |
| TD-009 | ❌ Open | EngramTools.cs 1220 lines (was 1034) — growing |
| TD-010 | ❌ Open | StoreReaderAdapter null check redundant |
| TD-011 | ❌ Open | GraphConfig no embedded resource cache |
| TD-012 | ❌ Open | StateFile write not atomic |
| TD-013 | ❌ Open | **P0**: `ApplyPulledMutationAsync` stub — sync data not persisted locally with SQLite |
| TD-014 | ❌ Open | SqliteStore 14× "Async" methods blocking |
| TD-015 | ✅ Resolved | Console.Error + /debug-test removed |
| TD-016 | ❌ Open | Exporter full scan, doesn't use incremental API |
| TD-017 | ❌ Open | WatchLoop prefetch without feeding Exporter |
| TD-018 | ❌ Open | `MemCurrentProject` doesn't expose ambiguity hint |
| TD-019 | ❌ Open | XML comments redundant in parsers (phase 2 pending per ADR-003) |

### New Findings

| # | Issue | Severity |
|---|-------|----------|
| **NEW-001** | TECHNICAL-DEBT.md baseline line counts for TD-001 (2136) and TD-002 (2397) are stale — both classes grew ~440 lines | 🟡 Medium |
| **NEW-002** | Doc drift between ARCHITECTURE.md line counts (~2400/~2100) and reality (2840/2570) | 🟡 Medium |
| **NEW-003** | API-REFERENCE.md `/health` version example "0.3.0" — should auto-derive or link to build property | 🟡 Medium |
| **NEW-004** | `docs/decisions/` default MD promote directory referenced in multiple places but doesn't exist in the repo | 🟢 Low |

---

## 5. Summary

### Must Fix Before Release (P0-P1)

1. **SEC-001 / BP-001**: Fix `.Result` deadlock in `SqliteStore.SyncMdToRepoAsync` and `PostgresStore.SyncMdToRepoAsync` — replace `PromoteToMdAsync(id, mdDir).Result` with `await PromoteToMdAsync(id, mdDir)`. These are production code paths.

2. **DOC-001**: Remove `/debug-test` endpoint documentation from `docs/API-REFERENCE.md` — the endpoint was removed. Stale documentation is misleading.

3. **DOC-002**: Update API-REFERENCE.md `/health` version from `"0.3.0"` to `"1.1.0"`.

### Should Fix Soon (P2)

4. **DOC-003**: Update MCP tool counts across all docs: 26 → 28 (README, README.es, ARCHITECTURE.md, EngramMcpServer.cs comment).

5. **DOC-004**: Either implement JWT auth or remove it from ARCHITECTURE.md tech stack section. Currently misleading.

6. **BP-004**: Add logging to empty/swallowing `catch (Exception)` blocks (particularly SqliteStore.cs:2415, DiagnosticService.cs:315).

7. **TD-013 (P0)**: `ApplyPulledMutationAsync` stub — this is a functional bug for SQLite+sync users. Already documented but unresolved.

8. **DOC-005/006**: Update line count baselines in ARCHITECTURE.md (`~2400` → `~2840`, `~2100` → `~2570`) and TECHNICAL-DEBT.md (TD-001, TD-002).

### Nice to Have (P3)

9. **BP-005**: Complete ADR-003 phase 2 — prune redundant `<param>`/`<returns>`/`<exception>` from parsers.

10. **DOC-008**: Add `mem_relations` and `mem_lineage_obs` to the ARCHITECTURE.md MCP tools section.

11. **NEW-004**: Create `docs/decisions/` directory with a `.gitkeep` so the default path is valid.

---

### Audit Metrics

| Metric | Count |
|--------|-------|
| **Critical issues** | 5 (DOC-001, DOC-002, DOC-003, SEC-001, BP-001) |
| **Warnings** | 14 |
| **OK/Verified** | 10+ categories |
| **TECHNICAL-DEBT items open** | 18 of 19 |
| **New findings** | 4 |
| **Files audited** | 12 source + 8 docs |
| **Lines of code scanned** | ~8,000 (source, selective) |
