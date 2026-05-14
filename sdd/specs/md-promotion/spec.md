# md-promotion Specification

## Purpose

This specification defines the capability to promote observations from the engram database to versioned .md files within the project repository. The promotion creates a second level of organizational memory that is human-readable, version-controllable, and linkable bidirectionally between database records and markdown files.

## Requirements

### Requirement: Observation Model Extension

The Observation model MUST include a `md_path` field of type TEXT. This field SHALL store a relative path from the repository root to the promoted markdown file. The field MUST be nullable to indicate an observation has not been promoted.

#### Scenario: Observation with md_path

- GIVEN an Observation record exists in the database with title "JWT auth middleware"
- WHEN the observation is retrieved via the API
- THEN the response MUST include `md_path` field
- AND if promoted, `md_path` MUST contain relative path like "docs/decisions/2024-01-15-jwt-auth-middleware.md"

#### Scenario: Observation without md_path

- GIVEN an Observation record exists that has never been promoted
- WHEN the observation is retrieved via the API
- THEN the `md_path` field MUST be null
- AND the observation is eligible for promotion

---

### Requirement: Markdown Template Engine

The system MUST provide an `MdTemplateEngine` that generates markdown files with canonical frontmatter. The template MUST include: observation_id, type, title, created_at, topic_key, and generated_at. The body MUST include the observation content.

#### Scenario: Generate canonical markdown

- GIVEN an Observation with id=42, type="decision", title="Switched to JWT", topic_key="architecture/auth-model", created_at="2024-01-15T10:30:00Z", content="Replaced sessions with JWT for scalability"
- WHEN MdTemplateEngine generates markdown
- THEN the output MUST contain frontmatter with all required fields
- AND the body MUST contain the exact content value

#### Scenario: Frontmatter date format

- GIVEN an Observation created at "2024-01-15T10:30:00Z"
- WHEN the template generates frontmatter
- THEN created_at in frontmatter MUST be in ISO 8601 format
- AND generated_at MUST be the current timestamp in ISO 8601 format

---

### Requirement: mem_promote_to_md Tool

The system MUST provide an MCP tool named `mem_promote_to_md` that accepts an observation_id parameter. This tool MUST: (1) retrieve the observation, (2) generate markdown using the template engine, (3) write the file to the destination directory, (4) update the observation's md_path field.

#### Scenario: Successful individual promotion

- GIVEN an Observation with id=42 exists and has never been promoted
- AND ENGRAM_MD_DIR is set to "docs/decisions/"
- WHEN mem_promote_to_md(observation_id=42) is called
- THEN a file MUST be created at "docs/decisions/2024-01-15-42.md"
- AND the observation's md_path MUST be updated to "docs/decisions/2024-01-15-42.md"
- AND the .md frontmatter MUST include observation_id=42

#### Scenario: Promotion of already-promoted observation

- GIVEN an Observation with id=42 already has md_path="docs/decisions/2024-01-15-42.md"
- WHEN mem_promote_to_md(observation_id=42) is called
- THEN the tool MUST return an error indicating the observation is already promoted
- AND MUST NOT overwrite the existing file

#### Scenario: Promotion of non-existent observation

- GIVEN no Observation exists with id=999
- WHEN mem_promote_to_md(observation_id=999) is called
- THEN the tool MUST return an error "Observation not found"

---

### Requirement: Markdown Filename Convention

Promoted markdown files MUST follow the naming convention `{YYYY-MM-DD}-{slug}.md` where slug is derived from the observation title. The date MUST be the creation date of the observation.

#### Scenario: Filename generation from title

- GIVEN an Observation with title="Fixed N+1 query in UserList", created_at="2024-03-20T14:00:00Z"
- WHEN the filename is generated
- THEN the filename MUST be "docs/decisions/2024-03-20-fixed-n1-query-userlist.md"
- AND all characters MUST be lowercase
- AND spaces MUST be replaced with hyphens

#### Scenario: Slug collision handling

- GIVEN two Observations both created on 2024-03-20 with titles "Auth fix" and "Auth fix"
- WHEN both are promoted
- THEN the first file MUST be "docs/decisions/2024-03-20-auth-fix.md"
- AND the second file MUST include a short hash: "docs/decisions/2024-03-20-auth-fix-a1b2.md"

---

### Requirement: Bidirectional Link

The system MUST maintain bidirectional links between observations and their promoted markdown files. The observation's md_path points to the file, and the file's frontmatter contains the observation_id that points back to the database record.

#### Scenario: Forward link verification

- GIVEN an Observation has md_path="docs/decisions/2024-01-15-jwt-auth-middleware.md"
- WHEN the file at that path is read
- THEN the frontmatter MUST contain observation_id matching the Observation's id

#### Scenario: Reverse link verification

- GIVEN a markdown file exists with frontmatter observation_id=42
- WHEN the observation with id=42 is queried
- THEN the observation's md_path MUST reference a file that exists

---

### Requirement: mem_sync_md_to_repo Batch Tool

The system MUST provide an MCP tool named `mem_sync_md_to_repo` that scans all observations without an md_path and promotes them. This tool MUST support a `--dry-run` flag that reports observations without creating files.

#### Scenario: Batch sync all unpromoted observations

- GIVEN 5 Observations exist without md_path
- WHEN mem_sync_md_to_repo() is called
- THEN 5 markdown files MUST be created
- AND all 5 observations MUST have their md_path updated

#### Scenario: Dry-run mode

- GIVEN 3 Observations exist without md_path
- WHEN mem_sync_md_to_repo(dry_run=true) is called
- THEN no files MUST be created
- AND no observations MUST be updated
- AND the response MUST list the 3 observations that would be promoted

#### Scenario: Empty sync (all promoted)

- GIVEN all Observations already have md_path populated
- WHEN mem_sync_md_to_repo() is called
- THEN no files MUST be created
- AND the response MUST indicate "No observations to promote"

---

### Requirement: Configurable Destination Directory

The destination directory for promoted markdown files MUST be configurable via the `ENGRAM_MD_DIR` environment variable. This variable MUST default to `docs/decisions/` when not set.

#### Scenario: Custom directory via environment variable

- GIVEN ENGRAM_MD_DIR is set to "archive/memories/"
- WHEN mem_promote_to_md is called
- THEN the markdown file MUST be created in "archive/memories/"
- AND NOT in "docs/decisions/"

#### Scenario: Default directory when not configured

- GIVEN ENGRAM_MD_DIR is not set or empty
- WHEN mem_promote_to_md is called
- THEN the markdown file MUST be created in "docs/decisions/"

---

### Requirement: Auto-Generated Index

The system MUST generate and maintain a `docs/decisions/index.md` file that lists all promoted observations. This index MUST be updated after each promotion operation.

#### Scenario: Index generation

- GIVEN 2 observations have been promoted to markdown
- WHEN GenerateIndexAsync is executed
- THEN docs/decisions/index.md MUST exist
- AND MUST contain entries for both promoted observations
- AND each entry MUST include title, date, and relative path

#### Scenario: Index update on new promotion

- GIVEN docs/decisions/index.md exists with 2 entries
- WHEN a third observation is promoted
- THEN docs/decisions/index.md MUST be updated to include the new entry
- AND existing entries MUST remain unchanged

---

### Requirement: Link Verification

The system MUST provide a mechanism to verify that bidirectional links are intact. This verification MUST check that: (1) all observations with md_path point to existing files, (2) all markdown files with observation_id reference existing observations.

#### Scenario: Link integrity check

- GIVEN an Observation with md_path="docs/decisions/2024-01-15-test.md" exists
- AND the file is deleted manually from the filesystem
- WHEN link verification is run
- THEN the verification MUST report a broken link
- AND SHOULD recommend corrective action

---

### Requirement: Rollback Capability

The system MUST support reverting a promotion. This operation MUST: (1) set the observation's md_path to null, (2) optionally delete the markdown file.

#### Scenario: Revoke promotion

- GIVEN an Observation has md_path="docs/decisions/2024-01-15-test.md"
- WHEN revoke operation is executed for this observation
- THEN the observation's md_path MUST be set to null
- AND the markdown file MAY be deleted (configurable)

---

### Requirement: SQLite and PostgreSQL Compatibility

The md_path column MUST be implemented in both SQLite and PostgreSQL stores. The column type MUST be TEXT and nullable.

#### Scenario: SQLite migration

- GIVEN a SQLite database with existing observations
- WHEN the migration to add md_path column runs
- THEN the column MUST be added without data loss
- AND the column MUST be nullable

#### Scenario: PostgreSQL migration

- GIVEN a PostgreSQL database with existing observations
- WHEN the migration to add md_path column runs
- THEN the column MUST be added without data loss
- AND the column MUST be nullable