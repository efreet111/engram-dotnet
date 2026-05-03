# Tasks: Upstream Parity — Phase 2 (API Parity)

## Phase 1: Delete Endpoints — Store Layer (Foundation)

- [ ] 1.1 RED: `TestDeleteSession_NotFound` — throws `SessionNotFoundException` for non-existent ID
- [ ] 1.2 RED: `TestDeleteSession_HasActiveObservations` — throws `SessionDeleteBlockedException` when session has active observations
- [ ] 1.3 RED: `TestDeleteSession_EmptySession` — deletes successfully, session removed
- [ ] 1.4 RED: `TestDeleteSession_DeletesPromptsAlso` — soft-deletes associated prompts
- [ ] 1.5 GREEN: `DeleteSessionAsync(id)` in SqliteStore — check exists, check observations, soft-delete prompts, hard-delete session
- [ ] 1.6 RED: `TestDeletePrompt_NotFound` — throws `PromptNotFoundException`
- [ ] 1.7 RED: `TestDeletePrompt_Success` — soft-deletes prompt, sets deleted_at
- [ ] 1.8 GREEN: `DeletePromptAsync(id)` in SqliteStore — soft-delete in prompt_tombstones
- [ ] 1.9 GREEN: Implement `DeleteSessionAsync` and `DeletePromptAsync` in PostgresStore (same logic, PostgreSQL syntax)
- [ ] 1.10 RED: PostgresStore delete tests — parity with SQLite tests

## Phase 2: Delete Endpoints — Server Layer

- [ ] 2.1 RED: `TestHandleDeleteSession_Success` — 200 with `{ id, status: "deleted" }`
- [ ] 2.2 RED: `TestHandleDeleteSession_NotFound` — 404
- [ ] 2.3 RED: `TestHandleDeleteSession_HasObservations` — 409
- [ ] 2.4 GREEN: `handleDeleteSession` in EngramServer.cs — route `DELETE /sessions/{id}`, map errors to HTTP status codes
- [ ] 2.5 RED: `TestHandleDeletePrompt_Success` — 200
- [ ] 2.6 RED: `TestHandleDeletePrompt_NotFound` — 404
- [ ] 2.7 RED: `TestHandleDeletePrompt_InvalidId` — 400
- [ ] 2.8 GREEN: `handleDeletePrompt` in EngramServer.cs — route `DELETE /prompts/{id}`

## Phase 3: mem_current_project MCP Tool

- [ ] 3.1 RED: `TestGetCurrentProject_NormalResult` — returns project, source, path, cwd
- [ ] 3.2 RED: `TestGetCurrentProject_AmbiguousNoError` — IsError=false, project="", available_projects populated
- [ ] 3.3 RED: `TestGetCurrentProject_WarningCase` — warning present for git_child source
- [ ] 3.4 GREEN: Register `mem_current_project` in EngramTools.cs — uses `DetectProjectFull` from Phase 1, never returns error

## Phase 4: Structured Error Responses

- [ ] 4.1 RED: `TestMcpErrors_StructuredFormat` — output contains error_code, message, available_projects, hint
- [ ] 4.2 GREEN: Create `McpErrors.cs` — `Structured(code, message, availableProjects, hint)` returns `CallToolResult`
- [ ] 4.3 RED: `TestWriteTools_AmbiguousError` — write tool returns structured error when cwd is ambiguous
- [ ] 4.4 RED: `TestReadTools_UnknownProjectError` — read tool returns structured error when project override is unknown

## Phase 5: Obsidian Export — Watch Mode

- [ ] 5.1 RED: `TestWatchModeRunsImmediatelyThenTicks` — initial export + subsequent cycles at interval
- [ ] 5.2 GREEN: Watch loop in `Program.cs` — `PeriodicTimer`, initial export, cycle loop, graceful shutdown
- [ ] 5.3 RED: `TestWatchModeContinuesOnError` — error in cycle 1 doesn't stop cycle 2
- [ ] 5.4 RED: `TestWatchModeGracefulShutdown` — Ctrl+C exits cleanly within 1s
- [ ] 5.5 GREEN: `--interval` flag parsing — accepts `30s`, `5m`, `1h`, defaults to `60s`

## Phase 6: Obsidian Export — Since Filter

- [ ] 6.1 RED: `TestParseSinceArgument_Iso8601` — parses `2025-01-01` correctly
- [ ] 6.2 RED: `TestParseSinceArgument_Relative` — parses `30d`, `7d`, `24h` correctly
- [ ] 6.3 RED: `TestParseSinceArgument_Invalid` — returns error for invalid format
- [ ] 6.4 GREEN: `ParseSinceArgument(string)` helper in Program.cs
- [ ] 6.5 RED: `TestExportWithSinceFilter` — only exports observations created after the since date
- [ ] 6.6 GREEN: `--since` flag in Exporter — filter observations by `created_at >= since`

## Phase 7: Export by Project

- [ ] 7.1 RED: `TestExportProjectAsync_Filtered` — returns only observations for the specified project
- [ ] 7.2 RED: `TestExportProjectAsync_NotFound` — throws when project doesn't exist
- [ ] 7.3 GREEN: `ExportProjectAsync(project)` in SqliteStore — filter by project column
- [ ] 7.4 GREEN: `ExportProjectAsync(project)` in PostgresStore — filter by project column
- [ ] 7.5 RED: `TestObsidianExportWithProjectFilter` — CLI `--project` exports only that project's files
- [ ] 7.6 GREEN: `--project` flag in Exporter — filter before writing files
- [ ] 7.7 RED: `TestServerExportWithProjectQuery` — `GET /export?project=X` returns only that project's data
- [ ] 7.8 GREEN: `?project=` query param in handleExport — delegates to `ExportProjectAsync`

## Phase 8: Integration + Docs

- [ ] 8.1 RED: `TestWatchWithSinceAndProject` — watch mode respects both filters in cycles
- [ ] 8.2 GREEN: Integration test — watch + since + project combined
- [ ] 8.3 DOCS: Update README.md — add new CLI flags (`--watch`, `--since`, `--project`)
- [ ] 8.4 DOCS: Update DOCS.md — add `DELETE /sessions/{id}`, `DELETE /prompts/{id}`, `mem_current_project`
- [ ] 8.5 DOCS: Update ROADMAP.md — mark Phase 2 as complete
