# Tasks: Obsidian Export (`obsidian-export`)

## Phase 1: Project Scaffolding and Foundation

- [x] 1.1 Create `src/Engram.Obsidian/Engram.Obsidian.csproj` — class library targeting `net10.0`, `<ProjectReference>` to `Engram.Store`, no extra NuGet packages
- [x] 1.2 Create `src/Engram.Obsidian/IObsidianStoreReader.cs` — narrow read-only interface with `ExportAsync()` and `StatsAsync()`
- [x] 1.3 Create `src/Engram.Obsidian/StoreReaderAdapter.cs` — wraps `IStore` → `IObsidianStoreReader`
- [x] 1.4 Create `tests/Engram.Obsidian.Tests/Engram.Obsidian.Tests.csproj` — xUnit test project referencing `Engram.Obsidian`
- [x] 1.5 Update `engram-dotnet.slnx` — add both projects to the solution
- [x] 1.6 Copy `graph.json` from Go original to `src/Engram.Obsidian/graph.json`
- [x] 1.7 Add `<EmbeddedResource Include="graph.json" />` to `Engram.Obsidian.csproj`

**Depends on**: Nothing (foundation)

---

## Phase 2: Core Primitives (Slug, Topic Prefix, Sync State)

- [x] 2.1 Create `src/Engram.Obsidian/Slug.cs` — port `slug.go`:
  - `Slugify(string title, long id) → string`
  - Lowercase, regex `[^a-z0-9]+` → `-`, trim, truncate 60, append `-{id}`
  - Edge case: empty title → `"observation-{id}"`

- [x] 2.2 Create `src/Engram.Obsidian/SyncState.cs` — port `state.go`:
  - `SyncState` class: `LastExportAt`, `Files` (dictionary `<long, string>`), `SessionHubs`, `TopicHubs`, `Version`
  - `ExportResult` class: `Created`, `Updated`, `Deleted`, `Skipped`, `HubsCreated`, `Errors` (list of exceptions)
  - `ReadState(string path) → SyncState` (file not found → empty state, no error)
  - `WriteState(string path, SyncState state)` — indented JSON, UTF-8 no BOM

- [x] 2.3 Create `src/Engram.Obsidian/TopicPrefix.cs` — port `topicPrefix` from `markdown.go`:
  - `TopicPrefix(string topicKey) → string` — everything before last `/`, or whole string if no `/`

**Depends on**: 1.1

---

## Phase 3: Graph Config

- [x] 3.1 Create `src/Engram.Obsidian/GraphConfig.cs` — port `graph.go`:
  - `GraphConfigMode` enum (`Preserve`, `Force`, `Skip`)
  - `ParseGraphConfigMode(string s) → GraphConfigMode` — case-sensitive, error for invalid values
  - `WriteGraphConfig(string vaultPath, GraphConfigMode mode)`:
    - Skip → no-op
    - Preserve → create only if `.obsidian/graph.json` doesn't exist
    - Force → always overwrite
    - Read embedded `graph.json` via `Assembly.GetExecutingAssembly().GetManifestResourceStream()`
    - Create `.obsidian/` directory with `Directory.CreateDirectory()` if needed

**Depends on**: 1.7 (embedded resource available)

---

## Phase 4: Markdown Rendering

- [x] 4.1 Create `src/Engram.Obsidian/MarkdownRenderer.cs` — port `markdown.go`:
  - `ObservationToMarkdown(Observation obs) → string` — full markdown document:
    - YAML frontmatter: `id`, `type`, `project`, `scope`, `topic_key`, `session_id`, `created_at`, `updated_at`, `revision_count`, `tags` (project + type), `aliases` (title)
    - H1 title: `# {title}`
    - Content body (verbatim)
    - Wikilink footer with `---` separator
  - `BuildWikilinks(string sessionId, string topicKey) → List<string>`:
    - Session wikilink: `*Session*: [[session-{sessionId}]]`
    - Topic wikilink: `*Topic*: [[topic-{prefix}]]` (prefix with `--` replacing `/`)

**Depends on**: 2.3 (TopicPrefix)

---

## Phase 5: Hub Generation

- [x] 5.1 Create `src/Engram.Obsidian/HubGenerator.cs` — port `hub.go`:
  - `ObsRef` record: `Slug`, `Title`, `TopicKey`, `Type`
  - `ShouldCreateTopicHub(int count) → bool` — return `true` when `count >= 2`
  - `SessionHubMarkdown(string sessionId, IList<ObsRef> observations) → string` — frontmatter + H1 + `## Observations` wikilink list
  - `TopicHubMarkdown(string prefix, IList<ObsRef> observations) → string` — frontmatter + H1 + `## Related Observations` wikilink list with type annotations

**Depends on**: 2.1 (Slug)

---

## Phase 6: Exporter Engine

- [x] 6.1 Create `src/Engram.Obsidian/ExportConfig.cs` — port `ExportConfig` struct from `exporter.go`:
  - `VaultPath` (required), `Project` (optional), `Limit` (0 = no limit), `Force`, `IncludePersonal`, `GraphConfig` mode

- [x] 6.2 Create `src/Engram.Obsidian/Exporter.cs` — port `exporter.go` (core ~250 lines):
  - Constructor: `Exporter(IObsidianStoreReader store, ExportConfig config)`
  - `Export() → ExportResult`:
    1. Validate `VaultPath` is not empty
    2. Write graph config
    3. Create vault namespace directories (`engram/`, `engram/_sessions/`, `engram/_topics/`)
    4. Read sync state (`ReadState`)
    5. If `--force`, reset state to empty
    6. Call `store.ExportAsync()` to get all data
    7. Handle deleted observations: remove files, track in state
    8. Filter observations:
       - Skip deleted
       - Apply project filter (`--project`)
       - Apply scope filter (default: team only, unless `--include-personal`)
       - Apply incremental filter (skip if `updated_at <= last_export_at` AND tracked in state)
    9. For each observation: compute path (`{project}/{type}/{slug}.md`), create directory, generate markdown, compare with existing file (content idempotency), write if changed
    10. Collect session and topic refs during the loop
    11. Generate session hub notes and topic hub notes
    12. Write state file with updated `last_export_at` and tracking info
    13. Return `ExportResult` with aggregated counts

- [x] 6.3 `obsToRef` helper — convert `Observation → ObsRef` using `Slugify`
- [x] 6.4 `obsTopicPrefix` helper — extract topic prefix from observation's `TopicKey`

**Depends on**: 2.1, 2.2, 3.1, 4.1, 5.1

---

## Phase 7: CLI Wiring

- [x] 7.1 Add `<ProjectReference Include="../Engram.Obsidian/Engram.Obsidian.csproj" />` to `src/Engram.Cli/Engram.Cli.csproj`
- [x] 7.2 Register `obsidian-export` command in `src/Engram.Cli/Program.cs`:
  - Options: `--vault` (required), `--project`, `--include-personal`, `--force`, `--graph-config` (default: `"preserve"`), `--limit`
  - Handler: open store, create adapter, create exporter, call `Export()`, print summary to console
  - Validate vault path exists (fail-fast)
  - Validate `--graph-config` value (try-parse, print error)

**Depends on**: 6.2

---

## Phase 8: Tests

- [x] 8.1 Port `slug_test.go` — test all edge cases: normal, special chars, empty, long, trailing hyphens
- [x] 8.2 Port `markdown_test.go` — test full rendering, null fields, wikilink presence/absence by scenario, YAML field formatting
- [x] 8.3 Port `hub_test.go` — test session hub rendering, topic hub rendering, threshold behavior, empty lists, topic prefix extraction
- [x] 8.4 Port `state_test.go` — test read/write round-trip, file-not-found, corrupted JSON, null map initialization
- [x] 8.5 Port `graph_test.go` — test all three modes (preserve, force, skip), file detection, embedded content verification, invalid mode parsing
- [x] 8.6 Port `testhelpers_test.go` — helper factory methods for creating test observations, sessions, sync states
- [x] 8.7 Port `exporter_test.go` — the big one (~628 lines in Go). Cover:
  - Full export (fresh vault)
  - Incremental export (nothing changed, new obs, updated obs)
  - Deleted observation cleanup
  - Project filter
  - Scope security (team default, personal with flag, legacy "project" scope)
  - Content idempotency (skip unchanged, update changed)
  - Force re-export
  - Error handling (unwritable path, missing vault)
  - Hub generation during export
  - State file correctness after export
  - Export result counts accuracy

**Depends on**: Phase 2, 3, 4, 5, 6 (tests are written alongside or after implementation)
