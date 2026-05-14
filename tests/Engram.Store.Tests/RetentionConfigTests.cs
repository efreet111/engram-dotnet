using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

public class RetentionConfigTests
{
    [Fact]
    public void ShouldExpire_ToolUse_ReturnsTrue()
    {
        var config = new RetentionConfig();
        Assert.True(config.ShouldExpire("tool_use"));
    }

    [Fact]
    public void ShouldExpire_Decision_ReturnsFalse()
    {
        var config = new RetentionConfig();
        Assert.False(config.ShouldExpire("decision"));
    }

    [Fact]
    public void ShouldExpire_Architecture_ReturnsFalse()
    {
        var config = new RetentionConfig();
        Assert.False(config.ShouldExpire("architecture"));
    }

    [Fact]
    public void ShouldExpire_SessionSummary_ReturnsFalse()
    {
        var config = new RetentionConfig();
        Assert.False(config.ShouldExpire("session_summary"));
    }

    [Fact]
    public void GetTtl_ToolUse_Returns30Days()
    {
        var config = new RetentionConfig();
        var ttl = config.GetTtl("tool_use");
        Assert.NotNull(ttl);
        Assert.Equal(30, ttl.Value.Days);
    }

    [Fact]
    public void GetTtl_Bugfix_Returns90Days()
    {
        var config = new RetentionConfig();
        var ttl = config.GetTtl("bugfix");
        Assert.NotNull(ttl);
        Assert.Equal(90, ttl.Value.Days);
    }

    [Fact]
    public void GetTtl_FileChange_Returns30Days()
    {
        var config = new RetentionConfig();
        var ttl = config.GetTtl("file_change");
        Assert.NotNull(ttl);
        Assert.Equal(30, ttl.Value.Days);
    }

    [Fact]
    public void GetTtl_Discovery_Returns60Days()
    {
        var config = new RetentionConfig();
        var ttl = config.GetTtl("discovery");
        Assert.NotNull(ttl);
        Assert.Equal(60, ttl.Value.Days);
    }

    [Fact]
    public void GetTtl_UnknownType_ReturnsNull()
    {
        var config = new RetentionConfig();
        var ttl = config.GetTtl("nonexistent_type");
        Assert.Null(ttl);
    }

    [Fact]
    public void ShouldExpire_Command_ReturnsTrue()
    {
        var config = new RetentionConfig();
        Assert.True(config.ShouldExpire("command"));
    }

    [Fact]
    public void ShouldExpire_Bugfix_ReturnsTrue()
    {
        var config = new RetentionConfig();
        Assert.True(config.ShouldExpire("bugfix"));
    }

    [Fact]
    public void GetTtl_SupportsCaseInsensitive()
    {
        var config = new RetentionConfig();
        var ttl = config.GetTtl("TOOL_USE");
        Assert.NotNull(ttl);
        Assert.Equal(30, ttl.Value.Days);
    }
}
