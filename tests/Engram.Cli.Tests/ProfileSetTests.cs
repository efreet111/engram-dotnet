using System.CommandLine;
using System.Text.Json;
using Engram.Cli;
using Engram.Store;
using Xunit;

namespace Engram.Cli.Tests;

/// <summary>
/// HU-023: Tests for `engram profile set <profile>` — .env generation, backup, idempotency,
/// --dry-run/--json flags, invalid-name handling, and the shared <see cref="ProfileConfig"/> logic.
/// </summary>
[Collection("EnvSensitive")]
public sealed class ProfileSetTests : IDisposable
{
    private readonly string _configDir;
    private string EnvPath => Path.Combine(_configDir, ".env");
    private string BackupPath => EnvPath + ".bak";

    public ProfileSetTests()
    {
        _configDir = Path.Combine(Path.GetTempPath(), "engram-profile-set", Guid.NewGuid().ToString("N"));
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

    // ─── Rendering / command behavior ─────────────────────────────────────

    [Fact]
    public async Task Set_WritesEnvFileWithProfileAndDefaults()
    {
        using var env = new EnvVarScope().Set("ENGRAM_PG_CONNECTION", null).Set("ENGRAM_SERVER_URL", null);
        var (output, _) = await InvokeAsync("profile set offline-first");

        Assert.Contains("Profile set to 'offline-first'", output);
        Assert.True(File.Exists(EnvPath));

        var content = File.ReadAllText(EnvPath);
        Assert.Contains("ENGRAM_PROFILE=offline-first", content);
        Assert.Contains("ENGRAM_DB_TYPE=sqlite", content);
        Assert.Contains("ENGRAM_SYNC_ENABLED=true", content);
    }

    [Fact]
    public async Task Set_Overwrite_CreatesBackupWithOldContent()
    {
        File.WriteAllText(EnvPath, "ENGRAM_PROFILE=local\nCUSTOM_VAR=keepme\n");
        var (output, _) = await InvokeAsync("profile set desktop");

        Assert.Contains("Profile set to 'desktop'", output);
        Assert.True(File.Exists(BackupPath));
        Assert.Contains("CUSTOM_VAR=keepme", File.ReadAllText(BackupPath));
    }

    [Fact]
    public async Task Set_Idempotent_NoChanges()
    {
        await InvokeAsync("profile set remote-server");
        var before = File.ReadAllText(EnvPath);

        var (output, _) = await InvokeAsync("profile set remote-server");
        Assert.Contains("already set", output);
        Assert.Equal(before, File.ReadAllText(EnvPath));
    }

    [Fact]
    public async Task Set_DryRun_DoesNotWrite()
    {
        var (output, _) = await InvokeAsync("profile set desktop --dry-run");
        Assert.Contains("[DRY RUN]", output);
        Assert.False(File.Exists(EnvPath));
    }

    [Fact]
    public async Task Set_Json_EmitsJson()
    {
        using var env = new EnvVarScope().Set("ENGRAM_PG_CONNECTION", null).Set("ENGRAM_SERVER_URL", null);
        var (output, _) = await InvokeAsync("profile set offline-first --json");

        var json = JsonSerializer.Deserialize<JsonElement>(output.Trim());
        Assert.Equal("offline-first", json.GetProperty("profile").GetString());
        Assert.True(json.GetProperty("changed").GetBoolean());
        Assert.False(json.GetProperty("dry_run").GetBoolean());
        Assert.False(json.GetProperty("already_set").GetBoolean());
        Assert.Contains("ENGRAM_SERVER_URL", json.GetProperty("missing_variables").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task Set_Json_DryRun_ChangedFalse()
    {
        var (output, _) = await InvokeAsync("profile set desktop --json --dry-run");
        var json = JsonSerializer.Deserialize<JsonElement>(output.Trim());
        Assert.Equal("desktop", json.GetProperty("profile").GetString());
        Assert.True(json.GetProperty("dry_run").GetBoolean());
        Assert.False(json.GetProperty("changed").GetBoolean());
        Assert.False(File.Exists(EnvPath));
    }

    [Fact]
    public async Task Set_InvalidName_PrintsError()
    {
        var (_, err) = await InvokeAsync("profile set bogus");
        Assert.Contains("error:", err);
        Assert.Contains("bogus", err);
        Assert.False(File.Exists(EnvPath));
    }

    [Fact]
    public async Task Set_RemoteServer_ReportsMissingPgConnection()
    {
        using var env = new EnvVarScope().Set("ENGRAM_PG_CONNECTION", null);
        var (output, _) = await InvokeAsync("profile set remote-server");
        Assert.Contains("ENGRAM_PG_CONNECTION", output);
    }

    // ─── Shared ProfileConfig logic ───────────────────────────────────────

    [Fact]
    public void WriteProfile_CreatesMissingDirectory()
    {
        var envPath = Path.Combine(_configDir, "sub", "nested", ".env");
        ProfileConfig.WriteProfile(DeployProfile.Local, envPath);
        Assert.True(File.Exists(envPath));
        Assert.Contains("ENGRAM_PROFILE=local", File.ReadAllText(envPath));
    }

    [Fact]
    public void WriteProfile_BacksUpExisting()
    {
        File.WriteAllText(EnvPath, "OLD=1\n");
        ProfileConfig.WriteProfile(DeployProfile.Desktop, EnvPath);
        Assert.Equal("OLD=1\n", File.ReadAllText(BackupPath));
    }

    [Fact]
    public void IsProfileAlreadySet_CaseInsensitive()
    {
        File.WriteAllText(EnvPath, "ENGRAM_PROFILE=Desktop\n");
        Assert.True(ProfileConfig.IsProfileAlreadySet(DeployProfile.Desktop, EnvPath));
        Assert.False(ProfileConfig.IsProfileAlreadySet(DeployProfile.Local, EnvPath));
    }
}
