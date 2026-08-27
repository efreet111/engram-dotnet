using System.CommandLine;
using System.Text.Json;
using Xunit;

namespace Engram.Cli.Tests;

public sealed class SyncStatusCliTests
{
    [Fact]
    public async Task SyncStatus_WithJsonFlag_ParsesOption()
    {
        var root = new RootCommand();
        var syncCmd = new Command("sync", "Sync operations");
        var syncStatusCmd = new Command("status", "Show mutation-based sync status");
        var jsonOpt = new Option<bool>("--json") { Description = "Output as JSON (machine-readable)", DefaultValueFactory = _ => false };
        syncStatusCmd.Options.Add(jsonOpt);
        syncCmd.Subcommands.Add(syncStatusCmd);
        root.Subcommands.Add(syncCmd);

        var result = root.Parse("sync status --json");
        var jsonValue = result.GetValue(jsonOpt);

        Assert.True(jsonValue);
    }

    [Fact]
    public async Task SyncStatus_WithoutJsonFlag_DefaultsToFalse()
    {
        var root = new RootCommand();
        var syncCmd = new Command("sync", "Sync operations");
        var syncStatusCmd = new Command("status", "Show mutation-based sync status");
        var jsonOpt = new Option<bool>("--json") { Description = "Output as JSON (machine-readable)", DefaultValueFactory = _ => false };
        syncStatusCmd.Options.Add(jsonOpt);
        syncCmd.Subcommands.Add(syncStatusCmd);
        root.Subcommands.Add(syncCmd);

        var result = root.Parse("sync status");
        var jsonValue = result.GetValue(jsonOpt);

        Assert.False(jsonValue);
    }

    [Fact]
    public void SyncStatus_WithLocalFlag_ParsesOption()
    {
        var syncStatusCmd = new Command("status", "Show mutation-based sync status");
        var localOpt = new Option<bool>("--local") { Description = "Show local enrollment status (no server required)" };
        var jsonOpt = new Option<bool>("--json") { Description = "Output as JSON (machine-readable)", DefaultValueFactory = _ => false };
        syncStatusCmd.Options.Add(localOpt);
        syncStatusCmd.Options.Add(jsonOpt);

        var result = syncStatusCmd.Parse("--local");

        Assert.True(result.GetValue(localOpt));
        Assert.False(result.GetValue(jsonOpt));
    }

    [Fact]
    public void SyncStatus_WithLocalAndJson_ParsesBoth()
    {
        var syncStatusCmd = new Command("status", "Show mutation-based sync status");
        var localOpt = new Option<bool>("--local") { Description = "Show local enrollment status (no server required)" };
        var jsonOpt = new Option<bool>("--json") { Description = "Output as JSON (machine-readable)", DefaultValueFactory = _ => false };
        syncStatusCmd.Options.Add(localOpt);
        syncStatusCmd.Options.Add(jsonOpt);

        var result = syncStatusCmd.Parse("--local --json");

        Assert.True(result.GetValue(localOpt));
        Assert.True(result.GetValue(jsonOpt));
    }

    [Fact]
    public void SyncStatus_WithoutLocalFlag_DefaultsToFalse()
    {
        var syncStatusCmd = new Command("status", "Show mutation-based sync status");
        var localOpt = new Option<bool>("--local") { Description = "Show local enrollment status (no server required)" };
        syncStatusCmd.Options.Add(localOpt);

        var result = syncStatusCmd.Parse("");

        Assert.False(result.GetValue(localOpt));
    }

    [Fact]
    public async Task SyncStatus_WithServerOffline_ShowsErrorMessage()
    {
        var syncStatusCmd = new Command("status", "Show mutation-based sync status");
        var jsonOpt = new Option<bool>("--json") { Description = "Output as JSON (machine-readable)", DefaultValueFactory = _ => false };
        syncStatusCmd.Options.Add(jsonOpt);

        var errorOut = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(errorOut);

        syncStatusCmd.SetAction(async (ParseResult _) =>
        {
            var serverUrl = "http://localhost:1";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
                var response = await client.GetAsync($"{serverUrl}/sync/status");
                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException)
            {
                await Console.Error.WriteLineAsync("error: No se pudo conectar al servidor — ¿está engram server corriendo?");
            }
            catch (TaskCanceledException)
            {
                await Console.Error.WriteLineAsync("error: No se pudo conectar al servidor — ¿está engram server corriendo? (timeout)");
            }
        });

        await syncStatusCmd.Parse("").InvokeAsync();

        Console.SetError(originalError);

        var output = errorOut.ToString();
        Assert.Contains("no se pudo conectar al servidor", output, StringComparison.OrdinalIgnoreCase);

        errorOut.Dispose();
    }

    [Fact]
    public void CliOutput_ShowsSuggestedAction_WhenPresent()
    {
        var output = FormatStatus("degraded", "Check server connectivity.");

        Assert.Contains("💡 Suggested action:", output, StringComparison.Ordinal);
        Assert.Contains("Check server connectivity.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CliOutput_WarningOnBlocked()
    {
        var output = FormatStatus("blocked", "Enroll the project.", pendingPush: 38);

        Assert.Contains("⚠️ WARNING: Sync blocked", output, StringComparison.Ordinal);
        Assert.Contains("data is NOT being synchronized!", output, StringComparison.Ordinal);
        Assert.Contains("Pending mutations: 38", output, StringComparison.Ordinal);
    }

    [Fact]
    public void CliOutput_NoExtraOnHealthy()
    {
        var output = FormatStatus("healthy", null);

        Assert.DoesNotContain("💡", output, StringComparison.Ordinal);
        Assert.DoesNotContain("⚠️", output, StringComparison.Ordinal);
    }

    private static string FormatStatus(string status, string? suggestedAction, int pendingPush = 0)
    {
        var json = JsonSerializer.Serialize(new
        {
            sync_enabled = true,
            phase = status,
            health = new
            {
                status,
                consecutive_failures = 0,
                backoff_until = (string?)null,
                last_sync_at = (string?)null,
                last_error = (string?)null,
                suggested_action = suggestedAction
            },
            counts = new
            {
                pending_push = pendingPush,
                total_pushed = 0,
                total_pulled = 0
            },
            cursor = new
            {
                last_pushed_seq = 0,
                last_pulled_seq = 0
            }
        });
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        using var writer = new StringWriter();

        SyncStatusFormatter.Write(doc, writer);

        return writer.ToString();
    }
}
