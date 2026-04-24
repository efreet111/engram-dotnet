using Engram.Store;

namespace Engram.Obsidian.Tests;

/// <summary>
/// Mock implementation of IObsidianStoreReader for testing.
/// </summary>
public class MockStoreReader : IObsidianStoreReader
{
    public ExportData ExportData { get; set; } = new();
    public Stats Stats { get; set; } = new();
    public Func<ExportData>? ExportFunc { get; set; }

    public Task<ExportData> ExportAsync()
    {
        if (ExportFunc != null)
            return Task.FromResult(ExportFunc());
        return Task.FromResult(ExportData);
    }

    public Task<Stats> StatsAsync() => Task.FromResult(Stats);
}

/// <summary>
/// Test helpers for creating fixtures.
/// </summary>
public static class TestFixtures
{
    public static Observation MakeObs(long id, string sessionId, string project, string type, string title, string topicKey, string ts)
    {
        return new Observation
        {
            Id = id,
            SessionId = sessionId,
            Type = type,
            Title = title,
            Content = title + " content",
            Scope = "team",
            CreatedAt = ts,
            UpdatedAt = ts,
            Project = project,
            TopicKey = string.IsNullOrEmpty(topicKey) ? null : topicKey,
        };
    }

    public static ExportData BuildPipelineFixtures(string deletedAt)
    {
        var sessions = new List<Session>
        {
            new() { Id = "sess-alpha", Project = "project-a", Directory = "", StartedAt = "2026-01-01T00:00:00Z" },
            new() { Id = "sess-beta", Project = "project-a", Directory = "", StartedAt = "2026-01-02T00:00:00Z" },
            new() { Id = "sess-gamma", Project = "project-b", Directory = "", StartedAt = "2026-01-03T00:00:00Z" },
            new() { Id = "sess-delta", Project = "project-b", Directory = "", StartedAt = "2026-01-04T00:00:00Z" },
            new() { Id = "sess-epsilon", Project = "project-c", Directory = "", StartedAt = "2026-01-05T00:00:00Z" },
        };

        var observations = new List<Observation>
        {
            // obs 1..4: project-a, sess-alpha, topic "sdd/plugin" (cluster ≥2)
            MakeObs(1, "sess-alpha", "project-a", "architecture", "SDD Plugin Arch", "sdd/plugin", "2026-01-01T01:00:00Z"),
            MakeObs(2, "sess-alpha", "project-a", "decision", "SDD Plugin Decision", "sdd/plugin", "2026-01-01T02:00:00Z"),
            MakeObs(3, "sess-alpha", "project-a", "bugfix", "SDD Plugin Fix", "sdd/plugin", "2026-01-01T03:00:00Z"),
            MakeObs(4, "sess-alpha", "project-a", "learning", "SDD Plugin Learning", "sdd/plugin", "2026-01-01T04:00:00Z"),
            // obs 5..8: project-a, sess-beta, topic "sdd/design" (same cluster "sdd")
            MakeObs(5, "sess-beta", "project-a", "architecture", "SDD Design Arch", "sdd/design", "2026-01-02T01:00:00Z"),
            MakeObs(6, "sess-beta", "project-a", "decision", "SDD Design Decision", "sdd/design", "2026-01-02T02:00:00Z"),
            MakeObs(7, "sess-beta", "project-a", "bugfix", "SDD Design Fix", "sdd/design", "2026-01-02T03:00:00Z"),
            MakeObs(8, "sess-beta", "project-a", "pattern", "SDD Design Pattern", "sdd/design", "2026-01-02T04:00:00Z"),
            // obs 9..12: project-b, sess-gamma, topic "auth/jwt" (cluster ≥2)
            MakeObs(9, "sess-gamma", "project-b", "architecture", "Auth JWT Arch", "auth/jwt", "2026-01-03T01:00:00Z"),
            MakeObs(10, "sess-gamma", "project-b", "decision", "Auth JWT Decision", "auth/jwt", "2026-01-03T02:00:00Z"),
            MakeObs(11, "sess-gamma", "project-b", "bugfix", "Auth JWT Fix", "auth/jwt", "2026-01-03T03:00:00Z"),
            MakeObs(12, "sess-gamma", "project-b", "learning", "Auth JWT Learning", "auth/jwt", "2026-01-03T04:00:00Z"),
            // obs 13..16: project-b, sess-delta, topic "auth/sessions"
            MakeObs(13, "sess-delta", "project-b", "architecture", "Auth Sessions Arch", "auth/sessions", "2026-01-04T01:00:00Z"),
            MakeObs(14, "sess-delta", "project-b", "decision", "Auth Sessions Decision", "auth/sessions", "2026-01-04T02:00:00Z"),
            MakeObs(15, "sess-delta", "project-b", "bugfix", "Auth Sessions Fix", "auth/sessions", "2026-01-04T03:00:00Z"),
            MakeObs(16, "sess-delta", "project-b", "pattern", "Auth Sessions Pattern", "auth/sessions", "2026-01-04T04:00:00Z"),
            // obs 17..19: project-c, sess-epsilon, no topic (singleton project)
            MakeObs(17, "sess-epsilon", "project-c", "architecture", "PC Arch", "", "2026-01-05T01:00:00Z"),
            MakeObs(18, "sess-epsilon", "project-c", "decision", "PC Decision", "", "2026-01-05T02:00:00Z"),
            MakeObs(19, "sess-epsilon", "project-c", "bugfix", "PC Fix", "", "2026-01-05T03:00:00Z"),
            // obs 20: deleted
            new Observation
            {
                Id = 20,
                SessionId = "sess-alpha",
                Type = "bugfix",
                Title = "Deleted Obs",
                Content = "this was deleted",
                Scope = "team",
                CreatedAt = "2026-01-01T00:00:00Z",
                UpdatedAt = deletedAt,
                DeletedAt = deletedAt,
                Project = "project-a",
            },
        };

        return new ExportData
        {
            Sessions = sessions,
            Observations = observations,
            Prompts = [],
        };
    }
}
