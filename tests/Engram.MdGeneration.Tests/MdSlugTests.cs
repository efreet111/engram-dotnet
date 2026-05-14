using Engram.MdGeneration;
using Xunit;

namespace Engram.MdGeneration.Tests;

public class MdSlugTests
{
    [Fact]
    public void Slugify_LowercasesAndHyphenates()
    {
        var result = MdSlug.Slugify("Hello World Test");
        Assert.Equal("hello-world-test", result);
    }

    [Fact]
    public void Slugify_RemovesSpecialChars()
    {
        var result = MdSlug.Slugify("RF-001: User Registration!");
        Assert.Equal("rf-001-user-registration", result);
    }

    [Fact]
    public void Slugify_TruncatesAt60Chars()
    {
        var longTitle = new string('a', 100) + " " + new string('b', 100);
        var result = MdSlug.Slugify(longTitle);
        Assert.True(result.Length <= 60);
        Assert.DoesNotContain("b", result);
    }

    [Fact]
    public void Slugify_EmptyTitle_ReturnsFallback()
    {
        var result = MdSlug.Slugify("");
        Assert.StartsWith("untitled-", result);
    }

    [Fact]
    public void ToFilename_IncludesDate()
    {
        var date = new DateTime(2026, 5, 14);
        var result = MdSlug.ToFilename("My Title", date);
        Assert.StartsWith("2026-05-14-", result);
        Assert.EndsWith(".md", result);
        Assert.Contains("my-title", result);
    }
}
