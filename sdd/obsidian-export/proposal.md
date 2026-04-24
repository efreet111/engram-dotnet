# Proposal: Obsidian Export (`obsidian-export`)

| Campo       | Valor |
|-------------|-------|
| **Change**  | `obsidian-export` |
| **Status**  | Proposed |
| **Roadmap** | #3 — Obsidian Export |

---

## Intent

Port the Engram → Obsidian vault export engine from the Go original (`internal/obsidian/`) to .NET. This gives .NET users the ability to export their Engram memories into an Obsidian vault as structured markdown files with YAML frontmatter, wikilinks, session hubs, topic hubs, and Obsidian graph configuration.

---

## Scope

### In Scope (Fase A)

- **New project**: `src/Engram.Obsidian/` — separate class library (like `Engram.Sync`)
- **Slug generation**: `Slugify(title, id)` → filesystem-safe filename
- **Markdown rendering**: `ObservationToMarkdown()` with YAML frontmatter, H1 title, content body, wikilink footer
- **Wikilink building**: `buildWikilinks()` for session and topic cross-references
- **Topic prefix extraction**: `topicPrefix()` for grouping observations under topic hubs
- **Sync state**: `SyncState` JSON persistence for incremental export
- **Export result**: `ExportResult` summary (created/updated/deleted/skipped/hubs/errors)
- **Hub notes**: Session hub notes (`_sessions/{sessionId}.md`) and topic hub notes (`_topics/{prefix}.md`)
- **Graph config**: `GraphConfigMode` (preserve/force/skip), embedded `graph.json` as embedded resource, `WriteGraphConfig()` writing to `.obsidian/graph.json`
- **Store reader interface**: `IObsidianStoreReader` (read-only, narrow contract) based on `IStore` methods needed
- **Export engine**: `Exporter` class with full export + incremental export (state-based)
- **Deleted observation cleanup**: remove files for observations with `deleted_at` set
- **Project filter**: `--project` flag to export a single project
- **Scope security**: only `scope=team` exported by default; `scope=personal` requires `--include-personal` flag
- **CLI command**: `engram obsidian-export` registered in `Program.cs`
- **Tests**: matching Go original coverage (slug, markdown, hub, state, exporter, graph)

### Out of Scope (Fase A)

- **Watcher** (continuous sync daemon) — deferred to Fase B
- `--watch` flag — Fase B
- `--since` flag — Fase B (use state file instead)
- Phase B AI synthesis — separate roadmap item
- Obsidian plugin or hot-reload — out of scope entirely

---

## Approach

### D1 — New project: `src/Engram.Obsidian/`

Create `Engram.Obsidian.csproj` as a class library targeting `net10.0`, referencing `Engram.Store`. This keeps export logic isolated and testable, consistent with the `Engram.Sync` pattern.

**Rationale**: The exporter is a pure file-IO + transformation pipeline. It has no server, no MCP, no DI. A separate project avoids coupling these concerns into `Engram.Store` and makes the dependency on `IStore` explicit.

### D2 — Store dependency: `IObsidianStoreReader`

Define a narrow read-only interface in `Engram.Obsidian`:

```csharp
public interface IObsidianStoreReader
{
    Task<ExportData> ExportAsync();
    Task<Stats> StatsAsync();
}
```

The `Exporter` accepts `IObsidianStoreReader` in its constructor. `Engram.Store`'s `IStore` already has `ExportAsync()` and `StatsAsync()`. An adapter class `StoreReaderAdapter` wraps `IStore` → `IObsidianStoreReader`.

**Rationale**: Narrow interface = easy to mock in tests. Same pattern as Go's `StoreReader` interface. The adapter is internal and keeps the rest of the codebase unchanged.

### D3 — Embedded `graph.json`

Use the .NET equivalent of Go's `//go:embed`:

```xml
<ItemGroup>
  <EmbeddedResource Include="graph.json" />
</ItemGroup>
```

Read at runtime via `Assembly.GetExecutingAssembly().GetManifestResourceStream()`.

### D4 — File I/O: `System.IO` directly

No abstraction layer for file writes. The exporter writes to the vault path directly using `File.WriteAllText`, `Directory.CreateDirectory`, `File.Delete`, etc. This is consistent with the Go original.

**Rationale**: Wrapping file IO for testability is over-engineering here. Tests can write to a temp directory and verify output.

### D5 — Sync state: `System.Text.Json` with snake_case

Use the same `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }` that the rest of the codebase uses. `SyncState` and `ExportResult` classes use `[JsonPropertyName]` attributes for explicit field naming matching the Go original's JSON output.

### D6 — CLI: `engram obsidian-export` command

Register in `Program.cs` using `System.CommandLine`:

```text
engram obsidian-export --vault <path>
                       [--project <name>]
                       [--include-personal]
                       [--force]
                       [--graph-config preserve|force|skip]
                       [--limit <n>]
```

The handler:
1. Opens the store via `OpenStore()`
2. Creates the `StoreReaderAdapter`
3. Creates `ExportConfig` from CLI args
4. Calls `exporter.Export()`
5. Prints the result summary

### D7 — Vault structure

```
{vault}/
  .obsidian/
    graph.json              ← written by WriteGraphConfig()
  engram/
    .engram-sync-state.json ← incremental state
    _sessions/
      {sessionId}.md        ← session hub notes
    _topics/
      {prefix}.md           ← topic hub notes
    {project}/
      {type}/
        {slug}.md           ← individual observation notes
```

### D8 — Scope security model

| Scope | Default export | Required flag |
|-------|---------------|---------------|
| `team` | Yes | — |
| `personal` | No | `--include-personal` |

Observations with `scope == "project"` (legacy) are treated as `team` for export purposes (consistent with `Normalizers.NormalizeScope()` in the Go original).

### D9 — Filename convention

Individual observation files follow: `{vault}/engram/{project}/{type}/{slug}.md` where:
- `project = obs.Project ?? "unknown"`
- `type = obs.Type` (e.g. `bugfix`, `architecture`, `decision`)
- `slug = Slugify(obs.Title, obs.Id)`

### D10 — Content equivalence

Each `.md` file must be byte-identical in structure to the Go output:
- YAML frontmatter with `---` delimiters
- Fields: `id`, `type`, `project`, `scope`, `topic_key`, `session_id`, `created_at`, `updated_at`, `revision_count`, `tags` (project + type), `aliases` (title)
- H1 title: `# {title}`
- Content body (verbatim)
- Wikilink footer with `---` separator, session wikilink, topic wikilink

---

## Key Changes by File

| File | Change Type | Description |
|------|------------|-------------|
| `src/Engram.Obsidian/Engram.Obsidian.csproj` | **NEW** | Class library, references Engram.Store, embedded graph.json |
| `src/Engram.Obsidian/IObsidianStoreReader.cs` | **NEW** | Narrow read-only interface (ExportAsync, StatsAsync) |
| `src/Engram.Obsidian/StoreReaderAdapter.cs` | **NEW** | Adapter: IStore → IObsidianStoreReader |
| `src/Engram.Obsidian/Slug.cs` | **NEW** | Slugify() — port of slug.go (40 lines) |
| `src/Engram.Obsidian/MarkdownRenderer.cs` | **NEW** | ObservationToMarkdown(), BuildWikilinks(), TopicPrefix() — port of markdown.go (100 lines) |
| `src/Engram.Obsidian/HubGenerator.cs` | **NEW** | ObsRef, ShouldCreateTopicHub, SessionHubMarkdown, TopicHubMarkdown — port of hub.go (77 lines) |
| `src/Engram.Obsidian/SyncState.cs` | **NEW** | SyncState, ExportResult — port of state.go (71 lines) |
| `src/Engram.Obsidian/GraphConfig.cs` | **NEW** | GraphConfigMode, WriteGraphConfig, embedded graph.json — port of graph.go (79 lines) |
| `src/Engram.Obsidian/Exporter.cs` | **NEW** | Exporter, ExportConfig, Export() — port of exporter.go (315 lines) |
| `src/Engram.Obsidian/graph.json` | **NEW** | Embedded Obsidian graph config (same as Go original) |
| `src/Engram.Cli/Program.cs` | Extend | Add `obsidian-export` command with System.CommandLine |
| `src/Engram.Cli/Engram.Cli.csproj` | Extend | Add `<ProjectReference Include="../Engram.Obsidian/Engram.Obsidian.csproj" />` |
| `engram-dotnet.slnx` | Extend | Add Engram.Obsidian project to `/src/` folder |
| `tests/Engram.Obsidian.Tests/` | **NEW** | Test project with xUnit covering all exported components |
| `tests/Engram.Obsidian.Tests/Engram.Obsidian.Tests.csproj` | **NEW** | Test project referencing Engram.Obsidian |

**Unchanged**: `IStore.cs`, `Models.cs`, all existing store implementations, server, MCP, sync.

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| Markdown output differs from Go original (byte-level) | Medium | Medium | Test output against known-good Go-generated files |
| Slug edge cases differ | Low | Low | Port slug_test.go verbatim, verify with Unicode / edge cases |
| Embedded resource path differs between dev and publish | Low | Medium | Use `GetManifestResourceNames()` fallback for debugging |
| File encoding issues (BOM vs no-BOM) | Low | Medium | Always write UTF8 without BOM (`new UTF8Encoding(false)`) |
| State file JSON serialization mismatch | Low | Medium | Use snake_case consistently via JsonSerializerOptions |
| Personal scope leak (security) | Low | High | Unit test: export with/without `--include-personal`, verify file count |

---

## Open Questions

| # | Question | Decision | Notes |
|---|----------|----------|-------|
| Q1 | Narrow interface (`IObsidianStoreReader`) or reuse `IStore` directly? | **Narrow interface** | Easier to mock, explicit contract. Adapter class bridges IStore. |
| Q2 | GraphConfig default value for CLI? | **`preserve`** | Safer default. Force only on explicit `--graph-config force`. |
| Q3 | Export limit: cap at file level or observation count? | **Observation count** | Same as Go: `--limit` caps total observations processed (deleted + exported + skipped). |
| Q4 | Should we validate vault path exists before writing? | **Yes, fail-fast** | If `--vault` directory doesn't exist, exit with error message. |
| Q5 | Watcher in Fase A or Fase B? | **Fase B** | The `Watcher` requires a long-running background process model. Fase A is CLI-only. |

---

## Next Step

Proceed to **specification** (`sdd-spec`) and **task breakdown** (`sdd-tasks`) for `sdd/obsidian-export/`.
