using Xunit;

namespace Engram.Server.Tests;

/// <summary>
/// Unit tests for IsSyncSelfLoop — see ADR-008.
/// </summary>
public sealed class SyncSelfLoopDetectionTests
{
    [Theory]
    [InlineData("http://localhost:7437", 7437)]
    [InlineData("http://127.0.0.1:7437", 7437)]
    [InlineData("http://LOCALHOST:7437", 7437)] // case-insensitive
    [InlineData("http://[::1]:7437", 7437)]
    public void IsSyncSelfLoop_ReturnsTrue_ForLoopbackHostAndSamePort(string url, int port)
    {
        Assert.True(EngramServer.IsSyncSelfLoop(url, port));
    }

    [Theory]
    [InlineData("http://192.168.1.5:7437", 7437)]
    [InlineData("http://10.0.0.1:7437", 7437)]
    [InlineData("http://sync.example.com:7437", 7437)]
    [InlineData("http://localhost:8000", 7437)] // different port
    [InlineData("http://127.0.0.1:9999", 7437)]
    public void IsSyncSelfLoop_ReturnsFalse_ForRemoteHostOrDifferentPort(string url, int port)
    {
        Assert.False(EngramServer.IsSyncSelfLoop(url, port));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData("ftp://localhost:7437")] // unsupported scheme
    public void IsSyncSelfLoop_ReturnsFalse_ForInvalidUrl(string url)
    {
        Assert.False(EngramServer.IsSyncSelfLoop(url, 7437));
    }
}
