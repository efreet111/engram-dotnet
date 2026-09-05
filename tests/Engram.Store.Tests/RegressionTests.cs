using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Regression tests for ENG-412 to verify that existing functionality
/// is not broken by the addition of the status field and new features.
/// Covers RT-002 through RT-013 from regression-test-plan.md.
///
/// Note: RT-001, RT-007, RT-025 (testing NULL status) are not possible because
/// the status column has NOT NULL constraint with DEFAULT 'active'. All observations
/// automatically get status='active' on creation, so legacy data simulation is not needed.
/// </summary>
public class RegressionTests : IDisposable
{
    private readonly SqliteStore _store;
    private readonly string _tempDir;
    private const string SessionId = "regression-test-session";

    public RegressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "engram-regression-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var cfg = new StoreConfig { DataDir = _tempDir };
        _store = new SqliteStore(cfg);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private async Task SeedSession()
        => await _store.CreateSessionAsync(SessionId, "test-project", "/tmp");

    private async Task<long> SeedObservation(string title, string content, string type = "decision",
        string? topicKey = null, string? project = "test-project", string? scope = "team")
    {
        return await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = title,
            Content = content,
            Type = type,
            Project = project,
            TopicKey = topicKey,
            Scope = scope,
        });
    }

    // ─── RT-002: Backward Compatibility ────────────────────────────────────────

    [Fact]
    public async Task RT002_ExistingObservations_AllHaveActiveStatus()
    {
        // Arrange: Create multiple observations
        await SeedSession();
        var id1 = await SeedObservation("Decision 1", "content 1");
        var id2 = await SeedObservation("Decision 2", "content 2");
        var id3 = await SeedObservation("Decision 3", "content 3");

        // Act: Retrieve all observations
        var obs1 = await _store.GetObservationAsync(id1);
        var obs2 = await _store.GetObservationAsync(id2);
        var obs3 = await _store.GetObservationAsync(id3);

        // Assert: All should have status = "active" by default
        Assert.Equal("active", obs1!.Status);
        Assert.Equal("active", obs2!.Status);
        Assert.Equal("active", obs3!.Status);
    }

    // ─── RT-003/RT-004/RT-005: Search Without New Parameters ──────────────────

    [Fact]
    public async Task RT003_SearchWithoutParameters_ReturnsOnlyActive()
    {
        // Arrange: Create observations with different status
        await SeedSession();
        var activeId = await SeedObservation("Active Decision", "active content");
        var deprecatedId = await SeedObservation("Deprecated Decision", "deprecated content");
        await _store.UpdateObservationAsync(deprecatedId, new UpdateObservationParams { Status = "deprecated" });

        // Act: Search without status parameter (default behavior)
        var results = await _store.SearchAsync("Decision", new SearchOptions
        {
            Project = "test-project",
        });

        // Assert: Should return only active observations
        Assert.Single(results);
        Assert.Equal(activeId, results[0].Observation.Id);
        Assert.Equal("active", results[0].Observation.Status);
    }

    [Fact]
    public async Task RT004_SearchWithProjectAndType_FiltersCorrectly()
    {
        // Arrange: Create observations with different project and type
        await SeedSession();
        var id1 = await SeedObservation("Decision 1", "content 1", type: "decision", project: "project-a");
        var id2 = await SeedObservation("Bug Fix 1", "content 2", type: "bugfix", project: "project-a");
        var id3 = await SeedObservation("Decision 2", "content 3", type: "decision", project: "project-b");

        // Act: Search with project and type filters
        var results = await _store.SearchAsync("content", new SearchOptions
        {
            Project = "project-a",
            Type = "decision",
        });

        // Assert: Should return only decisions from project-a
        Assert.Single(results);
        Assert.Equal(id1, results[0].Observation.Id);
    }

    [Fact]
    public async Task RT005_SearchWithScope_FiltersCorrectly()
    {
        // Arrange: Create observations with different scope
        await SeedSession();
        var teamId = await SeedObservation("Team Decision", "content 1", scope: "team");
        var personalId = await SeedObservation("Personal Decision", "content 2", scope: "personal");

        // Act: Search with scope filter
        var results = await _store.SearchAsync("Decision", new SearchOptions
        {
            Project = "test-project",
            Scope = "team",
        });

        // Assert: Should return only team observations
        Assert.Single(results);
        Assert.Equal(teamId, results[0].Observation.Id);
        Assert.Equal("team", results[0].Observation.Scope);
    }

    // ─── RT-006: Export Includes Status ────────────────────────────────────────

    [Fact]
    public async Task RT006_Export_IncludesStatusField()
    {
        // Arrange: Create observations with different status
        await SeedSession();
        var activeId = await SeedObservation("Active Decision", "active content");
        var deprecatedId = await SeedObservation("Deprecated Decision", "deprecated content");
        await _store.UpdateObservationAsync(deprecatedId, new UpdateObservationParams { Status = "deprecated" });

        // Act: Export observations
        var export = await _store.ExportAsync();

        // Assert: Export should include status field
        Assert.NotEmpty(export.Observations);
        var activeObs = export.Observations.First(o => o.Id == activeId);
        var deprecatedObs = export.Observations.First(o => o.Id == deprecatedId);

        Assert.Equal("active", activeObs.Status);
        Assert.Equal("deprecated", deprecatedObs.Status);
    }

    // ─── RT-008/RT-009/RT-010: Import Preserves Status ────────────────────────

    [Fact]
    public async Task RT008_Import_WithStatusField_PreservesStatus()
    {
        // Arrange: Create export data with different status values
        await SeedSession();
        var activeId = await SeedObservation("Active Decision", "active content");
        var deprecatedId = await SeedObservation("Deprecated Decision", "deprecated content");
        await _store.UpdateObservationAsync(deprecatedId, new UpdateObservationParams { Status = "deprecated" });

        var export = await _store.ExportAsync();

        // Act: Import into a fresh store
        var newTempDir = Path.Combine(Path.GetTempPath(), "engram-regression-import", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(newTempDir);
        try
        {
            var newCfg = new StoreConfig { DataDir = newTempDir };
            using var newStore = new SqliteStore(newCfg);
            await newStore.CreateSessionAsync(SessionId, "test-project", "/tmp");
            await newStore.ImportAsync(export);

            // Assert: Status should be preserved
            var importedActive = await newStore.GetObservationAsync(activeId);
            var importedDeprecated = await newStore.GetObservationAsync(deprecatedId);

            Assert.NotNull(importedActive);
            Assert.NotNull(importedDeprecated);
            Assert.Equal("active", importedActive!.Status);
            Assert.Equal("deprecated", importedDeprecated!.Status);
        }
        finally
        {
            try { Directory.Delete(newTempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task RT009_Import_WithoutStatusField_DefaultsToActive()
    {
        // Arrange: Create export data and manually remove status field (simulating old export)
        await SeedSession();
        var id = await SeedObservation("Legacy Decision", "legacy content");
        var export = await _store.ExportAsync();

        // Manually set status to null in the export (simulating old export format)
        foreach (var obs in export.Observations)
        {
            obs.Status = null!;
        }

        // Act: Import into a fresh store
        var newTempDir = Path.Combine(Path.GetTempPath(), "engram-regression-import-null", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(newTempDir);
        try
        {
            var newCfg = new StoreConfig { DataDir = newTempDir };
            using var newStore = new SqliteStore(newCfg);
            await newStore.CreateSessionAsync(SessionId, "test-project", "/tmp");
            await newStore.ImportAsync(export);

            // Assert: Observations without status should default to "active"
            var imported = await newStore.GetObservationAsync(id);
            Assert.NotNull(imported);
            Assert.Equal("active", imported!.Status);
        }
        finally
        {
            try { Directory.Delete(newTempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task RT010_RoundTrip_ExportImport_PreservesStatus()
    {
        // Arrange: Create observations with different status
        await SeedSession();
        var activeId = await SeedObservation("Active Decision", "active content");
        var deprecatedId = await SeedObservation("Deprecated Decision", "deprecated content");
        await _store.UpdateObservationAsync(deprecatedId, new UpdateObservationParams { Status = "deprecated" });

        // Act: Export → Import round trip
        var export = await _store.ExportAsync();

        var newTempDir = Path.Combine(Path.GetTempPath(), "engram-regression-roundtrip", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(newTempDir);
        try
        {
            var newCfg = new StoreConfig { DataDir = newTempDir };
            using var newStore = new SqliteStore(newCfg);
            await newStore.CreateSessionAsync(SessionId, "test-project", "/tmp");
            await newStore.ImportAsync(export);

            // Assert: Status should be preserved after round trip
            var importedActive = await newStore.GetObservationAsync(activeId);
            var importedDeprecated = await newStore.GetObservationAsync(deprecatedId);

            Assert.Equal("active", importedActive!.Status);
            Assert.Equal("deprecated", importedDeprecated!.Status);
        }
        finally
        {
            try { Directory.Delete(newTempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // ─── RT-014/RT-015: Topic Key Upsert with Status ──────────────────────────

    [Fact]
    public async Task RT014_Upsert_ActiveObservation_PreservesStatus()
    {
        // Arrange: Create observation with topic_key
        await SeedSession();
        var id = await SeedObservation("Decision v1", "original content", topicKey: "decision/auth");

        // Act: Update with same topic_key (upsert) - must match project and scope exactly
        var updatedId = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = "Decision v2",
            Content = "updated content",
            Type = "decision",
            Project = "test-project",
            TopicKey = "decision/auth",
            Scope = "team", // Must match the original observation's scope
        });

        // Assert: Should update the same observation, status remains "active"
        Assert.Equal(id, updatedId);
        var obs = await _store.GetObservationAsync(id);
        Assert.Equal("active", obs!.Status);
        Assert.Equal("Decision v2", obs.Title);
        Assert.Equal("updated content", obs.Content);
    }

    [Fact]
    public async Task RT015_Upsert_DeprecatedObservation_PreservesDeprecatedStatus()
    {
        // Arrange: Create observation and mark as deprecated
        await SeedSession();
        var id = await SeedObservation("Decision v1", "original content", topicKey: "decision/auth");
        await _store.UpdateObservationAsync(id, new UpdateObservationParams { Status = "deprecated" });

        // Act: Update with same topic_key (upsert) - must match project and scope exactly
        var updatedId = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Title = "Decision v2",
            Content = "updated content",
            Type = "decision",
            Project = "test-project",
            TopicKey = "decision/auth",
            Scope = "team", // Must match the original observation's scope
        });

        // Assert: Should update the same observation, status remains "deprecated"
        Assert.Equal(id, updatedId);
        var obs = await _store.GetObservationAsync(id);
        Assert.Equal("deprecated", obs!.Status);
        Assert.Equal("Decision v2", obs.Title);
    }

    // ─── RT-027: Edge Cases ───────────────────────────────────────────────────

    [Fact]
    public async Task RT027_InvalidStatus_AcceptedByStore_ValidationAtToolLayer()
    {
        // Arrange: Create observation
        await SeedSession();
        var id = await SeedObservation("Decision", "content");

        // Act & Assert: Store layer accepts any string (validation is at MCP tool layer)
        await _store.UpdateObservationAsync(id, new UpdateObservationParams { Status = "invalid" });
        var obs = await _store.GetObservationAsync(id);
        Assert.Equal("invalid", obs!.Status); // Store accepts it, tool layer validates

        // Reset to valid status
        await _store.UpdateObservationAsync(id, new UpdateObservationParams { Status = "active" });
        obs = await _store.GetObservationAsync(id);
        Assert.Equal("active", obs!.Status);
    }
}
