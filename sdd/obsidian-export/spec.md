# Specification: Obsidian Export (`obsidian-export`)

## Purpose

Define the behavioral requirements for exporting Engram memories into an Obsidian vault as structured markdown files. The exporter reads observations from the store, generates markdown with YAML frontmatter and wikilinks, and manages incremental sync state.

---

## Requirements

### REQ-SLUG-01: Slug Generation

The system MUST generate a filesystem-safe slug from an observation title and ID.

- Lowercase the title
- Replace any sequence of non-alphanumeric characters with a single hyphen
- Trim leading/trailing hyphens
- Truncate to 60 characters (trimming trailing hyphens after truncation)
- Append `-{id}` for collision safety
- If the title is empty, return `"observation-{id}"`

#### Scenario: Normal title

- GIVEN title `"Fixed N+1 query in UserList"` and id `42`
- WHEN `Slugify` is called
- THEN the result is `"fixed-n-1-query-in-userlist-42"`

#### Scenario: Title with special characters

- GIVEN title `"Auth / JWT — Token Refresh!"` and id `7`
- WHEN `Slugify` is called
- THEN the result is `"auth-jwt-token-refresh-7"`

#### Scenario: Empty title

- GIVEN title `""` and id `99`
- WHEN `Slugify` is called
- THEN the result is `"observation-99"`

#### Scenario: Title longer than 60 chars

- GIVEN title `"This is a very long observation title that exceeds the maximum allowed length for filesystem safety purposes"` and id `1`
- WHEN `Slugify` is called
- THEN the result is at most 60 chars before the `-1` suffix, trimmed of trailing hyphens

---

### REQ-MD-01: Observation to Markdown

The system MUST render an `Observation` into a markdown string with YAML frontmatter, an H1 title, the content body, and a wikilinks footer.

YAML frontmatter MUST include: `id`, `type`, `project`, `scope`, `topic_key`, `session_id`, `created_at`, `updated_at`, `revision_count`, `tags` (project + type), and `aliases` (title).

#### Scenario: Full observation rendering

- GIVEN an observation with `Id=1`, `Type="bugfix"`, `Project="mi-api"`, `Scope="team"`, `TopicKey="auth/jwt"`, `SessionId="sess-abc"`, `Title="Fixed auth bug"`, `Content="The fix was..."`, `CreatedAt="2025-01-01T00:00:00Z"`, `UpdatedAt="2025-01-01T01:00:00Z"`, `RevisionCount=2`
- WHEN `ObservationToMarkdown` is called
- THEN the output starts with `---\n`
- AND the output contains `id: 1`
- AND the output contains `type: bugfix`
- AND the output contains `project: mi-api`
- AND the output contains `scope: team`
- AND the output contains `topic_key: auth/jwt`
- AND the output contains `session_id: sess-abc`
- AND the output contains `created_at: "2025-01-01T00:00:00Z"`
- AND the output contains `tags:\n  - mi-api\n  - bugfix`
- AND the output contains `aliases:\n  - "Fixed auth bug"`
- AND the output contains `# Fixed auth bug`
- AND the output contains `The fix was...`
- AND the output ends with a wikilink footer

#### Scenario: Observation with null project

- GIVEN an observation with `Project=null` and `TopicKey=null`
- WHEN `ObservationToMarkdown` is called
- THEN `project: ""` is in the frontmatter
- AND `topic_key: ""` is in the frontmatter

#### Scenario: Wikilink footer with session and topic

- GIVEN an observation with `SessionId="sess-abc"` and `TopicKey="auth/jwt"`
- WHEN `ObservationToMarkdown` is called
- THEN the footer contains `*Session*: [[session-sess-abc]]`
- AND the footer contains `*Topic*: [[topic-auth]]` (prefix before last `/`)

#### Scenario: Wikilink footer without session

- GIVEN an observation with `SessionId=""` and `TopicKey="auth/jwt"`
- WHEN `ObservationToMarkdown` is called
- THEN the footer does NOT contain `*Session*:`
- AND the footer DOES contain `*Topic*: [[topic-auth]]`

#### Scenario: Wikilink footer without topic

- GIVEN an observation with `SessionId="sess-abc"` and `TopicKey=""`
- WHEN `ObservationToMarkdown` is called
- THEN the footer contains `*Session*: [[session-sess-abc]]`
- AND the footer does NOT contain `*Topic*:`

---

### REQ-HUB-01: Session Hub Notes

The system MUST generate session hub notes that list all observations in a session as wikilinks.

Each hub note MUST have YAML frontmatter with `type: session-hub`, `session_id`, and `tags: ["session"]`, followed by an H1 `# Session: {id}` and a `## Observations` list.

#### Scenario: Session with multiple observations

- GIVEN a session `"sess-abc"` with 3 observations
- WHEN `SessionHubMarkdown` is called with the session ID and `ObsRef` list
- THEN the output contains `type: session-hub`
- AND the output contains `session_id: sess-abc`
- AND the output contains `# Session: sess-abc`
- AND the output contains `## Observations`
- AND the output contains 3 wikilink items (`- [[slug]]`)

#### Scenario: Empty observation list

- GIVEN an empty `ObsRef` list
- WHEN `SessionHubMarkdown` is called
- THEN the output still contains frontmatter and an empty `## Observations` section (no list items)

---

### REQ-HUB-02: Topic Hub Notes

The system MUST generate topic hub notes for topic prefixes with 2 or more observations (`ShouldCreateTopicHub(count)` returns `true` when `count >= 2`).

Each hub note MUST have YAML frontmatter with `type: topic-hub`, `topic_prefix`, and `tags: ["topic"]`, followed by an H1 `# Topic: {prefix}` and a `## Related Observations` list with type annotations.

Topic prefix extraction MUST use the `topicPrefix()` algorithm: everything before the last `/` in the topic key. For `"auth/jwt"` → `"auth"`. For `"sdd/obsidian-plugin/explore"` → `"sdd/obsidian-plugin"`. For `"standalone"` → `"standalone"`.

#### Scenario: Topic with sufficient observations

- GIVEN topic prefix `"auth"` with 3 observations (`bugfix`, `decision`, `architecture`)
- WHEN `TopicHubMarkdown` is called
- THEN the output contains `type: topic-hub`
- AND the output contains `topic_prefix: auth`
- AND the output contains `# Topic: auth`
- AND the output contains `## Related Observations`
- AND each item has the type annotation: `- [[slug]] (type)`

#### Scenario: Topic prefix with slashes

- GIVEN topic key `"sdd/obsidian-plugin/explore"`
- WHEN `topicPrefix` is called
- THEN the result is `"sdd/obsidian-plugin"`

#### Scenario: Topic below threshold

- GIVEN `count = 1`
- WHEN `ShouldCreateTopicHub(count)` is called
- THEN it returns `false`

#### Scenario: Topic at threshold

- GIVEN `count = 2`
- WHEN `ShouldCreateTopicHub(count)` is called
- THEN it returns `true`

---

### REQ-STATE-01: Sync State Persistence

The system MUST persist export state as JSON at `{vault}/engram/.engram-sync-state.json`.

`SyncState` contains: `last_export_at` (RFC3339 string), `files` (map of obs ID → relative path), `session_hubs` (map of session ID → relative path), `topic_hubs` (map of topic prefix → relative path), and `version` (schema version, currently 1).

#### Scenario: Read state — file exists

- GIVEN a valid state JSON file at the expected path
- WHEN `ReadState` is called
- THEN a populated `SyncState` is returned
- AND `Files`, `SessionHubs`, and `TopicHubs` maps are non-null

#### Scenario: Read state — file does not exist

- GIVEN no state file at the expected path
- WHEN `ReadState` is called
- THEN an empty `SyncState` is returned (no error)
- AND `Files`, `SessionHubs`, and `TopicHubs` are empty but non-null maps

#### Scenario: Write state

- GIVEN a populated `SyncState`
- WHEN `WriteState` is called with a file path
- THEN the file is written as indented JSON (`"  "` indent)
- AND the file is UTF-8 without BOM
- AND the file is readable back by `ReadState`

---

### REQ-GRAPH-01: Graph Config Management

The system MUST manage Obsidian's `graph.json` configuration file at `{vault}/.obsidian/graph.json` using three modes:

- `preserve` (default): write the embedded default template only if `graph.json` does not already exist
- `force`: always overwrite `graph.json` with the embedded default
- `skip`: never touch `graph.json`

The embedded template (from `graph.json`) MUST define color groups for paths `engram/_sessions`, `engram/_topics`, and tags `#architecture`, `#bugfix`, `#decision`, `#pattern`.

#### Scenario: Preserve mode — file does not exist

- GIVEN vault without `.obsidian/graph.json`
- WHEN `WriteGraphConfig` is called with `preserve`
- THEN `.obsidian/graph.json` is created with the embedded default content

#### Scenario: Preserve mode — file exists

- GIVEN vault with existing `.obsidian/graph.json` (user-customized)
- WHEN `WriteGraphConfig` is called with `preserve`
- THEN the file is NOT modified

#### Scenario: Force mode

- GIVEN vault with existing `.obsidian/graph.json` (user-customized)
- WHEN `WriteGraphConfig` is called with `force`
- THEN the file is overwritten with the embedded default

#### Scenario: Skip mode

- GIVEN any vault
- WHEN `WriteGraphConfig` is called with `skip`
- THEN nothing is written, no error is returned

#### Scenario: Invalid mode string

- GIVEN mode string `"invalid"`
- WHEN `ParseGraphConfigMode` is called
- THEN an error is returned

---

### REQ-EXPORT-01: Full Export

The system MUST export ALL non-deleted observations from the store into the vault when no prior state exists (or `--force` is used).

#### Scenario: First export (no prior state)

- GIVEN a fresh vault directory with no `.engram-sync-state.json`
- AND the store has 10 non-deleted observations across 2 projects
- WHEN `Export()` is called
- THEN 10 markdown files are created under `{vault}/engram/{project}/{type}/`
- AND session hub notes are created for sessions with observations
- AND topic hub notes are created for topic prefixes with ≥2 observations
- AND `.engram-sync-state.json` is written with `last_export_at`, all 10 files tracked
- AND `ExportResult.Created == 10`
- AND `ExportResult.Skipped == 0`
- AND `ExportResult.Deleted == 0`

#### Scenario: Force re-export

- GIVEN a prior export with state file tracking 10 files
- WHEN `Export()` is called with `--force`
- THEN all files are re-created (may be created or updated depending on content)
- AND the state file is replaced (fresh empty state before export)

---

### REQ-EXPORT-02: Incremental Export

The system MUST skip unchanged observations during incremental export. An observation is considered unchanged when: its `updated_at` ≤ the previous `last_export_at` AND it is already tracked in the state file.

#### Scenario: Incremental export — nothing changed

- GIVEN a prior export with 10 observations, all unchanged
- WHEN `Export()` is called (no `--force`)
- THEN `ExportResult.Skipped == 10`
- AND no files are written/modified
- AND state file `last_export_at` is updated

#### Scenario: Incremental export — new observations

- GIVEN a prior export with 10 observations
- AND 3 new observations exist with `updated_at` > `last_export_at`
- WHEN `Export()` is called
- THEN 3 new files are created
- AND `ExportResult.Created == 3`
- AND `ExportResult.Skipped >= 10`
- AND state file tracks 13 files

#### Scenario: Incremental export — updated observation

- GIVEN a prior export with an observation tracked in state
- AND the observation's content has changed (file content differs from store)
- WHEN `Export()` is called
- THEN the file is overwritten
- AND `ExportResult.Updated >= 1`

---

### REQ-EXPORT-03: Deleted Observation Cleanup

The system MUST remove markdown files for observations that have been soft-deleted (`deleted_at` is set).

#### Scenario: Soft-deleted observation

- GIVEN a prior export with observation `#42` tracked in state
- AND the store now has observation `#42` with `deleted_at` set
- WHEN `Export()` is called
- THEN the file for `#42` is deleted from the vault
- AND `ExportResult.Deleted == 1`
- AND observation `#42` is removed from state `Files` map

#### Scenario: Deleted observation not tracked

- GIVEN a soft-deleted observation NOT in the state file
- WHEN `Export()` is called
- THEN no deletion attempt is made
- AND `ExportResult.Deleted == 0`

---

### REQ-EXPORT-04: Project Filter

The system MUST support filtering export to a single project via `--project`.

#### Scenario: Single project export

- GIVEN the store has observations for projects `"mi-api"` and `"web-app"`
- WHEN `Export()` is called with `Project = "mi-api"`
- THEN only `"mi-api"` observations are written to the vault
- AND `"web-app"` observations are excluded
- AND hub notes only include `"mi-api"` observations

---

### REQ-EXPORT-05: Scope Security

The system MUST only export `scope=team` observations by default. `scope=personal` observations require the `--include-personal` flag.

Observations with `scope="project"` (legacy) MUST be treated as `scope="personal"` for export purposes, following the two-tier memory model (PR #1) where `NormalizeScope()` maps any non-"team" value to "personal".

#### Scenario: Default export — team only

- GIVEN the store has 5 team observations and 3 personal observations
- WHEN `Export()` is called without `--include-personal`
- THEN only 5 files are created (team only)
- AND `ExportResult.Created == 5`

#### Scenario: Export with personal included

- GIVEN the store has 5 team observations and 3 personal observations
- WHEN `Export()` is called with `--include-personal`
- THEN all 8 files are created

#### Scenario: Legacy scope "project" treated as personal

- GIVEN an observation with `scope = "project"` (legacy)
- WHEN `Export()` is called without `--include-personal`
- THEN the observation is NOT exported (treated as personal)

---

### REQ-EXPORT-06: Content Idempotency

The system MUST skip writing when the on-disk file content is identical to the generated content (byte-level comparison). This prevents unnecessary file writes and filesystem watcher noise.

#### Scenario: Unchanged content skip

- GIVEN an existing file with content matching the generated markdown
- WHEN `Export()` processes this observation
- THEN the file is NOT overwritten
- AND `ExportResult.Skipped` is incremented

#### Scenario: Changed content update

- GIVEN an existing file with different content than generated markdown
- WHEN `Export()` processes this observation
- THEN the file IS overwritten
- AND `ExportResult.Updated` is incremented

---

### REQ-EXPORT-07: Export Result Summary

The system MUST return an `ExportResult` with counts for: `Created`, `Updated`, `Deleted`, `Skipped`, `HubsCreated`, and a list of `Errors`.

#### Scenario: Export with errors

- GIVEN a vault path that is not writable
- WHEN `Export()` is called
- THEN `ExportResult.Errors` contains one or more error entries
- AND partial results (files that were written) are still reported in `Created`/`Updated`

---

### REQ-CLI-01: obsidian-export Command

The system MUST register an `obsidian-export` CLI command via `System.CommandLine` with the following options:

| Option | Type | Required | Default | Description |
|--------|------|----------|---------|-------------|
| `--vault` | string | **Yes** | — | Path to the Obsidian vault root |
| `--project` | string | No | null | Filter export to a single project |
| `--include-personal` | bool | No | false | Include `scope=personal` observations |
| `--force` | bool | No | false | Ignore incremental state, full re-export |
| `--graph-config` | string | No | `"preserve"` | Graph config mode: `preserve`, `force`, `skip` |
| `--limit` | int | No | 0 | Max observations to process (0 = unlimited) |

#### Scenario: Help output

- GIVEN the CLI binary
- WHEN `engram obsidian-export --help` is run
- THEN the output lists all options with descriptions

#### Scenario: Missing required vault

- GIVEN no `--vault` argument
- WHEN the command handler runs
- THEN it exits with a non-zero code and prints an error message

#### Scenario: Invalid graph-config value

- GIVEN `--graph-config invalid_value`
- WHEN `ParseGraphConfigMode` is called
- THEN an error is returned: `invalid --graph-config value: invalid_value (accepted: preserve, force, skip)`

---

## Non-Functional Requirements

### NFR-IO-01: UTF-8 Without BOM

All generated markdown and JSON files MUST be written as UTF-8 without BOM (`new UTF8Encoding(false)`).

### NFR-IO-02: Directory Creation

The exporter MUST create all intermediate directories as needed (`0755` equivalent in .NET: default `Directory.CreateDirectory`).

### NFR-IO-03: No External Dependencies

The `Engram.Obsidian` project MUST NOT depend on any NuGet packages beyond `Engram.Store`. File I/O, JSON serialization, and string manipulation use `System.*` and `System.Text.Json` only.
