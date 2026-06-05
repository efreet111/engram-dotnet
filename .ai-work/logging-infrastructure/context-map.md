# FlowForge Context Map — Logging Infrastructure

**Feature Slug:** `logging-infrastructure`  
**Spec:** `sdd/logging-infrastructure/specs/logging-infrastructure.md`  
**Spec Status:** Draft (4 REQs)  
**Date:** 2026-06-04  
**Backend:** Any (logging is host-level, applies to SQLite + Postgres + HttpStore)

---

## Summary Table

| # | REQ | Status | Where in Code | What's Missing vs Spec |
|---|-----|--------|---------------|------------------------|
| 1 | **REQ-LOG-01** Request/Response Logging Middleware | **Partial** | `EngramServer.cs:77-136` inline middleware | Client IP not captured. Duration embedded in message string (`({Duration}ms)`), not a separate field. No structured JSON output. |
| 2 | **REQ-LOG-02** Structured JSON Logging | **Missing** | `EngramServer.cs:36-37` (`ClearProviders` + `AddConsole()`) | Uses default plain-text `ConsoleFormatter`. No JSON format. Field names don't match spec (`method/path/status` exist as template params but `@timestamp` not ISO8601, `level` is enum name, `duration_ms` and `error` object absent). |
| 3 | **REQ-LOG-03** POST Body Error Debugging | **Partial** | `EngramServer.cs:94-107` body capture in catch block; `CloudSyncEndpoints.cs:488-489` `ReadJsonAsync` logs error | Body capture only fires on unhandled exceptions. JSON deserialization errors in `ReadJson<T>` (EngramServer.cs:599-608) are **silently swallowed** — return `default` with no log. `CloudSyncEndpoints.ReadJsonAsync` logs the exception but NOT the body preview. |
| 4 | **REQ-LOG-04** Global Exception Handler | **Partial** | `EngramServer.cs:87-121` catch block in middleware | Works for routes registered after middleware. Returns 500 JSON with `{ error, type, bodyPreview, stackTrace }`. But spec says "not working for all cases" — verify if SyncManager BG service or MCP server is also covered (they aren't; they have their own host). |
| 5 | **Backlog:** `ENGRAM_LOG_LEVEL` env var | **Missing** | — | No env var binding exists. Log level is default (Information). No `appsettings.json` in the project. |

**Verdict:** 0 Done, 4 Partial/Missing. Feature has significant scaffolding but gaps in format, coverage, and consistency.

---

## Existing Logging Touchpoints

### `src/Engram.Server/` — Migration Surface

| File | Line | Pattern | Category | What It Does |
|------|------|---------|----------|-------------|
| `EngramServer.cs` | 36-37 | `builder.Logging.ClearProviders()` + `AddConsole()` | Config | Clears default providers, adds plain-text console logger |
| `EngramServer.cs` | 60 | `ILogger<SyncManager>` DI | DI | Passes typed logger to SyncManager background service |
| `EngramServer.cs` | 81 | `ILoggerFactory.CreateLogger("HTTP")` | Server | Creates ad-hoc logger for middleware |
| `EngramServer.cs` | 90 | `LogError(ex, "HTTP {Method} {Path} → 500...")` | Error | Logs unhandled exceptions with method/path/error type |
| `EngramServer.cs` | 127 | `LogWarning("HTTP {Method} {Path} → {Status}...")` | Warn | Logs 4xx responses with duration |
| `EngramServer.cs` | 132 | `LogInformation("HTTP {Method} {Path} → {Status}...")` | Info | Logs 2xx/3xx responses with duration |
| `EngramServer.cs` | 189 | `Console.Error.WriteLine(">>> DEBUG TEST...")` | Debug | Debug-only output on `/debug-test` endpoint |
| `CloudSyncEndpoints.cs` | 66,72,76,82,85,104,105 | `Console.Error.WriteLine(...)` | Debug | **7 debug print statements** left in production code between real sync logic |
| `CloudSyncEndpoints.cs` | 488-489 | `ILoggerFactory.CreateLogger("ReadJson")` + `LogError(ex, "ReadJsonAsync failed")` | Error | Logs when `ReadJsonAsync` fails deserialization (no body preview) |

### `src/Engram.Sync/` — Already Well-Structured (Reference Pattern)

| File | Pattern | Notes |
|------|---------|-------|
| `SyncManager.cs:16-68` | `LoggerMessage.Define` (9 instances) | High-performance `Action<ILogger, ...>` delegates. Uses EventIds 2000-2008. Levels: Debug, Info, Error, Critical. |
| `SyncManager.cs:73` | `ILogger<SyncManager>` | Injected via constructor |
| `SyncManager.cs` (throughout) | `_logger.Log*` calls | 10+ usages using standard structured templates |

`SyncManager` is the **model implementation** — it uses `LoggerMessage.Define` for hot paths and injects a typed `ILogger<T>`. The `EngramServer` middleware should adopt this pattern where performance matters.

### `src/Engram.Mcp/` — Console Redirection

| File | Line | Pattern | Notes |
|------|------|---------|-------|
| `EngramMcpServer.cs` | 23-27 | `ClearProviders()` + `AddConsole(opts => LogToStandardErrorThreshold = Trace)` | All MCP logs go to stderr (stdout is MCP protocol). This is correct and should stay. |

### `src/Engram.Cli/` — User-Facing Output (NOT Logs)

~100 `Console.WriteLine` calls in `Program.cs`. These are CLI user output (search results, export stats, merge results, etc.) — **NOT operational logs**. The spec rightly excludes them; they should remain `Console.WriteLine`.

### `src/Engram.Store/` — No Logging

The store implementations (`SqliteStore.cs`, `PostgresStore.cs`, `HttpStore.cs`) have **zero ILogger usage**. This is intentional design — stores are pure data access. All logging is at the API/Sync layer.

---

## Key Architectural Observations

### 1. Logging Provider Gap — No JSON Formatter

```csharp
// EngramServer.cs:36-37
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
```

This uses the default `ConsoleFormatter` (plain text). Output looks like:
```
info: HTTP[0] HTTP GET /health → 200 (4ms)
```

Spec wants:
```json
{"@timestamp":"2026-05-20T10:00:00Z","level":"info","method":"GET","path":"/health","status":200,"duration_ms":4}
```

**Recommendation:** Switch to `AddJsonConsole()` (built into `Microsoft.Extensions.Logging.Console` since .NET 8). No additional NuGet dependency. Requires configuring `JsonConsoleFormatterOptions` to match spec field naming.

### 2. Debug Print Statements in Production Code

`CloudSyncEndpoints.cs` has 7 `Console.Error.WriteLine` statements that are debug leftovers. These should be:
- Removed, OR
- Migrated to `ILogger.LogDebug` with structured templates

`EngramServer.cs:189` has `Console.Error.WriteLine(">>> DEBUG TEST endpoint hit!")` on a debug-only endpoint — low priority but should be cleaned for consistency.

### 3. Silently Swallowed Deserialization Errors

```csharp
// EngramServer.cs:599-608
private static async Task<T?> ReadJson<T>(HttpContext ctx)
{
    try { return await JsonSerializer.DeserializeAsync<T>(ctx.Request.Body, JsonOpts); }
    catch { return default; }
}
```

This is used by ALL POST handlers. When a client sends malformed JSON, the error is silently swallowed and the handler receives `null`, then returns a generic 400. There is NO log of what the malformed body looked like.

**REQ-LOG-03 requires this to log.** Approach options:
- **(A) Middleware:** Add a separate middleware that buffers the body and logs on JSON parsing errors (avoids touching 22+ handlers)
- **(B) Per-handler:** Modify `ReadJson<T>` to accept a logger and log the body preview on failure (simpler, centralized)
- **(C) Hybrid:** Middleware for general body capture (REQ-LOG-01), plus modify `ReadJson<T>` for the JSON-specific case (REQ-LOG-03)

Recommend **B** — one change in `ReadJson<T>` covers all 22+ handlers.

### 4. Global Exception Handler Coverage

The middleware at `EngramServer.cs:77-136` wraps all HTTP routes. However:
- **MCP server** (`EngramMcpServer.cs`) runs on a separate host with its own logger config — exceptions there are NOT caught by this middleware (intentional, MCP uses stdio).
- **SyncManager** (`SyncManager.cs`) is a `BackgroundService` — it has its own `try/catch` with `PanicExit` for critical failures.
- The spec says "not working for all cases" — likely referring to the `ReadJson<T>` silent catch preventing errors from reaching the middleware.

### 5. Duration Field Reporting

Current code logs duration inside the message string: `"({Duration}ms)"`. For structured JSON, duration should be a separate numeric field (`"duration_ms": 42`). The template parameter `{Duration}` is already passed as a named value, so `JsonConsoleFormatter` will include it in the output — but the field name would be `Duration` not `duration_ms`. Need to either rename the template parameter or add explicit property enrichment.

---

## Risks & Open Questions

### Q1: Serilog vs Built-in `JsonConsoleFormatter`

| Aspect | Built-in `AddJsonConsole()` | Serilog |
|--------|---------------------------|---------|
| NuGet dependency | None (in-box in ASP.NET 8+) | `Serilog.AspNetCore` (~5 packages) |
| Field naming | Limited control via `JsonConsoleFormatterOptions` | Full control via output templates + enrichers |
| Sinks | Console only | File, Seq, Elastic, Datadog, etc. |
| Project fit | ✅ Aligns with "no over-engineering" philosophy | ❌ Adds complexity for no current need |
| Performance | Good (uses `Utf8JsonWriter`) | Good (but more allocation for template rendering) |

**Recommendation:** Built-in `AddJsonConsole()` + custom `ConsoleFormatter` subclass (or post-output transformation) to match spec field names. Only add Serilog if file sinks or remote log shipping become a requirement.

### Q2: Client IP Source

REQ-LOG-01 requires client IP. Options:
- `ctx.Connection.RemoteIpAddress` — works for direct connections (Docker host network)
- `X-Forwarded-For` header — required if behind reverse proxy (nginx, Traefik)
- Both, with fallback: prefer `X-Forwarded-For`, fall back to `RemoteIpAddress`

The Docker setup (`docker-compose.yml`) exposes port 7437 directly — no reverse proxy currently. But the architecture doc mentions JWT auth which may come with a proxy. Recommend: log `RemoteIpAddress` now, add `X-Forwarded-For` support as a trivial follow-up.

### Q3: Log Level Env Var Naming

Backlog says `ENGRAM_LOG_LEVEL`. Standard ASP.NET would use `Logging__LogLevel__Default` or `Logging:LogLevel:Default`. However, all other env vars in this project use the `ENGRAM_*` prefix (`ENGRAM_DB_TYPE`, `ENGRAM_PORT`, `ENGRAM_SERVER_URL`, etc.).

**Recommendation:** Use `ENGRAM_LOG_LEVEL` to be consistent with project convention. Map it via `builder.Logging.AddFilter()` in `EngramServer.Build()`:
```csharp
var logLevel = Environment.GetEnvironmentVariable("ENGRAM_LOG_LEVEL") ?? "Information";
if (Enum.TryParse<LogLevel>(logLevel, ignoreCase: true, out var level))
    builder.Logging.SetMinimumLevel(level);
```

### Q4: MCP Logging Alignment

The MCP server (`EngramMcpServer.cs`) configures its own `AddConsole()` with `LogToStandardErrorThreshold = LogLevel.Trace`. This is correct — MCP must send all logs to stderr. When implementing JSON logging for the HTTP server, the MCP server should NOT be changed (it needs plain text for stderr readability). This means we'll have two logging configurations in the same process — which is fine since they're in separate `HostApplicationBuilder` instances.

### Q5: Store-Layer Logging

Currently stores have zero logging. Is this intentional? If logging middleware captures request/response at the API level, the store doesn't need its own logging for HTTP flows. However, for diagnostic purposes, slow query logging could be useful. Keep out of scope for this feature — create a separate story if needed.

---

## Recommended Scope (CKP-3 Files)

| # | File | Action | REQs |
|---|------|--------|------|
| 1 | `src/Engram.Server/EngramServer.cs` | **Modify** — Refactor middleware to produce structured JSON fields; add IP capture; add body debug logging in `ReadJson<T>` | LOG-01, LOG-02, LOG-03, LOG-04 |
| 2 | `src/Engram.Server/EngramServer.cs` | **Modify** — Add `ENGRAM_LOG_LEVEL` env var binding in `Build()` | Backlog |
| 3 | `src/Engram.Server/CloudSyncEndpoints.cs` | **Modify** — Remove 7 debug `Console.Error.WriteLine` statements; migrate `ReadJsonAsync` body preview to structured log | LOG-03 |
| 4 | `src/Engram.Server/EngramServer.cs` | **Modify** — Change `builder.Logging.AddConsole()` to `AddJsonConsole()` with options | LOG-02 |
| 5 | `sdd/logging-infrastructure/specs/logging-infrastructure.md` | **Update** — Mark REQs as implemented, add test scenarios | Docs |
| 6 | `tests/Engram.Server.Tests/` | **Add** — Integration tests for middleware, JSON body handling, and structured output | Testing |

**Total change surface:** ~2 files production code, ~1 test file, ~1 spec update. Estimated 2-3h per spec.

---

*Context Map prepared by FlowForge Discovery Agent — ready for CKP-1 review.*
