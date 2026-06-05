using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;

namespace Engram.Server;

/// <summary>
/// Middleware that captures request body on JSON deserialization errors (400).
/// Logs the first 1KB of the malformed body for debugging.
/// </summary>
public sealed class BodyDebugLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BodyDebugLoggingMiddleware> _logger;

    public BodyDebugLoggingMiddleware(RequestDelegate next, ILogger<BodyDebugLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        // Enable buffering for POST/PUT with JSON content-type
        if (HttpMethods.IsPost(ctx.Request.Method) || HttpMethods.IsPut(ctx.Request.Method))
        {
            if (ctx.Request.ContentType?.Contains("application/json") == true)
            {
                ctx.Request.EnableBuffering();
            }
        }

        await _next(ctx);

        // After response: if 400 with JSON error, try to capture body preview
        if (ctx.Response.StatusCode == 400)
        {
            try
            {
                ctx.Request.Body.Position = 0;
                using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen: true);
                var body = await reader.ReadToEndAsync();

                if (!string.IsNullOrEmpty(body))
                {
                    var preview = body.Length > 1024 ? body[..1024] + "..." : body;
                    _logger.LogWarning("JSON deserialization error ({Method} {Path}): body preview: {Preview}",
                        ctx.Request.Method, ctx.Request.Path, preview);
                }
            }
            catch
            {
                // Best-effort: body might not be re-readable
            }
        }
    }
}