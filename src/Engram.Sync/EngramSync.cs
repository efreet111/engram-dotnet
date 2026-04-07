using System.IO.Compression;
using System.Text.Json;
using Engram.Store;

namespace Engram.Sync;

// ─── Domain types ─────────────────────────────────────────────────────────────

/// <summary>
/// Index file that lists all chunks. Small and append-only, safe for git merges.
/// </summary>
public sealed class Manifest
{
    public int           Version { get; init; } = 1;
    public List<ChunkEntry> Chunks { get; init; } = [];
}

/// <summary>Describes a single chunk entry in the manifest.</summary>
public sealed class ChunkEntry
{
    public string Id        { get; init; } = "";  // SHA-256 hash prefix (8 chars)
    public string CreatedBy { get; init; } = "";  // Username or machine
    public string CreatedAt { get; init; } = "";  // ISO timestamp
    public int    Sessions  { get; init; }
    public int    Memories  { get; init; }
    public int    Prompts   { get; init; }
}

/// <summary>Content of a single gzipped JSONL chunk file.</summary>
public sealed class ChunkData
{
    public List<Session>     Sessions     { get; init; } = [];
    public List<Observation> Observations { get; init; } = [];
    public List<Prompt>      Prompts      { get; init; } = [];
}

/// <summary>Result returned after a sync operation.</summary>
public sealed class SyncResult
{
    public int  ChunksExported { get; init; }
    public int  ChunksImported { get; init; }
    public int  MemoriesImported { get; init; }
    public bool Pushed          { get; init; }
    public bool Pulled          { get; init; }
}

// ─── SyncConfig ───────────────────────────────────────────────────────────────

/// <summary>
/// Configuration for the sync engine.
/// Populated from environment variables (ENGRAM_SYNC_DIR, ENGRAM_SYNC_REPO, etc.).
/// </summary>
public sealed class SyncConfig
{
    /// <summary>Path to the .engram/ directory (default: ~/.engram/sync)</summary>
    public string SyncDir  { get; init; } = "";

    /// <summary>Remote git repository URL (optional)</summary>
    public string RepoUrl  { get; init; } = "";

    /// <summary>Git branch to sync against (default: main)</summary>
    public string Branch   { get; init; } = "main";

    /// <summary>Whether auto-sync on save is enabled.</summary>
    public bool   AutoSync { get; init; } = false;

    public static SyncConfig FromEnvironment() => new()
    {
        SyncDir  = Environment.GetEnvironmentVariable("ENGRAM_SYNC_DIR")
                   ?? Path.Combine(
                       Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                       ".engram", "sync"),
        RepoUrl  = Environment.GetEnvironmentVariable("ENGRAM_SYNC_REPO") ?? "",
        Branch   = Environment.GetEnvironmentVariable("ENGRAM_SYNC_BRANCH") ?? "main",
        AutoSync = Environment.GetEnvironmentVariable("ENGRAM_AUTO_SYNC") == "true",
    };

    public bool IsConfigured => !string.IsNullOrEmpty(RepoUrl);
}

// ─── EngramSync ───────────────────────────────────────────────────────────────

/// <summary>
/// Git-friendly memory synchronisation engine.
/// Memories are stored as gzipped JSONL chunks to avoid merge conflicts.
///
/// Directory structure:
///   .engram/
///   ├── manifest.json          ← chunk index (small, mergeable)
///   ├── chunks/
///   │   ├── a3f8c1d2.jsonl.gz  ← chunk (compressed)
///   │   └── ...
///   └── engram.db              ← local DB (git-ignored)
///
/// NOTE: Full git pull/push implementation is Phase 5.
///       This class already handles local export/import of chunks.
/// </summary>
public sealed class EngramSync(IStore store, SyncConfig cfg)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented        = false,
    };

    // ─── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// Export new memories to a local chunk file and update the manifest.
    /// Returns true if a new chunk was written.
    /// </summary>
    public async Task<bool> ExportChunkAsync(CancellationToken ct = default)
    {
        var data       = await store.ExportAsync();
        var synced     = await store.GetSyncedChunksAsync();
        var chunkData  = FilterUnsyncedData(data, synced);

        if (chunkData.Observations.Count == 0 && chunkData.Sessions.Count == 0 && chunkData.Prompts.Count == 0)
            return false;

        Directory.CreateDirectory(ChunksDir);

        // Serialize to JSONL bytes
        var lines = new List<string>
        {
            JsonSerializer.Serialize(chunkData, JsonOpts),
        };
        var raw = string.Join('\n', lines);

        // Compute chunk id from SHA-256 of raw bytes
        var chunkId = ComputeChunkId(raw);
        var chunkPath = Path.Combine(ChunksDir, $"{chunkId}.jsonl.gz");

        // Write gzipped
        await using (var fs = File.Create(chunkPath))
        await using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(raw);
            await gz.WriteAsync(bytes, ct);
        }

        // Update manifest
        var manifest = await LoadManifestAsync();
        manifest.Chunks.Add(new ChunkEntry
        {
            Id        = chunkId,
            CreatedBy = Environment.MachineName,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            Sessions  = chunkData.Sessions.Count,
            Memories  = chunkData.Observations.Count,
            Prompts   = chunkData.Prompts.Count,
        });
        await SaveManifestAsync(manifest);

        // Mark as synced in the store
        await store.RecordSyncedChunkAsync(chunkId);

        return true;
    }

    /// <summary>
    /// Import all chunks from the local sync directory that haven't been imported yet.
    /// Returns the number of memories imported.
    /// </summary>
    public async Task<int> ImportNewChunksAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(ChunksDir)) return 0;

        var synced       = await store.GetSyncedChunksAsync();
        var manifest     = await LoadManifestAsync();
        var totalImported = 0;

        foreach (var entry in manifest.Chunks)
        {
            if (synced.Contains(entry.Id)) continue;

            var chunkPath = Path.Combine(ChunksDir, $"{entry.Id}.jsonl.gz");
            if (!File.Exists(chunkPath)) continue;

            var chunkData = await ReadChunkAsync(chunkPath, ct);
            if (chunkData is null) continue;

            var exportData = new ExportData
            {
                Sessions     = chunkData.Sessions,
                Observations = chunkData.Observations,
                Prompts      = chunkData.Prompts,
            };

            var result = await store.ImportAsync(exportData);
            totalImported += result.ObservationsImported;

            await store.RecordSyncedChunkAsync(entry.Id);
        }

        return totalImported;
    }

    /// <summary>Returns the current sync status.</summary>
    public async Task<SyncStatus> GetStatusAsync()
    {
        var manifest = await LoadManifestAsync();
        var synced   = await store.GetSyncedChunksAsync();

        return new SyncStatus
        {
            Enabled       = cfg.IsConfigured,
            RepoUrl       = cfg.RepoUrl,
            Branch        = cfg.Branch,
            TotalChunks   = manifest.Chunks.Count,
            SyncedChunks  = synced.Count,
            PendingChunks = manifest.Chunks.Count - synced.Count,
        };
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private string ChunksDir   => Path.Combine(cfg.SyncDir, "chunks");
    private string ManifestPath => Path.Combine(cfg.SyncDir, "manifest.json");

    private async Task<Manifest> LoadManifestAsync()
    {
        if (!File.Exists(ManifestPath))
            return new Manifest();

        await using var fs = File.OpenRead(ManifestPath);
        return await JsonSerializer.DeserializeAsync<Manifest>(fs, JsonOpts) ?? new Manifest();
    }

    private async Task SaveManifestAsync(Manifest manifest)
    {
        Directory.CreateDirectory(cfg.SyncDir);
        await using var fs = File.Create(ManifestPath);
        await JsonSerializer.SerializeAsync(fs, manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented        = true,
        });
    }

    private static async Task<ChunkData?> ReadChunkAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var fs = File.OpenRead(path);
            await using var gz = new GZipStream(fs, CompressionMode.Decompress);
            return await JsonSerializer.DeserializeAsync<ChunkData>(gz, JsonOpts, ct);
        }
        catch
        {
            return null;
        }
    }

    private static ChunkData FilterUnsyncedData(ExportData data, ISet<string> synced)
    {
        // For simplicity: include everything (the store de-dupes on import)
        // A future optimization could filter by sync_id or created_at
        _ = synced; // will be used for filtering in Phase 5 full implementation
        return new ChunkData
        {
            Sessions     = data.Sessions,
            Observations = data.Observations,
            Prompts      = data.Prompts,
        };
    }

    private static string ComputeChunkId(string content)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }
}

// ─── SyncStatus ───────────────────────────────────────────────────────────────

public sealed class SyncStatus
{
    public bool   Enabled       { get; init; }
    public string RepoUrl       { get; init; } = "";
    public string Branch        { get; init; } = "";
    public int    TotalChunks   { get; init; }
    public int    SyncedChunks  { get; init; }
    public int    PendingChunks { get; init; }
}
