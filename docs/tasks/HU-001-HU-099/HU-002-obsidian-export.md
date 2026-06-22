# HU-002: obsidian-export

**Status**: 🟡 In Progress
**Owner**: @owner
**Created**: 2026-06-01
**Priority**: Should

---

## 🎯 Intent

Port the Engram → Obsidian vault export engine from the Go original (`internal/obsidian/`) to .NET. This gives .NET users the ability to export their Engram memories into an Obsidian vault as structured markdown files with YAML frontmatter, wikilinks, session hubs, topic hubs, and Obsidian graph configuration.

---

## 📋 Scope

### In Scope
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

### Out of Scope
- **Watcher** (continuous sync daemon) — deferred to Fase B
- `--watch` flag — Fase B
- `--since` flag — Fase B (use state file instead)
- Phase B AI synthesis — separate roadmap item
- Obsidian plugin or hot-reload — out of scope entirely

---

## 🔗 Origin

Migrated from `sdd/obsidian-export/`

Original artifacts:
- Proposal: `sdd/obsidian-export/proposal.md`
- Spec: `sdd/obsidian-export/spec.md`

---

## 📝 Notes

This HU was created during FlowDoc adoption (2026-06-01) to consolidate documentation into the FlowDoc structure.

---

## 🔄 Migration Reference

Original location: `sdd/obsidian-export/`
Current status: Migrated to FlowDoc

See `sdd/README.md` for full migration mapping.