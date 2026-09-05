using Engram.Verification;
using Xunit;

namespace Engram.Verification.Tests;

/// <summary>
/// Unit tests for StatusValidator (ENG-412).
/// </summary>
public class StatusValidatorTests
{
    // ─── FR-002: valid statuses ──────────────────────────────────────────────

    [Theory]
    [InlineData("active")]
    [InlineData("deprecated")]
    [InlineData("deleted")]
    public void IsValid_ReturnsTrue_ForValidStatuses(string status)
    {
        Assert.True(StatusValidator.IsValid(status));
    }

    // ─── FR-002: invalid statuses ────────────────────────────────────────────

    [Theory]
    [InlineData("archived")]
    [InlineData("updated")]
    [InlineData("")]
    [InlineData("ACTIVE")]      // case-sensitive
    [InlineData("Active")]
    [InlineData(" unknown")]
    [InlineData("active ")]
    public void IsValid_ReturnsFalse_ForInvalidStatuses(string status)
    {
        Assert.False(StatusValidator.IsValid(status));
    }

    [Fact]
    public void IsValid_ReturnsFalse_ForNull()
    {
        Assert.False(StatusValidator.IsValid(null));
    }

    // ─── ValidValuesDisplay ──────────────────────────────────────────────────

    [Fact]
    public void ValidValuesDisplay_ContainsAllStatuses()
    {
        var display = StatusValidator.ValidValuesDisplay;
        Assert.Contains("active", display);
        Assert.Contains("deprecated", display);
        Assert.Contains("deleted", display);
    }

    // ─── ValidStatuses array ─────────────────────────────────────────────────

    [Fact]
    public void ValidStatuses_HasExactlyThreeEntries()
    {
        Assert.Equal(3, StatusValidator.ValidStatuses.Length);
    }
}
