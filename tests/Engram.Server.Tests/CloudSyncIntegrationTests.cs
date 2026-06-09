using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Engram.Server;
using Engram.Store;
using Microsoft.AspNetCore.Builder;
using Testcontainers.PostgreSql;
using Xunit;

namespace Engram.Server.Tests;

/// <summary>
/// Integration tests for CloudSyncEndpoints — full roundtrip with PostgresStore.
/// Uses Testcontainers.PostgreSql for a real PostgreSQL instance.
/// </summary>
public sealed class CloudSyncPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("engram")
        .WithUsername("engram")
        .WithPassword("engram")
        .Build();

    public PostgresStore Store { get; private set; } = null!;
    public WebApplication App { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;
    public string BaseUrl { get; private set; } = "";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var connStr = _container.GetConnectionString();

        var cfg = new StoreConfig
        {
            DbType = StoreDbType.Postgres,
            PgConnectionString = connStr,
            DataDir = "/tmp",
        };

        Store = new PostgresStore(cfg);

        var port = GetFreePort();
        BaseUrl = $"http://localhost:{port}";

        App = EngramServer.Build(Store, cfg);
        App.Urls.Clear();
        App.Urls.Add(BaseUrl);
        await App.StartAsync();

        Client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync();
        await App.DisposeAsync();
        Store.Dispose();
        await _container.DisposeAsync();
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// Seed a test project as sync-enabled so push passes the pause gate.
    /// </summary>
    public async Task EnableProjectSync(string project)
    {
        await using var conn = new Npgsql.NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = new Npgsql.NpgsqlCommand(
            "INSERT INTO cloud_project_controls (project, sync_enabled) VALUES (@p, true) " +
            "ON CONFLICT (project) DO UPDATE SET sync_enabled = true",
            conn);
        cmd.Parameters.AddWithValue("p", project);
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Integration tests: Push → cloud_mutations → Pull roundtrip using real PostgreSQL.
/// </summary>
[Collection("CloudSyncPostgres")]
[Trait("Category", "RequiresDocker")]
public class CloudSyncIntegrationTests : IClassFixture<CloudSyncPostgresFixture>
{
    private readonly CloudSyncPostgresFixture _fixture;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public CloudSyncIntegrationTests(CloudSyncPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PushToPullRoundtrip_WithPostgresStore()
    {
        // Arrange
        const string project = "int-test-proj";

        // Enable sync for the project so push doesn't get 409
        await _fixture.EnableProjectSync(project);

        // Seed a session so FK constraints are satisfied for observation mutations
        await _fixture.Store.CreateSessionAsync("int-session", project, "/tmp");

        // Create push payload with various entity types
        var pushBody = new
        {
            entries = new[]
            {
                new
                {
                    project,
                    entity = "session",
                    entity_key = "int-session",
                    op = "upsert",
                    payload = """{"directory":"/tmp","project":"int-test-proj"}"""
                },
                new
                {
                    project,
                    entity = "observation",
                    entity_key = "obs-1",
                    op = "upsert",
                    payload = """{"sessionId":"int-session","title":"Integration test obs","content":"Roundtrip test"}"""
                },
            }
        };

        // Act — Push
        var pushResp = await _fixture.Client.PostAsJsonAsync("/sync/mutations/push", pushBody, JsonOpts);
        Assert.Equal(HttpStatusCode.OK, pushResp.StatusCode);

        var pushJson = await pushResp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(pushJson);

        var acceptedSeqs = pushJson["accepted_seqs"]?.AsArray();
        Assert.NotNull(acceptedSeqs);
        Assert.Equal(2, acceptedSeqs.Count);
        Assert.True((long)acceptedSeqs[0]! > 0);
        Assert.True((long)acceptedSeqs[1]! > 0);

        Assert.Equal(project, (string?)pushJson["project"]);

        // Act — Pull with since_seq=0
        var pullResp = await _fixture.Client.GetAsync($"/sync/mutations/pull?since_seq=0&limit=100&project={project}");
        Assert.Equal(HttpStatusCode.OK, pullResp.StatusCode);

        var pullJson = await pullResp.Content.ReadFromJsonAsync<JsonObject>(JsonOpts);
        Assert.NotNull(pullJson);

        // Assert — pulled mutations match what was pushed
        var mutations = pullJson["mutations"]?.AsArray();
        Assert.NotNull(mutations);
        Assert.Equal(2, mutations.Count);

        // Verify first mutation
        var first = mutations[0]!;
        Assert.Equal((long)acceptedSeqs[0]!, (long)first["seq"]!);
        Assert.Equal(project, (string)first["project"]!);
        Assert.Equal("session", (string)first["entity"]!);
        Assert.Equal("int-session", (string)first["entity_key"]!);
        Assert.Equal("upsert", (string)first["op"]!);

        // Verify second mutation
        var second = mutations[1]!;
        Assert.Equal((long)acceptedSeqs[1]!, (long)second["seq"]!);
        Assert.Equal(project, (string)second["project"]!);
        Assert.Equal("observation", (string)second["entity"]!);
        Assert.Equal("obs-1", (string)second["entity_key"]!);
        Assert.Equal("upsert", (string)second["op"]!);

        // Verify no more data
        Assert.False((bool)pullJson["has_more"]!);
        Assert.Equal((long)acceptedSeqs[1]!, (long)pullJson["latest_seq"]!);
    }
}
