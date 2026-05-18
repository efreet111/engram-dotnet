using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Engram.Sync.Transport;

/// <summary>
/// HTTP implementation of IMutationTransport.
/// Follows Go engram API contract exactly with exponential backoff retry.
/// </summary>
public sealed class MutationTransport : IMutationTransport, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string? _authToken;
    private readonly int _maxRetries;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Create mutation transport with HttpClient from DI.
    /// </summary>
    /// <param name="http">HttpClient from IHttpClientFactory</param>
    /// <param name="baseUrl">Cloud server base URL (e.g., "https://cloud.engram.dev")</param>
    /// <param name="authToken">Optional bearer token for auth</param>
    /// <param name="maxRetries">Max retry attempts on transient errors (default 3)</param>
    public MutationTransport(HttpClient http, string baseUrl, string? authToken = null, int maxRetries = 3)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _baseUrl = baseUrl.TrimEnd('/');
        _authToken = authToken;
        _maxRetries = maxRetries;
    }

    public async Task<PushResult> PushMutationsAsync(
        IReadOnlyList<MutationEntry> entries,
        string? createdBy = null,
        CancellationToken ct = default)
    {
        if (entries.Count == 0)
            throw new ArgumentException("Cannot push empty mutation batch", nameof(entries));

        var request = new PushRequest(entries, createdBy);
        var response = await SendWithRetryAsync(
            HttpMethod.Post,
            "/sync/mutations/push",
            request,
            ct);

        // Check for pause error header BEFORE deserializing
        if (response.Headers.TryGetValues("X-Engram-Pause-Error", out var pauseErrors))
        {
            var project = entries.FirstOrDefault()?.Project ?? "";
            return new PushResult([], project, pauseErrors.FirstOrDefault());
        }

        var pushResp = await DeserializeAsync<PushResponse>(response);
        return new PushResult(pushResp.AcceptedSeqs, pushResp.Project);
    }

    public async Task<PullResult> PullMutationsAsync(
        long sinceSeq,
        int limit = 100,
        CancellationToken ct = default)
    {
        var url = $"/sync/mutations/pull?since_seq={sinceSeq}&limit={Math.Min(limit, 100)}";
        var response = await SendWithRetryAsync(HttpMethod.Get, url, null, ct);

        // Check for pause error header BEFORE deserializing
        if (response.Headers.TryGetValues("X-Engram-Pause-Error", out var pauseErrors))
        {
            return new PullResult([], false, sinceSeq, "");
        }

        var pullResp = await DeserializeAsync<PullResponse>(response);
        return new PullResult(pullResp.Mutations, pullResp.HasMore, pullResp.LatestSeq, pullResp.Project);
    }

    /// <summary>
    /// Send HTTP request with exponential backoff retry on transient errors.
    /// Retry only on HttpRequestException (network errors), NOT on 4xx/5xx.
    /// 409 Conflict is NOT retried — paused state must be handled by caller.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct)
    {
        var url = $"{_baseUrl}{path}";
        var delay = TimeSpan.FromSeconds(1);

        for (int attempt = 0; attempt < _maxRetries; attempt++)
        {
            try
            {
                var response = await SendOnceAsync(method, url, body, ct);
                return response; // Success or non-retryable error
            }
            catch (HttpRequestException) when (attempt < _maxRetries - 1)
            {
                // Transient network error — retry with backoff
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)); // 1s, 2s, 4s
            }
        }

        // Final attempt — let exception propagate
        return await SendOnceAsync(method, url, body, ct);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(
        HttpMethod method,
        string url,
        object? body,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);

        // Auth header
        if (!string.IsNullOrEmpty(_authToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);

        // Body for POST
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOpts);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        var response = await _http.SendAsync(request, ct);

        // Handle errors
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            ErrorResponse? error = null;

            // Try to parse error response
            if (!string.IsNullOrEmpty(responseBody))
            {
                try
                {
                    error = JsonSerializer.Deserialize<ErrorResponse>(responseBody, JsonOpts);
                }
                catch { /* Ignore parse errors */ }
            }

            // 409 Conflict → sync paused (special case)
            if (response.StatusCode == HttpStatusCode.Conflict && error?.ErrorCode == "sync-paused")
            {
                // Return a special response that caller can handle
                return CreatePauseResponse(error);
            }

            // Other errors → throw exception
            throw new MutationTransportException(
                (int)response.StatusCode,
                error?.ErrorClass,
                error?.ErrorCode,
                responseBody);
        }

        return response;
    }

    private static HttpResponseMessage CreatePauseResponse(ErrorResponse error)
    {
        // Create a fake 200 response with pause info in a custom header
        // Caller will check for this and handle accordingly
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Headers.Add("X-Engram-Pause-Error", error.Error);
        return response;
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, JsonOpts)!;
    }

    private static async Task<ErrorResponse> DeserializeAsync(string json)
    {
        return JsonSerializer.Deserialize<ErrorResponse>(json, JsonOpts)!;
    }

    public void Dispose() => _http.Dispose();
}
