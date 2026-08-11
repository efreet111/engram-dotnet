using System;
using Engram.Store;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Engram.Postgres.Tests;

/// <summary>
/// Tests for HU-013 sync behavior management in PostgresStore.
/// Validates enrollment with behavior, behavior retrieval,
/// and listing enrolled projects with behavior metadata.
/// Requires Docker for Testcontainers PostgreSQL.
/// </summary>
public sealed class SyncBehaviorPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("engram")
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
            DELETE FROM sync_mutations;
            DELETE FROM sync_enrolled_projects;
            DELETE FROM sync_state WHERE target_key != 'cloud';
        ", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

[Trait("Category", "RequiresDocker")]
public class SyncBehaviorPostgresTests : IClassFixture<SyncBehaviorPostgresFixture>
{
    private readonly SyncBehaviorPostgresFixture _fixture;

    public SyncBehaviorPostgresTests(SyncBehaviorPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private PostgresStore Store => _fixture.Store;

    // ─── Enroll with behavior ──────────────────────────────────────────────────

    /// <summary>
    /// Enrolling a project with explicit behavior stores it correctly in Postgres.
    /// </summary>
    [Fact(Skip = "Requires Docker")]
    public async Task EnrollProject_StoresBehavior()
    {
        // Enroll with explicit silent-skip behavior
        var result = await Store.EnrollProjectAsync(
            "test-project", "test-user", "silent-skip", excludedServers: null);

        Assert.Equal("enrolled", result.Status);
        Assert.Equal("silent-skip", result.Behavior);

        // Verify behavior persisted in DB
        var storedBehavior = await Store.GetProjectBehaviorAsync("test-project");
        Assert.Equal("silent-skip", storedBehavior);
    }

    /// <summary>
    /// Enrolling with fail-loud behavior stores correctly.
    /// </summary>
    [Fact(Skip = "Requires Docker")]
    public async Task EnrollProject_FailLoudBehavior_StoresCorrectly()
    {
        var result = await Store.EnrollProjectAsync(
            "loud-project", "test-user", "fail-loud", excludedServers: null);

        Assert.Equal("enrolled", result.Status);
        Assert.Equal("fail-loud", result.Behavior);

        var storedBehavior = await Store.GetProjectBehaviorAsync("loud-project");
        Assert.Equal("fail-loud", storedBehavior);
    }

    /// <summary>
    /// Enrolling with both behavior and excluded_servers stores all fields.
    /// </summary>
    [Fact(Skip = "Requires Docker")]
    public async Task EnrollProject_WithExcludedServers_StoresAllFields()
    {
        var result = await Store.EnrollProjectAsync(
            "excl-project", "test-user", "silent-skip", excludedServers: "[\"server-a\",\"server-b\"]");

        Assert.Equal("enrolled", result.Status);
        Assert.Equal("silent-skip", result.Behavior);

        // Verify via ListEnrolledProjectsWithBehaviorAsync that excluded_servers is stored
        var projects = await Store.ListEnrolledProjectsWithBehaviorAsync();
        var enrolled = projects.FirstOrDefault(p => p.Project == "excl-project");
        Assert.NotNull(enrolled);
        Assert.Equal("silent-skip", enrolled.Behavior);
        Assert.Contains("server-a", enrolled.ExcludedServers);
        Assert.Contains("server-b", enrolled.ExcludedServers);
    }

    /// <summary>
    /// Re-enrolling a project updates the behavior (UPSERT via ON CONFLICT DO UPDATE).
    /// </summary>
    [Fact(Skip = "Requires Docker")]
    public async Task EnrollProject_ReEnrollment_UpdatesBehavior()
    {
        // Initial enrollment: fail-loud
        await Store.EnrollProjectAsync("re-enroll", "test-user", "fail-loud", excludedServers: null);
        Assert.Equal("fail-loud", await Store.GetProjectBehaviorAsync("re-enroll"));

        // Re-enroll: change to silent-skip
        var result = await Store.EnrollProjectAsync(
            "re-enroll", "test-user", "silent-skip", excludedServers: null);
        Assert.Equal("enrolled", result.Status);
        Assert.Equal("silent-skip", result.Behavior);

        var storedBehavior = await Store.GetProjectBehaviorAsync("re-enroll");
        Assert.Equal("silent-skip", storedBehavior);
    }

    // ─── Get project behavior ──────────────────────────────────────────────────

    /// <summary>
    /// GetProjectBehaviorAsync returns the stored behavior for an enrolled project.
    /// </summary>
    [Fact(Skip = "Requires Docker")]
    public async Task GetProjectBehavior_ReturnsStoredBehavior()
    {
        await Store.EnrollProjectAsync("behavior-proj", "test-user", "silent-skip", excludedServers: null);

        var behavior = await Store.GetProjectBehaviorAsync("behavior-proj");

        Assert.Equal("silent-skip", behavior);
    }

    /// <summary>
    /// GetProjectBehaviorAsync returns null for a non-enrolled project.
    /// </summary>
    [Fact(Skip = "Requires Docker")]
    public async Task GetProjectBehavior_ReturnsNull_ForUnenrolled()
    {
        var behavior = await Store.GetProjectBehaviorAsync("nonexistent-project");

        Assert.Null(behavior);
    }

    /// <summary>
    /// GetProjectBehaviorAsync returns fail-loud for projects enrolled without behavior.
    /// </summary>
    [Fact(Skip = "Requires Docker")]
    public async Task GetProjectBehavior_EnrolledWithoutBehavior_ReturnsFailLoud()
    {
        // Use the old overload without behavior parameter
        await Store.EnrollProjectAsync("old-enrollment", "test-user");

        var behavior = await Store.GetProjectBehaviorAsync("old-enrollment");

        // COALESCE(behavior, 'fail-loud') in the schema
        Assert.Equal("fail-loud", behavior);
    }

    // ─── List enrolled projects with behavior ──────────────────────────────────

    /// <summary>
    /// ListEnrolledProjectsWithBehaviorAsync returns all enrolled projects
    /// with their behavior metadata.
    /// </summary>
    [Fact(Skip = "Requires Docker")]
    public async Task ListEnrolledProjectsWithBehavior_ReturnsAll()
    {
        await Store.EnrollProjectAsync("list-proj-a", "user-1", "fail-loud", excludedServers: null);
        await Store.EnrollProjectAsync("list-proj-b", "user-1", "silent-skip", excludedServers: "[\"srv-x\"]");
        await Store.EnrollProjectAsync("list-proj-c", "user-2", "silent-skip", excludedServers: null);

        // List all (no user filter)
        var allEnrolled = await Store.ListEnrolledProjectsWithBehaviorAsync();
        Assert.True(allEnrolled.Count >= 3);

        var projA = allEnrolled.First(p => p.Project == "list-proj-a");
        Assert.Equal("fail-loud", projA.Behavior);
        Assert.Null(projA.ExcludedServers);

        var projB = allEnrolled.First(p => p.Project == "list-proj-b");
        Assert.Equal("silent-skip", projB.Behavior);
        Assert.Contains("srv-x", projB.ExcludedServers);

        var projC = allEnrolled.First(p => p.Project == "list-proj-c");
        Assert.Equal("silent-skip", projC.Behavior);

        // List filtered by user
        var user1Enrolled = await Store.ListEnrolledProjectsWithBehaviorAsync("user-1");
        Assert.Equal(2, user1Enrolled.Count);
        Assert.Contains(user1Enrolled, p => p.Project == "list-proj-a");
        Assert.Contains(user1Enrolled, p => p.Project == "list-proj-b");
    }

    /// <summary>
    /// ListEnrolledProjectsWithBehaviorAsync returns empty list when no projects enrolled.
    /// </summary>
    [Fact(Skip = "Requires Docker")]
    public async Task ListEnrolledProjectsWithBehavior_ReturnsEmpty_WhenNone()
    {
        var projects = await Store.ListEnrolledProjectsWithBehaviorAsync();

        Assert.NotNull(projects);
        Assert.Empty(projects);
    }

    // ─── Behavior column migration ──────────────────────────────────────────

    /// <summary>
    /// Verify the behavior column exists in the sync_enrolled_projects table
    /// after PostgresStore schema initialization.
    /// </summary>
    [Fact(Skip = "Requires Docker")]
    public async Task BehaviorColumn_ExistsInPostgresSchema()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT column_name
            FROM information_schema.columns
            WHERE table_name = 'sync_enrolled_projects'
            AND column_name = 'behavior'
        ", conn);
        var result = await cmd.ExecuteScalarAsync();

        Assert.NotNull(result);
        Assert.Equal("behavior", result.ToString());
    }

    /// <summary>
    /// Verify the excluded_servers column exists in the sync_enrolled_projects table.
    /// </summary>
    [Fact(Skip = "Requires Docker")]
    public async Task ExcludedServersColumn_ExistsInPostgresSchema()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT column_name
            FROM information_schema.columns
            WHERE table_name = 'sync_enrolled_projects'
            AND column_name = 'excluded_servers'
        ", conn);
        var result = await cmd.ExecuteScalarAsync();

        Assert.NotNull(result);
        Assert.Equal("excluded_servers", result.ToString());
    }
}
