using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Unit tests for DeployProfileExtensions, ProfileDefaults, and ProfileValidator.
/// Design: sdd/deploy-profile-system/design/design.md
/// Tasks: HU-012
/// </summary>
[Collection("ENGRAM_PROFILE")]
public class DeployProfileTests
{
    // ─── DeployProfileExtensions.FromEnvironment() ─────────────────────────

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
    [InlineData("remote-server", DeployProfile.RemoteServer)]
    [InlineData("Remote-Server", DeployProfile.RemoteServer)]
    [InlineData("REMOTE-SERVER", DeployProfile.RemoteServer)]
    [InlineData("offline-first", DeployProfile.OfflineFirst)]
    [InlineData("Offline-First", DeployProfile.OfflineFirst)]
    [InlineData("OFFLINE-FIRST", DeployProfile.OfflineFirst)]
    [InlineData("desktop", DeployProfile.Desktop)]
    [InlineData("Desktop", DeployProfile.Desktop)]
    [InlineData("DESKTOP", DeployProfile.Desktop)]
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
    [InlineData("server")]
    [InlineData("sync")]
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
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", original);
        }
    }

    // ─── ProfileDefaults.For() ─────────────────────────────────────────────

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
    public void For_RemoteServer_HasCorrectKeysAndValues()
    {
        var defaults = ProfileDefaults.For(DeployProfile.RemoteServer);
        Assert.NotNull(defaults);
        Assert.Equal(2, defaults.Count);
        Assert.Equal("postgres", defaults["ENGRAM_DB_TYPE"]);
        Assert.Equal("false", defaults["ENGRAM_SYNC_ENABLED"]);
    }

    [Fact]
    public void For_OfflineFirst_HasCorrectKeysAndValues()
    {
        var defaults = ProfileDefaults.For(DeployProfile.OfflineFirst);
        Assert.NotNull(defaults);
        Assert.Equal(4, defaults.Count);
        Assert.Equal("sqlite", defaults["ENGRAM_DB_TYPE"]); // bugfix: was postgres, now sqlite
        Assert.Equal("true", defaults["ENGRAM_SYNC_ENABLED"]);
        Assert.Equal("30", defaults["ENGRAM_SYNC_POLL_SECONDS"]);
        Assert.Equal("cloud", defaults["ENGRAM_SYNC_TARGET"]);
    }

    [Fact]
    public void For_Desktop_HasCorrectKeysAndValues()
    {
        var defaults = ProfileDefaults.For(DeployProfile.Desktop);
        Assert.NotNull(defaults);
        Assert.Equal(4, defaults.Count);
        Assert.Equal("postgres", defaults["ENGRAM_DB_TYPE"]);
        Assert.Equal("true", defaults["ENGRAM_SYNC_ENABLED"]);
        Assert.Equal("30", defaults["ENGRAM_SYNC_POLL_SECONDS"]);
        Assert.Equal("cloud", defaults["ENGRAM_SYNC_TARGET"]);
    }

    // ─── ProfileValidator.Validate() ───────────────────────────────────────
    // Note: Validate() takes StoreConfig (not DeployProfile) because it validates
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
    public void Validate_RemoteServerWithPostgresMissingConnection_Throws()
    {
        var originalPg = Environment.GetEnvironmentVariable("ENGRAM_PG_CONNECTION");
        var originalProfile = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "remote-server");
            Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", "postgres");
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", null);

            var cfg = StoreConfig.FromEnvironment();
            var ex = Assert.Throws<InvalidOperationException>(
                () => ProfileValidator.Validate(cfg));
            Assert.Contains("ENGRAM_PG_CONNECTION", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", originalPg);
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", originalProfile);
        }
    }

    [Fact]
    public void Validate_RemoteServerWithPostgresAllSet_Passes()
    {
        var originalPg = Environment.GetEnvironmentVariable("ENGRAM_PG_CONNECTION");
        var originalProfile = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "remote-server");
            Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", "postgres");
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", "Host=db.example.com;Database=test");

            var cfg = StoreConfig.FromEnvironment();
            ProfileValidator.Validate(cfg); // Should not throw
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", originalPg);
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", originalProfile);
        }
    }

    [Fact]
    public void Validate_RemoteServerWithLocalhost_Throws()
    {
        var originalPg = Environment.GetEnvironmentVariable("ENGRAM_PG_CONNECTION");
        var originalProfile = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "remote-server");
            Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", "postgres");
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", "Host=localhost;Database=test");

            var cfg = StoreConfig.FromEnvironment();
            var ex = Assert.Throws<InvalidOperationException>(
                () => ProfileValidator.Validate(cfg));
            Assert.Contains("localhost", ex.Message);
            Assert.Contains("ENGRAM_PG_CONNECTION", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", originalPg);
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", originalProfile);
        }
    }

    [Fact]
    public void Validate_RemoteServerWithoutEngramUser_Passes()
    {
        // RemoteServer does NOT require ENGRAM_USER because clients identify
        // themselves via the X-Engram-User header on each request. The server
        // falls back to OS user name via StoreConfig.User.
        var originalPg = Environment.GetEnvironmentVariable("ENGRAM_PG_CONNECTION");
        var originalUser = Environment.GetEnvironmentVariable("ENGRAM_USER");
        var originalProfile = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "remote-server");
            Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", "postgres");
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", "Host=db.example.com;Database=test");
            Environment.SetEnvironmentVariable("ENGRAM_USER", null);

            var cfg = StoreConfig.FromEnvironment();
            ProfileValidator.Validate(cfg); // Should NOT throw — ENGRAM_USER is not required

            // Verify the fallback works
            Assert.NotNull(cfg.User); // Should fall back to OS user name
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", originalPg);
            Environment.SetEnvironmentVariable("ENGRAM_USER", originalUser);
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", originalProfile);
        }
    }

    [Theory]
    [InlineData("Host=127.0.0.1;Database=test")]
    [InlineData("Server=::1;Database=test")]
    [InlineData("Data Source=::1;Database=test")]
    public void Validate_RemoteServerWithLocalhostVariants_Throws(string connectionString)
    {
        var originalPg = Environment.GetEnvironmentVariable("ENGRAM_PG_CONNECTION");
        var originalProfile = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "remote-server");
            Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", "postgres");
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", connectionString);

            var cfg = StoreConfig.FromEnvironment();
            var ex = Assert.Throws<InvalidOperationException>(
                () => ProfileValidator.Validate(cfg));
            Assert.Contains("localhost", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", originalPg);
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", originalProfile);
        }
    }

    [Fact]
    public void Validate_OfflineFirstMissingServerUrl_Throws()
    {
        var originalUrl = Environment.GetEnvironmentVariable("ENGRAM_SERVER_URL");
        var originalUser = Environment.GetEnvironmentVariable("ENGRAM_USER");
        var originalProfile = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "offline-first");
            Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", "sqlite");
            Environment.SetEnvironmentVariable("ENGRAM_SERVER_URL", null);
            Environment.SetEnvironmentVariable("ENGRAM_USER", "test-user");

            var cfg = StoreConfig.FromEnvironment();
            var ex = Assert.Throws<InvalidOperationException>(
                () => ProfileValidator.Validate(cfg));
            Assert.Contains("ENGRAM_SERVER_URL", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_SERVER_URL", originalUrl);
            Environment.SetEnvironmentVariable("ENGRAM_USER", originalUser);
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", originalProfile);
        }
    }

    [Fact]
    public void Validate_OfflineFirstAllSet_Passes()
    {
        var originalUrl = Environment.GetEnvironmentVariable("ENGRAM_SERVER_URL");
        var originalUser = Environment.GetEnvironmentVariable("ENGRAM_USER");
        var originalProfile = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "offline-first");
            Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", "sqlite");
            Environment.SetEnvironmentVariable("ENGRAM_SERVER_URL", "https://engram.example.com");
            Environment.SetEnvironmentVariable("ENGRAM_USER", "test-user");

            var cfg = StoreConfig.FromEnvironment();
            ProfileValidator.Validate(cfg); // Should not throw
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENGRAM_SERVER_URL", originalUrl);
            Environment.SetEnvironmentVariable("ENGRAM_USER", originalUser);
            Environment.SetEnvironmentVariable("ENGRAM_PROFILE", originalProfile);
        }
    }
}
