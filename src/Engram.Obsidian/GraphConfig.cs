using System.Reflection;

namespace Engram.Obsidian;

/// <summary>
/// Controls how WriteGraphConfig handles an existing graph.json.
/// Port of Go's internal/obsidian/graph.go.
/// </summary>
public enum GraphConfigMode
{
    /// <summary>
    /// Writes the default template only if graph.json is absent.
    /// </summary>
    Preserve,

    /// <summary>
    /// Always overwrites graph.json with the embedded default.
    /// </summary>
    Force,

    /// <summary>
    /// Never reads, writes, or creates graph.json.
    /// </summary>
    Skip,
}

/// <summary>
/// Parses a string into a GraphConfigMode.
/// Case-sensitive. Returns an error for invalid values.
/// </summary>
public static class GraphConfig
{
    /// <summary>
    /// Parses s into a GraphConfigMode.
    /// Accepted values: "preserve", "force", "skip" (case-sensitive).
    /// </summary>
    public static GraphConfigMode Parse(string s)
    {
        return s switch
        {
            "preserve" => GraphConfigMode.Preserve,
            "force"    => GraphConfigMode.Force,
            "skip"     => GraphConfigMode.Skip,
            _          => throw new ArgumentException(
                             $"Invalid --graph-config value: {s} (accepted: preserve, force, skip)"),
        };
    }

    /// <summary>
    /// Writes the embedded graph.json default into {vaultPath}/.obsidian/graph.json
    /// according to the given mode.
    ///
    /// - preserve: creates the file only when it does not already exist.
    /// - force:    always creates or overwrites the file with the embedded default.
    /// - skip:     no-op; returns immediately.
    ///
    /// The .obsidian/ directory is created with 0755 permissions if it does not exist
    /// (except in skip mode where nothing is written).
    /// </summary>
    public static void WriteGraphConfig(string vaultPath, GraphConfigMode mode)
    {
        if (mode == GraphConfigMode.Skip)
            return;

        var obsidianDir = Path.Combine(vaultPath, ".obsidian");
        var graphPath = Path.Combine(obsidianDir, "graph.json");

        if (mode == GraphConfigMode.Preserve)
        {
            if (File.Exists(graphPath))
            {
                // File exists — preserve it
                return;
            }
            // File does not exist — fall through to create
        }

        // force or preserve-with-absent-file: create dir + write
        Directory.CreateDirectory(obsidianDir);

        var template = LoadEmbeddedGraphJson();
        File.WriteAllBytes(graphPath, template);
    }

    /// <summary>
    /// Reads the embedded graph.json resource from the assembly.
    /// </summary>
    private static byte[] LoadEmbeddedGraphJson()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Engram.Obsidian.graph.json";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. " +
                "Ensure graph.json is included as an EmbeddedResource in the .csproj.");

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
