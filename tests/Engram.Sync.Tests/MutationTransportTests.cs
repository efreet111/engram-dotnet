using System.Net;
using System.Text;
using System.Text.Json;
using Engram.Sync.Transport;
using Moq;
using Moq.Protected;
using Xunit;

namespace Engram.Sync.Tests;

/// <summary>
/// Unit tests for MutationTransport — HTTP implementation of IMutationTransport.
/// Uses Moq to mock HttpMessageHandler for controlled HTTP responses.
/// </summary>
public sealed class MutationTransportTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private const string BaseUrl = "https://cloud.engram.dev";

    // ─── Task 1.5.1a: Push 200 OK → AcceptedSeqs ───────────────────────────

    [Fact]
    public async Task PushMutationsAsync_ReturnsAcceptedSeqs_When200OK()
    {
        // Arrange
        var expectedSeqs = new List<long> { 101, 102, 103 };
        var pushResponse = new PushResponse(expectedSeqs, "test-proj", "request_body", "");
        var json = JsonSerializer.Serialize(pushResponse, JsonOpts);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var transport = new MutationTransport(httpClient, BaseUrl);
        var entries = new List<MutationEntry>
        {
            new("test-proj", "session", "s1", "upsert", "{}"),
        };

        // Act
        var result = await transport.PushMutationsAsync(entries);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedSeqs, result.AcceptedSeqs);
        Assert.Equal("test-proj", result.Project);
        Assert.Null(result.PauseError);
    }

    // ─── Task 1.5.1b: Push 409 sync-paused → PauseError ────────────────────

    [Fact]
    public async Task PushMutationsAsync_ReturnsPauseError_When409SyncPaused()
    {
        // Arrange
        var errorResponse = new ErrorResponse("policy", "sync-paused", "Sync is paused for this project", "test-proj", "request", "");
        var json = JsonSerializer.Serialize(errorResponse, JsonOpts);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Conflict,
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var transport = new MutationTransport(httpClient, BaseUrl);
        var entries = new List<MutationEntry>
        {
            new("test-proj", "session", "s1", "upsert", "{}"),
        };

        // Act
        var result = await transport.PushMutationsAsync(entries);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.AcceptedSeqs);
        Assert.NotNull(result.PauseError);
        Assert.Contains("paused", result.PauseError, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Task 1.5.1c: Push 4xx/5xx → MutationTransportException ────────────

    [Fact]
    public async Task PushMutationsAsync_ThrowsMutationTransportException_When4xx()
    {
        // Arrange
        var errorResponse = new ErrorResponse("validation", "invalid-payload", "Bad request", "", "", "");
        var json = JsonSerializer.Serialize(errorResponse, JsonOpts);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var transport = new MutationTransport(httpClient, BaseUrl);
        var entries = new List<MutationEntry>
        {
            new("test-proj", "session", "s1", "upsert", "{}"),
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<MutationTransportException>(
            () => transport.PushMutationsAsync(entries));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("validation", ex.ErrorClass);
        Assert.Equal("invalid-payload", ex.ErrorCode);
    }

    [Fact]
    public async Task PushMutationsAsync_ThrowsMutationTransportException_When5xx()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("Internal Server Error", Encoding.UTF8, "text/plain"),
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var transport = new MutationTransport(httpClient, BaseUrl);
        var entries = new List<MutationEntry>
        {
            new("test-proj", "session", "s1", "upsert", "{}"),
        };

        // Act & Assert
        var ex = await Assert.ThrowsAsync<MutationTransportException>(
            () => transport.PushMutationsAsync(entries));

        Assert.Equal(500, ex.StatusCode);
    }

    // ─── Task 1.5.1d: Pull 200 OK → Mutations + HasMore + LatestSeq ────────

    [Fact]
    public async Task PullMutationsAsync_ReturnsMutations_HasMore_LatestSeq()
    {
        // Arrange
        var mutations = new List<PulledMutation>
        {
            new(1, "proj", "session", "s1", "upsert", "{}", "2025-01-01T00:00:00Z"),
            new(2, "proj", "observation", "o1", "upsert", "{\"title\":\"test\"}", "2025-01-01T00:01:00Z"),
        };
        var pullResponse = new PullResponse(mutations, true, 2, "proj", "query_param", "");
        var json = JsonSerializer.Serialize(pullResponse, JsonOpts);

        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var transport = new MutationTransport(httpClient, BaseUrl);

        // Act
        var result = await transport.PullMutationsAsync(0, 100);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Mutations.Count);
        Assert.True(result.HasMore);
        Assert.Equal(2, result.LatestSeq);
        Assert.Equal("proj", result.Project);
        Assert.Equal(1, result.Mutations[0].Seq);
        Assert.Equal("session", result.Mutations[0].Entity);
        Assert.Equal(2, result.Mutations[1].Seq);
        Assert.Equal("observation", result.Mutations[1].Entity);
    }

    // ─── Task 1.5.1e: Exponential backoff retry on HttpRequestException ────

    [Fact]
    public async Task PushMutationsAsync_RetriesOnHttpRequestException_WithBackoff()
    {
        // Arrange
        var successResponse = new PushResponse(new List<long> { 42 }, "proj", "request_body", "");
        var successJson = JsonSerializer.Serialize(successResponse, JsonOpts);

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var callCount = 0;
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, ct) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new HttpRequestException("Transient network error");
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(successJson, Encoding.UTF8, "application/json"),
                });
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            // Short timeout for fast test
            Timeout = TimeSpan.FromSeconds(30)
        };
        var transport = new MutationTransport(httpClient, BaseUrl, maxRetries: 3);
        var entries = new List<MutationEntry>
        {
            new("proj", "session", "s1", "upsert", "{}"),
        };

        // Act
        var result = await transport.PushMutationsAsync(entries);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result.AcceptedSeqs);
        Assert.Equal(42, result.AcceptedSeqs[0]);
        Assert.Equal(2, callCount); // 1 failed + 1 successful
    }

    // ─── Task 1.5.1f: Empty batch → ArgumentException ──────────────────────

    [Fact]
    public async Task PushMutationsAsync_EmptyBatch_ThrowsArgumentException()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(handlerMock.Object);
        var transport = new MutationTransport(httpClient, BaseUrl);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => transport.PushMutationsAsync(new List<MutationEntry>()));
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Extra: Pull with 409 pause header ───────────────────────────────────

    [Fact]
    public async Task PullMutationsAsync_ReturnsEmpty_When409PauseHeader()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Conflict,
                Content = new StringContent(
                    """{"error_class":"policy","error_code":"sync-paused","error":"Sync is paused"}""",
                    Encoding.UTF8,
                    "application/json"),
            });

        var httpClient = new HttpClient(handlerMock.Object);
        var transport = new MutationTransport(httpClient, BaseUrl);

        // Act
        var result = await transport.PullMutationsAsync(0, 100);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Mutations);
        Assert.False(result.HasMore);
    }
}
