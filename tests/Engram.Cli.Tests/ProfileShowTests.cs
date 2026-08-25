using System.CommandLine;
using System.Text.Json;
using Engram.Cli;
using Engram.Store;
using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// HU-023: Tests for `engram profile show` — active profile rendering (text + JSON),
/// effective-variable sources, and the shared <see cref="ProfileConfig"/> logic it relies on.
/// </summary>
[Collection("EnvSensitive")]
public sealed class ProfileShowTests : IDisposable
{
    private readonly string _configDir;

    public ProfileShowTests()
    {
        _configDir = Path.Combine(Path.GetTempPath(), "engram-profile-show", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_configDir);
        Environment.SetEnvironmentVariable("ENGRAM_CONFIG_DIR", _configDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ENGRAM_CONFIG_DIR", null);
        try { Directory.Delete(_configDir, recursive: true); } catch { /* best-effort */ }
    }

    private static async Task<(string Out, string Err)> InvokeAsync(string args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            await ProfileCommandTree.Build().Parse(args).InvokeAsync();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
        return (stdout.ToString(), stderr.ToString());
    }

    // ─── Rendering ────────────────────────────────────────────────────────

    [Fact]
    public async Task Show_NoEnvProfile_PrintsLocalDefault()
    {
        using var env = new EnvVarScope().Set("ENGRAM_PROFILE", null);
        var (output, _) = await InvokeAsync("profile show");
        Assert.Contains("Profile: local (default)", output);
    }

    [Fact]
    public async Task Show_EnvProfileOfflineFirst_PrintsLabel()
    {
        using var env = new EnvVarScope().Set("ENGRAM_PROFILE", "offline-first");
        var (output, _) = await InvokeAsync("profile show");
        Assert.Contains("Profile: offline-first", output);
    }

    [Fact]
    public async Task Show_ListsEffectiveVariables()
    {
        using var env = new EnvVarScope().Set("ENGRAM_PROFILE", null);
        var (output, _) = await InvokeAsync("profile show");
        Assert.Contains("Effective variables:", output);
        Assert.Contains("ENGRAM_DB_TYPE", output);
        Assert.Contains("ENGRAM_SYNC_ENABLED", output);
        Assert.Contains("[profile-default]", output);
    }

    [Fact]
    public async Task Show_Json_EmitsProfileAndVariables()
    {
        using var env = new EnvVarScope().Set("ENGRAM_PROFILE", null);
        var (output, _) = await InvokeAsync("profile show --json");

        var json = JsonSerializer.Deserialize<JsonElement>(output.Trim());
        Assert.Equal("local", json.GetProperty("profile").GetString());
        Assert.True(json.GetProperty("is_default").GetBoolean());

        var dbType = json.GetProperty("variables").GetProperty("ENGRAM_DB_TYPE");
        Assert.Equal("sqlite", dbType.GetProperty("value").GetString());
        Assert.Equal("profile-default", dbType.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Show_Override_MarksOverrideSource()
    {
        using var env = new EnvVarScope().Set("ENGRAM_PROFILE", null).Set("ENGRAM_DB_TYPE", "postgres");
        var (output, _) = await InvokeAsync("profile show --json");

        var dbType = JsonSerializer.Deserialize<JsonElement>(output.Trim())
            .GetProperty("variables").GetProperty("ENGRAM_DB_TYPE");
        Assert.Equal("postgres", dbType.GetProperty("value").GetString());
        Assert.Equal("override", dbType.GetProperty("source").GetString());
    }

    [Fact(Skip = "Desktop profile deferred — see HU-024 and ADR-014")]
    public async Task Show_WithConfigFile_ReportsFileAndDeclaredProfile()
    {
        using var env = new EnvVarScope().Set("ENGRAM_PROFILE", null);
        var envPath = Path.Combine(_configDir, ".env");
        File.WriteAllText(envPath, "ENGRAM_PROFILE=desktop\n");

        var (output, _) = await InvokeAsync("profile show");
        Assert.Contains("Config file:", output);
        Assert.Contains("declares: desktop", output);
    }

    [Fact]
    public async Task Show_WithoutConfigFile_ReportsUsingEnvVars()
    {
        using var env = new EnvVarScope().Set("ENGRAM_PROFILE", null);
        var (output, _) = await InvokeAsync("profile show");
        Assert.Contains("not found", output);
        Assert.Contains("environment variables", output);
    }

    // ─── Shared ProfileConfig logic ───────────────────────────────────────

    [Fact(Skip = "Desktop profile deferred — see HU-024 and ADR-014")]
    public void ParseProfileName_ValidNames_ReturnCorrectEnum()
    {
        Assert.Equal(DeployProfile.Local, ProfileConfig.ParseProfileName("local"));
        Assert.Equal(DeployProfile.RemoteServer, ProfileConfig.ParseProfileName("Remote-Server"));
        Assert.Equal(DeployProfile.OfflineFirst, ProfileConfig.ParseProfileName("offline-first"));
        Assert.Equal(DeployProfile.Desktop, ProfileConfig.ParseProfileName("DESKTOP"));
    }

    [Fact]
    public void ParseProfileName_InvalidName_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ProfileConfig.ParseProfileName("bogus"));
        Assert.Contains("bogus", ex.Message);
    }

    [Fact]
    public void ReadProfileFromFile_MissingFile_ReturnsNull()
    {
        Assert.Null(ProfileConfig.ReadProfileFromFile(Path.Combine(_configDir, "missing.env")));
    }

    [Fact]
    public void ReadProfileFromFile_IgnoresCommentsAndBlankLines()
    {
        var envPath = Path.Combine(_configDir, "custom.env");
        File.WriteAllText(envPath, "# comment\n\n  ENGRAM_PROFILE = offline-first  \n");
        Assert.Equal("offline-first", ProfileConfig.ReadProfileFromFile(envPath));
    }

    [Fact]
    public void StoreConfigForProfile_OfflineFirst_IsSqliteAndSyncEnabled()
    {
        using var env = new EnvVarScope().Set("ENGRAM_SYNC_ENABLED", null);
        var cfg = ProfileConfig.StoreConfigForProfile(DeployProfile.OfflineFirst);
        Assert.Equal(StoreDbType.Sqlite, cfg.DbType);
        Assert.True(cfg.IsSyncEnabled);
    }

    [Fact]
    public void StoreConfigForProfile_RemoteServer_IsPostgres()
    {
        var cfg = ProfileConfig.StoreConfigForProfile(DeployProfile.RemoteServer);
        Assert.Equal(StoreDbType.Postgres, cfg.DbType);
        Assert.True(cfg.IsPostgres);
    }
}
