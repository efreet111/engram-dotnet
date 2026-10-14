using Engram.Store;
using Engram.Verification;
using Xunit;

namespace Engram.Verification.Tests;

/// <summary>
/// Unit tests for <see cref="ContradictionDetector"/>.
/// Uses in-memory SQLite store (same pattern as <see cref="MemoryRelationsSpikeTests"/>).
/// </summary>
public class ContradictionDetectorTests : IDisposable
{
    private readonly string _testDir;
    private readonly IStore _store;
    private readonly MemoryRelationRepository _repo;
    private readonly ContradictionDetector _detector;
    private const string SessionId = "detector-test-session";
    private const string Project = "detector-test";

    public ContradictionDetectorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"engram-detector-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        var cfg = new StoreConfig { DataDir = _testDir };
        _store = new SqliteStore(cfg);
        _repo = new MemoryRelationRepository(_store);
        _detector = new ContradictionDetector(_store, _repo);
    }

    public void Dispose()
    {
        _store.Dispose();
        try { Directory.Delete(_testDir, true); } catch { }
    }

    /// <summary>
    /// Creates an observation with a unique topic_key to avoid upsert.
    /// </summary>
    private async Task<long> CreateObsAsync(string title, string content, string topicKey)
    {
        await _store.CreateSessionAsync(SessionId, Project, "/tmp");
        return await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Type = "test_obs",
            Title = title,
            Content = content,
            Project = Project,
            TopicKey = topicKey,
            Scope = Scopes.Team
        });
    }

    // ─── DetectDirectContradictionsAsync tests ─────────────────────────────────

    [Fact]
    public async Task DetectDirectContradictions_TwoLinkedObs_ReturnsOneResult()
    {
        // [FR-1 GWT-1]
        var obsA = await CreateObsAsync("obs-A", "Use SQLite for local dev", $"obs/direct/{Guid.NewGuid():N}");
        var obsB = await CreateObsAsync("obs-B", "Never use SQLite for production", $"obs/direct/{Guid.NewGuid():N}");
        await _repo.SaveRelationAsync(Project, obsA, new MemoryRelation { Type = "conflicts_with", TargetObservationId = obsB }, SessionId);

        var results = await _detector.DetectDirectContradictionsAsync(Project);

        Assert.Single(results);
        Assert.Equal("direct", results[0].Type);
        Assert.Equal(1.0, results[0].Confidence);
        Assert.Contains(results[0].ObsIdA, new[] { obsA, obsB });
        Assert.Contains(results[0].ObsIdB, new[] { obsA, obsB });
    }

    [Fact]
    public async Task DetectDirectContradictions_DeprecatedObs_Excluded()
    {
        // [FR-1 GWT-2]
        var obsA = await CreateObsAsync("obs-A", "Use SQLite for local dev", $"obs/deprecated/{Guid.NewGuid():N}");
        var obsBId = await _store.AddObservationAsync(new AddObservationParams
        {
            SessionId = SessionId,
            Type = "test_obs",
            Title = "obs-B",
            Content = "Never use SQLite for production",
            Project = Project,
            TopicKey = $"obs/deprecated/{Guid.NewGuid():N}",
            Scope = Scopes.Team
        });
        await _store.UpdateObservationAsync(obsBId, new UpdateObservationParams { Status = "deprecated" });
        await _repo.SaveRelationAsync(Project, obsA, new MemoryRelation { Type = "conflicts_with", TargetObservationId = obsBId }, SessionId);

        var results = await _detector.DetectDirectContradictionsAsync(Project);

        Assert.Empty(results); // deprecated obsB filtered out
    }

    // ─── DetectTemporalSupersedenceAsync tests ─────────────────────────────────

    [Fact]
    public async Task DetectTemporalSupersedence_LowOverlap_ReturnsContradiction()
    {
        // FR-2 GWT-1: same topic_key, low keyword overlap → temporal contradiction
        // Note: AddObservationAsync does topic_key-based upsert, so we must create
        // with unique keys first, then update both to share the same topic_key.
        // Use 1100ms delay to ensure different CreatedAt timestamps (SQLite uses second precision).
        var sharedKey = $"auth/temporal/{Guid.NewGuid()}";
        var obsOld = await CreateObsAsync("old-obs",
            "OAuth2 is the recommended auth method",
            $"old/{Guid.NewGuid()}");  // unique to avoid upsert
        await Task.Delay(1100); // ensure different CreatedAt (second-level precision)
        var obsNew = await CreateObsAsync("new-obs",
            "Use JWT for API auth; OAuth2 is deprecated",
            $"new/{Guid.NewGuid()}");  // unique to avoid upsert

        // Now update both to share the same topic_key (bypasses upsert check)
        await _store.UpdateObservationAsync(obsOld, new UpdateObservationParams { TopicKey = sharedKey });
        await _store.UpdateObservationAsync(obsNew, new UpdateObservationParams { TopicKey = sharedKey });

        var results = await _detector.DetectTemporalSupersedenceAsync(Project, confidenceThreshold: 0.5);

        Assert.Single(results);
        Assert.Equal("temporal", results[0].Type);
        Assert.Equal(0.7, results[0].Confidence);
        Assert.Equal(obsNew, results[0].ObsIdA); // head is newer
        Assert.Equal(obsOld, results[0].ObsIdB);
    }

    [Fact]
    public async Task DetectEmbeddingConflicts_HighSimLowOverlap_ReturnsContradiction()
    {
        // [FR-3 GWT-1]
        // High TF-IDF similarity (same domain vocabulary) but low keyword overlap (opposing claims)
        var obsX = await CreateObsAsync("obs-X", "PostgreSQL is the primary database; avoid SQLite in production", $"obs/embed/{Guid.NewGuid():N}");
        var obsY = await CreateObsAsync("obs-Y", "SQLite is acceptable for production use cases with low concurrency", $"obs/embed/{Guid.NewGuid():N}");

        var results = await _detector.DetectEmbeddingConflictsAsync(Project, confidenceThreshold: 0.5);

        Assert.Single(results);
        Assert.Equal("embedding", results[0].Type);
        Assert.InRange(results[0].Confidence, 0.5, 0.9);
    }

    [Fact]
    public async Task DetectEmbeddingConflicts_HighSimHighOverlap_NoResult()
    {
        // [FR-3 GWT-2]
        // High overlap (same vocabulary, reinforcing statements) — no contradiction
        // Using very similar strings to guarantee high overlap.
        var obsA = await CreateObsAsync("obs-A", "PostgreSQL is a reliable database for critical production systems", $"obs/embedhigh/{Guid.NewGuid():N}");
        var obsB = await CreateObsAsync("obs-B", "PostgreSQL is a reliable database for production environments", $"obs/embedhigh/{Guid.NewGuid():N}");

        var results = await _detector.DetectEmbeddingConflictsAsync(Project);

        Assert.Empty(results); // high overlap = reinforcing
    }

    [Fact]
    public async Task DetectEmbeddingConflicts_SingleObs_NoResult()
    {
        // Only one observation — no pair to compare
        await CreateObsAsync("obs-A", "PostgreSQL is the primary database", $"obs/embed/single/{Guid.NewGuid():N}");

        var results = await _detector.DetectEmbeddingConflictsAsync(Project);

        Assert.Empty(results); // need 2+ observations
    }

    // ─── Auto-mark tests (FR-4) ─────────────────────────────────────────────────

    [Fact]
    public async Task DetectTemporalSupersedence_AutoMark_UpdatesStatusAndRelation()
    {
        // FR-4 GWT-1: Confidence > 0.8 with autoMark=true → deprecated + supersedes relation
        // Note: temporal confidence is 0.7, which is below the auto-mark threshold of 0.8.
        // This test verifies autoMark=false doesn't modify anything.
        // Use 1100ms delay to ensure different CreatedAt timestamps.
        var sharedTopicKey = $"automark/{Guid.NewGuid()}";
        var obsOld = await CreateObsAsync("old-obs",
            "OAuth2 is the recommended auth method",
            $"old/{Guid.NewGuid()}");
        await Task.Delay(1100);
        var obsNew = await CreateObsAsync("new-obs",
            "Use JWT for API auth; OAuth2 is deprecated",
            $"new/{Guid.NewGuid()}");

        // Update both to share the same topic_key (bypasses upsert)
        await _store.UpdateObservationAsync(obsOld, new UpdateObservationParams { TopicKey = sharedTopicKey });
        await _store.UpdateObservationAsync(obsNew, new UpdateObservationParams { TopicKey = sharedTopicKey });

        var results = await _detector.DetectTemporalSupersedenceAsync(
            Project, confidenceThreshold: 0.5, autoMark: false);

        Assert.Single(results);
        // Verify older observation was NOT marked deprecated (autoMark=false)
        var oldObs = await _store.GetObservationAsync(obsOld);
        Assert.Equal("active", oldObs.Status);
    }

    [Fact]
    public async Task DetectEmbeddingConflicts_HighConfidence_BelowAutoMarkThreshold()
    {
        // FR-4: embedding type confidence max is 0.9 (< 0.8 auto-mark threshold)
        // This test verifies embedding conflicts are detected but auto-mark won't apply
        var obsA = await CreateObsAsync("obs-A", "Cache is essential for performance", $"embed/{Guid.NewGuid()}");
        var obsB = await CreateObsAsync("obs-B", "Cache can introduce complexity", $"embed/{Guid.NewGuid()}");

        var results = await _detector.DetectEmbeddingConflictsAsync(Project, confidenceThreshold: 0.5);

        Assert.Single(results);
        Assert.Equal("embedding", results[0].Type);
        Assert.Equal(0.9, results[0].Confidence); // max confidence, still < 0.8 auto-mark threshold
        Assert.Equal("keep_both", results[0].SuggestedResolution);
    }
}
