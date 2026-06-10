using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engram.Obsidian;

/// <summary>
/// Tracks the state of a previous export run.
/// Persisted as JSON in {vault}/.engram/state.json or {vault}/.engram/state-{project}.json.
/// Port of Go's internal/obsidian/state.go.
/// </summary>
public class SyncState
{
    [JsonPropertyName("last_export_at")]
    public string LastExportAt { get; set; } = "";

    /// <summary>
    /// Mutation sequence cursor for incremental export (ENG-208 Phase 6).
    /// Null for date-based incremental export (backward compatible with v0/v1).
    /// </summary>
    [JsonPropertyName("last_seq")]
    public long? LastSeq { get; set; }

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
public static partial class StateFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Sanitizes a project name for use in a filename.
    /// Replaces non-alphanumeric chars (except . _ -) with underscore.
    /// </summary>
    public static string SanitizeProjectName(string project)
    {
        if (string.IsNullOrEmpty(project))
            return "";

        var chars = project.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            // Allow: letters, digits, dots, underscores, hyphens
            if (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-')
                continue;
            chars[i] = '_';
        }
        return new string(chars);
    }

    /// <summary>
    /// Resolves the state file path for a given vault directory and project.
    /// Returns .engram/state.json when project is null/empty, otherwise .engram/state-{sanitized}.json
    /// </summary>
    public static string ResolveStatePath(string vaultDir, string? project)
    {
        var engramDir = Path.Combine(vaultDir, ".engram");
        if (string.IsNullOrEmpty(project))
            return Path.Combine(engramDir, "state.json");

        var sanitized = SanitizeProjectName(project);
        return Path.Combine(engramDir, $"state-{sanitized}.json");
    }

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
