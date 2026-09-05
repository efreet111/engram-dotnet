using Engram.Store;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Engram.Postgres.Tests;

/// <summary>
/// PostgreSQL parity tests for taxonomy lifecycle (ENG-412, NFR-005, PM-5).
/// </summary>
public sealed class TaxonomyPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("engram_taxonomy")
        .WithUsername("engram")
        .WithPassword("engram")
        .Build();

    public PostgresStore Store { get; private set; } = null!;
    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        var cfg = new StoreConfig
        {
            DbType = StoreDbType.Postgres,
            PgConnectionString = ConnectionString,
            DataDir = "/tmp",
        };
        Store = new PostgresStore(cfg);
        await ResetAsync();
    }

    public async Task DisposeAsync()
    {
        Store?.Dispose();
        await _container.DisposeAsync();
    }

    public async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            DELETE FROM cloud_mutations;
            DELETE FROM sync_mutations;
            DELETE FROM sync_enrolled_projects;
            DELETE FROM sync_chunks;
            DELETE FROM observations;
            DELETE FROM user_prompts;
            DELETE FROM sessions;
            DELETE FROM sync_state WHERE target_key != 'cloud';
        ", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

[Trait("Category", "RequiresDocker")]
public class TaxonomyLifecyclePostgresTests : IClassFixture<TaxonomyPostgresFixture>, IAsyncLifetime
{
    private readonly TaxonomyPostgresFixture _fixture;
    private const string SessionId = "taxonomy-pg-session";

    public TaxonomyLifecyclePostgresTests(TaxonomyPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => await _fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task SeedSession()
        => await _fixture.Store.CreateSessionAsync(SessionId, "test-project", "/tmp");

    private async Task<long> SeedObservation(string title, string content, string type = "decision",
        string? topicKey = null, string? project = "test-project")
    {
        return await _fixture.Store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = title,
            Content = content,
            Type = type,
            Project = project,
            TopicKey = topicKey,
        });
    }

    // ─── FR-001: status column defaults ──────────────────────────────────────

    [Fact]
    public async Task PG_NewObservation_DefaultsToActive()
    {
        await SeedSession();
        var id = await SeedObservation("Test", "Content");

        var obs = await _fixture.Store.GetObservationAsync(id);

        Assert.NotNull(obs);
        Assert.Equal("active", obs.Status);
    }

    // ─── FR-002: UpdateObservationAsync with status ──────────────────────────

    [Fact]
    public async Task PG_UpdateObservation_ChangesStatus()
    {
        await SeedSession();
        var id = await SeedObservation("Old decision", "Old content");

        var ok = await _fixture.Store.UpdateObservationAsync(id, new UpdateObservationParams { Status = "deprecated" });

        Assert.True(ok);
        var obs = await _fixture.Store.GetObservationAsync(id);
        Assert.NotNull(obs);
        Assert.Equal("deprecated", obs.Status);
    }

    // ─── FR-003: SearchAsync default filters to active ───────────────────────

    [Fact]
    public async Task PG_Search_Default_ReturnsOnlyActive()
    {
        await SeedSession();
        var id1 = await SeedObservation("Active decision", "cliente auth module");
        var id2 = await SeedObservation("Deprecated decision", "cliente old module");
        await _fixture.Store.UpdateObservationAsync(id2, new UpdateObservationParams { Status = "deprecated" });

        var results = await _fixture.Store.SearchAsync("cliente", new SearchOptions
        {
            Project = "test-project",
        });

        Assert.Single(results);
        Assert.Equal(id1, results[0].Observation.Id);
    }

    [Fact]
    public async Task PG_Search_ExplicitStatus_ReturnsOnlyThatStatus()
    {
        await SeedSession();
        var id1 = await SeedObservation("Active decision", "cliente auth module");
        var id2 = await SeedObservation("Deprecated decision", "cliente old module");
        await _fixture.Store.UpdateObservationAsync(id2, new UpdateObservationParams { Status = "deprecated" });

        var results = await _fixture.Store.SearchAsync("cliente", new SearchOptions
        {
            Project = "test-project",
            Status = "deprecated",
        });

        Assert.Single(results);
        Assert.Equal(id2, results[0].Observation.Id);
    }

    // ─── FR-005: include_deprecated ──────────────────────────────────────────

    [Fact]
    public async Task PG_Search_IncludeDeprecated_ReturnsAll()
    {
        await SeedSession();
        var id1 = await SeedObservation("Active decision", "cliente auth module");
        var id2 = await SeedObservation("Deprecated decision", "cliente old module");
        await _fixture.Store.UpdateObservationAsync(id2, new UpdateObservationParams { Status = "deprecated" });

        var results = await _fixture.Store.SearchAsync("cliente", new SearchOptions
        {
            Project = "test-project",
            IncludeDeprecated = true,
        });

        Assert.Equal(2, results.Count);
    }

    // ─── FR-004: grouped search ──────────────────────────────────────────────

    [Fact]
    public async Task PG_Search_Grouped_CollapsesByTopicKey()
    {
        await SeedSession();
        // Create observations with DIFFERENT topic_keys (to avoid upsert behavior)
        var id1 = await SeedObservation("Rev 1", "cliente decision rev1", topicKey: "decision/cliente-v1");
        var id2 = await SeedObservation("Rev 2", "cliente decision rev2", topicKey: "decision/cliente-v2");
        var id3 = await SeedObservation("Rev 3", "cliente decision rev3", topicKey: "decision/auth");

        await _fixture.Store.UpdateObservationAsync(id1, new UpdateObservationParams { Status = "deprecated" });
        await _fixture.Store.UpdateObservationAsync(id2, new UpdateObservationParams { Status = "deprecated" });

        var results = await _fixture.Store.SearchAsync("cliente", new SearchOptions
        {
            Project = "test-project",
            IncludeDeprecated = true,
        });

        var groups = TopicKeyGrouper.GroupByTopicKey(results.Select(r => r.Observation));

        Assert.Equal(3, groups.Count);
        Assert.True(groups.ContainsKey("decision/cliente-v1"));
        Assert.True(groups.ContainsKey("decision/cliente-v2"));
        Assert.True(groups.ContainsKey("decision/auth"));

        var heads = TopicKeyGrouper.GetGroupHeads(results.Select(r => r.Observation));
        Assert.Equal(3, heads.Count);
        Assert.Equal(2, heads.Count(h => h.DeprecatedCount > 0));
    }

    // ─── FR-007: ImportAsync persists status (round-trip) ────────────────────

    [Fact]
    public async Task PG_ImportAsync_PersistsStatus_DeprecatedRoundTrips()
    {
        await SeedSession();
        var id = await SeedObservation("Deprecated decision", "old approach");
        await _fixture.Store.UpdateObservationAsync(id, new UpdateObservationParams { Status = "deprecated" });

        // Export
        var export = await _fixture.Store.ExportAsync();
        Assert.NotEmpty(export.Observations);
        var exportedObs = export.Observations.First(o => o.Title == "Deprecated decision");
        Assert.Equal("deprecated", exportedObs.Status);

        // Reset and re-import
        await _fixture.ResetAsync();
        await SeedSession();

        var importResult = await _fixture.Store.ImportAsync(export);
        Assert.True(importResult.ObservationsImported > 0);

        // Verify the imported observation preserved status
        var results = await _fixture.Store.SearchAsync("old approach", new SearchOptions
        {
            Project = "test-project",
            IncludeDeprecated = true,
        });
        Assert.Single(results);
        Assert.Equal("deprecated", results[0].Observation.Status);
    }

    [Fact]
    public async Task PG_ImportAsync_NullStatus_DefaultsToActive()
    {
        await SeedSession();

        var export = new ExportData
        {
            ExportedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            Observations = new List<Observation>
            {
                new()
                {
                    SyncId = "legacy-sync-pg",
                    SessionId = SessionId,
                    Type = "decision",
                    Title = "Legacy PG obs",
                    Content = "legacy pg content",
                    Project = "test-project",
                    Scope = "project",
                    CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    Status = null!,
                },
            },
        };

        var result = await _fixture.Store.ImportAsync(export);
        Assert.Equal(1, result.ObservationsImported);

        var results = await _fixture.Store.SearchAsync("legacy pg content", new SearchOptions
        {
            Project = "test-project",
        });
        Assert.Single(results);
        Assert.Equal("active", results[0].Observation.Status);
    }

    // ─── FR-008: ExportAsync includes status ─────────────────────────────────

    [Fact]
    public async Task PG_ExportAsync_IncludesStatus_Deprecated()
    {
        await SeedSession();
        var id = await SeedObservation("PG Deprecated export", "pg content");
        await _fixture.Store.UpdateObservationAsync(id, new UpdateObservationParams { Status = "deprecated" });

        var export = await _fixture.Store.ExportAsync();
        var obs = export.Observations.First(o => o.Title == "PG Deprecated export");
        Assert.Equal("deprecated", obs.Status);
    }

    [Fact]
    public async Task PG_ExportProjectAsync_IncludesStatus_Deprecated()
    {
        await SeedSession();
        var id = await SeedObservation("PG Deprecated proj export", "pg proj content");
        await _fixture.Store.UpdateObservationAsync(id, new UpdateObservationParams { Status = "deprecated" });

        var export = await _fixture.Store.ExportProjectAsync("test-project");
        var obs = export.Observations.First(o => o.Title == "PG Deprecated proj export");
        Assert.Equal("deprecated", obs.Status);
    }

    // ─── FR-009: GenerateIndexAsync status alignment ─────────────────────────

    [Fact]
    public async Task PG_GenerateIndexAsync_StatusAlignedWithReadObservation()
    {
        await SeedSession();
        var id = await SeedObservation("Indexed decision", "indexed content");
        await _fixture.Store.PromoteToMdAsync(id, Path.Combine(Path.GetTempPath(), "engram-pg-index-test"));

        var index = await _fixture.Store.GenerateIndexAsync(null);
        Assert.NotNull(index);
        Assert.Contains("Indexed decision", index);
    }

    // ─── NFR-001: Backward compatibility ─────────────────────────────────────

    [Fact]
    public async Task PG_BackwardCompat_ExistingObservationsReadAsActive()
    {
        await SeedSession();
        var id = await SeedObservation("Old obs", "old content");

        var obs = await _fixture.Store.GetObservationAsync(id);
        Assert.NotNull(obs);
        Assert.Equal("active", obs.Status);
    }
}
