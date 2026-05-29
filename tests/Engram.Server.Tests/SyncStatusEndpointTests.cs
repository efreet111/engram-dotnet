using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engram.Server;
using Engram.Sync;
using Engram.Store;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Engram.Server.Tests;

public sealed class SyncStatusEndpointTests : IAsyncDisposable
{
    private readonly Mock<IStore> _storeMock;
    private readonly Mock<ILocalSyncStore> _localMock;
    private readonly Mock<ICloudMutationStore> _cloudMock;
    private readonly Mock<ISyncStatusProvider> _providerMock;
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public SyncStatusEndpointTests()
    {
        var port = GetFreePort();
        var baseUrl = $"http://localhost:{port}";

        _storeMock = new Mock<IStore>();
        _storeMock.Setup(s => s.BackendName).Returns("mock");
        _localMock = _storeMock.As<ILocalSyncStore>();
        _cloudMock = _storeMock.As<ICloudMutationStore>();
        _providerMock = new Mock<ISyncStatusProvider>();

        _localMock.Setup(s => s.GetSyncStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SyncState?)null);
        _localMock.Setup(s => s.ListPendingSyncMutationsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncMutation>());
        _cloudMock.Setup(s => s.GetEnrolledProjectsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EnrolledProject>());

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IStore>(_storeMock.Object);
        builder.Services.AddSingleton(_providerMock.Object);

        _app = builder.Build();
        _app.Urls.Clear();
        _app.Urls.Add(baseUrl);
        _app.MapCloudSyncRoutes();
        _app.StartAsync().GetAwaiter().GetResult();

        _client = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task GET_sync_status_Returns200_WithCorrectSchema()
    {
        var metrics = new SyncMetrics();
        _providerMock.Setup(p => p.IsEnabled).Returns(true);
        _providerMock.Setup(p => p.Phase).Returns(SyncPhase.Healthy);
        _providerMock.Setup(p => p.ConsecutiveFailures).Returns(0);
        _providerMock.Setup(p => p.BackoffUntil).Returns((DateTime?)null);
        _providerMock.Setup(p => p.Metrics).Returns(metrics);

        var resp = await _client.GetAsync("/sync/status");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);

        Assert.True((bool)json["sync_enabled"]!);
        Assert.Equal("healthy", (string)json["phase"]!);
        Assert.Equal("cloud", (string)json["target"]!);

        var cursor = json["cursor"]!;
        Assert.NotNull(cursor["last_pushed_seq"]);
        Assert.NotNull(cursor["last_pulled_seq"]);
        Assert.NotNull(cursor["last_enqueued_seq"]);

        var health = json["health"]!;
        Assert.Equal("healthy", (string)health["status"]!);
        Assert.Equal(0, (int)health["consecutive_failures"]!);

        var counts = json["counts"]!;
        Assert.NotNull(counts["pending_push"]);
        Assert.NotNull(counts["total_pushed"]);
        Assert.NotNull(counts["total_pulled"]);
        Assert.NotNull(counts["deferred_pending"]);

        Assert.NotNull(json["enrolled_projects"]);
        Assert.NotNull(json["paused_projects"]);
    }

    [Fact]
    public async Task GET_sync_status_WithSyncDisabled_ReturnsDisabledState()
    {
        var metrics = new SyncMetrics();
        _providerMock.Setup(p => p.IsEnabled).Returns(false);
        _providerMock.Setup(p => p.Phase).Returns(SyncPhase.Disabled);
        _providerMock.Setup(p => p.ConsecutiveFailures).Returns(10);
        _providerMock.Setup(p => p.BackoffUntil).Returns((DateTime?)null);
        _providerMock.Setup(p => p.Metrics).Returns(metrics);

        var resp = await _client.GetAsync("/sync/status");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);

        Assert.False((bool)json["sync_enabled"]!);
        Assert.Equal("disabled", (string)json["health"]!["status"]!);
        Assert.Equal(10, (int)json["health"]!["consecutive_failures"]!);
    }

    [Fact]
    public async Task GET_sync_status_CloudBackendWithoutSyncManager_ReturnsCloudRelayState()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IStore>(_storeMock.Object);

        var port = GetFreePort();
        var baseUrl = $"http://localhost:{port}";
        await using var app = builder.Build();
        app.Urls.Clear();
        app.Urls.Add(baseUrl);
        app.MapCloudSyncRoutes();
        await app.StartAsync();

        using var client = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var resp = await client.GetAsync("/sync/status");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);

        Assert.True((bool)json["sync_enabled"]!);
        Assert.Equal("cloud", (string)json["phase"]!);
        Assert.Equal("healthy", (string)json["health"]!["status"]!);
    }

    [Fact]
    public async Task GET_sync_status_WithEnrolledProjects_ReturnsProjectList()
    {
        var metrics = new SyncMetrics();
        _providerMock.Setup(p => p.IsEnabled).Returns(true);
        _providerMock.Setup(p => p.Phase).Returns(SyncPhase.Healthy);
        _providerMock.Setup(p => p.ConsecutiveFailures).Returns(0);
        _providerMock.Setup(p => p.BackoffUntil).Returns((DateTime?)null);
        _providerMock.Setup(p => p.Metrics).Returns(metrics);

        _cloudMock.Setup(s => s.GetEnrolledProjectsAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EnrolledProject>
            {
                new("project-a", "2026-05-18T00:00:00Z", "user1"),
                new("project-b", "2026-05-18T01:00:00Z", "user1")
            });

        var resp = await _client.GetAsync("/sync/status");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);

        var projects = json["enrolled_projects"]?.AsArray();
        Assert.NotNull(projects);
        Assert.Equal(2, projects.Count);
        Assert.Equal("project-a", (string)projects[0]!);
        Assert.Equal("project-b", (string)projects[1]!);
    }
}
