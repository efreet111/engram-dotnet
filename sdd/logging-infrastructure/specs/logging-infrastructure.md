# Logging Infrastructure — Specification

**Status**: Draft  
**Priority**: High (bloquea debugging en producción)  
**Estimated Effort**: 2-3h  
**Related**: Global exception handler (commit da5c431)

---

## REQ-LOG-01: Request/Response Logging Middleware

Middleware MUST log ALL incoming HTTP requests and outgoing responses.

### Scenario: Request logged
- GIVEN any request to any endpoint
- WHEN the request is received
- THEN log method, path, status code, duration, and client IP

### Scenario: Error response logged
- GIVEN a request that causes a 5xx error
- WHEN the response is sent
- THEN log the full error details (message, stack trace, exception type)

---

## REQ-LOG-02: Structured JSON Logging

Logs MUST use structured format for machine parsing.

| Field | Type | Example |
|-------|------|---------|
| `@timestamp` | ISO8601 | `2026-05-20T10:00:00Z` |
| `level` | string | `error`, `warn`, `info`, `debug` |
| `method` | string | `GET`, `POST` |
| `path` | string | `/sync/enroll` |
| `status` | int | `500` |
| `duration_ms` | int | `142` |
| `error` | object | `{ message, type, stackTrace }` |

### Scenario: All fields present
- GIVEN a request is processed
- WHEN logs are emitted
- THEN all fields above are present

---

## REQ-LOG-03: POST Body Error Debugging

POST endpoints MUST log deserialization errors with body preview.

### Scenario: Invalid JSON
- GIVEN a POST request with malformed JSON body
- WHEN deserialization fails
- THEN log the first 1KB of the raw body and the JSON error

---

## REQ-LOG-04: Global Exception Handler

All unhandled exceptions MUST be caught and logged before returning 500.

Currently partially implemented (commit da5c431) but not working for all cases.

### Scenario: All routes covered
- GIVEN any route throws an exception
- WHEN the exception is caught
- THEN the response includes `{ error, type }` JSON

---

## Implementation Notes

### Middleware Location
Register BEFORE all other middleware in `EngramServer.cs`:

```csharp
app.Use(async (ctx, next) =>
{
    var sw = Stopwatch.StartNew();
    try
    {
        await next(ctx);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Unhandled");
        ctx.Response.StatusCode = 500;
    }
    finally
    {
        sw.Stop();
        logger.LogInformation("{Method} {Path} → {Status} ({Duration}ms)",
            ctx.Request.Method, ctx.Request.Path, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
    }
});
```

### Logger Provider
Use `ILogger<T>` with a non-static type, OR `ILoggerFactory.CreateLogger()`.

### Body Capture
For POST debugging, add optional body capture middleware that reads and logs the first 1KB of request body on error.

---

## Testing Strategy

| Layer | What to Test | Approach |
|-------|-------------|----------|
| Unit | Middleware catches exceptions | Mock HttpContext, assert 500 |
| Integration | POST with invalid JSON returns 400 | WebApplicationFactory |
| Manual | Verify log output format | curl + grep journalctl |

---

## Files Changed

| File | Action |
|------|--------|
| `src/Engram.Server/EngramServer.cs` | Modify → add logging middleware |
| `src/Engram.Server/CloudSyncEndpoints.cs` | Modify → add body debug logging |
