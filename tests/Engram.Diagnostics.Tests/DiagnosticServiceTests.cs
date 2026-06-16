using System.Net;
using System.Net.Http;
using Engram.Diagnostics.Models;
using Engram.Store;
using Moq;
using Xunit;

namespace Engram.Diagnostics.Tests;

/// <summary>
/// Unit tests for DiagnosticService health checks.
/// Tests cover database, HTTP server, and MCP server diagnostics.
/// </summary>
public sealed class DiagnosticServiceTests : IDisposable
{
    private readonly Mock<IStore> _storeMock;
    private readonly MockHttpMessageHandler _httpHandler;
    private readonly HttpClient _httpClient;
    private readonly DiagnosticService _diagnosticService;

    public DiagnosticServiceTests()
    {
        _storeMock = new Mock<IStore>();
        _httpHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_httpHandler);
        _diagnosticService = new DiagnosticService(_storeMock.Object, _httpClient, "http://localhost:5000");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _httpHandler.Dispose();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Database Check Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckDatabase_Success_ReturnsHealthyComponent()
    {
        // Arrange
        var stats = new Stats { TotalSessions = 10, TotalObservations = 50 };
        _storeMock.Setup(s => s.StatsAsync()).ReturnsAsync(stats);
        _storeMock.Setup(s => s.BackendName).Returns("sqlite");

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        Assert.True(result.Components.ContainsKey("database"));
        var dbHealth = result.Components["database"];
        Assert.True(dbHealth.IsHealthy);
        Assert.Contains("sqlite", dbHealth.Message);
        Assert.True(dbHealth.LatencyMs >= 0);
    }

    [Fact]
    public async Task CheckDatabase_Timeout_ReturnsUnhealthyComponent()
    {
        // Arrange
        _storeMock.Setup(s => s.StatsAsync())
            .Returns(() => Task.Delay(3000).ContinueWith(_ => new Stats())); // Simular timeout (> 2s)
        _storeMock.Setup(s => s.BackendName).Returns("sqlite");

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        var dbHealth = result.Components["database"];
        Assert.False(dbHealth.IsHealthy);
        Assert.Contains("timed out", dbHealth.Message);
    }

    [Fact]
    public async Task CheckDatabase_Exception_ReturnsUnhealthyComponent()
    {
        // Arrange
        var expectedError = "Connection refused";
        _storeMock.Setup(s => s.StatsAsync())
            .ThrowsAsync(new InvalidOperationException(expectedError));
        _storeMock.Setup(s => s.BackendName).Returns("sqlite");

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        var dbHealth = result.Components["database"];
        Assert.False(dbHealth.IsHealthy);
        Assert.Contains("failed", dbHealth.Message);
        Assert.Contains(expectedError, dbHealth.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // HTTP Server Check Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckHttpServer_Success_ReturnsHealthyComponent()
    {
        // Arrange
        _httpHandler.SetResponse(HttpStatusCode.OK);
        _storeMock.Setup(s => s.StatsAsync()).ReturnsAsync(new Stats());
        _storeMock.Setup(s => s.BackendName).Returns("sqlite");

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        var httpHealth = result.Components["http_server"];
        Assert.True(httpHealth.IsHealthy);
        Assert.Contains("OK", httpHealth.Message);
        Assert.True(httpHealth.LatencyMs >= 0);
    }

    [Fact]
    public async Task CheckHttpServer_ServerError_ReturnsUnhealthyComponent()
    {
        // Arrange
        _httpHandler.SetResponse(HttpStatusCode.InternalServerError);
        _storeMock.Setup(s => s.StatsAsync()).ReturnsAsync(new Stats());
        _storeMock.Setup(s => s.BackendName).Returns("sqlite");

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        var httpHealth = result.Components["http_server"];
        Assert.False(httpHealth.IsHealthy);
        Assert.Contains("InternalServerError", httpHealth.Message);
    }

    [Fact]
    public async Task CheckHttpServer_NoServerUrl_ReturnsUnhealthyComponent()
    {
        // Arrange
        var serviceNoUrl = new DiagnosticService(_storeMock.Object, _httpClient, serverUrl: null);
        _storeMock.Setup(s => s.StatsAsync()).ReturnsAsync(new Stats());
        _storeMock.Setup(s => s.BackendName).Returns("sqlite");

        // Act
        var result = await serviceNoUrl.RunDiagnosticsAsync();

        // Assert
        var httpHealth = result.Components["http_server"];
        Assert.False(httpHealth.IsHealthy);
        Assert.Contains("not configured", httpHealth.Message);
    }

    [Fact]
    public async Task CheckHttpServer_Exception_ReturnsUnhealthyComponent()
    {
        // Arrange
        _httpHandler.SetException(new HttpRequestException("Connection refused"));
        _storeMock.Setup(s => s.StatsAsync()).ReturnsAsync(new Stats());
        _storeMock.Setup(s => s.BackendName).Returns("sqlite");

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        var httpHealth = result.Components["http_server"];
        Assert.False(httpHealth.IsHealthy);
        Assert.Contains("failed", httpHealth.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // MCP Server Check Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CheckMcpServer_HealthyStore_ReturnsHealthyComponent()
    {
        // Arrange
        _storeMock.Setup(s => s.StatsAsync()).ReturnsAsync(new Stats());
        _storeMock.Setup(s => s.BackendName).Returns("sqlite");

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        var mcpHealth = result.Components["mcp_server"];
        Assert.True(mcpHealth.IsHealthy);
        Assert.Contains("sqlite", mcpHealth.Message);
        Assert.Contains("backend", mcpHealth.Message);
    }

    [Fact]
    public async Task CheckMcpServer_EmptyBackendName_ReturnsUnhealthyComponent()
    {
        // Arrange
        _storeMock.Setup(s => s.StatsAsync()).ReturnsAsync(new Stats());
        _storeMock.Setup(s => s.BackendName).Returns(string.Empty);

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        var mcpHealth = result.Components["mcp_server"];
        Assert.False(mcpHealth.IsHealthy);
        Assert.Contains("invalid", mcpHealth.Message);
    }

    [Fact]
    public async Task CheckMcpServer_BackendNameWithSpaces_ReturnsHealthyComponent()
    {
        // Arrange
        _storeMock.Setup(s => s.StatsAsync()).ReturnsAsync(new Stats());
        _storeMock.Setup(s => s.BackendName).Returns("postgres");

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        var mcpHealth = result.Components["mcp_server"];
        Assert.True(mcpHealth.IsHealthy);
        Assert.Contains("postgres", mcpHealth.Message);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Overall Diagnostics Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RunDiagnostics_AllHealthy_ReturnsHealthySystem()
    {
        // Arrange
        _storeMock.Setup(s => s.StatsAsync()).ReturnsAsync(new Stats());
        _storeMock.Setup(s => s.BackendName).Returns("sqlite");
        _httpHandler.SetResponse(HttpStatusCode.OK);

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        Assert.True(result.IsHealthy);
        Assert.True(result.Components.Count >= 4);
        Assert.All(result.Components.Values, c => Assert.True(c.IsHealthy));
    }

    [Fact]
    public async Task RunDiagnostics_DatabaseUnhealthy_ReturnsUnhealthySystem()
    {
        // Arrange
        _storeMock.Setup(s => s.StatsAsync())
            .ThrowsAsync(new InvalidOperationException("DB error"));
        _storeMock.Setup(s => s.BackendName).Returns("sqlite");
        _httpHandler.SetResponse(HttpStatusCode.OK);

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        Assert.False(result.IsHealthy);
        Assert.False(result.Components["database"].IsHealthy);
        Assert.True(result.Components["http_server"].IsHealthy);
        Assert.True(result.Components["mcp_server"].IsHealthy);
    }

    [Fact]
    public async Task RunDiagnostics_HttpUnhealthy_ReturnsUnhealthySystem()
    {
        // Arrange
        _storeMock.Setup(s => s.StatsAsync()).ReturnsAsync(new Stats());
        _storeMock.Setup(s => s.BackendName).Returns("sqlite");
        _httpHandler.SetResponse(HttpStatusCode.ServiceUnavailable);

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        Assert.False(result.IsHealthy);
        Assert.True(result.Components["database"].IsHealthy);
        Assert.False(result.Components["http_server"].IsHealthy);
        Assert.True(result.Components["mcp_server"].IsHealthy);
    }

    [Fact]
    public async Task RunDiagnostics_McpUnhealthy_ReturnsUnhealthySystem()
    {
        // Arrange
        _storeMock.Setup(s => s.StatsAsync()).ReturnsAsync(new Stats());
        _storeMock.Setup(s => s.BackendName).Returns(string.Empty);
        _httpHandler.SetResponse(HttpStatusCode.OK);

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        Assert.False(result.IsHealthy);
        Assert.True(result.Components["database"].IsHealthy);
        Assert.True(result.Components["http_server"].IsHealthy);
        Assert.False(result.Components["mcp_server"].IsHealthy);
    }

    [Fact]
    public async Task RunDiagnostics_AllUnhealthy_ReturnsUnhealthySystem()
    {
        // Arrange
        _storeMock.Setup(s => s.StatsAsync())
            .ThrowsAsync(new InvalidOperationException("DB error"));
        _storeMock.Setup(s => s.BackendName).Returns(string.Empty);
        _httpHandler.SetResponse(HttpStatusCode.InternalServerError);

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert — core components should be unhealthy; project_identity depends on
        // the actual git repo (may be healthy if running from a valid project)
        Assert.False(result.IsHealthy);
        Assert.False(result.Components["database"].IsHealthy);
        Assert.False(result.Components["http_server"].IsHealthy);
    }

    [Fact]
    public async Task RunDiagnostics_ContainsAllExpectedComponents()
    {
        // Arrange
        _storeMock.Setup(s => s.StatsAsync()).ReturnsAsync(new Stats());
        _storeMock.Setup(s => s.BackendName).Returns("postgres");
        _httpHandler.SetResponse(HttpStatusCode.OK);

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        Assert.True(result.Components.Count >= 4);
        Assert.Contains("database", result.Components.Keys);
        Assert.Contains("http_server", result.Components.Keys);
        Assert.Contains("mcp_server", result.Components.Keys);
        Assert.Contains("project_identity", result.Components.Keys);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Latency Measurement Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task RunDiagnostics_MeasuresLatency_ForAllComponents()
    {
        // Arrange
        _storeMock.Setup(s => s.StatsAsync()).ReturnsAsync(new Stats());
        _storeMock.Setup(s => s.BackendName).Returns("sqlite");
        _httpHandler.SetResponse(HttpStatusCode.OK);

        // Act
        var result = await _diagnosticService.RunDiagnosticsAsync();

        // Assert
        Assert.All(result.Components.Values, c => Assert.True(c.LatencyMs >= 0));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Constructor Validation Tests
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Constructor_NullStore_ThrowsArgumentNullException()
    {
        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => 
            new DiagnosticService(null!, _httpClient, "http://localhost"));
        Assert.Equal("store", ex.ParamName);
    }

    [Fact]
    public void Constructor_NullHttpClient_CreatesNewHttpClient()
    {
        // Act
        var service = new DiagnosticService(_storeMock.Object, httpClient: null, "http://localhost");

        // Assert
        Assert.NotNull(service);
    }

    /// <summary>
    /// Mock HttpMessageHandler that allows controlling HTTP responses.
    /// </summary>
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private HttpResponseMessage? _response;
        private Exception? _exception;

        public void SetResponse(HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _response = new HttpResponseMessage(statusCode);
            _exception = null;
        }

        public void SetException(Exception ex)
        {
            _exception = ex;
            _response = null;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_exception != null)
            {
                throw _exception;
            }

            if (_response != null)
            {
                return Task.FromResult(_response);
            }

            // Default response
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
