using System.Collections.Concurrent;
using Engram.Store;

namespace Engram.Cli;

/// <summary>
/// Configuration for the code-aware file watch loop.
/// HU-053 (ENG-483) — named <c>FileWatchConfig</c>/<c>FileWatchLoop</c> to avoid collision
/// with the obsidian-export <see cref="WatchConfig"/>/<see cref="WatchLoop"/> (ENG-208).
/// </summary>
public record FileWatchConfig
{
    /// <summary>Resolved absolute paths to watch.</summary>
    public required IReadOnlyList<string> Targets { get; init; }

    /// <summary>Original positional arguments (for the startup message format).</summary>
    public required string[] Paths { get; init; }

    /// <summary>Minimum changed lines to trigger a capture (comparison <c>&gt;=</c>).</summary>
    public required int Threshold { get; init; }

    /// <summary>Simple glob patterns to exclude (repeatable).</summary>
    public required string[] IgnorePatterns { get; init; }

    /// <summary>Resolved, normalized project name.</summary>
    public required string Project { get; init; }

    /// <summary>Session id shared by all captures of this invocation (<c>watch-{ts}</c>).</summary>
    public required string SessionId { get; init; }

    /// <summary>Current working directory (for relative-path normalization).</summary>
    public required string Cwd { get; init; }
}

/// <summary>
/// Event-driven file watch loop. Wires one <see cref="FileSystemWatcher"/> per unique
/// directory, debounces bursts (2s trailing per file), evaluates positional diffs against
/// baselines, and captures code changes via the canonical store chain. Baselines only
/// update on capture, so sub-threshold changes accumulate (FR-002).
/// </summary>
public static class FileWatchLoop
{
    private const int DebounceMs = 2000;
    private const int MaxFileBytes = 10 * 1024 * 1024; // 10 MB cap (NFR-001)
    private const int ReadRetries = 3;
    private const int ReadRetryDelayMs = 500;
    private const int WatcherBufferSize = 64 * 1024; // 64 KB (NFR-001)

    /// <summary>
    /// Runs the watch loop until cancellation. Returns the number of memories captured.
    /// Opens the store and creates the session once at startup (fail-loud — exceptions
    /// propagate to the caller). A capture in flight at cancellation completes before
    /// the watchers/store are disposed (FR-008).
    /// </summary>
    public static async Task<int> RunAsync(FileWatchConfig config, CancellationToken ct)
    {
        using var store = OpenStore(StoreConfig.FromEnvironment());
        await store.CreateSessionAsync(config.SessionId, config.Project, "");

        var captured = 0;
        var baselines = new ConcurrentDictionary<string, string[]?>(StringComparer.Ordinal);
        var timers = new ConcurrentDictionary<string, Timer>(StringComparer.Ordinal);
        var inFlight = new List<Task>();
        var inFlightLock = new object();
        var targetSet = new HashSet<string>(config.Targets, StringComparer.Ordinal);

        // 1. Read baselines (skip oversized/unreadable with ⚠, FR-009/NFR-001)
        var watched = 0;
        foreach (var path in config.Targets)
        {
            if (IsTooLarge(path))
            {
                Console.WriteLine($"⚠ Skipping file over 10 MB: {WatchCommand.NormalizeRelPath(path, config.Cwd)}");
                continue;
            }
            try
            {
                baselines[path] = File.ReadAllLines(path);
                watched++;
            }
            catch (Exception)
            {
                Console.WriteLine($"⚠ Could not read baseline: {WatchCommand.NormalizeRelPath(path, config.Cwd)}");
            }
        }

        // 2. Startup output (after baselines, before watchers — §6.4.1)
        Console.WriteLine(FormatStartup(config.Paths, watched));

        if (watched == 0)
        {
            return captured;
        }

        // 3. One FileSystemWatcher per unique directory (dedup), membership check in handler
        var directories = config.Targets
            .Select(p => Path.GetDirectoryName(p))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var watchers = new List<FileSystemWatcher>();
        foreach (var dir in directories)
        {
            var watcher = new FileSystemWatcher(dir!)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                InternalBufferSize = WatcherBufferSize,
                IncludeSubdirectories = false,
            };
            watcher.Changed += (_, e) => OnFileEvent(e.FullPath);
            watcher.Created += (_, e) => OnFileEvent(e.FullPath);
            watcher.Deleted += (_, e) => OnDeleted(e.FullPath);
            watcher.Renamed += (_, e) => { OnDeleted(e.OldFullPath); OnFileEvent(e.FullPath); };
            watcher.Error += (_, e) => Console.Error.WriteLine($"⚠ FileSystemWatcher error: {e.GetException().Message}");
            watcher.EnableRaisingEvents = true;
            watchers.Add(watcher);
        }

        // 4. Wait for cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, ct);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }

        // 5. Await in-flight captures before disposing the store (FR-008 B)
        Task[] pending;
        lock (inFlightLock) { pending = inFlight.ToArray(); }
        try { await Task.WhenAll(pending); } catch { /* best-effort */ }

        // 6. Dispose watchers + timers
        foreach (var w in watchers) w.Dispose();
        foreach (var t in timers.Values) t.Dispose();

        return captured;

        // ─── local functions ────────────────────────────────────────────────

        void OnFileEvent(string fullPath)
        {
            fullPath = Path.GetFullPath(fullPath);
            if (!targetSet.Contains(fullPath)) return; // membership check (§6.4.2)
            if (WatchCommand.IsBinaryFile(fullPath)) return;
            if (WatchCommand.MatchesIgnorePattern(fullPath, config.Cwd, config.IgnorePatterns)) return;
            ScheduleDebounce(fullPath);
        }

        void OnDeleted(string fullPath)
        {
            fullPath = Path.GetFullPath(fullPath);
            if (!targetSet.Contains(fullPath)) return;
            baselines.TryRemove(fullPath, out _);
            if (timers.TryRemove(fullPath, out var timer)) timer.Dispose();
        }

        void ScheduleDebounce(string fullPath)
        {
            if (timers.TryGetValue(fullPath, out var existing))
            {
                existing.Change(DebounceMs, Timeout.Infinite); // trailing: reset the window
                return;
            }

            var timer = new Timer(_ =>
            {
                if (timers.TryRemove(fullPath, out var t)) t.Dispose();
                var task = EvaluateAsync(fullPath);
                lock (inFlightLock) { inFlight.Add(task); }
                _ = task.ContinueWith(
                    t2 => { lock (inFlightLock) { inFlight.Remove(t2); } },
                    TaskScheduler.Default);
            }, null, DebounceMs, Timeout.Infinite);

            if (!timers.TryAdd(fullPath, timer))
            {
                // Raced with a concurrent event — refresh the existing timer.
                timer.Dispose();
                if (timers.TryGetValue(fullPath, out var raced)) raced.Change(DebounceMs, Timeout.Infinite);
            }
        }

        async Task EvaluateAsync(string fullPath)
        {
            if (IsTooLarge(fullPath))
            {
                Console.WriteLine($"⚠ Skipping file over 10 MB: {WatchCommand.NormalizeRelPath(fullPath, config.Cwd)}");
                baselines.TryRemove(fullPath, out _);
                return;
            }

            var current = ReadWithRetry(fullPath);
            if (current is null)
            {
                // File locked — reschedule so the change is not lost (§6.4.4)
                ScheduleDebounce(fullPath);
                return;
            }

            // baseline null → new file: all lines count as "added"
            baselines.TryGetValue(fullPath, out var baseline);
            var (modified, added, removed) = WatchCommand.CountChangedLines(baseline ?? [], current);
            var total = modified + added + removed;

            if (total >= config.Threshold)
            {
                var relPath = WatchCommand.NormalizeRelPath(fullPath, config.Cwd);
                var title = $"Changed: {relPath}";
                var content = WatchCommand.BuildChangeContent(
                    relPath,
                    modified,
                    added,
                    removed,
                    config.Threshold,
                    WatchCommand.BuildSnippets(baseline ?? [], current));

                var id = await store.AddObservationAsync(new AddObservationParams
                {
                    SessionId = config.SessionId,
                    Type = "code_change",
                    Title = title,
                    Content = content,
                    Project = config.Project,
                    FilePath = relPath,
                    TopicKey = $"code-change:{relPath}",
                });

                Interlocked.Increment(ref captured);
                baselines[fullPath] = current; // baseline updates only on capture
                Console.WriteLine($"✓ Memory saved: #{id} \"{title}\" (code_change) [project: {config.Project}]");
            }
            // else: sub-threshold — baseline NOT updated (changes accumulate)
        }
    }

    private static string[]? ReadWithRetry(string fullPath)
    {
        for (var attempt = 0; attempt < ReadRetries; attempt++)
        {
            try
            {
                return File.ReadAllLines(fullPath);
            }
            catch (IOException)
            {
                Thread.Sleep(ReadRetryDelayMs);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(ReadRetryDelayMs);
            }
        }
        return null;
    }

    private static bool IsTooLarge(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length > MaxFileBytes;
        }
        catch
        {
            return true; // cannot stat → treat as unreadable/oversized
        }
    }

    private static string FormatStartup(string[] paths, int count)
    {
        if (paths.Length == 1)
        {
            var p = paths[0];
            if (WatchCommand.HasGlobChars(p)) return $"👀 Watching {count} files matching \"{p}\"...";
            if (Directory.Exists(p)) return $"👀 Watching {count} files in {p}...";
            return $"👀 Watching {p}...";
        }
        return $"👀 Watching {count} files...";
    }

    /// <summary>
    /// Replicates the store-opening logic of <c>Program.cs</c>'s local <c>OpenStore</c>
    /// (the local function is not reusable from here). Thin-client → HttpStore, else
    /// Sqlite/Postgres by <see cref="StoreDbType"/>. Fail-loud on invalid profile/connection.
    /// </summary>
    private static IStore OpenStore(StoreConfig cfg)
    {
        ProfileValidator.Validate(cfg);

        if (cfg.IsThinClient)
            return new HttpStore(cfg);

        if (cfg.IsPostgres && string.IsNullOrWhiteSpace(cfg.PgConnectionString))
            throw new InvalidOperationException("ENGRAM_PG_CONNECTION is required when ENGRAM_DB_TYPE=postgres");

        return cfg.DbType switch
        {
            StoreDbType.Postgres => new PostgresStore(cfg),
            _ => new SqliteStore(cfg),
        };
    }
}
