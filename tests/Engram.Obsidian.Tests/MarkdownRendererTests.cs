using Engram.Store;
using Xunit;

namespace Engram.Obsidian.Tests;

public class MarkdownRendererTests
{
    [Fact]
    public void ObservationToMarkdown_AllFields_FrontmatterBodyAndWikilinks()
    {
        var obs = new Observation
        {
            Id = 1,
            Type = "bugfix",
            Title = "Fixed the bug",
            Content = "The fix was simple.",
            Project = "eng",
            Scope = "project",
            TopicKey = "auth/jwt",
            SessionId = "abc123",
            RevisionCount = 2,
            CreatedAt = "2026-01-01T10:00:00Z",
            UpdatedAt = "2026-01-02T10:00:00Z",
        };

        var got = MarkdownRenderer.ObservationToMarkdown(obs);

        // Frontmatter block must open with ---
        Assert.StartsWith("---\n", got);

        // All required frontmatter keys must be present
        foreach (var key in new[] { "id:", "type:", "project:", "scope:", "topic_key:", "session_id:", "created_at:", "updated_at:", "revision_count:" })
            Assert.Contains(key, got);

        // Specific frontmatter values
        Assert.Contains("type: bugfix", got);
        Assert.Contains("topic_key: auth/jwt", got);
        Assert.Contains("session_id: abc123", got);
        Assert.Contains("revision_count: 2", got);

        // Title as H1 heading
        Assert.Contains("# Fixed the bug", got);

        // Content body
        Assert.Contains("The fix was simple.", got);

        // Session wikilink
        Assert.Contains("[[session-abc123]]", got);

        // Topic wikilink — prefix = "auth" (first segment of "auth/jwt")
        Assert.Contains("[[topic-auth]]", got);
    }

    [Fact]
    public void ObservationToMarkdown_NoTopicKey_EmptyTopicKeyInFrontmatter_NoTopicWikilink()
    {
        var obs = new Observation
        {
            Id = 2,
            Type = "decision",
            Title = "Chose SQLite",
            Content = "SQLite is the right choice.",
            Project = "eng",
            Scope = "project",
            TopicKey = null,
            SessionId = "sess-001",
            CreatedAt = "2026-02-01T00:00:00Z",
            UpdatedAt = "2026-02-01T00:00:00Z",
        };

        var got = MarkdownRenderer.ObservationToMarkdown(obs);

        // topic_key must appear empty in frontmatter
        Assert.Contains("topic_key: \"\"", got);

        // No topic wikilink should be emitted
        Assert.DoesNotContain("[[topic-", got);

        // Session wikilink must still be present
        Assert.Contains("[[session-sess-001]]", got);
    }

    [Fact]
    public void ObservationToMarkdown_NoSessionId_NoSessionWikilink()
    {
        var obs = new Observation
        {
            Id = 3,
            Type = "architecture",
            Title = "DB Schema decision",
            Content = "We chose normalized schema.",
            Project = "core",
            Scope = "project",
            TopicKey = "arch/db",
            SessionId = "",
            CreatedAt = "2026-03-01T00:00:00Z",
            UpdatedAt = "2026-03-01T00:00:00Z",
        };

        var got = MarkdownRenderer.ObservationToMarkdown(obs);

        // No session wikilink when session_id is empty
        Assert.DoesNotContain("[[session-]]", got);

        // Topic wikilink must still be present — prefix = "arch"
        Assert.Contains("[[topic-arch]]", got);
    }

    [Fact]
    public void ObservationToMarkdown_MultiSegmentTopicKey_PrefixUsesLastSlashPart()
    {
        var obs = new Observation
        {
            Id = 4,
            Type = "architecture",
            Title = "SDD Explore",
            Content = "Content here.",
            Project = "engram",
            Scope = "project",
            TopicKey = "sdd/obsidian-plugin/explore",
            SessionId = "s-99",
            CreatedAt = "2026-04-01T00:00:00Z",
            UpdatedAt = "2026-04-01T00:00:00Z",
        };

        var got = MarkdownRenderer.ObservationToMarkdown(obs);

        // Design says: wikilink prefix = topic_key split on LAST "/"
        // "sdd/obsidian-plugin/explore" → split on last "/" → prefix = "sdd/obsidian-plugin"
        // In the wikilink, "/" → "--" → [[topic-sdd--obsidian-plugin]]
        Assert.Contains("[[topic-sdd--obsidian-plugin]]", got);
    }

    [Fact]
    public void ObservationToMarkdown_TagsIncludeProjectAndType()
    {
        var obs = new Observation
        {
            Id = 5,
            Type = "bugfix",
            Title = "Test",
            Content = "Content",
            Project = "my-app",
            Scope = "team",
            CreatedAt = "2026-01-01T00:00:00Z",
            UpdatedAt = "2026-01-01T00:00:00Z",
        };

        var got = MarkdownRenderer.ObservationToMarkdown(obs);

        Assert.Contains("- my-app", got);
        Assert.Contains("- bugfix", got);
    }

    [Fact]
    public void ObservationToMarkdown_AliasesIncludeTitle()
    {
        var obs = new Observation
        {
            Id = 6,
            Title = "My Special Title",
            Content = "Content",
            Type = "decision",
            Scope = "team",
            CreatedAt = "2026-01-01T00:00:00Z",
            UpdatedAt = "2026-01-01T00:00:00Z",
        };

        var got = MarkdownRenderer.ObservationToMarkdown(obs);

        Assert.Contains("aliases:", got);
        Assert.Contains("\"My Special Title\"", got);
    }
}
