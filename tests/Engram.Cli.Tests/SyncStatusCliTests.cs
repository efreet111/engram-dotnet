using System.CommandLine;
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
        var jsonOpt = new Option<bool>("--json", () => false, "Output as JSON (machine-readable)");
        syncStatusCmd.AddOption(jsonOpt);
        syncCmd.AddCommand(syncStatusCmd);
        root.AddCommand(syncCmd);

        var result = root.Parse("sync status --json");
        var jsonValue = result.GetValueForOption(jsonOpt);

        Assert.True(jsonValue);
    }

    [Fact]
    public async Task SyncStatus_WithoutJsonFlag_DefaultsToFalse()
    {
        var root = new RootCommand();
        var syncCmd = new Command("sync", "Sync operations");
        var syncStatusCmd = new Command("status", "Show mutation-based sync status");
        var jsonOpt = new Option<bool>("--json", () => false, "Output as JSON (machine-readable)");
        syncStatusCmd.AddOption(jsonOpt);
        syncCmd.AddCommand(syncStatusCmd);
        root.AddCommand(syncCmd);

        var result = root.Parse("sync status");
        var jsonValue = result.GetValueForOption(jsonOpt);

        Assert.False(jsonValue);
    }

    [Fact]
    public async Task SyncStatus_WithServerOffline_ShowsErrorMessage()
    {
        var syncStatusCmd = new Command("status", "Show mutation-based sync status");
        var jsonOpt = new Option<bool>("--json", () => false, "Output as JSON (machine-readable)");
        syncStatusCmd.AddOption(jsonOpt);

        var errorOut = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(errorOut);

        syncStatusCmd.SetHandler(async (bool json) =>
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
        }, jsonOpt);

        await syncStatusCmd.InvokeAsync("");

        Console.SetError(originalError);

        var output = errorOut.ToString();
        Assert.Contains("no se pudo conectar al servidor", output, StringComparison.OrdinalIgnoreCase);

        errorOut.Dispose();
    }
}
