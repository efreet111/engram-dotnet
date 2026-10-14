namespace Engram.Sync;

/// <summary>
/// HU-065 F1: Helper for reading sync configuration from ~/.engram/config.json.
/// Exposed publicly so Engram.Cli can delegate to it and tests can verify behavior.
/// </summary>
public static class SyncConfigFileHelper
{
    /// <summary>
    /// Reads sync.remote_url from ~/.engram/config.json.
    /// Returns null if not found, empty, or file does not exist.
    /// Does not throw — parsing errors are silently ignored.
    /// </summary>
    /// <param name="configPath">Override the config file path (useful for tests).</param>
    public static string? LoadRemoteUrlFromFile(string? configPath = null)
    {
        try
        {
            configPath ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".engram", "config.json");
            if (!File.Exists(configPath)) return null;

            var json = File.ReadAllText(configPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("sync", out var syncProp)) return null;
            if (!syncProp.TryGetProperty("remote_url", out var remoteUrlProp)) return null;
            if (remoteUrlProp.ValueKind != System.Text.Json.JsonValueKind.String) return null;
            var value = remoteUrlProp.GetString();
            if (string.IsNullOrWhiteSpace(value)) return null;
            return value;
        }
        catch { return null; }
    }

    /// <summary>
    /// Reads auto_sync from ~/.engram/config.json.
    /// Returns true if auto_sync is enabled (default), false if disabled.
    /// </summary>
    public static bool LoadAutoSyncFromFile(string? configPath = null)
    {
        try
        {
            configPath ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".engram", "config.json");
            if (!File.Exists(configPath)) return true;

            var json = File.ReadAllText(configPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("auto_sync", out var autoSyncProp))
            {
                if (autoSyncProp.ValueKind == System.Text.Json.JsonValueKind.False)
                    return false;
                if (autoSyncProp.ValueKind == System.Text.Json.JsonValueKind.Number
                    && autoSyncProp.GetInt32() == 0)
                    return false;
            }
            return true;
        }
        catch { return true; }
    }
}
