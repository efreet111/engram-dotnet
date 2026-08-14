# HU-015 — CLI Reference Manual

**Status**: 🟢 Done
**Owner**: @owner
**Created**: 2026-08-11

**As**: Developer / User
**I want**: A complete CLI reference manual that documents all `engram` commands, subcommands, flags, and usage examples
**To**: Avoid guessing commands, reduce friction for new users, and provide a searchable reference for power users

---

## 🎯 Intent

Currently there is no dedicated CLI manual. Commands are documented partially across different docs (API reference covers endpoints, MCP config covers server setup, etc.) but the CLI commands themselves are undocumented. Users and developers must read source code to discover available commands and their flags.

This HU creates `docs/CLI-REFERENCE.md` — a complete reference manual for all `engram` CLI commands.

---

## 📋 Scope

### In Scope
- Document all `engram` CLI commands and subcommands
- Every flag, option, and argument with type and description
- Usage examples for common workflows
- Error handling and exit codes
- Shell completion hints (if available)

### Out of Scope
- Tutorial content (quick-start guide already exists)
- API REST documentation (separate doc)
- MCP tools reference (separate doc)

---

## Acceptance Criteria

- [x] All 17+ commands documented with syntax, flags, and examples
- [x] `engram sync enroll` covers all HU-013 flags: `--interactive`, `--behavior`, `--exclude-server`
- [x] `engram sync unenroll` covers retention prompt behavior
- [x] `engram project id`, `engram migrate` documented with examples
- [x] `engram obsidian-export` documented with all options
- [x] `engram retention check/prune` documented with TTL behavior
- [x] Document links to relevant guides (DEPLOYMENT.md, OFFLINE-FIRST-SYNC.md, etc.)
- [x] Created in `docs/CLI-REFERENCE.md`

---

## Tasks (Implementation)

- [x] Audit `src/Engram.Cli/Program.cs` for all commands and subcommands
- [x] Document global flags (`--help`, `--version`, `--json`)
- [x] Document `engram serve` command
- [x] Document `engram mcp` command
- [x] Document `engram search`, `engram save`, `engram context`, `engram stats`
- [x] Document `engram export`, `engram import`
- [x] Document `engram sync` subcommands: `status`, `enroll`, `unenroll`, `export`, `import`
- [x] Document `engram project id`, `engram migrate`
- [x] Document `engram promote`
- [x] Document `engram projects list/consolidate/prune`
- [x] Document `engram retention check/prune`
- [x] Document `engram obsidian-export`
- [x] Document `engram version`, `engram doctor`
- [x] Add to AGENTS.md or docs index if needed

---

## Command Inventory (from Program.cs)

| Command | Description |
|---------|-------------|
| `engram serve` | Start the HTTP API server |
| `engram mcp` | Start the MCP server (stdio transport) |
| `engram search` | Search memories |
| `engram save` | Save a memory |
| `engram context` | Show recent memory context |
| `engram stats` | Show memory system statistics |
| `engram export` | Export all memories to JSON |
| `engram import` | Import memories from JSON export |
| `engram sync status` | Show mutation-based sync status |
| `engram sync enroll` | Enroll a project for sync push (HU-013) |
| `engram sync unenroll` | Unenroll a project from sync push (HU-013) |
| `engram sync export` | Export chunk to sync directory |
| `engram sync import` | Import chunk from sync directory |
| `engram project id` | Show/regenerate project identity GUID |
| `engram migrate` | Migrate project data to new identity |
| `engram promote` | Promote observations to .md files |
| `engram projects list` | List all projects with stats |
| `engram projects consolidate` | Merge similar project names |
| `engram projects prune` | Remove projects with 0 observations |
| `engram retention check` | Show retention statistics |
| `engram retention prune` | Prune old observations by TTL |
| `engram obsidian-export` | Export memories to Obsidian vault |
| `engram version` | Print version |
| `engram doctor` | Run diagnostic health checks |

---

## Notes

- Source of truth for command structure: `src/Engram.Cli/Program.cs`
- For `engram sync enroll --interactive` and `--behavior` flags, see HU-013 implementation
- For `engram project id --json --regenerate` behavior, see ENG-432
- For `engram obsidian-export --project --include-personal --vault` etc., see HU-002 implementation
