# Tasks: Promotion Level 2 — .md Artifact Promotion

## Phase 1: Foundation

- [x] 1.1 Add `MdPath` property to `Observation`, `AddObservationParams`, `UpdateObservationParams` in `src/Engram.Store/Models.cs`
- [x] 1.2 Create `src/Engram.MdGeneration/Engram.MdGeneration.csproj` (net10.0, ref Engram.Store)
- [x] 1.3 Create `src/Engram.MdGeneration/MdSlug.cs` with `Slugify(title, id)` — lowercase, hyphens, 60-char truncate, collision hash
- [x] 1.4 Create `src/Engram.MdGeneration/MdTemplateEngine.cs` — `Render(Observation)` returns YAML frontmatter + body
- [x] 1.5 Create `src/Engram.MdGeneration/MdIndexGenerator.cs` — `GenerateIndex(IReadOnlyList<Observation>)` returns index.md content

## Phase 2: Store Implementation

- [x] 2.1 Add `PromoteToMdAsync`, `SyncMdToRepoAsync`, `GenerateIndexAsync` to `IStore` in `src/Engram.Store/IStore.cs`
- [x] 2.2 Add `md_path TEXT` column migration + implement new methods in `src/Engram.Store/SqliteStore.cs`
- [x] 2.3 Add `md_path TEXT` column migration + implement new methods in `src/Engram.Store/PostgresStore.cs`
- [x] 2.4 Proxy new methods in `src/Engram.Store/HttpStore.cs`
- [x] 2.5 Create `src/Engram.MdGeneration/PromotionService.cs` orchestrating promote + batch sync + index

## Phase 3: Integration

- [x] 3.1 Register `mem_promote_to_md` tool in `src/Engram.Mcp/EngramTools.cs` — accepts observation_id, calls PromotionService
- [x] 3.2 Register `mem_sync_md_to_repo` tool in `src/Engram.Mcp/EngramTools.cs` — accepts dry_run, calls PromotionService
- [x] 3.3 Add `engram promote` subcommand in `src/Engram.Cli/Program.cs` following existing CLI pattern
- [x] 3.4 Add HTTP POST endpoints for promote/sync in `src/Engram.Server/EngramServer.cs`
- [x] 3.5 Add link verification method — checks all observation.md_path → file exists + frontmatter → observation exists

## Phase 4: Testing

- [x] 4.1 Unit: `MdTemplateEngine.Render()` — frontmatter fields, null fields, edge cases
- [x] 4.2 Unit: `MdSlug.Slugify()` — collision, empty title, max length truncation
- [x] 4.3 Unit: `PromotionService` — dry-run doesn't write files, errors on missing observation
- [x] 4.4 Integration: `SqliteStore.PromoteToMdAsync` roundtrip — md_path persisted, file created, bidir link intact
- [x] 4.5 Integration: `SqliteStore.SyncMdToRepoAsync` batch — N unpromoted promoted, empty sync, dry-run preview
- [x] 4.6 E2E: `mem_promote_to_md` MCP tool → .md on disk + DB updated
- [x] 4.7 E2E: `engram promote` CLI command → file created + observation updated
