using Engram.Store;
using Xunit;

namespace Engram.Store.Tests;

/// <summary>
/// Integration tests for <see cref="StoreConfig.FromEnvironment"/> merge precedence.
/// Design: sdd/deploy-profile-system/design/design.md
/// Tasks: Phase 3 (3.2)
/// 
/// Precedence rule: explicit env var > profile default > hardcoded default.
/// </summary>
public class StoreConfigMergeTests : IDisposable
{
    private readonly string? _originalProfile;
    private readonly string? _originalDbType;
    private readonly string? _originalPgConn;
    private readonly string? _originalUser;

    public StoreConfigMergeTests()
    {
        _originalProfile = Environment.GetEnvironmentVariable("ENGRAM_PROFILE");
        _originalDbType  = Environment.GetEnvironmentVariable("ENGRAM_DB_TYPE");
        _originalPgConn  = Environment.GetEnvironmentVariable("ENGRAM_PG_CONNECTION");
        _originalUser    = Environment.GetEnvironmentVariable("ENGRAM_USER");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", _originalProfile);
        Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", _originalDbType);
        Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", _originalPgConn);
        Environment.SetEnvironmentVariable("ENGRAM_USER", _originalUser);
    }

    // ─── Profile default > hardcoded ───────────────────────────────────────

    [Fact]
    public void FromEnvironment_ServerProfile_SetsDbTypeToPostgres()
    {
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "server");
        Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", null);

        var cfg = StoreConfig.FromEnvironment();

        Assert.Equal(DeployProfile.Server, cfg.Profile);
        Assert.Equal(StoreDbType.Postgres, cfg.DbType);
    }

    [Fact]
    public void FromEnvironment_NoProfile_DefaultsToSqlite()
    {
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", null);
        Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", null);

        var cfg = StoreConfig.FromEnvironment();

        Assert.Equal(DeployProfile.Local, cfg.Profile);
        Assert.Equal(StoreDbType.Sqlite, cfg.DbType);
    }

    // ─── Explicit env var > profile default ────────────────────────────────

    [Fact]
    public void FromEnvironment_ExplicitDbType_OverridesProfileDefault()
    {
        // Server profile defaults to postgres, but explicit sqlite wins
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "server");
        Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", "sqlite");

        var cfg = StoreConfig.FromEnvironment();

        Assert.Equal(DeployProfile.Server, cfg.Profile);
        Assert.Equal(StoreDbType.Sqlite, cfg.DbType); // explicit wins over profile default
    }

    [Fact]
    public void FromEnvironment_LocalProfile_ExplicitPostgres_Overrides()
    {
        // Local profile defaults to sqlite, but explicit postgres wins
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "local");
        Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", "postgres");

        var cfg = StoreConfig.FromEnvironment();

        Assert.Equal(DeployProfile.Local, cfg.Profile);
        Assert.Equal(StoreDbType.Postgres, cfg.DbType); // explicit wins over profile default
    }

    [Fact]
    public void FromEnvironment_ExplicitUser_OverridesProfileDefault()
    {
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "server");
        Environment.SetEnvironmentVariable("ENGRAM_USER", "explicit-user");

        var cfg = StoreConfig.FromEnvironment();

        Assert.Equal("explicit-user", cfg.User);
    }

    // ─── Full precedence chain: env > profile > hardcoded ─────────────────

    [Fact]
    public void FromEnvironment_ProfileSync_ExplicitPgConn_Overrides()
    {
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "sync");
        Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", "Host=override;Database=test");

        var cfg = StoreConfig.FromEnvironment();

        Assert.Equal("Host=override;Database=test", cfg.PgConnectionString);
    }

    [Fact]
    public void FromEnvironment_ProfileServer_NoExplicitVars_UsesProfileDefaults()
    {
        // Server profile set, but no explicit vars — profile defaults should be used
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "server");
        Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", null);
        Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", null);
        Environment.SetEnvironmentVariable("ENGRAM_USER", null);

        var cfg = StoreConfig.FromEnvironment();

        Assert.Equal(DeployProfile.Server, cfg.Profile);
        Assert.Equal(StoreDbType.Postgres, cfg.DbType);
        Assert.Null(cfg.PgConnectionString); // profile default is null (not in ProfileDefaults)
        Assert.Equal(Environment.UserName, cfg.User); // falls back to OS user
    }

    [Fact]
    public void FromEnvironment_MixedExplicitAndProfile_PrecedenceHolds()
    {
        // Sync profile with one explicit override, one from profile, one from hardcoded
        Environment.SetEnvironmentVariable("ENGRAM_PROFILE", "sync");
        Environment.SetEnvironmentVariable("ENGRAM_DB_TYPE", null);      // → profile: postgres
        Environment.SetEnvironmentVariable("ENGRAM_PG_CONNECTION", "Host=explicit;Database=db"); // explicit
        Environment.SetEnvironmentVariable("ENGRAM_USER", null);         // → hardcoded OS user

        var cfg = StoreConfig.FromEnvironment();

        Assert.Equal(DeployProfile.Sync, cfg.Profile);
        Assert.Equal(StoreDbType.Postgres, cfg.DbType);                  // profile default
        Assert.Equal("Host=explicit;Database=db", cfg.PgConnectionString); // explicit env
        Assert.Equal(Environment.UserName, cfg.User);                     // hardcoded fallback
    }
}
