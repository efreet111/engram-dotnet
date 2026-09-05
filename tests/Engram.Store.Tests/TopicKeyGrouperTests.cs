using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Unit tests for TopicKeyGrouper (ENG-412, NFR-004).
/// </summary>
public class TopicKeyGrouperTests
{
    private static Observation MakeObs(long id, string? topicKey, string status = "active", string createdAt = "")
        => new()
        {
            Id = id,
            TopicKey = topicKey,
            Status = status,
            CreatedAt = string.IsNullOrEmpty(createdAt) ? $"2026-01-{id:D2}" : createdAt,
            Title = $"Obs {id}",
            Content = $"Content {id}",
            Type = "decision",
        };

    // ─── GroupByTopicKey ─────────────────────────────────────────────────────

    [Fact]
    public void GroupByTopicKey_EmptyInput_ReturnsEmptyDict()
    {
        var result = TopicKeyGrouper.GroupByTopicKey([]);
        Assert.Empty(result);
    }

    [Fact]
    public void GroupByTopicKey_SingleGroup_AllSameKey()
    {
        var obs = new[]
        {
            MakeObs(1, "decision/auth"),
            MakeObs(2, "decision/auth"),
            MakeObs(3, "decision/auth"),
        };

        var result = TopicKeyGrouper.GroupByTopicKey(obs);

        Assert.Single(result);
        Assert.True(result.ContainsKey("decision/auth"));
        Assert.Equal(3, result["decision/auth"].Count);
    }

    [Fact]
    public void GroupByTopicKey_MultipleGroups_DifferentKeys()
    {
        var obs = new[]
        {
            MakeObs(1, "decision/auth"),
            MakeObs(2, "decision/cliente"),
            MakeObs(3, "decision/auth"),
        };

        var result = TopicKeyGrouper.GroupByTopicKey(obs);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result["decision/auth"].Count);
        Assert.Single(result["decision/cliente"]);
    }

    [Fact]
    public void GroupByTopicKey_NullTopicKey_PlacedUnderNoTopic()
    {
        var obs = new[]
        {
            MakeObs(1, null),
            MakeObs(2, "decision/auth"),
        };

        var result = TopicKeyGrouper.GroupByTopicKey(obs);

        Assert.True(result.ContainsKey("(no topic)"));
        Assert.Single(result["(no topic)"]);
    }

    [Fact]
    public void GroupByTopicKey_EmptyTopicKey_PlacedUnderNoTopic()
    {
        var obs = new[] { MakeObs(1, "") };

        var result = TopicKeyGrouper.GroupByTopicKey(obs);

        Assert.True(result.ContainsKey("(no topic)"));
    }

    [Fact]
    public void GroupByTopicKey_WithinGroup_SortedByCreatedAtDesc()
    {
        var obs = new[]
        {
            MakeObs(1, "decision/auth", createdAt: "2026-01-01"),
            MakeObs(3, "decision/auth", createdAt: "2026-01-03"),
            MakeObs(2, "decision/auth", createdAt: "2026-01-02"),
        };

        var result = TopicKeyGrouper.GroupByTopicKey(obs);
        var group = result["decision/auth"];

        Assert.Equal("2026-01-03", group[0].CreatedAt); // most recent first
        Assert.Equal("2026-01-02", group[1].CreatedAt);
        Assert.Equal("2026-01-01", group[2].CreatedAt);
    }

    // ─── GetGroupHeads ───────────────────────────────────────────────────────

    [Fact]
    public void GetGroupHeads_EmptyInput_ReturnsEmptyList()
    {
        var result = TopicKeyGrouper.GetGroupHeads([]);
        Assert.Empty(result);
    }

    [Fact]
    public void GetGroupHeads_ReturnsMostRecentAsHead()
    {
        var obs = new[]
        {
            MakeObs(1, "decision/auth", createdAt: "2026-01-01"),
            MakeObs(3, "decision/auth", createdAt: "2026-01-03"),
            MakeObs(2, "decision/auth", createdAt: "2026-01-02"),
        };

        var result = TopicKeyGrouper.GetGroupHeads(obs);

        Assert.Single(result);
        Assert.Equal(3, result[0].Head.Id); // most recent
        Assert.Equal(3, result[0].TotalCount);
    }

    [Fact]
    public void GetGroupHeads_CountsDeprecatedCorrectly()
    {
        var obs = new[]
        {
            MakeObs(1, "decision/auth", status: "deprecated", createdAt: "2026-01-01"),
            MakeObs(2, "decision/auth", status: "deprecated", createdAt: "2026-01-02"),
            MakeObs(3, "decision/auth", status: "active", createdAt: "2026-01-03"),
        };

        var result = TopicKeyGrouper.GetGroupHeads(obs);

        Assert.Single(result);
        Assert.Equal(2, result[0].DeprecatedCount);
        Assert.Equal(3, result[0].TotalCount);
        Assert.Equal("active", result[0].Head.Status);
    }

    [Fact]
    public void GetGroupHeads_MultipleGroups()
    {
        var obs = new[]
        {
            MakeObs(1, "decision/auth", status: "deprecated"),
            MakeObs(2, "decision/auth", status: "active"),
            MakeObs(3, "decision/cliente", status: "active"),
        };

        var result = TopicKeyGrouper.GetGroupHeads(obs);

        Assert.Equal(2, result.Count);
    }
}
