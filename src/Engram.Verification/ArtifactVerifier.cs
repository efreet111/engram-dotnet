namespace Engram.Verification;

/// <summary>
/// Verifies that code changes satisfy a specification's requirements.
/// Implementations act as a judge — evaluating each requirement against the code diff.
/// </summary>
public interface IVerifier
{
    /// <summary>
    /// Judge whether the given code diff satisfies the spec's requirements.
    /// </summary>
    /// <param name="spec">Parsed specification with objective and requirements.</param>
    /// <param name="codeDiff">Unified diff or file listing of changes made.</param>
    /// <param name="currentCycle">Zero-based cycle number for this verification round.</param>
    /// <returns>A structured report with per-requirement verdicts.</returns>
    Task<VerificationReport> VerifyAsync(
        SpecParseResult spec,
        string codeDiff,
        int currentCycle);
}

/// <summary>
/// LLM-based verifier that calls the Anthropic API (<c>/v1/messages</c>) to judge
/// code changes against specification requirements.
/// </summary>
/// <remarks>
/// Configuration is read from environment variables:
/// <list type="bullet">
///   <item><c>ENGRAM_VERIFICATION_MODEL</c> — model name (default: <c>claude-sonnet-4-20250514</c>)</item>
///   <item><c>ANTHROPIC_API_KEY</c> — required; API key for Anthropic</item>
/// </list>
/// </remarks>
public sealed class LlmVerifier : IVerifier, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _model;
    private readonly string _apiKey;

    /// <summary>
    /// Initializes a new instance. Reads config from environment variables.
    /// </summary>
    /// <param name="http">Optional <see cref="HttpClient"/>. If null, a new one is created.</param>
    /// <exception cref="InvalidOperationException"><c>ANTHROPIC_API_KEY</c> is not set.</exception>
    public LlmVerifier(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _model = Environment.GetEnvironmentVariable("ENGRAM_VERIFICATION_MODEL")
                 ?? "claude-sonnet-4-20250514";
        _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                  ?? throw new InvalidOperationException("ANTHROPIC_API_KEY is not set.");
    }

    /// <inheritdoc />
    public async Task<VerificationReport> VerifyAsync(
        SpecParseResult spec,
        string codeDiff,
        int currentCycle)
    {
        var prompt = BuildPrompt(spec, codeDiff);
        var response = await CallAnthropicAsync(prompt);
        return ParseResponse(response, currentCycle);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string BuildPrompt(SpecParseResult spec, string codeDiff)
    {
        var reqLines = string.Join("\n",
            spec.Requirements.Select(r => $"- {r.Id}: {r.Description}"));

        var prompt = "You are a code review judge. Verify the following code changes against the specification.\n\n" +
            $"## Objective\n{spec.Objective}\n\n" +
            "## Requirements\n" + reqLines + "\n\n" +
            "## Code Changes\n```diff\n" + codeDiff + "\n```\n\n" +
            "For each requirement, determine: PASS (fully met), FAIL (not met or violated), or UNTESTED (cannot determine).\n\n" +
            "Respond in this JSON format only (no other text):\n" +
            "{\n  \"items\": [\n    {\n      \"requirement_id\": \"RF-001\",\n" +
            "      \"verdict\": \"Pass|Fail|Untested\",\n      \"reasoning\": \"short explanation\",\n" +
            "      \"confidence\": 0.95,\n      \"evidence\": \"file path or line reference\"\n    }\n  ],\n" +
            "  \"summary\": \"Overall assessment\"\n}";

        return prompt;
    }

    private async Task<string> CallAnthropicAsync(string prompt)
    {
        var request = new
        {
            model = _model,
            max_tokens = 4096,
            messages = new[] { new { role = "user", content = prompt } }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(request);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        _http.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", _apiKey);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("anthropic-version", "2023-06-01");

        var response = await _http.PostAsync(
            "https://api.anthropic.com/v1/messages", content);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync();
    }

    private static VerificationReport ParseResponse(string jsonResponse, int currentCycle)
    {
        try
        {
            // Try to extract the inner JSON block from the LLM response.
            // The Anthropic response wraps it in a content array — we search
            // for the first top-level JSON object instead.
            var jsonStr = ExtractJsonBlock(jsonResponse);
            if (jsonStr is null)
                return ErrorReport("Failed to extract JSON from LLM response", currentCycle);

            var report = System.Text.Json.JsonSerializer.Deserialize<VerificationReport>(jsonStr);
            if (report is null)
                return ErrorReport("Failed to deserialize LLM response into report", currentCycle);

            return report with { Cycle = currentCycle };
        }
        catch (Exception ex)
        {
            return ErrorReport($"LLM response parsing error: {ex.Message}", currentCycle);
        }
    }

    private static string? ExtractJsonBlock(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) return null;

        var end = text.LastIndexOf('}');
        if (end < 0 || end <= start) return null;

        return text[start..(end + 1)];
    }

    private static VerificationReport ErrorReport(string error, int cycle) => new()
    {
        Summary = error,
        Cycle = cycle,
        Escalate = true,
    };

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// Fake verifier for testing. Returns a pre-configured result regardless of input.
/// </summary>
public sealed class FakeVerifier : IVerifier
{
    /// <summary>
    /// The result to return from every verification call.
    /// </summary>
    public required VerificationReport Result { get; init; }

    /// <inheritdoc />
    public Task<VerificationReport> VerifyAsync(
        SpecParseResult spec,
        string codeDiff,
        int currentCycle)
    {
        return Task.FromResult(Result with { Cycle = currentCycle });
    }
}
