using Engram.MdGeneration;
using Engram.Store;
using Xunit;

namespace Engram.MdGeneration.Tests;

public class MdTemplateEngineTests
{
    private readonly MdTemplateEngine _engine = new();

    [Fact]
    public void Render_IncludesAllFrontmatterFields()
    {
        var obs = new Observation
        {
            Id = 42,
            Type = "decision",
            Title = "Switch to JWT",
            Content = "We decided to use JWT for auth.",
            CreatedAt = "2026-05-14 10:00:00",
            TopicKey = "architecture/auth"
        };

        var result = _engine.Render(obs);

        Assert.Contains("observation_id: 42", result);
        Assert.Contains("type: \"decision\"", result);
        Assert.Contains("title: \"Switch to JWT\"", result);
        Assert.Contains("created_at: \"2026-05-14 10:00:00\"", result);
        Assert.Contains("topic_key: \"architecture/auth\"", result);
        Assert.Contains("generated_at:", result);
        Assert.Contains("# Switch to JWT", result);
        Assert.Contains("We decided to use JWT for auth.", result);
    }

    [Fact]
    public void Render_NullTopicKey_OmitsField()
    {
        var obs = new Observation
        {
            Id = 1,
            Type = "bugfix",
            Title = "Fix null ref",
            Content = "Fixed the issue.",
            CreatedAt = "2026-05-14 10:00:00"
        };

        var result = _engine.Render(obs);

        Assert.DoesNotContain("topic_key:", result);
    }

    [Fact]
    public void Render_EscapesSpecialChars()
    {
        var obs = new Observation
        {
            Id = 1,
            Type = "decision",
            Title = "Use \"quotes\" and \\backslash\\",
            Content = "Test content",
            CreatedAt = "2026-05-14"
        };

        var result = _engine.Render(obs);

        Assert.Contains("title: \"Use \\\"quotes\\\"", result);
    }

    [Fact]
    public void Render_EmptyContent_StillGeneratesFrontmatter()
    {
        var obs = new Observation
        {
            Id = 1,
            Type = "note",
            Title = "Empty",
            Content = "",
            CreatedAt = "2026-05-14"
        };

        var result = _engine.Render(obs);

        Assert.Contains("observation_id: 1", result);
        Assert.Contains("# Empty", result);
    }
}
