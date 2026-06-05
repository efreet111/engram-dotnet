---
feature: logging-infrastructure
status: Ready
date: 2026-06-04
source_spec: sdd/logging-infrastructure/specs/logging-infrastructure.md
backend: Any (SQLite + Postgres + HttpStore; logging is host-level)
---

# Spec: Logging Infrastructure

##1. Objective and Scope

Replace the existing inline plain-text logging in `EngramServer.cs` with structured JSON logging that captures all HTTP request/response fields required for production debugging. Covers: request/response middleware, JSON formatter configuration, POST body error capture, global exception handling, and `ENGRAM_LOG_LEVEL` env var.

### In Scope
- Structured JSON log output with spec field names (`@timestamp`, `level`, `method`, `path`, `status`, `duration_ms`, `error`)
- Client IP capture in log lines
- POST body preview (first 1KB) on deserialization failure
- Global exception handler returning JSON `{error, type}` with stack trace in log
- `ENGRAM_LOG_LEVEL` env var binding
- Removal of 7 debug `Console.Error.WriteLine` statements in `CloudSyncEndpoints.cs`

### Out of Scope
- Log shipping to external systems (Loki, CloudWatch, Datadog) — local stdout/journalctl only
- OpenTelemetry / distributed tracing — separate feature
- CLI user-facing output (`Console.WriteLine` in `Program.cs` and `Engram.Cli`) — NOT logs, remain unchanged
- Store-layer logging (stores are pure data access; API/Sync layer covers HTTP flows)
- MCP server logging changes — already correct (stderr, plain text, separate host)

---

## 2. Functional Requirements (FR)

### FR-LOG-01: Request/Response Logging Middleware

All incoming HTTP requests and outgoing responses MUST be logged with method, path, status code, duration (ms), and client IP.

**Scenario A: Successful request logged**
- GIVEN a request to any endpoint
- WHEN the request is received and a response is sent
- THEN a log line is emitted with `method`, `path`, `status`, `duration_ms`, and `client_ip`

**Scenario B: Error response includes stack trace**
- GIVEN a request that causes a 5xx error
- WHEN the response is sent
- THEN the log line includes an `error` object with `message`, `type`, and `stackTrace`

**Implementation:** `EngramServer.cs:77-136` (partial — missing `client_ip`, `duration_ms` as separate field, structured JSON)

---

### FR-LOG-02: Structured JSON Logging

Log output MUST be structured JSON (machine-parseable) with the following field names and types:

| Field | Type | Example |
|-------|------|---------|
| `@timestamp` | ISO8601 string | `2026-05-20T10:00:00Z` |
| `level` | string | `error`, `warn`, `info`, `debug` |
| `method` | string | `GET`, `POST` |
| `path` | string | `/sync/enroll` |
| `status` | int | `200`, `404`, `500` |
| `duration_ms` | int | `42` |
| `client_ip` | string | `192.168.1.1` |
| `error` | object (optional) | `{ message, type, stackTrace }` |

**Scenario A: All fields present in log line**
- GIVEN a request is processed
- WHEN logs are emitted
- THEN the output is valid JSON containing all fields above

**Scenario B: No error object on success**
- GIVEN a request that returns2xx or 3xx
- THEN the log line has no `error` field (not null, absent)

**Implementation:** `EngramServer.cs:36-37` (partial — uses `AddConsole()` plain-text formatter, no JSON, field names don't match spec)

---

### FR-LOG-03: POST Body Error Debugging

When a POST request contains malformed JSON, the first 1KB of the raw request body MUST be logged alongside the deserialization error.

**Scenario A: Malformed JSON logged with body preview**
- GIVEN a POST request with a malformed JSON body
- WHEN deserialization fails
- THEN a log entry is emitted containing the JSON error message and a `body_preview` field with the first 1KB of raw body

**Scenario B: 400 response returned**
- GIVEN a POST request with malformed JSON
- WHEN deserialization fails
- THEN the server returns HTTP 400 with a JSON error body (not a 500)

**Implementation:** `EngramServer.cs:599-608` — `ReadJson<T>` silently swallows errors (partial); `CloudSyncEndpoints.cs:488-489` logs error but not body preview (partial)

---

### FR-LOG-04: Global Exception Handler

All unhandled exceptions MUST be caught, logged with full details, and return a JSON response with `{error, type}`.

**Scenario A: All registered routes covered**
- GIVEN any route registered after the middleware throws an exception
- WHEN the exception is caught
- THEN the response is HTTP 500 with JSON `{error, type}` and the log contains the full stack trace

**Scenario B: Response does not leak internals**
- GIVEN an unhandled exception
- THEN the JSON response body contains only `error` (message) and `type` (exception type name) — no stack trace in body

**Implementation:** `EngramServer.cs:87-121` (partial — works for registered routes; MCP server and SyncManager BG service have separate hosts and are not covered by this middleware, which is intentional)

---

### FR-LOG-05: Configurable Log Level via Environment Variable

The minimum log level MUST be configurable via the `ENGRAM_LOG_LEVEL` environment variable (e.g., `info`, `warn`, `error`). If unset, defaults to `Information`.

**Scenario A: Log level respected**
- GIVEN `ENGRAM_LOG_LEVEL=warn` is set in the environment
- WHEN a request triggers a log event at `Information` level
- THEN that log line is NOT emitted

**Scenario B: Default level when unset**
- GIVEN no `ENGRAM_LOG_LEVEL` env var is set
- WHEN the server starts
- THEN the minimum log level is `Information`

**Implementation:** Not implemented — no env var binding exists currently.

---

## 3. Non-Functional Requirements (NFR)

- NFR-LOG-001: Log output MUST be valid JSON (parseable by `jq`, Loki, CloudWatch, etc.)
- NFR-LOG-002: Duration measurement MUST NOT be affected by body buffering (start timer before reading body)
- NFR-LOG-003: `ENGRAM_LOG_LEVEL` must follow the project's `ENGRAM_*` prefix convention (consistent with `ENGRAM_DB_TYPE`, `ENGRAM_PORT`, etc.)
- NFR-LOG-004: MCP server logging MUST NOT be changed (uses stderr/plain-text, correct by design)
- NFR-LOG-005: `ReadJson<T>` failure MUST NOT silently swallow errors — must log body preview on deserialization failure

---

## 4. Existing Implementation Map

| FR | Status | Where | What's Partial / Needs Change |
|----|--------|-------|-------------------------------|
| FR-LOG-01 | Partial | `EngramServer.cs:77-136` | Missing `client_ip` capture. `duration_ms` embedded in message string `({Duration}ms)`, not a separate JSON field. |
| FR-LOG-02 | Partial | `EngramServer.cs:36-37` | Uses `AddConsole()` plain-text formatter. No JSON. `@timestamp` not ISO8601, `level` is enum name, `duration_ms` absent. |
| FR-LOG-03 | Partial | `EngramServer.cs:599-608` + `CloudSyncEndpoints.cs:488-489` | `ReadJson<T>` silently swallows errors (returns `default`). `ReadJsonAsync` logs error but NOT body preview. |
| FR-LOG-04 | Partial | `EngramServer.cs:87-121` | Works for registered routes. MCP and SyncManager have separate hosts (intentional). |
| FR-LOG-05 | Missing | — | No `ENGRAM_LOG_LEVEL` env var binding exists. |

---

## 5. Developer Manual Tests (required — mark [x] before /flow-close)

All tests run against `http://localhost:7437` (or actual server URL). Capture log output via stdout or `journalctl`.

| ID | Case / Flow | Steps (summary) | Expected Result | [x] |
|----|-------------|----------------|-----------------|-----|
| PM-1 | GET /health → JSON log line |1. `curl -s http://localhost:7437/health`<br>2. Inspect stdout log output | JSON log line with `level`, `method`=GET, `path`=/health, `status`=200, `duration_ms`, `@timestamp` (ISO8601), `client_ip` | [x] |
| PM-2 | GET /foo (404) → log status 404 | 1. `curl -s http://localhost:7437/foo`<br>2. Inspect log output | JSON log line with `status`=404, `level`=warn or info, no `error` object | [x] |
| PM-3 | POST malformed JSON → 400 + body preview | 1. `curl -s -X POST http://localhost:7437/sync/mutations/push -H "Content-Type: application/json" -d '{invalid}'`<br>2. Inspect log output | HTTP 400 returned. Log line contains `body_preview` field (first1KB of `{invalid}`) and JSON error details | [x] |
| PM-4 | Endpoint throws 5xx → JSON error + stack trace in log | 1. Trigger an artificial5xx (e.g., via a misconfigured request that hits an unhandled code path)<br>2. Inspect log output | Log line contains `error` object with `message`, `type`, `stackTrace`. Response is JSON `{error, type}` with no stack trace in body | [≈] Deferred — see verify-report |
| PM-5 | ENGRAM_LOG_LEVEL=warn suppresses info | 1. Stop server, set `ENGRAM_LOG_LEVEL=warn`, restart<br>2. `curl -s http://localhost:7437/health`<br>3. Inspect log output | No log line for the health request (info-level suppressed). 404 or error paths (warn/error) ARE logged | [x] |
| PM-6 | Verify client_ip field present | 1. `curl -s http://localhost:7437/health`<br>2. Inspect log output | JSON log line contains `client_ip` field with a valid IP string (not empty, not `null`) | [x] |
| PM-7 | Debug Console.WriteLine not migrated | 1. Grep `src/Engram.Cli/Program.cs` for `Console.WriteLine`<br>2. Verify these are user-facing output (search results, export stats), NOT operational logs | Console.WriteLine calls in CLI remain as-is (not replaced with ILogger). No regression in CLI output | [x] |

---

## Memory Signal

- type: none
- significance: low
- summary: "Logging infrastructure spec — 5 FRs, 7 PM-*, partial implementation across EngramServer.cs and CloudSyncEndpoints.cs."
