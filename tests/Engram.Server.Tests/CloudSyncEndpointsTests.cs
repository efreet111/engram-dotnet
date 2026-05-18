using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engram.Server;
using Engram.Store;
using Microsoft.AspNetCore.Builder;
using Moq;
using Xunit;

namespace Engram.Server.Tests;

/// <summary>
/// Unit tests for CloudSyncEndpoints push/pull handlers.
/// Uses WebApplication with a mock store implementing both IStore and ICloudMutationStore.
/// </summary>
public sealed class CloudSyncEndpointsTests : IAsyncDisposable
{
    private readonly Mock<IStore> _storeMock;
    private readonly Mock<ICloudMutationStore> _cloudMock;
    private readonly WebApplication _app;
    private readonly HttpClient _client;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public CloudSyncEndpointsTests()
    {
        var port = GetFreePort();
        var baseUrl = $"http://localhost:{port}";
        var tempDir = Path.Combine(Path.GetTempPath(), "engram-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        // Create a mock that implements BOTH IStore and ICloudMutationStore
        // The As<>() proxy makes the same object castable to ICloudMutationStore
        _storeMock = new Mock<IStore>();
        _storeMock.Setup(s => s.BackendName).Returns("mock");
        _cloudMock = _storeMock.As<ICloudMutationStore>();

        var cfg = new StoreConfig { DataDir = tempDir };
        _app = EngramServer.Build(_storeMock.Object, cfg);
        _app.Urls.Clear();
        _app.Urls.Add(baseUrl);
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

    // ═══════════════════════════════════════════════════════════════════════
    // Task 1.5.2: Push validation
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Push_EmptyBatch_Returns400_EmptyBatch()
    {
        // Arrange
        var body = new { entries = new object[] { } };

        // Act
        var resp = await _client.PostAsJsonAsync("/sync/mutations/push", body, JsonOpts);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal("empty-batch", (string?)json["error_code"]);
    }

    [Fact]
    public async Task Push_BatchTooLarge_Returns400_BatchTooLarge()
    {
        // Arrange — 101 entries (max is 100)
        var entries = new List<object>();
        for (int i = 0; i < 101; i++)
        {
            entries.Add(new
            {
                project = "test-proj",
                entity = "session",
                entity_key = $"s{i}",
                op = "upsert",
                payload = "{}"
            });
        }
        var body = new { entries };
        _cloudMock.Setup(s => s.IsProjectSyncEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var resp = await _client.PostAsJsonAsync("/sync/mutations/push", body, JsonOpts);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal("batch-too-large", (string?)json["error_code"]);
    }

    [Fact]
    public async Task Push_EntryWithoutProject_Returns400_InvalidEntry()
    {
        // Arrange
        var body = new
        {
            entries = new[]
            {
                new { project = "", entity = "session", entity_key = "s1", op = "upsert", payload = "{}" }
            }
        };

        // Act
        var resp = await _client.PostAsJsonAsync("/sync/mutations/push", body, JsonOpts);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal("invalid-entry", (string?)json["error_code"]);
    }

    [Fact]
    public async Task Push_RelationMissingRequiredFields_Returns400_InvalidRelation()
    {
        // Arrange — relation payload missing sync_id field
        var body = new
        {
            entries = new[]
            {
                new
                {
                    project = "test-proj",
                    entity = "relation",
                    entity_key = "r1",
                    op = "upsert",
                    payload = """{"source_id":"src1","target_id":"tgt1"}""" // missing sync_id, judgment_status, marked_by_actor, marked_by_kind
                }
            }
        };

        // Act
        var resp = await _client.PostAsJsonAsync("/sync/mutations/push", body, JsonOpts);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal("invalid-relation", (string?)json["error_code"]);
    }

    [Fact]
    public async Task Push_PauseGate_Returns409_SyncPaused_AndAuditLogged()
    {
        // Arrange — mock sync as paused for the project
        _cloudMock.Setup(s => s.IsProjectSyncEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _cloudMock.Setup(s => s.InsertAuditEntryAsync(It.IsAny<AuditEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var body = new
        {
            entries = new[]
            {
                new { project = "paused-proj", entity = "session", entity_key = "s1", op = "upsert", payload = "{}" }
            }
        };

        // Act
        var resp = await _client.PostAsJsonAsync("/sync/mutations/push", body, JsonOpts);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);
        Assert.Equal("sync-paused", (string?)json["error_code"]);

        // Verify audit log was written
        _cloudMock.Verify(
            v => v.InsertAuditEntryAsync(
                It.Is<AuditEntry>(a => a.Action == "push" && a.Outcome == "rejected" && a.ReasonCode == "sync-paused"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Push_Success_Returns200_WithAcceptedSeqs_AndProjectEnvelope()
    {
        // Arrange
        _cloudMock.Setup(s => s.IsProjectSyncEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _cloudMock.Setup(s => s.InsertMutationBatchAsync(
                It.IsAny<IReadOnlyList<Engram.Store.MutationEntry>>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<long> { 1, 2, 3 });

        var body = new
        {
            entries = new[]
            {
                new { project = "test-proj", entity = "session", entity_key = "s1", op = "upsert", payload = "{}" },
                new { project = "test-proj", entity = "observation", entity_key = "o1", op = "upsert", payload = """{"title":"test"}""" }
            }
        };

        // Act
        var resp = await _client.PostAsJsonAsync("/sync/mutations/push", body, JsonOpts);

        // Assert
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);

        // Check accepted_seqs
        var seqs = json["accepted_seqs"]?.AsArray();
        Assert.NotNull(seqs);
        Assert.Equal(3, seqs.Count);
        Assert.Equal(1, (long)seqs[0]!);
        Assert.Equal(2, (long)seqs[1]!);
        Assert.Equal(3, (long)seqs[2]!);

        // Check project envelope
        Assert.Equal("test-proj", (string?)json["project"]);
        Assert.Equal("request_body", (string?)json["project_source"]);

        // Verify batch was inserted
        _cloudMock.Verify(
            v => v.InsertMutationBatchAsync(
                It.Is<IReadOnlyList<Engram.Store.MutationEntry>>(list => list.Count == 2),
                null,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Task 1.5.3: Pull cursor + enrollment filter
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Pull_WithoutSinceSeq_DefaultsToZero()
    {
        // Arrange
        _cloudMock.Setup(s => s.ListMutationsSinceAsync(
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<StoredMutation>(), false, 0L));

        // Act
        var resp = await _client.GetAsync("/sync/mutations/pull");

        // Assert
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Verify since_seq defaulted to 0
        _cloudMock.Verify(
            v => v.ListMutationsSinceAsync(0, 100, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Pull_WithLimitOver100_CappedAt100()
    {
        // Arrange
        _cloudMock.Setup(s => s.ListMutationsSinceAsync(
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<StoredMutation>(), false, 0L));

        // Act — request limit=200, should be capped to 100
        var resp = await _client.GetAsync("/sync/mutations/pull?since_seq=0&limit=200");

        // Assert
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Verify limit was capped at 100
        _cloudMock.Verify(
            v => v.ListMutationsSinceAsync(0, 100, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Pull_ReturnsHasMore_WhenMoreMutationsAvailable()
    {
        // Arrange
        _cloudMock.Setup(s => s.ListMutationsSinceAsync(
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                new List<StoredMutation>
                {
                    new(1, "proj", "session", "s1", "upsert", "{}", "2025-01-01T00:00:00Z"),
                    new(2, "proj", "observation", "o1", "upsert", "{}", "2025-01-01T00:01:00Z"),
                },
                true,    // hasMore
                2L));    // latestSeq

        // Act
        var resp = await _client.GetAsync("/sync/mutations/pull?since_seq=0&limit=2");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);

        // Assert
        Assert.True((bool)json["has_more"]!);
        Assert.Equal(2, (long)json["latest_seq"]!);

        var mutations = json["mutations"]?.AsArray();
        Assert.NotNull(mutations);
        Assert.Equal(2, mutations.Count);
        Assert.Equal(1, (long)mutations[0]!["seq"]!);
        Assert.Equal(2, (long)mutations[1]!["seq"]!);
    }

    [Fact]
    public async Task Pull_WithProjectFilter_ReturnsScopedMutations()
    {
        // Arrange
        _cloudMock.Setup(s => s.ListMutationsSinceAsync(
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.Is<List<string>?>(allowed => allowed != null && allowed.Count == 1 && allowed[0] == "filtered-proj"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                new List<StoredMutation>
                {
                    new(10, "filtered-proj", "session", "s1", "upsert", "{}", "2025-01-01T00:00:00Z"),
                },
                false,
                10L));

        // Act — pull with project filter
        var resp = await _client.GetAsync("/sync/mutations/pull?since_seq=0&limit=100&project=filtered-proj");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);

        // Assert
        var mutations = json["mutations"]?.AsArray();
        Assert.NotNull(mutations);
        Assert.Single(mutations);
        Assert.Equal("filtered-proj", (string)mutations[0]!["project"]!);
    }

    [Fact]
    public async Task Pull_ReturnsProjectEnvelope()
    {
        // Arrange
        _cloudMock.Setup(s => s.ListMutationsSinceAsync(
                It.IsAny<long>(),
                It.IsAny<int>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<StoredMutation>(), false, 0L));

        // Act — pull without project filter
        var resp = await _client.GetAsync("/sync/mutations/pull?since_seq=0");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var json = await resp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(json);

        // Assert project envelope fields exist
        Assert.NotNull(json["project"]);
        Assert.NotNull(json["project_source"]);
        Assert.NotNull(json["project_path"]);
    }
}
