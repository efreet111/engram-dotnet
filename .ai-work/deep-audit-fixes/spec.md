# Spec: deep-audit-fixes

## 1. Objective and scope

Fix 5 critical issues found by forge-verify (2026-06-22):
- 1 deadlock risk (SEC-001/BP-001)
- 4 documentation/code mismatches (DOC-001, DOC-002, DOC-003, DOC-004)

Scope: only these 5 items. No other files, no other changes.

---

## 2. Functional requirements

### FR-001: Replace `.Result` with `await` in SqliteStore.SyncMdToRepoAsync

**Scenario A — happy path:**
Given `SyncMdToRepoAsync` is called during markdown sync
When `PromoteToMdAsync(id, mdDir)` completes successfully
Then the result is returned without blocking the thread.

**Scenario B — deadlock prevention:**
Given `SyncMdToRepoAsync` is called from an async request handler on a captured synchronization context
When `PromoteToMdAsync` would deadlock with `.Result`
Then using `await` allows the continuation without deadlock.

---

### FR-002: Remove `/debug-test` endpoint from API-REFERENCE.md

**Scenario A:**
Given a reader consults API-REFERENCE.md
When they look up `POST /debug-test`
Then the endpoint is not listed — it was removed from the codebase.

**Scenario B:**
Given a reader follows the API-REFERENCE.md to test the endpoint
When they issue `POST /debug-test` against a running server
Then they get a 404 — the endpoint does not exist.

---

### FR-003: Update `/health` version example in API-REFERENCE.md

**Scenario A:**
Given a reader reads the `/health` response example
When they compare it to the actual API response
Then the `"version"` field shows `"1.1.0"` in both the docs and the code.

**Scenario B:**
Given the server returns `"1.1.0"` in the `version` field
When a client parses the documented example
Then the example matches the real response exactly.

---

### FR-004: Update MCP tool count from 26 to 28

**Scenario A — documentation consistency:**
Given a reader checks the MCP tool count claim in README.md, README.es.md, or ARCHITECTURE.md
When they count the actual `[McpServerTool]` attributes in `EngramMcpServer.cs` or `EngramTools.cs`
Then they find exactly 28 tools — not 26.

**Scenario B — source comment consistency:**
Given a developer reads `EngramMcpServer.cs:10` comment
When they compare it to the actual attribute count
Then the comment reflects 28 tools, not the stale value of 19 or 26.

---

### FR-005: Correct JWT auth claim in ARCHITECTURE.md

**Scenario A — no misleading claim:**
Given a reader checks the tech stack in ARCHITECTURE.md
When they look for JWT authentication
Then it is either absent or explicitly marked as planned/future — not listed as an active part of the stack.

**Scenario B — code and docs aligned:**
Given `HttpStore.cs:52-53` still has the `// future` comment
When ARCHITECTURE.md describes auth capabilities
Then it does not overstate what is implemented.

---

## 3. Non-functional requirements

NFR-001: No functional changes beyond the described fixes.
NFR-002: No new tests required for documentation-only fixes (FR-002, FR-003, FR-004, FR-005).
NFR-003: FR-001 (`.Result` → `await`) must be tested under T3 (Postgres integration) before merge.

---

## 4. Files to change

| FR | File | Change |
|----|------|--------|
| FR-001 | `src/Stores/SqliteStore.cs:1639` | `.Result` → `await` |
| FR-001 | `src/Stores/PostgresStore.cs:1445` | `.Result` → `await` |
| FR-002 | `docs/API-REFERENCE.md` | Remove `POST /debug-test` section |
| FR-003 | `docs/API-REFERENCE.md` | `"0.3.0"` → `"1.1.0"` in `/health` example |
| FR-004 | `docs/README.md` | Tool count 26 → 28 |
| FR-004 | `docs/README.es.md` | Tool count 26 → 28 |
| FR-004 | `docs/ARCHITECTURE.md` | Tool count 26 → 28 (and add `mem_relations`, `mem_lineage_obs` to tool list) |
| FR-004 | `src/Mcp/EngramMcpServer.cs:10` | Comment count 19 → 28 |
| FR-005 | `docs/ARCHITECTURE.md` | Remove or mark `[planned]` for JWT in tech stack |

---

## 5. Open questions

None — all 5 fixes are unambiguous from the audit report.
