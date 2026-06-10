using Engram.Store;

namespace Engram.Obsidian;

/// <summary>
/// Reads from the store and writes markdown files to an Obsidian vault.
/// Port of Go's internal/obsidian/exporter.go.
/// </summary>
public class Exporter
{
    private readonly IObsidianStoreReader _store;
    private readonly ExportConfig _config;

    public Exporter(IObsidianStoreReader store, ExportConfig config)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Performs a full or incremental export from the store to the vault.
    /// Returns an ExportResult summarizing what happened.
    /// </summary>
    public ExportResult Export()
    {
        // ── Validate config ───────────────────────────────────────────────────
        if (string.IsNullOrEmpty(_config.VaultPath))
            throw new ArgumentException("obsidian: --vault path is required");

        var result = new ExportResult();

        // ── Write graph config (first cycle only, controlled by caller) ───────
        var graphMode = _config.GraphConfig;
        GraphConfig.WriteGraphConfig(_config.VaultPath, graphMode);

        // ── Create vault namespace directories ────────────────────────────────
        var engRoot = Path.Combine(_config.VaultPath, "engram");
        var sessionsDir = Path.Combine(engRoot, "_sessions");
        var topicsDir = Path.Combine(engRoot, "_topics");
        foreach (var d in new[] { engRoot, sessionsDir, topicsDir })
            Directory.CreateDirectory(d);

        // ── Read incremental state (per-project state files) ──────────────────
        var stateFile = StateFile.ResolveStatePath(_config.VaultPath, _config.Project);
        var state = StateFile.ReadState(stateFile);
        if (_config.Force)
        {
            state = new SyncState
            {
                Files = [],
                SessionHubs = [],
                TopicHubs = [],
            };
        }

        // ── Determine cutoff time ─────────────────────────────────────────────
        DateTime cutoff = default;

        // Priority: --since flag > state file > default
        if (!string.IsNullOrEmpty(_config.Since))
        {
            // Parse --since argument (ISO 8601 or relative like "30d")
            cutoff = ParseSinceValue(_config.Since);
        }
        else if (!string.IsNullOrEmpty(state.LastExportAt))
        {
            DateTime.TryParse(state.LastExportAt, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out cutoff);
        }

        // ── Fetch data from store ─────────────────────────────────────────────
        var data = _store.ExportAsync().GetAwaiter().GetResult();

        // ── Handle deleted observations: clean up files ───────────────────────
        foreach (var obs in data.Observations)
        {
            if (obs.DeletedAt == null)
                continue;
            if (!state.Files.TryGetValue(obs.Id, out var relPath))
                continue;

            var absPath = Path.Combine(engRoot, relPath);
            try
            {
                File.Delete(absPath);
                result.Deleted++;
                state.Files.Remove(obs.Id);
            }
            catch (Exception ex) when (ex is not FileNotFoundException)
            {
                result.Errors.Add(new IOException($"delete {absPath}: {ex.Message}", ex));
            }
        }

        // ── Build session map for hub generation ──────────────────────────────
        var sessionMap = new Dictionary<string, Session>();
        foreach (var s in data.Sessions)
            sessionMap[s.Id] = s;

        // ── Filter and export observations ────────────────────────────────────
        var sessionObsRefs = new Dictionary<string, List<ObsRef>>();
        var topicObsRefs = new Dictionary<string, List<ObsRef>>();

        foreach (var obs in data.Observations)
        {
            // Skip deleted
            if (obs.DeletedAt != null)
                continue;

            // Project filter
            if (!string.IsNullOrEmpty(_config.Project))
            {
                var proj = obs.Project ?? "";
                if (proj != _config.Project)
                    continue;
            }

            // Scope filter: team only by default, personal requires explicit flag
            var normalizedScope = NormalizeScope(obs.Scope);
            if (!_config.IncludePersonal && normalizedScope == Scopes.Personal)
                continue;

            // --since filter: skip if created_at < cutoff
            if (cutoff != default && DateTime.TryParse(obs.CreatedAt, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var createdAt))
            {
                if (createdAt < cutoff)
                {
                    result.Skipped++;
                    continue;
                }
            }

            // Incremental filter: skip if updated_at <= cutoff AND already in state
            if (cutoff != default && DateTime.TryParse(obs.UpdatedAt, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var updatedAt))
            {
                if (updatedAt <= cutoff && state.Files.ContainsKey(obs.Id))
                {
                    result.Skipped++;
                    // Still collect for hub building
                    var ref_ = ObsToRef(obs);
                    CollectRefs(ref_, obs, sessionObsRefs, topicObsRefs);
                    continue;
                }
            }

            // Determine target file path
            var project = string.IsNullOrEmpty(obs.Project) ? "unknown" : obs.Project;
            var slug = Slug.Slugify(obs.Title, obs.Id);
            var relPathObs = Path.Combine(project, obs.Type, slug + ".md");
            var absDir = Path.Combine(engRoot, project, obs.Type);
            var absPath = Path.Combine(engRoot, relPathObs);

            // Create directory
            Directory.CreateDirectory(absDir);

            // Generate markdown content
            var content = MarkdownRenderer.ObservationToMarkdown(obs);

            // Check idempotency: if file exists and content unchanged, skip
            if (File.Exists(absPath))
            {
                var existing = File.ReadAllText(absPath);
                if (existing == content)
                {
                    result.Skipped++;
                    // Track in state in case it wasn't tracked (e.g. after --force)
                    state.Files[obs.Id] = relPathObs;
                    var ref_ = ObsToRef(obs);
                    CollectRefs(ref_, obs, sessionObsRefs, topicObsRefs);
                    continue;
                }

                // Content changed → update
                File.WriteAllText(absPath, content, System.Text.Encoding.UTF8);
                result.Updated++;
            }
            else
            {
                // New file
                File.WriteAllText(absPath, content, System.Text.Encoding.UTF8);
                result.Created++;
            }

            state.Files[obs.Id] = relPathObs;

            // Collect for hub generation
            var ref2 = ObsToRef(obs);
            CollectRefs(ref2, obs, sessionObsRefs, topicObsRefs);

            // Check limit: stop processing if we've reached the max
            if (_config.Limit > 0 && (result.Created + result.Updated) >= _config.Limit)
                break;
        }

        // ── Generate session hub notes ────────────────────────────────────────
        foreach (var (sessionId, refs) in sessionObsRefs)
        {
            if (refs.Count == 0)
                continue;
            var hubPath = Path.Combine(sessionsDir, sessionId + ".md");
            var hubContent = HubGenerator.SessionHubMarkdown(sessionId, refs);
            File.WriteAllText(hubPath, hubContent, System.Text.Encoding.UTF8);
            state.SessionHubs[sessionId] = Path.Combine("_sessions", sessionId + ".md");
            result.HubsCreated++;
        }

        // ── Generate topic hub notes ──────────────────────────────────────────
        foreach (var (prefix, refs) in topicObsRefs)
        {
            if (!HubGenerator.ShouldCreateTopicHub(refs.Count))
                continue;
            var safeName = prefix.Replace("/", "--");
            var hubPath = Path.Combine(topicsDir, safeName + ".md");
            var hubContent = HubGenerator.TopicHubMarkdown(prefix, refs);
            File.WriteAllText(hubPath, hubContent, System.Text.Encoding.UTF8);
            state.TopicHubs[prefix] = Path.Combine("_topics", safeName + ".md");
            result.HubsCreated++;
        }

        // ── Persist updated state ─────────────────────────────────────────────
        state.LastExportAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        state.Version = 1;
        StateFile.WriteState(stateFile, state);

        return result;
    }

    /// <summary>
    /// Converts an observation to a lightweight ObsRef for hub building.
    /// </summary>
    private static ObsRef ObsToRef(Observation obs)
    {
        return new ObsRef
        {
            Slug = Slug.Slugify(obs.Title, obs.Id),
            Title = obs.Title,
            TopicKey = obs.TopicKey ?? "",
            Type = obs.Type,
        };
    }

    /// <summary>
    /// Extracts the topic prefix from an observation for hub grouping.
    /// Returns "" if the observation has no topic_key.
    /// </summary>
    private static string ObsTopicPrefix(Observation obs)
    {
        if (string.IsNullOrEmpty(obs.TopicKey))
            return "";
        return TopicPrefix.Extract(obs.TopicKey);
    }

    /// <summary>
    /// Adds an observation reference to the session and topic collections.
    /// </summary>
    private static void CollectRefs(
        ObsRef ref_,
        Observation obs,
        Dictionary<string, List<ObsRef>> sessionRefs,
        Dictionary<string, List<ObsRef>> topicRefs)
    {
        if (!string.IsNullOrEmpty(obs.SessionId))
        {
            if (!sessionRefs.TryGetValue(obs.SessionId, out var list))
            {
                list = [];
                sessionRefs[obs.SessionId] = list;
            }
            list.Add(ref_);
        }

        var prefix = ObsTopicPrefix(obs);
        if (!string.IsNullOrEmpty(prefix))
        {
            if (!topicRefs.TryGetValue(prefix, out var list))
            {
                list = [];
                topicRefs[prefix] = list;
            }
            list.Add(ref_);
        }
    }

/// <summary>
/// Normalizes a scope value to either "team" or "personal".
/// Mirrors SqliteStore.NormalizeScope.
/// </summary>
private static string NormalizeScope(string? scope)
{
    var v = (scope ?? "").Trim().ToLowerInvariant();
    return v == Scopes.Team ? Scopes.Team : Scopes.Personal;
}

/// <summary>
/// Parses a --since value (ISO 8601 or relative duration).
/// </summary>
private static DateTime ParseSinceValue(string since)
{
    // Try ISO 8601 first
    if (DateTime.TryParse(since, null,
        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
        out var isoResult))
    {
        return isoResult;
    }

    // Try relative duration (30d, 7d, 24h, 5m)
    if (since.Length >= 2)
    {
        var suffix = since[^1];
        if (int.TryParse(since[..^1], out var value) && value > 0)
        {
            var now = DateTime.UtcNow;
            return suffix switch
            {
                'd' => now.AddDays(-value),
                'h' => now.AddHours(-value),
                'm' => now.AddMinutes(-value),
                _ => throw new ArgumentException($"invalid --since format: '{since}'. Use ISO 8601 (2025-01-01) or relative (30d, 7d, 24h, 5m)"),
            };
        }
    }

    throw new ArgumentException($"invalid --since format: '{since}'. Use ISO 8601 (2025-01-01) or relative (30d, 7d, 24h, 5m)", nameof(since));
}
}
