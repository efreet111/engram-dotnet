using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Unit tests for DeployProfileExtensions, ProfileDefaults, and ProfileValidator.
/// Design: sdd/deploy-profile-system/design/design.md
/// Tasks: Phase 2 (2.3–2.5)
/// </summary>
public class DeployProfileTests
{
    // ─── 2.3 DeployProfileExtensions.FromEnvironment() ─────────────────────────

    [Fact]
    public void FromEnvironment_Unset_ReturnsLocal()
    {
        var original = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", null);
            var result = DeployProfileExtensions.FromEnvironment();
            Assert.Equal(DeployProfile.Local, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", original);
        }
    }

    [Theory]
    [InlineData("local", DeployProfile.Local)]
    [InlineData("Local", DeployProfile.Local)]
    [InlineData("LOCAL", DeployProfile.Local)]
    [InlineData("server", DeployProfile.Server)]
    [InlineData("Server", DeployProfile.Server)]
    [InlineData("SERVER", DeployProfile.Server)]
    [InlineData("sync", DeployProfile.Sync)]
    [InlineData("Sync", DeployProfile.Sync)]
    [InlineData("SYNC", DeployProfile.Sync)]
    public void FromEnvironment_ValidValues_ReturnsCorrectProfile(string raw, DeployProfile expected)
    {
        var original = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", raw);
            var result = DeployProfileExtensions.FromEnvironment();
            Assert.Equal(expected, result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", original);
        }
    }

    [Theory]
    [InlineData("lokal")]
    [InlineData("unknown")]
    [InlineData("invalid")]
    [InlineData("sinc")]
    public void FromEnvironment_InvalidValue_Throws(string raw)
    {
        var original = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", raw);
            var ex = Assert.Throws<InvalidOperationException>(
                () => DeployProfileExtensions.FromEnvironment());
            Assert.Contains("Unknown profile", ex.Message);
            Assert.Contains("local, server, or sync", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", original);
        }
    }

    // ─── 2.4 ProfileDefaults.For() ─────────────────────────────────────────────

    [Fact]
    public void For_Local_HasCorrectKeysAndValues()
    {
        var defaults = ProfileDefaults.For(DeployProfile.Local);
        Assert.NotNull(defaults);
        Assert.Equal(2, defaults.Count);
        Assert.Equal("sqlite", defaults["ENGRAM_DB_TYPE"]);
        Assert.Equal("false", defaults["ENGRAM_SYNC_ENABLED"]);
    }

    [Fact]
    public void For_Server_HasCorrectKeysAndValues()
    {
        var defaults = ProfileDefaults.For(DeployProfile.Server);
        Assert.NotNull(defaults);
        Assert.Equal(2, defaults.Count);
        Assert.Equal("postgres", defaults["ENGRAM_DB_TYPE"]);
        Assert.Equal("false", defaults["ENGRAM_SYNC_ENABLED"]);
    }

    [Fact]
    public void For_Sync_HasCorrectKeysAndValues()
    {
        var defaults = ProfileDefaults.For(DeployProfile.Sync);
        Assert.NotNull(defaults);
        Assert.Equal(4, defaults.Count);
        Assert.Equal("postgres", defaults["ENGRAM_DB_TYPE"]);
        Assert.Equal("true", defaults["ENGRAM_SYNC_ENABLED"]);
        Assert.Equal("30", defaults["ENGRAM_SYNC_POLL_SECONDS"]);
        Assert.Equal("cloud", defaults["ENGRAM_SYNC_TARGET"]);
    }

    // ─── 2.5 ProfileValidator.Validate() ───────────────────────────────────────
    // Note: Validate() now takes StoreConfig (not DeployProfile) because it validates
    // the effective configuration after merge.

    [Fact]
    public void Validate_LocalWithSqlite_Passes()
    {
        // Local + SQLite: no required vars
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "local");
        Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", "sqlite");
        var cfg = StoreConfig.FromEnvironment();
        ProfileValidator.Validate(cfg); // Should not throw
    }

    [Fact]
    public void Validate_ServerWithPostgresMissingConnection_Throws()
    {
        var originalPg = Environment.GetEnvironmentVariable("ENGRAM_PG_CONNECTION");
        var originalUser = Environment.GetEnvironmentVariable("ENGRAM_USER");
        var originalProfile = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "server");
            Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", "postgres");
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", null);
            Environment.SetEnvironmentVariable("ENGRAM_USER", "test-user");

            var cfg = StoreConfig.FromEnvironment();
            var ex = Assert.Throws<InvalidOperationException>(
                () => ProfileValidator.Validate(cfg));
            Assert.Contains("ENGRAM_PG_CONNECTION", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", originalPg);
            Environment.SetEnvironmentVariable("ENGRAM_USER", originalUser);
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", originalProfile);
        }
    }

    [Fact]
    public void Validate_ServerWithPostgresAllSet_Passes()
    {
        var originalPg = Environment.GetEnvironmentVariable("ENGRAM_PG_CONNECTION");
        var originalUser = Environment.GetEnvironmentVariable("ENGRAM_USER");
        var originalProfile = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "server");
            Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", "postgres");
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", "Host=localhost;Database=test");
            Environment.SetEnvironmentVariable("ENGRAM_USER", "test-user");

            var cfg = StoreConfig.FromEnvironment();
            ProfileValidator.Validate(cfg); // Should not throw
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", originalPg);
            Environment.SetEnvironmentVariable("ENGRAM_USER", originalUser);
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", originalProfile);
        }
    }

    [Fact]
    public void Validate_SyncWithPostgresMissingServerUrl_Throws()
    {
        var originalPg = Environment.GetEnvironmentVariable("ENGRAM_PG_CONNECTION");
        var originalUrl = Environment.GetEnvironmentVariable("ENGRAM_SERVER_URL");
        var originalUser = Environment.GetEnvironmentVariable("ENGRAM_USER");
        var originalProfile = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "sync");
            Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", "postgres");
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", "Host=localhost;Database=test");
            Environment.SetEnvironmentVariable("ENGRAM_SERVER_URL", null);
            Environment.SetEnvironmentVariable("ENGRAM_USER", "test-user");

            var cfg = StoreConfig.FromEnvironment();
            var ex = Assert.Throws<InvalidOperationException>(
                () => ProfileValidator.Validate(cfg));
            Assert.Contains("ENGRAM_SERVER_URL", ex.Message);
            // PG_CONNECTION is set, so only SERVER_URL is reported as missing
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", originalPg);
            Environment.SetEnvironmentVariable("ENGRAM_SERVER_URL", originalUrl);
            Environment.SetEnvironmentVariable("ENGRAM_USER", originalUser);
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", originalProfile);
        }
    }

    [Fact]
    public void Validate_SyncAllSet_Passes()
    {
        var originalPg = Environment.GetEnvironmentVariable("ENGRAM_PG_CONNECTION");
        var originalUrl = Environment.GetEnvironmentVariable("ENGRAM_SERVER_URL");
        var originalUser = Environment.GetEnvironmentVariable("ENGRAM_USER");
        var originalProfile = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "sync");
            Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", "postgres");
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", "Host=localhost;Database=test");
            Environment.SetEnvironmentVariable("ENGRAM_SERVER_URL", "https://engram.example.com");
            Environment.SetEnvironmentVariable("ENGRAM_USER", "test-user");

            var cfg = StoreConfig.FromEnvironment();
            ProfileValidator.Validate(cfg); // Should not throw
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", originalPg);
            Environment.SetEnvironmentVariable("ENGRAM_SERVER_URL", originalUrl);
            Environment.SetEnvironmentVariable("ENGRAM_USER", originalUser);
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", originalProfile);
        }
    }
}
