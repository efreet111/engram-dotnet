using Xunit;

namespace Engram.Obsidian.Tests;

public class HubGeneratorTests
{
    [Fact]
    public void SessionHubMarkdown_ContainsFrontmatterAndWikilinks()
    {
        var refs = new List<ObsRef>
        {
            new() { Slug = "fixed-auth-bug-1", Title = "Fixed auth bug", Type = "bugfix" },
            new() { Slug = "sdd-proposal-obsidian-2", Title = "SDD Proposal: Obsidian", Type = "architecture" },
        };

        var got = HubGenerator.SessionHubMarkdown("sess-42", refs);

        Assert.StartsWith("---\n", got);
        Assert.Contains("type: session-hub", got);
        Assert.Contains("session_id: sess-42", got);
        Assert.Contains("# Session: sess-42", got);
        Assert.Contains("[[fixed-auth-bug-1]]", got);
        Assert.Contains("[[sdd-proposal-obsidian-2]]", got);
    }

    [Fact]
    public void SessionHubMarkdown_SingleObservation_RendersCorrectly()
    {
        var refs = new List<ObsRef>
        {
            new() { Slug = "only-obs-99", Title = "Only observation", Type = "decision" },
        };

        var got = HubGenerator.SessionHubMarkdown("sess-01", refs);

        Assert.Contains("[[only-obs-99]]", got);
        Assert.Contains("# Session: sess-01", got);
    }

    [Fact]
    public void TopicHubMarkdown_ContainsFrontmatterAndTypeAnnotations()
    {
        var refs = new List<ObsRef>
        {
            new() { Slug = "sdd-spec-obs-1", Title = "SDD Spec", Type = "architecture", TopicKey = "sdd/spec" },
            new() { Slug = "sdd-design-obs-2", Title = "SDD Design", Type = "architecture", TopicKey = "sdd/design" },
        };

        var got = HubGenerator.TopicHubMarkdown("sdd", refs);

        Assert.StartsWith("---\n", got);
        Assert.Contains("type: topic-hub", got);
        Assert.Contains("topic_prefix: sdd", got);
        Assert.Contains("# Topic: sdd", got);
        Assert.Contains("[[sdd-spec-obs-1]]", got);
        Assert.Contains("[[sdd-design-obs-2]]", got);
    }

    [Fact]
    public void TopicHubMarkdown_ShowsTypeAnnotation()
    {
        var refs = new List<ObsRef>
        {
            new() { Slug = "explore-obs-1", Title = "Explore", Type = "architecture", TopicKey = "sdd/explore" },
            new() { Slug = "proposal-obs-2", Title = "Proposal", Type = "decision", TopicKey = "sdd/proposal" },
        };

        var got = HubGenerator.TopicHubMarkdown("sdd", refs);

        Assert.Contains("(architecture)", got);
        Assert.Contains("(decision)", got);
    }

    [Fact]
    public void ShouldCreateTopicHub_ThresholdRule()
    {
        Assert.False(HubGenerator.ShouldCreateTopicHub(0));
        Assert.False(HubGenerator.ShouldCreateTopicHub(1));
        Assert.True(HubGenerator.ShouldCreateTopicHub(2));
        Assert.True(HubGenerator.ShouldCreateTopicHub(3));
    }
}
