using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

public class NormalizersTests
{
    // ─── NormalizeProject ─────────────────────────────────────────────────────

    [Theory]
[InlineData("MyProject",        "myproject")]           // lowercased
[InlineData("my_project",       "my-project")]          // underscore to hyphen
[InlineData("My-Project",       "my-project")]          // hyphen preserved, lowercased
[InlineData("MY PROJECT",       "my-project")]          // space to hyphen, lowercased
[InlineData("  my project  ",   "my-project")]          // trimmed and space to hyphen
[InlineData("proj--foo",        "proj-foo")]            // double-dash collapsed
[InlineData("proj__foo",        "proj-foo")]            // double-underscore to hyphen
[InlineData("proj123",          "proj123")]             // alphanumeric unchanged
[InlineData("my  project",      "my-project")]          // multiple spaces collapsed and hyphen
[InlineData("My_Project_Name",  "my-project-name")]     // multiple underscores and case
    public void NormalizeProject_TrimLowercaseAndCollapseRepeated(string input, string expected)
    {
        var result = Normalizers.NormalizeProject(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void NormalizeProject_EmptyInput_ReturnsEmpty()
    {
        var result = Normalizers.NormalizeProject("");
        Assert.Equal("", result);
    }

    // ─── SuggestTopicKey ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("architecture", "auth architecture",        "architecture")]
    [InlineData("bugfix",       "bug in parser",            "bug")]
    [InlineData("bugfix",       "fixed null reference",     "bug")]
    [InlineData("decision",     "chose zustand over redux", "decision")]
    [InlineData("pattern",      "jwt pattern",              "pattern")]
    [InlineData("config",       "docker config setup",      "config")]
    [InlineData("discovery",    "discovered FTS5 quirk",    "discovery")]
    [InlineData("session_summary", "session summary",       "session")]
    public void SuggestTopicKey_InfersCorrectFamily(string type, string title, string expectedFamily)
    {
        var key = Normalizers.SuggestTopicKey(type, title, "");
        Assert.StartsWith(expectedFamily, key);
    }

    [Fact]
    public void SuggestTopicKey_ProducesSlashSeparatedKey()
    {
        var key = Normalizers.SuggestTopicKey("architecture", "JWT auth middleware", "");
        Assert.Contains("/", key);
    }

    [Fact]
    public void SuggestTopicKey_IsLowercase()
    {
        var key = Normalizers.SuggestTopicKey("architecture", "JWT Auth Middleware Setup", "Some Content");
        Assert.Equal(key.ToLowerInvariant(), key);
    }

    [Fact]
    public void SuggestTopicKey_ReplacesSpacesWithDashes()
    {
        var key = Normalizers.SuggestTopicKey("bugfix", "Fixed N+1 query in user list", "");
        Assert.DoesNotContain(" ", key);
    }

    [Fact]
    public void SuggestTopicKey_NullTitle_FallsBackToContent()
    {
        var key = Normalizers.SuggestTopicKey(null, null, "This is a bugfix for login");
        Assert.NotEmpty(key);
    }

    [Fact]
    public void SuggestTopicKey_AllNull_ReturnsFallback()
    {
        var key = Normalizers.SuggestTopicKey(null, null, null);
        Assert.NotEmpty(key);
    }
}
