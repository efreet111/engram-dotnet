# Requirement Traceability Specification

## Purpose

This specification defines the `requirement-traceability` capability for engram-dotnet, enabling automated tracking of requirement origin and lineage from source (GitHub issue, bug report, technical decision) to code. The capability persists traceability data as Engram observations and provides MCP tools for querying source information and lineage trees.

## Requirements

### Requirement: Traceability Section in Spec.md

The system SHALL recognize a canonical `## Traceability` section in `spec.md` files containing requirement entries with the following fields:
- `Source`: Identifier of the originating artifact (e.g., GITHUB-ISSUE-42, DECISION-001)
- `Author`: Person or team who created the requirement
- `Date`: ISO 8601 date when the requirement was created
- `Rationale`: Explanation of why this requirement exists
- `Relations`: Links to other requirements (depends_on, supersedes, conflicts_with, related_to)

The parser MUST tolerate missing optional fields (Author, Date) but MUST reject entries without Source or Rationale.

#### Scenario: Valid Traceability Section
- GIVEN a `spec.md` containing `## Traceability` with entry `### RF-001` having Source "GITHUB-ISSUE-42", Rationale "Users with Unicode emails cannot register"
- WHEN the parser processes the file
- THEN it returns traceability entries with fields: `id: "RF-001"`, `source: "GITHUB-ISSUE-42"`, `rationale: "Users with Unicode emails cannot register"`

#### Scenario: Missing Optional Author Field
- GIVEN a `spec.md` containing `## Traceability` with entry `### RF-001` having Source and Rationale but no Author
- WHEN the parser processes the file
- THEN it returns the entry with `author: null`
- AND does not throw an error

#### Scenario: Missing Required Rationale
- GIVEN a `spec.md` containing `## Traceability` with entry `### RF-001` having Source but no Rationale
- WHEN the parser processes the file
- THEN the entry is marked as `invalid` with reason "Rationale is required"

---

### Requirement: mem_trace_source Tool

The system SHALL provide an MCP tool named `mem_trace_source` that accepts `rf_id` and optional `project` and returns the traceability information for that requirement.

The tool MUST:
- Search for an observation with `topic_key: trace/{project}/{rf_id}`
- Return source, author, date, rationale, and relations
- Return "untraced" status if no observation exists

#### Scenario: Traced Requirement
- GIVEN a requirement RF-001 with source "GITHUB-ISSUE-42" and rationale "Unicode email support" exists in the observation store
- WHEN `mem_trace_source(rf_id="RF-001", project="my-project")` is invoked
- THEN it returns `source: "GITHUB-ISSUE-42"`, `rationale: "Unicode email support"`, `status: "traced"`

#### Scenario: Untraced Requirement
- GIVEN a requirement RF-999 does not exist in the observation store
- WHEN `mem_trace_source(rf_id="RF-999", project="my-project")` is invoked
- THEN it returns `status: "untraced"`, `message: "No traceability data found for RF-999"`

#### Scenario: Project Not Provided
- GIVEN a requirement RF-001 exists in the default project
- WHEN `mem_trace_source(rf_id="RF-001")` is invoked without project
- THEN it uses the default project from configuration
- AND returns the traceability data

---

### Requirement: mem_lineage Tool

The system SHALL provide an MCP tool named `mem_lineage` that accepts `rf_id` and optional `project` and returns a complete lineage tree showing the requirement's origin, any reworks, and relations to other requirements.

The tool MUST:
- Build a tree starting from the given RF
- Follow relations transitively (depends_on → supersedes → etc.)
- Include the original source at the root
- Detect and report cyclic relationships

The lineage tree MUST include:
- `root`: Original source (e.g., GITHUB-ISSUE-42)
- `requirements[]`: Array of RF/RNF in the lineage with relation type
- `depth`: Maximum depth traversed

#### Scenario: Simple Lineage
- GIVEN RF-002 depends on RF-001, and RF-001 has source "GITHUB-ISSUE-42"
- WHEN `mem_lineage(rf_id="RF-002")` is invoked
- THEN it returns a tree with root "GITHUB-ISSUE-42", requirements [{"id": "RF-001", "relation": "depends_on"}, {"id": "RF-002", "relation": "root"}]

#### Scenario: Lineage with Reworks
- GIVEN RF-003 supersedes RF-002, and RF-002 depends on RF-001 with source "DECISION-001"
- WHEN `mem_lineage(rf_id="RF-003")` is invoked
- THEN it returns a tree showing DECISION-001 → RF-001 → RF-002 → RF-003

#### Scenario: Cyclic Relations Detected
- GIVEN RF-A depends on RF-B, and RF-B depends on RF-A (cycle)
- WHEN `mem_lineage(rf_id="RF-A")` is invoked
- THEN it returns `error: "cyclic_relation_detected"`, `cycle: ["RF-A", "RF-B"]`, `message: "Cycle detected at depth N, stopped at 10 hops"`

#### Scenario: Max Depth Reached
- GIVEN a chain of 15 requirements (RF-001 → RF-002 → ... → RF-015)
- WHEN `mem_lineage(rf_id="RF-001")` is invoked
- THEN it returns the first 10 levels with `truncated: true`, `depth: 10`

---

### Requirement: Traceability Persistence

The system SHALL persist traceability data as Engram observations with `topic_key` format `trace/{project}/{rf_id}`.

Each observation MUST contain:
- `title`: "{rf_id}: {requirement text truncated to 50 chars}"
- `type`: "requirement"
- `content`: Full traceability information with Source, Author, Date, Rationale, Relations
- `topic_key`: "trace/{project}/{rf_id}"

#### Scenario: Persist New Traceability
- GIVEN a traceability entry for RF-001 with source "GITHUB-ISSUE-42" and rationale "Unicode support"
- WHEN the spec is saved
- THEN an observation is created with topic_key "trace/my-project/rf-001"
- AND content contains all traceability fields

#### Scenario: Update Existing Traceability
- GIVEN an observation exists for RF-001 with topic_key "trace/my-project/rf-001"
- WHEN the spec is saved with updated Rationale
- THEN the existing observation is updated (not duplicated)
- AND retains the same topic_key

---

### Requirement: Relation Types

The system SHALL support four relation types between requirements:
- `depends_on`: This requirement requires another requirement to be satisfied first
- `supersedes`: This requirement replaces a previous requirement
- `conflicts_with`: This requirement cannot coexist with another requirement
- `related_to`: General association between related requirements

The system MUST validate relation syntax and reject invalid relation types.

#### Scenario: Valid Relation Syntax
- GIVEN a traceability entry with `Relations: Depends on RF-001`
- WHEN the parser processes the entry
- THEN it extracts relation type "depends_on" and target "RF-001"

#### Scenario: Invalid Relation Type
- GIVEN a traceability entry with `Relations: Blocks RF-002` (invalid type)
- WHEN the parser processes the entry
- THEN it marks the relation as `invalid` with reason "Unknown relation type: Blocks"

#### Scenario: Multiple Relations
- GIVEN a traceability entry with `Relations: Supersedes RF-001, Depends on RF-003`
- WHEN the parser processes the entry
- THEN it extracts two relations: [{"type": "supersedes", "target": "RF-001"}, {"type": "depends_on", "target": "RF-003"}]

---

### Requirement: Cycle Detection in Lineage

The system SHALL detect cyclic relationships in requirement lineage and prevent infinite loops.

The cycle detection MUST:
- Limit traversal to maximum 10 hops
- Track visited requirements to detect cycles
- Report the cycle path when detected

#### Scenario: Direct Cycle
- GIVEN RF-A has relation "depends_on RF-B" and RF-B has relation "depends_on RF-A"
- WHEN `mem_lineage(rf_id="RF-A")` is invoked
- THEN it detects the cycle and returns `cycle: ["RF-A", "RF-B"]`

#### Scenario: Indirect Cycle
- GIVEN chain A → B → C → B (C references B creating cycle)
- WHEN `mem_lineage(rf_id="RF-A")` is invoked
- THEN it detects cycle at depth 3 and returns the cycle path

---

### Requirement: mem_traceability Source Validity Verification

The system SHALL extend the existing `mem_traceability` tool to include verification of source validity.

The tool MUST:
- Check if the source (e.g., GITHUB-ISSUE-42) is still active/open
- Include a warning when source is closed/inactive
- Show whether the RF has been superseded by another requirement

Source validity checking MUST:
- Accept source format strings (GITHUB-ISSUE-N, DECISION-N, etc.)
- For GITHUB-ISSUE sources: query the issue status via GitHub API if available
- For non-queryable sources: mark as "unknown" with note "Manual verification required"

#### Scenario: Active Source
- GIVEN an RF with source "GITHUB-ISSUE-42" where the issue is still open
- WHEN `mem_traceability` is invoked
- THEN it includes `source_status: "active"` in the traceability entry

#### Scenario: Inactive Source Warning
- GIVEN an RF with source "GITHUB-ISSUE-42" where the issue is closed
- WHEN `mem_traceability` is invoked
- THEN it includes `source_status: "inactive"`, `warning: "Source issue is closed - verify requirement is still relevant"`

#### Scenario: Source Without API Access
- GIVEN an RF with source "DECISION-001" (internal decision, no API)
- WHEN `mem_traceability` is invoked
- THEN it includes `source_status: "unknown"`, `note: "Manual verification required for DECISION-001"`

#### Scenario: Superseded Requirement
- GIVEN RF-002 has source "GITHUB-ISSUE-42" but supersedes RF-001
- WHEN `mem_traceability` is invoked
- THEN it includes `superseded_by: "RF-002"` in RF-001's entry

---

## Summary

| Domain | Requirements | Scenarios |
|--------|-------------|-----------|
| requirement-traceability | 7 | 19 |

### Coverage
- Happy paths: Fully covered (traceability parsing, mem_trace_source, mem_lineage, persistence, relation types, source validity)
- Edge cases: Fully covered (missing optional fields, multiple relations, max depth, direct/indirect cycles)
- Error states: Covered (invalid relation types, untraced requirements, API unavailable for source check)

### Next Step
Ready for design (sdd-design). If design already exists, ready for tasks (sdd-tasks).