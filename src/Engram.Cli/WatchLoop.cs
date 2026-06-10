using Engram.Obsidian;
using Engram.Store;

namespace Engram.Cli;

/// <summary>
/// Configuration for watch mode.
/// ENG-208 Phase 9.
/// </summary>
public class WatchConfig
{
    /// <summary>Path to the Obsidian vault root.</summary>
    public required string VaultPath { get; init; }

    /// <summary>Project filter (null for all projects).</summary>
    public string? Project { get; init; }

    /// <summary>Interval between export cycles.</summary>
    public TimeSpan Interval { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Initial --since value for first cycle only (then use state file).</summary>
    public DateTime? InitialSince { get; init; }

    /// <summary>The store reader to use for exports.</summary>
    public required IObsidianStoreReader StoreReader { get; init; }
}

/// <summary>
/// Watch loop that continuously exports at intervals.
/// ENG-208 Phase 9.
/// </summary>
public static class WatchLoop
{
    /// <summary>
    /// Runs the watch loop until cancellation.
    /// </summary>
    /// <param name="config">Watch configuration</param>
    /// <param name="ct">Cancellation token</param>
    public static async Task RunAsync(WatchConfig config, CancellationToken ct)
    {
        var stateFile = StateFile.ResolveStatePath(config.VaultPath, config.Project);

        // Read state once at start
        var state = StateFile.ReadState(stateFile);

        // Initial export (may use InitialSince for first cycle only)
        await RunCycleAsync(config, stateFile, state, config.InitialSince, ct);

        // Watch loop
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(config.Interval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            // Re-read state to determine export method
            state = StateFile.ReadState(stateFile);
            DateTime? sinceForCycle = null;

            // Seq-based if we have last_seq, otherwise timestamp-based
            if (state.LastSeq.HasValue && state.LastSeq > 0)
            {
                try
                {
                    // Try seq-based incremental export
                    var incrementalData = await config.StoreReader.ExportSinceAsync(config.Project, state.LastSeq.Value, 100);

                    // Update seq if response has it
                    if (incrementalData.NextSeq > 0)
                    {
                        state.LastSeq = incrementalData.NextSeq;
                    }

                    // Fallback to timestamp if no new data
                    if (incrementalData.Observations.Count == 0 && incrementalData.Sessions.Count == 0)
                    {
                        sinceForCycle = string.IsNullOrEmpty(state.LastExportAt)
                            ? null
                            : DateTime.Parse(state.LastExportAt);
                    }
                }
                catch
                {
                    // Server unreachable - fallback to timestamp
                    sinceForCycle = string.IsNullOrEmpty(state.LastExportAt)
                        ? null
                        : DateTime.Parse(state.LastExportAt);
                }
            }
            else
            {
                // No seq - use timestamp
                sinceForCycle = string.IsNullOrEmpty(state.LastExportAt)
                    ? null
                    : DateTime.Parse(state.LastExportAt);
            }

            // Run cycle with timestamp filter, passing state to preserve LastSeq
            await RunCycleAsync(config, stateFile, state, sinceForCycle, ct);
        }
    }

    /// <summary>
    /// Runs a single export cycle.
    /// </summary>
    private static async Task RunCycleAsync(WatchConfig config, string stateFile, Engram.Obsidian.SyncState state, DateTime? since, CancellationToken ct)
    {
        var exportConfig = new ExportConfig
        {
            VaultPath = config.VaultPath,
            Project = config.Project,
            IncludePersonal = false,
            Force = false,
            GraphConfig = GraphConfigMode.Preserve,
            Limit = 0,
            Since = since?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? "",
            Watch = true,
            Interval = config.Interval.ToString(@"hh\:mm\:ss"),
        };

        var exporter = new Exporter(config.StoreReader, exportConfig);

        try
        {
            var result = exporter.Export();

            // Log to stderr
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            await Console.Error.WriteLineAsync($"[watch] exported {result.Created} observations at {timestamp}");

            // Persist state with last_export_at (use passed-in state to preserve LastSeq)
            state.LastExportAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            StateFile.WriteState(stateFile, state);
        }
        catch (Exception ex)
        {
            // Log error to stderr and continue
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            await Console.Error.WriteLineAsync($"[watch] error at {timestamp}: {ex.Message}");

            // Still persist state so we don't re-export everything on next cycle
            try
            {
                state.LastExportAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                StateFile.WriteState(stateFile, state);
            }
            catch
            {
                // Best effort - ignore
            }
        }
    }
}