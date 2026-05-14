# Artifact Verification Specification

## Purpose

This specification defines the `artifact-verification` capability for engram-dotnet, enabling automated verification that code changes comply with functional and non-functional requirements defined in `spec.md`. The capability generates structured compliance reports and traceability matrices, supporting the EngramFlow Verify Agent workflow with rework ticket generation and cycle count tracking.

## Requirements

### Requirement: Canonical Spec.md Format Parser

The system SHALL provide a parser that identifies requirements from `spec.md` files following the canonical EngramFlow format:
- Section `## Objetivo` or `## Objective` for the objective statement
- Section `## Functional Requirements` containing items prefixed with `- RF-NNN:`
- Section `## Non-Functional Requirements` containing items prefixed with `- RNF-NNNN:`

The parser MUST tolerate minor deviations by searching for section headers and RF/RNF patterns using keyword matching.

#### Scenario: Valid Canonical Format

- GIVEN a `spec.md` containing `## Objetivo`, `## Functional Requirements` with `- RF-001:`, and `## Non-Functional Requirements` with `- RNF-001:`
- WHEN the parser processes the file
- THEN it returns an object with `objective`, `functionalRequirements`, and `nonFunctionalRequirements` arrays
- AND each requirement contains `id`, `text`, and `lineNumber`

#### Scenario: Missing Optional Section

- GIVEN a `spec.md` containing only `## Objetivo` and `## Functional Requirements`
- WHEN the parser processes the file
- THEN it returns `nonFunctionalRequirements` as an empty array
- AND does not throw an error

#### Scenario: Unparseable Format

- GIVEN a `spec.md` with no recognizable `## Objetivo`, `## Objective`, `## Functional Requirements`, or `## Non-Functional Requirements` sections
- WHEN the parser processes the file
- THEN it returns a status of `unparseable`
- AND includes a `reason` field describing what was expected

---

### Requirement: mem_verify_artifact Tool

The system SHALL provide an MCP tool named `mem_verify_artifact` that accepts `specPath`, `planPath`, and `diff` (or `changedFiles`) and produces a structured compliance report.

The tool MUST extract RF and RNF from the spec, evaluate each against the provided code diff, and generate a report containing:
- `passed`: array of RF/RNF confirmed implemented
- `failed`: array of RF/RNF not detected in code
- `untested`: array of RF/RNF that could not be evaluated
- `coverage_pct`: percentage of tested requirements that passed

#### Scenario: All Requirements Passed

- GIVEN a `spec.md` with RF-001 "User can login" and RF-002 "User can logout", a `plan.md` with tasks implementing both, and a diff containing login/logout functions
- WHEN `mem_verify_artifact` is invoked
- THEN the report contains `passed: ["RF-001", "RF-002"]`, `failed: []`, `coverage_pct: 100`

#### Scenario: Some Requirements Failed

- GIVEN a `spec.md` with RF-001 "User can login" and RF-002 "User can logout", but the diff only contains login code
- WHEN `mem_verify_artifact` is invoked
- THEN the report contains `passed: ["RF-001"]`, `failed: ["RF-002"]`, `coverage_pct: 50`

#### Scenario: LLM Judge Timeout

- GIVEN `mem_verify_artifact` is evaluating an RF against a large diff and the LLM request exceeds the timeout
- WHEN the timeout occurs
- THEN the requirement is added to `untested` with reason "timeout"
- AND the tool does not crash and returns partial results

#### Scenario: Empty Diff

- GIVEN a valid `spec.md` with requirements but an empty or missing diff
- WHEN `mem_verify_artifact` is invoked
- THEN all requirements are marked as `failed` with reason "no code changes provided"

---

### Requirement: mem_traceability Tool

The system SHALL provide an MCP tool named `mem_traceability` that generates a traceability matrix mapping each RF and RNF to relevant file paths in the codebase.

The tool MUST accept `specPath` and optional `filePatterns` and output:
- A matrix of `requirementId` → `filePaths[]`
- Identification of requirements with no test coverage
- Confidence scores for each mapping

#### Scenario: Full Traceability

- GIVEN a `spec.md` with RF-001, RF-002, and RNF-001
- WHEN `mem_traceability` is invoked on a codebase with test files
- THEN it returns a matrix mapping each requirement to file paths that cover it
- AND identifies requirements lacking test coverage

#### Scenario: No Coverage Found

- GIVEN a `spec.md` with RF-001 but no test files match the requirements
- WHEN `mem_traceability` is invoked
- THEN the matrix shows `RF-001` with empty `filePaths`
- AND includes a warning that no coverage was detected

---

### Requirement: Rework Ticket Generation

The system SHALL generate a `rework_ticket.md` file when verification detects failed requirements.

The ticket format MUST include:
- Header: `Rework Ticket — Cycle {N}/{MAX}`
- Failed Items list with checkboxes
- Instructions section for the Dev Agent

The system MUST track `cycle_count` per change and increment on each failed verification.

#### Scenario: First Cycle Failure

- GIVEN verification detected RF-002 failed
- WHEN the rework ticket is generated
- THEN the header shows "Cycle 1/3"
- AND includes the failed item with reason
- AND increments cycle_count to 1

#### Scenario: Cycle Count Escalation

- GIVEN verification detected failures for the third consecutive time (cycle_count = 2)
- WHEN the rework ticket is generated
- THEN the header shows "Cycle 3/3"
- AND includes `escalate: true` in the report metadata
- AND the instructions indicate escalation to human reviewer

---

### Requirement: LLM-as-Judge Configuration

The system SHALL support configurable LLM models for verification through the `ENGRAM_VERIFICATION_MODEL` environment variable.

The system MUST default to `Sonnet` if the variable is not set. Each verification MUST include a `confidence` score (0.0-1.0) from the LLM judge.

#### Scenario: Custom Model Configuration

- GIVEN `ENGRAM_VERIFICATION_MODEL` is set to `opus`
- WHEN `mem_verify_artifact` invokes the LLM judge
- THEN it uses the `opus` model for evaluation
- AND includes the model used in the report metadata

#### Scenario: Default Model Fallback

- GIVEN `ENGRAM_VERIFICATION_MODEL` is not set or invalid
- WHEN `mem_verify_artifact` invokes the LLM judge
- THEN it defaults to `Sonnet`
- AND logs a warning about using the default model

#### Scenario: Confidence Score in Report

- GIVEN the LLM judge evaluates an RF requirement
- WHEN evaluation completes
- THEN each passed/failed item includes a `confidence` field between 0.0 and 1.0
- AND the report includes overall `averageConfidence`

---

### Requirement: Cycle Count Persistence

The system SHALL persist `cycle_count` as an Engram observation with topic_key `cycle-count/{change-name}` to survive across sessions.

The cycle count MUST be loaded on verification start and updated after each verification cycle.

#### Scenario: Session Recovery

- GIVEN a previous verification cycle set cycle_count to 1 for change "feature-x"
- WHEN a new verification session starts for "feature-x"
- THEN the system loads cycle_count = 1 from persistent storage
- AND increments to 2 for the new cycle

#### Scenario: Count Reset on Success

- GIVEN cycle_count is 2 for change "feature-x"
- WHEN verification passes all requirements
- THEN cycle_count is reset to 0
- AND stored observation reflects the reset value

---

## Summary

| Domain | Requirements | Scenarios |
|--------|-------------|-----------|
| artifact-verification | 6 | 14 |

### Coverage
- Happy paths: Fully covered (spec parser, verify, traceability, rework ticket, LLM config, cycle persistence)
- Edge cases: Fully covered (missing sections, unparseable, timeout, empty diff, escalation, custom model, session recovery)
- Error states: Covered (timeout handling, unparseable detection, empty diff)

### Next Step
Ready for design (sdd-design). If design already exists, ready for tasks (sdd-tasks).