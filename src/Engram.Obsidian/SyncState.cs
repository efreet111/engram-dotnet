using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engram.Obsidian;

/// <summary>
/// Tracks the state of a previous export run.
/// Persisted as JSON in {vault}/engram/.engram-sync-state.json.
/// Port of Go's internal/obsidian/state.go.
/// </summary>
public class SyncState
{
    [JsonPropertyName("last_export_at")]
    public string LastExportAt { get; set; } = "";

    [JsonPropertyName("files")]
    public Dictionary<long, string> Files { get; set; } = [];

    [JsonPropertyName("session_hubs")]
    public Dictionary<string, string> SessionHubs { get; set; } = [];

    [JsonPropertyName("topic_hubs")]
    public Dictionary<string, string> TopicHubs { get; set; } = [];

    [JsonPropertyName("version")]
    public int Version { get; set; }
}

/// <summary>
/// Summarizes what happened during an export run.
/// </summary>
public class ExportResult
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Deleted { get; set; }
    public int Skipped { get; set; }
    public int HubsCreated { get; set; }
    public List<Exception> Errors { get; set; } = [];
}

/// <summary>
/// Reads and writes the sync state JSON file.
/// </summary>
public static class StateFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Reads the sync state from the given JSON file path.
    /// If the file does not exist, returns an empty SyncState with no error.
    /// </summary>
    public static SyncState ReadState(string path)
    {
        if (!File.Exists(path))
        {
            return new SyncState
            {
                Files = [],
                SessionHubs = [],
                TopicHubs = [],
            };
        }

        var json = File.ReadAllText(path);
        var state = JsonSerializer.Deserialize<SyncState>(json, JsonOptions)
                    ?? new SyncState();

        // Ensure maps are non-null after deserialization
        state.Files ??= [];
        state.SessionHubs ??= [];
        state.TopicHubs ??= [];

        return state;
    }

    /// <summary>
    /// Persists the sync state as indented JSON to the given file path.
    /// UTF-8 without BOM.
    /// </summary>
    public static void WriteState(string path, SyncState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        // Ensure parent directory exists
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, json, System.Text.Encoding.UTF8);
    }
}
