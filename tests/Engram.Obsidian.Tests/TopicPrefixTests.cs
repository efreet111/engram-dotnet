using Xunit;

namespace Engram.Obsidian.Tests;

public class TopicPrefixTests
{
    [Theory]
    [InlineData("auth/jwt", "auth")]
    [InlineData("sdd/obsidian-plugin/explore", "sdd/obsidian-plugin")]
    [InlineData("standalone", "standalone")]
    [InlineData("a/b/c/d", "a/b/c")]
    [InlineData("", "")]
    public void Extract_ReturnsCorrectPrefix(string topicKey, string expected)
    {
        Assert.Equal(expected, TopicPrefix.Extract(topicKey));
    }

    [Fact]
    public void Extract_NullInput_ReturnsEmptyString()
    {
        Assert.Equal("", TopicPrefix.Extract(null!));
    }
}
