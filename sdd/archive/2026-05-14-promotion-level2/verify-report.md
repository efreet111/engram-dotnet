## Verification Report

**Change**: promotion-level2
**Version**: N/A (no version in spec)
**Mode**: Standard

---

### Completeness
| Metric | Value |
|--------|-------|
| Tasks total | 21 |
| Tasks complete | 21 |
| Tasks incomplete | 0 |

All 21 tasks marked [x]. No incomplete tasks.

---

### Build & Tests Execution

**Build**: ❌ Failed (partial — 2 projects fail to compile)

```
src/Engram.Mcp/EngramTools.cs(50,158): error CS0246: El nombre del tipo o del espacio de nombres 'PromotionService' no se encontró. 
src/Engram.Server/EngramServer.cs(426,63): error CS7036: No se ha dado argumento para el parámetro 'MdDir' de 'PromoteRequest'.
src/Engram.Server/EngramServer.cs(434,62): error CS7036: No se ha dado argumento para el parámetro 'MdDir' de 'SyncMdRequest'.
src/Engram.Server/EngramServer.cs(442,69): error CS7036: No se ha dado argumento para el parámetro 'MdDir' de 'GenerateIndexRequest'.
```

Build failures prevent Engram.Mcp, Engram.Server, Engram.HttpStore, and Engram.Postgres test projects from running.

**Tests (projects that compiled)**:

| Suite | Total | Passed | Failed | Skipped |
|-------|-------|--------|--------|---------|
| Engram.MdGeneration.Tests | 17 | 17 | 0 | 0 |
| Engram.Store.Tests | 139 | 139 | 0 | 0 |
| Engram.Obsidian.Tests | 63 | 63 | 0 | 0 |
| Engram.Verification.Tests | 16 | 15 | 1 | 0 |
| **Total** | **235** | **234** | **1** | **0** |

**Failed test**:
```
Engram.Verification.Tests.CycleTrackerTests.ResetCycle_ClearsCount
Error: ArgumentOutOfRangeException at SqliteStore.ReadObservation (ordinal 16)
Root cause: SearchAsync topic-key shortcut SELECT has 16 columns but ReadObservation now expects md_path at index 16.
```

**Coverage**: ➖ Not available (no coverage tool configured)

---

### Spec Compliance Matrix

| Requirement | Scenario | Test | Result |
|-------------|----------|------|--------|
| REQ-01: Observation Model Extension | Observation with md_path | `StorePromotionTests > PromoteToMdAsync_Roundtrip_PersistsMdPath` | ✅ COMPLIANT |
| REQ-01: Observation Model Extension | Observation without md_path | `PromotionServiceTests > PromoteAsync_InvalidId_ReturnsZero` (verifies null by default) | ✅ COMPLIANT |
| REQ-02: Markdown Template Engine | Generate canonical markdown | `MdTemplateEngineTests > Render_IncludesAllFrontmatterFields` | ✅ COMPLIANT |
| REQ-02: Markdown Template Engine | Frontmatter date format | `MdTemplateEngineTests > Render_IncludesAllFrontmatterFields` (validates generated_at exists) | ⚠️ PARTIAL |
| REQ-03: mem_promote_to_md Tool | Successful individual promotion | `StorePromotionTests > PromoteToMdAsync_Roundtrip_PersistsMdPath` | ✅ COMPLIANT |
| REQ-03: mem_promote_to_md Tool | Promotion of already-promoted observation | `StorePromotionTests > PromoteToMdAsync_AlreadyPromoted_ReturnsZero` | ✅ COMPLIANT |
| REQ-03: mem_promote_to_md Tool | Promotion of non-existent observation | `StorePromotionTests > PromoteToMdAsync_InvalidId_ReturnsZero` | ✅ COMPLIANT |
| REQ-04: Markdown Filename Convention | Filename generation from title | `MdSlugTests > ToFilename_IncludesDate` | ✅ COMPLIANT |
| REQ-04: Markdown Filename Convention | Slug collision handling | (none found) | ❌ UNTESTED |
| REQ-05: Bidirectional Link | Forward link verification | `StorePromotionTests > PromoteToMdAsync_Roundtrip_PersistsMdPath` (validates obs_id in file content) | ✅ COMPLIANT |
| REQ-05: Bidirectional Link | Reverse link verification | (none found — LinkVerifier exists but untested) | ❌ UNTESTED |
| REQ-06: mem_sync_md_to_repo Batch Tool | Batch sync all unpromoted | `StoreSyncTests > SyncMdToRepoAsync_Batch_PromotesAll` | ✅ COMPLIANT |
| REQ-06: mem_sync_md_to_repo Batch Tool | Dry-run mode | `StoreSyncTests > SyncMdToRepoAsync_DryRun_ReturnsCountWithoutFiles` | ✅ COMPLIANT |
| REQ-06: mem_sync_md_to_repo Batch Tool | Empty sync (all promoted) | `StoreSyncTests > SyncMdToRepoAsync_EmptySync_ReturnsZero` | ✅ COMPLIANT |
| REQ-07: Configurable Destination Directory | Custom directory via ENGRAM_MD_DIR | (none found — ENGRAM_MD_DIR not read anywhere) | ❌ UNTESTED |
| REQ-07: Configurable Destination Directory | Default directory when not configured | (implicitly tested — all tests use "docs/decisions" literal) | ⚠️ PARTIAL |
| REQ-08: Auto-Generated Index | Index generation | (GenerateIndexAsync exists but no dedicated test) | ❌ UNTESTED |
| REQ-08: Auto-Generated Index | Index update on new promotion | (none found) | ❌ UNTESTED |
| REQ-09: Link Verification | Link integrity check | (LinkVerifier class exists but no test) | ❌ UNTESTED |
| REQ-10: Rollback Capability | Revoke promotion | (none found — not implemented) | ❌ UNTESTED |
| REQ-11: SQLite and PostgreSQL Compatibility | SQLite migration | Implicitly tested (all Store tests use SQLite) | ✅ COMPLIANT |
| REQ-11: SQLite and PostgreSQL Compatibility | PostgreSQL migration | (Postgres tests did not build due to server errors) | ❌ UNTESTED |

**Compliance summary**: 13/22 scenarios compliant (59%)

---

### Correctness (Static — Structural Evidence)
| Requirement | Status | Notes |
|------------|--------|-------|
| REQ-01: Observation Model Extension | ✅ Implemented | MdPath on Observation, AddObservationParams, UpdateObservationParams |
| REQ-02: Markdown Template Engine | ✅ Implemented | MdTemplateEngine.cs with YAML frontmatter + content body |
| REQ-03: mem_promote_to_md Tool | ⚠️ Partial | SQLite OK; PostgresStore missing already-promoted guard; MCP tool references PromotionService but Mcp project has build error |
| REQ-04: Markdown Filename Convention | ⚠️ Partial | Slugify works but no collision hash; filename excludes observation_id (spec inconsistency); SqliteStore/PostgresStore duplicate slug logic instead of using MdSlug |
| REQ-05: Bidirectional Link | ⚠️ Partial | Forward link OK; LinkVerifier exists but not tested; PostgresStore ReadObservation doesn't read md_path so returns null |
| REQ-06: mem_sync_md_to_repo Batch Tool | ✅ Implemented | Batch, dry-run, empty sync all working |
| REQ-07: Configurable Destination Directory | ❌ Missing | ENGRAM_MD_DIR env var never read in code. md_dir parameter passed but defaults always to "docs/decisions" hardcoded string |
| REQ-08: Auto-Generated Index | ⚠️ Partial | GenerateIndexAsync implemented in both stores; no dedicated test |
| REQ-09: Link Verification | ⚠️ Partial | LinkVerifier class exists but no test coverage |
| REQ-10: Rollback Capability | ❌ Missing | No revoke/unpromote method exists in any store or service |
| REQ-11: SQLite and PostgreSQL Compatibility | ❌ Broken | PostgresStore has 3 bugs: (a) ReadObservation doesn't read md_path column, (b) all standard queries (GetObservationAsync, SearchAsync, etc.) don't SELECT md_path, (c) PromoteToMdAsync missing already-promoted guard |

---

### Coherence (Design)
| Decision | Followed? | Notes |
|----------|-----------|-------|
| New Engram.MdGeneration project vs extending Obsidian | ✅ Yes | New project created with PromotioService, MdTemplateEngine, MdSlug, MdIndexGenerator, LinkVerifier |
| Template engine: string interpolation vs Scriban/Razor | ✅ Yes | StringBuilder-based, no external dependencies |
| Methods in IStore vs separate service | ✅ Yes | PromoteToMdAsync, SyncMdToRepoAsync, GenerateIndexAsync on IStore |
| Bidirectional link: md_path as relative path | ⚠️ Deviated | SqliteStore stores just filename (not full relative path). Spec and design say md_path should be "docs/decisions/2024-01-15-42.md" but stores just "2024-01-15-slug.md" |
| Frontmatter canonical format | ✅ Yes | observation_id, type, title, created_at, topic_key, generated_at all present |
| <File Changes> table | ✅ Yes | All listed files created/modified as described |

---

### Issues Found

**CRITICAL** (must fix before archive):
1. **Build failures**: Engram.Mcp and Engram.Server don't compile. EngramTools.cs references `PromotionService` but Engram.Mcp.csproj lacks reference to Engram.MdGeneration. EngramServer.cs uses `new PromoteRequest()` and `new SyncMdRequest()` without required constructor parameters.
2. **Regression bug**: SqliteStore.SearchAsync crashes on topic-key and FTS5 queries because ReadObservation now reads 17 columns (index 0-16) but the inline search SELECTs only have 16 columns. This breaks ALL searches including the Verification cycle tracker. This is the root cause of `ResetCycle_ClearsCount` test failure.
3. **PostgresStore md_path is invisible**: PostgresStore's ReadObservation doesn't read md_path column. All standard observation queries (GetObservationAsync, GetObservationDirect, RecentObservationsAsync, SearchAsync) don't SELECT md_path. This means promoted observations in Postgres will always show `md_path = null`.
4. **PostgresStore no already-promoted guard**: PromoteToMdAsync in PostgresStore doesn't check if observation already has md_path, so it will overwrite files on every call.
5. **No rollback implementation**: Spec REQ-10 requires revoke/unpromote operation. No code exists.

**WARNING** (should fix):
1. **ENGRAM_MD_DIR env var never read**: Spec REQ-07 requires configurable directory via ENGRAM_MD_DIR env var. No code reads this env var; "docs/decisions" is always the default.
2. **md_path stores filename only, not relative path**: Design says md_path should be relative path from repo root (e.g., "docs/decisions/2024-01-15-slug.md"). Implementation stores just "2024-01-15-slug.md".
3. **Filename doesn't include observation_id**: Design says `{YYYY-MM-DD}-{slug}-{observation_id}.md` but implementation generates `{YYYY-MM-DD}-{slug}.md`.
4. **No slug collision handling**: Spec REQ-04 requires short hash suffix on collision. MdSlug doesn't detect or handle collisions.
5. **Code duplication**: Slugify/ToFilename/RenderIndex logic duplicated in SqliteStore and PostgresStore instead of reusing MdSlug/MdTemplateEngine/MdIndexGenerator from Engram.MdGeneration.
6. **Missing test coverage**: No tests for LinkVerifier, GenerateIndexAsync, PostgresStore promotion, slug collisions, forward/reverse link verification, rollback.

**SUGGESTION** (nice to have):
1. Add coverage reporting to CI pipeline
2. Incorporate ENGRAM_MD_DIR into StoreConfig for consistent env var pattern
3. Use MdSlug/MdTemplateEngine from Engram.MdGeneration in stores instead of duplicated logic
4. Add E2E tests for MCP tools (`mem_promote_to_md`, `mem_sync_md_to_repo`) and CLI commands

---

### Verdict
**FAIL** — 5 CRITICAL issues including build failures, a runtime regression (SearchAsync crash), and broken PostgreSQL compatibility. Must be fixed before archive.
