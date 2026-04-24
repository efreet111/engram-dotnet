namespace Engram.Obsidian;

/// <summary>
/// Holds all CLI flags for the obsidian-export command.
/// Port of Go's ExportConfig from internal/obsidian/exporter.go.
/// </summary>
public record ExportConfig
{
    /// <summary>Path to the Obsidian vault root (required).</summary>
    public string VaultPath { get; init; } = "";

    /// <summary>Filter export to a single project (optional).</summary>
    public string? Project { get; init; }

    /// <summary>0 = no limit.</summary>
    public int Limit { get; init; }

    /// <summary>If true, ignore state and do a full re-export.</summary>
    public bool Force { get; init; }

    /// <summary>If true, include scope=personal observations in the export.</summary>
    public bool IncludePersonal { get; init; }

    /// <summary>Controls graph.json handling: preserve|force|skip.</summary>
    public GraphConfigMode GraphConfig { get; init; } = GraphConfigMode.Preserve;
}
