using Engram.Cli;
using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// Tests for --since argument parsing in obsidian-export command.
/// ENG-208 Phase 8.
/// </summary>
public class ObsidianSinceTests
{
    // ─── ParseSinceArgument Valid Formats ─────────────────────────────────────

    [Theory]
    [InlineData("2025-01-01", "2025-01-01T00:00:00Z")]
    [InlineData("2025-12-31T15:30:00Z", "2025-12-31T15:30:00Z")]
    [InlineData("2026-06-09T10:00:00Z", "2026-06-09T10:00:00Z")]
    public void ParseSinceArgument_ValidIso8601_Parses(string input, string expectedPartial)
    {
        var result = SinceArgumentParser.Parse(input);

        // Should parse without throwing
        Assert.NotEqual(default, result);

        // The result should contain the expected date portion
        var isoResult = result.ToString("yyyy-MM-ddTHH:mm:ssZ");
        Assert.StartsWith(expectedPartial, isoResult);
    }

    [Theory]
    [InlineData("30d")]
    [InlineData("7d")]
    [InlineData("1d")]
    public void ParseSinceArgument_ValidRelativeDays_Parses(string input)
    {
        var now = DateTime.UtcNow;
        var result = SinceArgumentParser.Parse(input);

        // Should be approximately now minus the days
        var expected = now.AddDays(-int.Parse(input.TrimEnd('d')));

        // Allow 5 second tolerance for test execution time
        var diff = Math.Abs((expected - result).TotalSeconds);
        Assert.True(diff < 5, $"Expected close to {expected}, got {result}");
    }

    [Theory]
    [InlineData("24h")]
    [InlineData("1h")]
    public void ParseSinceArgument_ValidRelativeHours_Parses(string input)
    {
        var now = DateTime.UtcNow;
        var result = SinceArgumentParser.Parse(input);

        var hours = int.Parse(input.TrimEnd('h'));
        var expected = now.AddHours(-hours);

        var diff = Math.Abs((expected - result).TotalSeconds);
        Assert.True(diff < 5, $"Expected close to {expected}, got {result}");
    }

    [Theory]
    [InlineData("60m")]
    [InlineData("5m")]
    public void ParseSinceArgument_ValidRelativeMinutes_Parses(string input)
    {
        var now = DateTime.UtcNow;
        var result = SinceArgumentParser.Parse(input);

        var minutes = int.Parse(input.TrimEnd('m'));
        var expected = now.AddMinutes(-minutes);

        var diff = Math.Abs((expected - result).TotalSeconds);
        Assert.True(diff < 5, $"Expected close to {expected}, got {result}");
    }

    // ─── ParseSinceArgument Invalid Formats ────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("99x")]
    [InlineData("abc")]
    public void ParseSinceArgument_InvalidFormat_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => SinceArgumentParser.Parse(input));
    }

    [Theory]
    [InlineData("0d")]
    [InlineData("0h")]
    [InlineData("0m")]
    public void ParseSinceArgument_ZeroDuration_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => SinceArgumentParser.Parse(input));
    }

    [Theory]
    [InlineData("-1d")]
    [InlineData("-30h")]
    public void ParseSinceArgument_NegativeDuration_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => SinceArgumentParser.Parse(input));
    }

    // ─── TryParse ──────────────────────────────────────────────────────

    [Fact]
    public void TryParse_ValidInput_ReturnsTrue()
    {
        var success = SinceArgumentParser.TryParse("30d", out var result);

        Assert.True(success);
        Assert.NotEqual(default, result);
    }

    [Fact]
    public void TryParse_InvalidInput_ReturnsFalse()
    {
        var success = SinceArgumentParser.TryParse("invalid", out var result);

        Assert.False(success);
        Assert.Equal(default, result);
    }

    [Fact]
    public void TryParse_EmptyInput_ReturnsFalse()
    {
        var success = SinceArgumentParser.TryParse("", out var result);

        Assert.False(success);
        Assert.Equal(default, result);
    }
}