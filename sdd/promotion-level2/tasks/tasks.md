# Tasks: Promotion Level 2 — .md Artifact Promotion

## Phase 1: Foundation

- [ ] 1.1 Add `MdPath` property to `Observation`, `AddObservationParams`, `UpdateObservationParams` in `src/Engram.Store/Models.cs`
- [ ] 1.2 Create `src/Engram.MdGeneration/Engram.MdGeneration.csproj` (net10.0, ref Engram.Store)
- [ ] 1.3 Create `src/Engram.MdGeneration/MdSlug.cs` with `Slugify(title, id)` — lowercase, hyphens, 60-char truncate, collision hash
- [ ] 1.4 Create `src/Engram.MdGeneration/MdTemplateEngine.cs` — `Render(Observation)` returns YAML frontmatter + body
- [ ] 1.5 Create `src/Engram.MdGeneration/MdIndexGenerator.cs` — `GenerateIndexAsync(IList<Observation>, mdDir)` writes `index.md`

## Phase 2: Store Implementation

- [ ] 2.1 Add `PromoteToMdAsync`, `SyncMdToRepoAsync`, `GenerateIndexAsync` to `IStore` in `src/Engram.Store/IStore.cs`
- [ ] 2.2 Add `md_path TEXT` column migration + implement new methods in `src/Engram.Store/SqliteStore.cs`
- [ ] 2.3 Add `md_path TEXT` column migration + implement new methods in `src/Engram.Store/PostgresStore.cs`
- [ ] 2.4 Proxy new methods in `src/Engram.Store/HttpStore.cs`
- [ ] 2.5 Create `src/Engram.MdGeneration/PromotionService.cs` orchestrating promote + batch sync + index

## Phase 3: Integration

- [ ] 3.1 Register `mem_promote_to_md` tool in `src/Engram.Mcp/EngramTools.cs` — accepts observation_id, calls PromotionService
- [ ] 3.2 Register `mem_sync_md_to_repo` tool in `src/Engram.Mcp/EngramTools.cs` — accepts dry_run, calls PromotionService
- [ ] 3.3 Add `engram promote` subcommand in `src/Engram.Cli/Program.cs` following existing CLI pattern
- [ ] 3.4 Add HTTP POST endpoints for promote/sync in `src/Engram.Server/EngramServer.cs`
- [ ] 3.5 Add link verification method — checks all observation.md_path → file exists + frontmatter → observation exists

## Phase 4: Testing

- [ ] 4.1 Unit: `MdTemplateEngine.Render()` — frontmatter fields, null fields, edge cases
- [ ] 4.2 Unit: `MdSlug.Slugify()` — collision, empty title, max length truncation
- [ ] 4.3 Unit: `PromotionService` — dry-run doesn't write files, errors on missing observation
- [ ] 4.4 Integration: `SqliteStore.PromoteToMdAsync` roundtrip — md_path persisted, file created, bidir link intact
- [ ] 4.5 Integration: `SqliteStore.SyncMdToRepoAsync` batch — N unpromoted promoted, empty sync, dry-run preview
- [ ] 4.6 E2E: `mem_promote_to_md` MCP tool → .md on disk + DB updated
- [ ] 4.7 E2E: `engram promote` CLI command → file created + observation updated
