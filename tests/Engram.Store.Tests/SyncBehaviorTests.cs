using Engram.Store;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Tests for HU-013 multi-project sync behavior management (ENG-514).
/// Covers: behavior column migration, silent-skip filtering in count queries,
/// enrollment with behavior, and project behavior retrieval.
/// </summary>
public class SyncBehaviorTests : IDisposable
{
    private readonly SqliteStore _store;
    private readonly string _tempDir;
    private const string TargetKey = "cloud";

    public SyncBehaviorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-sync-behavior-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var cfg = new StoreConfig { DataDir = _tempDir };
        _store = new SqliteStore(cfg);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private string DbPath => Path.Combine(_tempDir, "engram.db");

    // ─── Phase 1: Schema migration ───────────────────────────────────────────

    [Fact]
    public async Task BehaviorColumn_Exists_AfterSchemaInit()
    {
        // Verify the behavior column exists after store initialization
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(sync_enrolled_projects)";
        using var r = cmd.ExecuteReader();

        var columns = new List<string>();
        while (r.Read())
        {
            columns.Add(r.GetString(1)); // col 1 = name
        }

        Assert.Contains("behavior", columns);
    }

    [Fact]
    public async Task BehaviorColumn_HasFailLoudDefault()
    {
        // Enroll without specifying behavior → should default to 'fail-loud'
        await _store.EnrollProjectLocalAsync("test-project");
        var behavior = await _store.GetProjectBehaviorAsync("test-project");
        Assert.Equal("fail-loud", behavior);
    }

    // ─── Phase 2: Enrollment with behavior ────────────────────────────────────

    [Fact]
    public async Task EnrollProjectLocal_StoresBehavior()
    {
        await _store.EnrollProjectLocalAsync("proj-skip", "silent-skip");
        var behavior = await _store.GetProjectBehaviorAsync("proj-skip");
        Assert.Equal("silent-skip", behavior);
    }

    [Fact]
    public async Task EnrollProjectLocal_DefaultBehavior_IsFailLoud()
    {
        await _store.EnrollProjectLocalAsync("proj-default");
        var behavior = await _store.GetProjectBehaviorAsync("proj-default");
        Assert.Equal("fail-loud", behavior);
    }

    [Fact]
    public async Task EnrollProjectLocal_NullBehavior_DefaultsToFailLoud()
    {
        await _store.EnrollProjectLocalAsync("proj-null", null);
        var behavior = await _store.GetProjectBehaviorAsync("proj-null");
        Assert.Equal("fail-loud", behavior);
    }

    [Fact]
    public async Task EnrollProjectLocal_UpdatesBehavior_OnReEnroll()
    {
        await _store.EnrollProjectLocalAsync("proj-change", "fail-loud");
        await _store.EnrollProjectLocalAsync("proj-change", "silent-skip");
        var behavior = await _store.GetProjectBehaviorAsync("proj-change");
        Assert.Equal("silent-skip", behavior);
    }

    [Fact]
    public async Task GetEnrolledProjectsLocal_ReturnsAllWithBehavior()
    {
        await _store.EnrollProjectLocalAsync("proj-a", "fail-loud");
        await _store.EnrollProjectLocalAsync("proj-b", "silent-skip");

        var enrolled = await _store.GetEnrolledProjectsLocalAsync();
        Assert.Equal(2, enrolled.Count);

        var a = enrolled.First(e => e.Project == "proj-a");
        Assert.Equal("fail-loud", a.Behavior);

        var b = enrolled.First(e => e.Project == "proj-b");
        Assert.Equal("silent-skip", b.Behavior);
    }

    // ─── Phase 2: CountPendingNonEnrolled filters silent-skip ─────────────────

    [Fact]
    public async Task CountPendingNonEnrolled_IncludesFailLoudProjects()
    {
        // Enroll proj-a with fail-loud, add pending mutation → should BLOCK
        await _store.EnrollProjectLocalAsync("proj-a", "fail-loud");
        SeedMutation("proj-a");

        var nonEnrolled = await _store.CountPendingNonEnrolledAsync(TargetKey);
        Assert.Single(nonEnrolled);
        Assert.Equal("proj-a", nonEnrolled[0].Project);
        Assert.Equal(1, nonEnrolled[0].Count);
    }

    [Fact]
    public async Task CountPendingNonEnrolled_ExcludesSilentSkipProjects()
    {
        // Enroll proj-skip with silent-skip, add pending mutation → should NOT block
        await _store.EnrollProjectLocalAsync("proj-skip", "silent-skip");
        SeedMutation("proj-skip");

        var nonEnrolled = await _store.CountPendingNonEnrolledAsync(TargetKey);
        Assert.Empty(nonEnrolled);
    }

    [Fact]
    public async Task CountPendingNonEnrolled_ReturnsUnenrolledProjects()
    {
        // No enrollment, add pending mutation → should block
        SeedMutation("proj-unknown");

        var nonEnrolled = await _store.CountPendingNonEnrolledAsync(TargetKey);
        Assert.Single(nonEnrolled);
        Assert.Equal("proj-unknown", nonEnrolled[0].Project);
    }

    [Fact]
    public async Task CountPendingNonEnrolled_MixedBehaviors_CorrectFiltering()
    {
        await _store.EnrollProjectLocalAsync("proj-fail", "fail-loud");
        await _store.EnrollProjectLocalAsync("proj-skip", "silent-skip");
        // proj-unknown is not enrolled at all

        SeedMutation("proj-fail");
        SeedMutation("proj-skip");
        SeedMutation("proj-unknown");

        var nonEnrolled = await _store.CountPendingNonEnrolledAsync(TargetKey);
        Assert.Equal(2, nonEnrolled.Count);

        var projects = nonEnrolled.Select(p => p.Project).ToHashSet();
        Assert.Contains("proj-fail", projects);
        Assert.Contains("proj-unknown", projects);
        Assert.DoesNotContain("proj-skip", projects);
    }

    // ─── GetProjectBehavior helpers ───────────────────────────────────────────

    [Fact]
    public async Task GetProjectBehavior_ReturnsNull_ForUnenrolled()
    {
        var behavior = await _store.GetProjectBehaviorAsync("nonexistent");
        Assert.Null(behavior);
    }

    [Fact]
    public async Task GetProjectBehavior_ReturnsCorrectValue()
    {
        await _store.EnrollProjectLocalAsync("proj-test", "silent-skip");
        var behavior = await _store.GetProjectBehaviorAsync("proj-test");
        Assert.Equal("silent-skip", behavior);
    }

    // ─── ListDistinctProjectsWithPendingMutations ─────────────────────────────

    [Fact]
    public async Task ListDistinctProjects_ReturnsProjectsWithPendingMutations()
    {
        SeedMutation("proj-a");
        SeedMutation("proj-a", entityKey: "test-2");
        SeedMutation("proj-b");

        var projects = await _store.ListDistinctProjectsWithPendingMutationsAsync(TargetKey);
        Assert.Equal(2, projects.Count);
        Assert.Contains("proj-a", projects);
        Assert.Contains("proj-b", projects);
    }

    [Fact]
    public async Task ListDistinctProjects_ExcludesEmptyProject()
    {
        // Seed a mutation with empty project (should be excluded)
        ExecuteSql(@"
            INSERT INTO sync_mutations (target_key, entity, entity_key, op, payload, source, project, occurred_at)
            VALUES ('cloud', 'observation', 'empty-test', 'create', '{}', 'local', '', datetime('now'))");

        var projects = await _store.ListDistinctProjectsWithPendingMutationsAsync(TargetKey);
        Assert.DoesNotContain("", projects);
    }

    [Fact]
    public async Task ListDistinctProjects_ExcludesAckedMutations()
    {
        // Seed a mutation and immediately ack it
        SeedMutation("proj-acked");
        ExecuteSql("UPDATE sync_mutations SET acked_at = datetime('now') WHERE project = 'proj-acked'");

        var projects = await _store.ListDistinctProjectsWithPendingMutationsAsync(TargetKey);
        Assert.DoesNotContain("proj-acked", projects);
    }

    // ─── Raw SQL helpers ─────────────────────────────────────────────────────

    private void SeedMutation(string project, string entity = "observation", string entityKey = "test-1")
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var txn = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = txn;
        cmd.CommandText = @"
            INSERT INTO sync_mutations (target_key, entity, entity_key, op, payload, source, project, occurred_at)
            VALUES (@target, @entity, @entityKey, 'create', '{}', 'local', @project, datetime('now'))";
        cmd.Parameters.AddWithValue("@target", TargetKey);
        cmd.Parameters.AddWithValue("@entity", entity);
        cmd.Parameters.AddWithValue("@entityKey", entityKey);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.ExecuteNonQuery();
        txn.Commit();
    }

    private void ExecuteSql(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
