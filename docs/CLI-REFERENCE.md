# Engram CLI Reference

**Version:** 1.3.0  
**Project:** [engram-dotnet](https://github.com/engramhq/engram-dotnet)

---

## Quick Reference

| Command | Description |
|---------|-------------|
| `engram serve` | Start HTTP API server |
| `engram mcp` | Start MCP server (stdio transport) |
| `engram search <query>` | Search memories |
| `engram save <title> <content>` | Save a memory |
| `engram context` | Show recent memory context |
| `engram stats` | Show memory statistics |
| `engram export` | Export memories to JSON |
| `engram import <file>` | Import from JSON |
| `engram sync status` | Show sync status |
| `engram sync enroll` | Enroll project for sync |
| `engram sync unenroll` | Unenroll project |
| `engram sync push` | Push pending mutations |
| `engram sync export` | Export sync chunk |
| `engram sync import` | Import sync chunk |
| `engram sync setup` | Interactive sync setup wizard |
| `engram project id` | Show/manage project identity |
| `engram project migrate` | Migrate to new identity |
| `engram projects list` | List projects with stats |
| `engram projects consolidate` | Merge similar projects |
| `engram projects prune` | Remove empty projects |
| `engram promote` | Promote observations to .md |
| `engram retention check` | Show retention statistics |
| `engram retention prune` | Prune old observations |
| `engram obsidian-export` | Export to Obsidian vault |
| `engram relations` | Manage observation relations |
| `engram lineage` | Build lineage tree |
| `engram version` | Print version |
| `engram doctor` | Run diagnostics |

---

## Core Commands

### engram serve

**Description**: Start the HTTP API server.

**Syntax**: `engram serve [options]`

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--port` | int | 7437 | Port to listen on (env: `ENGRAM_PORT`) |
| `--no-auto-enroll` | bool | false | Disable auto-generation of `.engram-id` |

**Environment Variables**:

| Variable | Description |
|----------|-------------|
| `ENGRAM_PORT` | Override default port |
| `ENGRAM_AUTO_ENROLL` | `false` or `0` to disable auto-enroll |
| `ENGRAM_SERVER_URL` | Server URL for remote deployment info |

**Examples**:

```bash
# Start server on default port 7437
engram serve

# Start on custom port
engram serve --port 8080

# Start with auto-enroll disabled
engram serve --no-auto-enroll
```

---

### engram mcp

**Description**: Start the MCP server with stdio transport.

**Syntax**: `engram mcp [options]`

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--project` | string | auto-detected | Override detected project name |
| `--no-auto-enroll` | bool | false | Disable auto-generation of `.engram-id` |

**Project Detection Chain**: `--project` → `ENGRAM_PROJECT` → git remote → git root → cwd basename

**Examples**:

```bash
# Start MCP server with auto-detected project
engram mcp

# Start with specific project
engram mcp --project my-project

# Start with auto-enroll disabled
engram mcp --no-auto-enroll
```

---

### engram search \<query\>

**Description**: Search memories by query string.

**Syntax**: `engram search <query> [options]`

**Arguments**:

| Argument | Type | Description |
|----------|------|-------------|
| `query` | string | Search query |

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--type` | string | — | Filter by observation type |
| `--project` | string | — | Filter by project name |
| `--scope` | string | — | Filter by scope: `team` or `personal` |
| `--limit` | int | 10 | Maximum number of results |

**Examples**:

```bash
# Basic search
engram search "authentication"

# Search with type filter
engram search "auth" --type bugfix

# Search in specific project with higher limit
engram search "database" --project my-app --limit 20

# Search only team-scoped memories
engram search "api" --scope team
```

---

### engram save \<title\> \<content\>

**Description**: Save a new memory observation.

**Syntax**: `engram save <title> <content> [options]`

**Arguments**:

| Argument | Type | Description |
|----------|------|-------------|
| `title` | string | Memory title |
| `content` | string | Memory content |

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--type` | string | `manual` | Observation type |
| `--project` | string | — | Project name |
| `--scope` | string | auto-classified | `team` (shared) or `personal` (private) |
| `--topic` | string | — | Topic key for upsert |

**Examples**:

```bash
# Save a simple memory
engram save "Fixed auth bug" "JWT token expiration was set incorrectly"

# Save with project and type
engram save "Database migration" "Added users table" --project my-app --type architecture

# Save with topic key for upsert
engram save "API endpoint" "/health returns system status" --topic architecture/api
```

---

### engram context [project]

**Description**: Show recent memory context from previous sessions.

**Syntax**: `engram context [project] [options]`

**Arguments**:

| Argument | Type | Default | Description |
|----------|------|---------|-------------|
| `project` | string | current directory | Project name (optional) |

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--scope` | string | — | Scope filter (`team` or `personal`) |

**Examples**:

```bash
# Show context for current project
engram context

# Show context for specific project
engram context my-app

# Show only team-scoped context
engram context --scope team
```

---

### engram stats

**Description**: Show memory system statistics.

**Syntax**: `engram stats`

**Examples**:

```bash
engram stats
```

**Output includes**: Total sessions, observations, prompts, projects, and database type/location.

---

### engram export [file]

**Description**: Export all memories to a JSON file.

**Syntax**: `engram export [file]`

**Arguments**:

| Argument | Type | Default | Description |
|----------|------|---------|-------------|
| `file` | string | `engram-export.json` | Output file path |

**Examples**:

```bash
# Export to default file
engram export

# Export to specific file
engram export backup-2024-01-15.json
```

---

### engram import \<file\>

**Description**: Import memories from a JSON export file.

**Syntax**: `engram import <file>`

**Arguments**:

| Argument | Type | Description |
|----------|------|-------------|
| `file` | string | Input JSON file path |

**Examples**:

```bash
engram import engram-export.json
```

---

## Sync Commands

### engram sync status

**Description**: Show mutation-based sync status from the server.

**Syntax**: `engram sync status [options]`

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--json` | bool | false | Output as JSON (machine-readable) |

**Examples**:

```bash
# Human-readable output
engram sync status

# JSON output
engram sync status --json
```

---

### engram sync enroll

**Description**: Enroll a project for sync push (HU-013: per-project behavior).

**Syntax**: `engram sync enroll [options]`

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--project` | string | — | Project to enroll |
| `--interactive` | bool | false | Interactive enrollment with project selection |
| `--behavior` | string | `fail-loud` | Sync behavior: `silent-skip` or `fail-loud` |
| `--exclude-server` | string[] | — | Server ID to exclude from sync (can be repeated) |

**Sync Behaviors**:

| Behavior | Description |
|----------|-------------|
| `silent-skip` | Silently skip mutations when offline or server unreachable |
| `fail-loud` | Fail loudly when offline or server unreachable |

**Interactive Flow** (`--interactive`):

1. Shows numbered list of projects with pending mutations
2. User selects projects (comma-separated numbers, `all`, or `none`)
3. For multiple projects: offers "apply same to all" option
4. For each project: selects behavior (`1` = silent-skip, `2` = fail-loud, Enter = fail-loud)
5. Shows confirmation with excluded servers if any

**Examples**:

```bash
# Enroll specific project with default behavior
engram sync enroll --project my-app

# Enroll with explicit behavior
engram sync enroll --project my-app --behavior silent-skip

# Interactive enrollment
engram sync enroll --interactive

# Enroll excluding specific servers
engram sync enroll --project my-app --exclude-server server-1 --exclude-server server-2
```

---

### engram sync unenroll

**Description**: Unenroll a project from sync push (HU-013).

**Syntax**: `engram sync unenroll [options]`

**Options**:

| Flag | Type | Description |
|------|------|-------------|
| `--project` | string | Project to unenroll (required) |

**Retention Prompt**:
After specifying the project, you will be prompted:
```
¿Qué hago con la configuración guardada? [1] Mantener [2] Eliminar:
```
- `[1] Mantener` — Retains enrollment in DB, sets `sync_enabled=false` in YAML config
- `[2] Eliminar` — Removes project from sync entirely

**Examples**:

```bash
engram sync unenroll --project my-app
```

---

### engram sync export

**Description**: Export a new chunk to the sync directory.

**Syntax**: `engram sync export`

**Examples**:

```bash
engram sync export
```

---

### engram sync import

**Description**: Import new chunks from the sync directory.

**Syntax**: `engram sync import`

**Examples**:

```bash
engram sync import
```

---

### engram sync push

**Description**: Push pending mutations for a specific project or all enrolled projects (HU-014).

**Syntax**: `engram sync push [options]`

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--project` | string | — | Project to push mutations for (mutually exclusive with `--all`) |
| `--all` | bool | false | Push all enrolled projects with active behavior |
| `--target` | string | `cloud` | Target key for sync state |

**Behavior**:
- `--project` and `--all` are mutually exclusive
- Requires `ENGRAM_SERVER_URL` to be set
- Projects with `silent-skip` behavior are skipped
- Only pushes projects with pending mutations

**Examples**:

```bash
# Push mutations for a specific project
engram sync push --project my-app

# Push all enrolled projects
engram sync push --all

# Push to specific target
engram sync push --project my-app --target cloud
```

---

### engram sync setup

**Description**: Interactive initial sync configuration wizard (HU-014 R6).

**Syntax**: `engram sync setup`

**Behavior**:
- Shows current auto-sync status
- Prompts: "Enable auto-sync every 30s for projects with pending mutations? (Y/n)"
- Saves preference to `~/.engram/config.json`
- Requires restart of engram server/mcp for changes to take effect

**Examples**:

```bash
# Run interactive setup wizard
engram sync setup
```

> **See also**: [SYNC-SETUP.md](SYNC-SETUP.md) for full sync setup guide.

---

## Project Commands

### engram project id

**Description**: Show or manage the project identity GUID (`.engram-id`).

**Syntax**: `engram project id [options]`

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--json` | bool | false | Output as JSON (machine-readable) |
| `--regenerate` | bool | false | Recompute and overwrite `.engram-id` with deterministic GUID |
| `--set` | string | — | Set `.engram-id` to a specific GUID (valid UUID) |
| `-y` | bool | false | Skip confirmation prompt |

**JSON Output Fields**:

| Field | Type | Description |
|-------|------|-------------|
| `project_id` | string | Current project GUID |
| `source` | string | Origin: `file`, `computed`, `manual`, or `none` |
| `computed` | string | Deterministic GUID computed from git remote |

**Examples**:

```bash
# Show current project identity
engram project id

# Show as JSON
engram project id --json

# Regenerate deterministic GUID
engram project id --regenerate

# Set custom GUID
engram project id --set 550e8400-e29b-41d4-a716-446655440000
```

---

### engram project migrate

**Description**: Migrate project data to a new identity.

**Syntax**: `engram project migrate --to <guid> [options]`

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--to` | string | — | Target project GUID (required) |
| `--from` | string | `.engram-id` | Source project GUID |
| `--dry-run` | bool | false | Preview without making changes |
| `-y` | bool | false | Skip confirmation prompt |

**Examples**:

```bash
# Migrate from current .engram-id to new GUID
engram project migrate --to 550e8400-e29b-41d4-a716-446655440000

# Dry-run preview
engram project migrate --to 550e8400-e29b-41d4-a716-446655440000 --dry-run

# Migrate from specific source
engram project migrate --from old-guid --to new-guid
```

---

### engram projects list

**Description**: List all projects with statistics.

**Syntax**: `engram projects list`

**Examples**:

```bash
engram projects list
```

**Output**: Table with project name, observation count, session count, and prompt count.

---

### engram projects consolidate

**Description**: Merge similar project names into a canonical name.

**Syntax**: `engram projects consolidate [options]`

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--all` | bool | false | Consolidate all similar projects (no interactive prompts) |
| `--dry-run` | bool | false | Preview without making changes |

**Interactive Flow** (without `--all`):

1. Detects canonical project for current directory
2. Finds similar project names
3. Shows numbered list with match types
4. User selects which to merge (comma-separated, `all`, or `none`)

**Examples**:

```bash
# Interactive consolidation
engram projects consolidate

# Dry-run preview
engram projects consolidate --dry-run

# Consolidate all groups automatically
engram projects consolidate --all
```

---

### engram projects prune

**Description**: Remove projects with zero observations.

**Syntax**: `engram projects prune [options]`

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--dry-run` | bool | false | Preview without deleting |

**Interactive Flow**:

1. Shows projects with 0 observations
2. User selects which to prune (comma-separated, `all`, or `none`)

**Examples**:

```bash
# Interactive prune
engram projects prune

# Dry-run preview
engram projects prune --dry-run
```

---

## Promote Command

### engram promote

**Description**: Promote observations to `.md` files.

**Syntax**: `engram promote [options]`

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--id` | long | — | Observation ID to promote |
| `--md-dir` | string | `docs/decisions` | Target directory for `.md` files |
| `--sync` | bool | false | Promote all unpromoted observations |
| `--dry-run` | bool | false | Preview without writing |

**Examples**:

```bash
# Promote specific observation
engram promote --id 42

# Promote all unpromoted observations
engram promote --sync

# Dry-run preview
engram promote --sync --dry-run

# Custom output directory
engram promote --id 42 --md-dir docs/architecture/decisions
```

---

## Retention Commands

### engram retention check

**Description**: Show retention statistics by type and age bucket.

**Syntax**: `engram retention check`

**TTL Policy**: Observations are pruned based on type and age. Only observations **without** a `topic_key` are eligible for pruning.

| Type | TTL | Env Override |
|------|-----|--------------|
| `tool_use`, `file_change`, `command` | 30 days | `ENGRAM_TTL_tool_use`, `ENGRAM_TTL_file_change`, `ENGRAM_TTL_command` |
| `bugfix`, `pattern` | 90 days | `ENGRAM_TTL_bugfix`, `ENGRAM_TTL_pattern` |
| `learning`, `discovery` | 60 days | `ENGRAM_TTL_learning`, `ENGRAM_TTL_discovery` |
| `decision`, `architecture`, `session_summary` | Never | — |

**Notes**:
- Observations with a `topic_key` are **never pruned** (they are considered pinned)
- Override defaults via env vars: `ENGRAM_TTL_{TYPE}=30d`, `ENGRAM_TTL_{TYPE}=180d`, etc.

**Examples**:

```bash
engram retention check
```

---

### engram retention prune

**Description**: Prune old observations by TTL policy.

**Syntax**: `engram retention prune [options]`

**TTL Policy**: Only observations **without** a `topic_key` are pruned. See `retention check` for full TTL table by type.

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--type` | string | — | Filter by observation type |
| `--dry-run` | bool | false | Preview without modifying |

**Examples**:

```bash
# Prune all old observations
engram retention prune

# Dry-run preview
engram retention prune --dry-run

# Prune specific type only
engram retention prune --type manual
```

---

## Obsidian Export

### engram obsidian-export

**Description**: Export memories to an Obsidian vault as markdown files.

**Syntax**: `engram obsidian-export --vault <path> [options]`

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--vault` | string | — | Path to Obsidian vault root (required) |
| `--project` | string | — | Filter export to a single project |
| `--include-personal` | bool | false | Include `scope=personal` observations (default: team only) |
| `--force` | bool | false | Ignore state file, do full re-export |
| `--graph-config` | string | `preserve` | Graph config mode: `preserve`, `force`, or `skip` |
| `--limit` | int | 0 | Max observations to export (0 = no limit) |
| `--since` | string | — | Filter by date: ISO 8601 (`2025-01-01`) or relative (`30d`, `7d`, `24h`, `5m`) |
| `--watch` | bool | false | Run in watch mode (continuous export) |
| `--interval` | string | `60s` | Watch interval when `--watch` is set (`30s`, `5m`, `1h`) |

**Graph Config Modes**:

| Mode | Description |
|------|-------------|
| `preserve` | Keep existing graph config in vault |
| `force` | Overwrite with engram's graph config |
| `skip` | Skip graph config entirely |

**Examples**:

```bash
# Basic export to vault
engram obsidian-export --vault /path/to/vault

# Export specific project
engram obsidian-export --vault /path/to/vault --project my-app

# Include personal observations
engram obsidian-export --vault /path/to/vault --include-personal

# Force full re-export
engram obsidian-export --vault /path/to/vault --force

# Export last 30 days only
engram obsidian-export --vault /path/to/vault --since 30d

# Export to specific date
engram obsidian-export --vault /path/to/vault --since 2025-01-01

# Watch mode with custom interval
engram obsidian-export --vault /path/to/vault --watch --interval 5m
```

---

## Relations Command

### engram relations

**Description**: Manage memory observation relations (ENG-404).

**Syntax**: `engram relations --action <action> --observation-id <id> [options]`

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--action` | string | — | Action: `add`, `get`, or `delete` (required) |
| `--observation-id` | long | — | Source observation ID (required) |
| `--target-id` | long | 0 | Target observation ID (required for add/delete) |
| `--type` | string | — | Relation type: `depends_on`, `supersedes`, `conflicts_with`, `related_to` (required for add/delete) |
| `--project` | string | auto-detected | Project name |

**Relation Types**:

| Type | Description |
|------|-------------|
| `depends_on` | This observation depends on another |
| `supersedes` | This observation supersedes another |
| `conflicts_with` | This observation conflicts with another |
| `related_to` | This observation is related to another |

**Examples**:

```bash
# Get all relations for an observation
engram relations --action get --observation-id 42

# Add a relation
engram relations --action add --observation-id 42 --target-id 43 --type depends_on

# Delete a relation
engram relations --action delete --observation-id 42 --target-id 43 --type supersedes
```

---

## Lineage Command

### engram lineage

**Description**: Build lineage tree for a memory observation (ENG-404).

**Syntax**: `engram lineage --observation-id <id> [options]`

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--observation-id` | long | — | Root observation ID (required) |
| `--max-hops` | int | 5 | Maximum traversal depth (1-10) |
| `--project` | string | auto-detected | Project name |

**Examples**:

```bash
# Build lineage tree (default 5 hops)
engram lineage --observation-id 42

# Build with custom depth
engram lineage --observation-id 42 --max-hops 10

# Specific project
engram lineage --observation-id 42 --project my-app
```

---

## Utility Commands

### engram version

**Description**: Print version information.

**Syntax**: `engram version`

**Examples**:

```bash
engram version
```

---

### engram doctor

**Description**: Run diagnostic health checks on the engram ecosystem.

**Syntax**: `engram doctor [options]`

**Options**:

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--server` | string | `ENGRAM_SERVER_URL` env | Engram server URL |

**Output**: Component-by-component health report with latency metrics and suggested actions.

**Exit codes**: `0` = all healthy, `1` = some components unhealthy.

**Examples**:

```bash
# Run with default server from environment
engram doctor

# Specify server explicitly
engram doctor --server http://localhost:7437
```

---

## Environment Variables

| Variable | Used By | Description |
|----------|---------|-------------|
| `ENGRAM_PORT` | `serve` | Server port |
| `ENGRAM_AUTO_ENROLL` | `serve`, `mcp` | `false` or `0` to disable auto-enroll |
| `ENGRAM_SERVER_URL` | `serve`, `sync status`, `doctor` | Server URL for remote deployment |
| `ENGRAM_PROJECT` | `mcp` | Override detected project |
| `ENGRAM_DB_TYPE` | all | `sqlite` or `postgres` |
| `ENGRAM_PG_CONNECTION` | all | PostgreSQL connection string |
| `ENGRAM_DATA_DIR` | all | SQLite data directory |
| `ENGRAM_URL` | all | Remote HTTP store URL |
| `ENGRAM_USER` | `mcp` | User identity for team mode |
| `ENGRAM_SYNC_ENABLED` | `sync` | Enable offline-first sync |
| `ENGRAM_SYNC_REPO` | `sync export/import` | Git sync repository path |
| `ENGRAM_SYNC_AUTO_SYNC` | `sync push`, `sync setup` | Override auto-sync preference (set by `sync setup`) |

---

## Exit Codes

| Code | Description |
|------|-------------|
| 0 | Success |
| 1 | Error (connection failed, invalid input, etc.) |
| 2 | Not implemented (store type doesn't support operation) |

---

## Related Documentation

| Document | Description |
|----------|-------------|
| [DEPLOYMENT.md](DEPLOYMENT.md) | Backend selection, environment variables, profiles |
| [OFFLINE-FIRST-SYNC.md](OFFLINE-FIRST-SYNC.md) | Sync architecture, enrollment, multi-server |
| [SYNC-SETUP.md](SYNC-SETUP.md) | Step-by-step sync setup guide |
| [API-REFERENCE.md](API-REFERENCE.md) | REST API endpoints |
| [MCP-CONFIG.md](MCP-CONFIG.md) | MCP server configuration |
| [01-QUICK-START.md](01-QUICK-START.md) | Getting started guide |
| [DEVELOPMENT.md](DEVELOPMENT.md) | Local dev setup, testing workflow |
