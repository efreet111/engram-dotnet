using Xunit;

namespace Engram.Obsidian.Tests;

public class SlugTests
{
    [Theory]
    [InlineData("Fixed FTS5 syntax error", 1401, "fixed-fts5-syntax-error-1401")]
    [InlineData("SDD Proposal: obsidian-plugin", 1720, "sdd-proposal-obsidian-plugin-1720")]
    [InlineData("", 42, "observation-42")]
    [InlineData("This is a very long title that exceeds sixty characters limit by a lot", 99, "this-is-a-very-long-title-that-exceeds-sixty-characters-limi-99")]
    [InlineData("Fixed authentication", 1, "fixed-authentication-1")]
    [InlineData("Fixed authentication", 2, "fixed-authentication-2")]
    [InlineData("!!! Hello World !!!", 5, "hello-world-5")]
    [InlineData("Fix bug #42 in v2.0", 10, "fix-bug-42-in-v2-0-10")]
    public void Slugify_ProducesExpectedSlug(string title, long id, string expected)
    {
        var result = Slug.Slugify(title, id);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Slugify_UnicodeCharacters_ReplacedWithHyphens()
    {
        // Go original: "Lösung für das Problem" → "l-sung-f-r-das-problem-7"
        var result = Slug.Slugify("Lösung für das Problem", 7);
        Assert.Equal("l-sung-f-r-das-problem-7", result);
    }

    [Fact]
    public void Slugify_TrailingHyphensAfterTruncation_Removed()
    {
        // Title that after truncation would end with a hyphen
        var title = "This-is-exactly-sixty-characters-long-and-ends-with-a-hyphen-and-more-text";
        var result = Slug.Slugify(title, 1);
        // The character right before the ID suffix should not be a hyphen
        var idx = result.LastIndexOf('-');
        Assert.True(idx > 0);
        var charBeforeId = result[idx - 1];
        Assert.NotEqual('-', charBeforeId);
    }

    [Fact]
    public void Slugify_CollisionSafety_DifferentIdsDifferentSlugs()
    {
        var slug1 = Slug.Slugify("Same title", 1);
        var slug2 = Slug.Slugify("Same title", 2);
        Assert.NotEqual(slug1, slug2);
    }
}
