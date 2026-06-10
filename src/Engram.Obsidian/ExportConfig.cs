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

    // Watch mode (ENG-208 Phase 9)
    /// <summary>If true, run in watch mode (continuous export at intervals).</summary>
    public bool Watch { get; init; }

    /// <summary>Interval for watch mode (e.g., "30s", "5m", "1h").</summary>
    public string Interval { get; init; } = "60s";

    // Since filter (ENG-208 Phase 8)
    /// <summary>Export only observations created at or after this date (ISO 8601 or relative like "30d").</summary>
    public string Since { get; init; } = "";
}
