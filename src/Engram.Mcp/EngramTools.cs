using System.ComponentModel;
using System.Text;
using Engram.Store;
using ModelContextProtocol.Server;

namespace Engram.Mcp;

/// <summary>
/// Configuration injected into MCP tools.
/// DefaultProject is auto-detected from the working directory and applied
/// when the LLM sends an empty project field.
///
/// In team/centralized mode (ENGRAM_URL set), User is the developer identity
/// provided by IT (ENGRAM_USER). The DefaultProject is automatically prefixed
/// as "user/project" or "team/project" depending on scope, so memories are
/// namespaced correctly in the shared server.
/// The LLM always sees only the bare project name — the prefix is transparent.
/// </summary>
public sealed class McpConfig
{
    public string DefaultProject { get; init; } = "";

    /// <summary>
    /// Developer identity (from ENGRAM_USER). Empty in local mode.
    /// </summary>
    public string User { get; init; } = "";

    /// <summary>
    /// Builds the namespaced project string used for storage.
    /// scope "team"     → "team/{project}"   (shared across all developers)
    /// scope "personal" → "{user}/{project}" (private per developer)
    /// No user configured → bare project name (local mode, no namespacing).
    /// </summary>
    public string ResolveNamespacedProject(string? project, string scope)
    {
        var p = string.IsNullOrEmpty(project) ? DefaultProject : project;
        if (string.IsNullOrEmpty(User)) return p;
        return scope == Engram.Store.Scopes.Team ? $"team/{p}" : $"{User}/{p}";
    }
}

/// <summary>
/// All 15 Engram MCP tools, port-faithful to the Go implementation.
/// Registered via [McpServerToolType] / [McpServerTool] attributes.
/// IStore and McpConfig are injected via DI constructor.
/// </summary>
[McpServerToolType]
public sealed class EngramTools(IStore store, McpConfig cfg)
{
    // ─── mem_search ──────────────────────────────────────────────────────────

    [McpServerTool(Name = "mem_search", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Search your persistent memory across all sessions. Use this to find past decisions, bugs fixed, patterns used, files changed, or any context from previous coding sessions.")]
    public async Task<string> MemSearch(
        [Description("Search query — natural language or keywords")] string query,
        [Description("Filter by type: tool_use, file_change, command, file_read, search, manual, decision, architecture, bugfix, pattern")] string? type = null,
        [Description("Filter by project name")] string? project = null,
        [Description("Filter by scope: team, personal. Omit to search both.")] string? scope = null,
        [Description("Max results (default: 10, max: 20)")] int limit = 10)
    {
        var clampedLimit = Math.Clamp(limit, 1, 20);

        IList<SearchResult> results;

        if (scope is null && !string.IsNullOrEmpty(cfg.User))
        {
            // Cross-namespace: search both team/{project} and {user}/{project}
            var teamProject     = ResolveProject(project, Engram.Store.Scopes.Team);
            var personalProject = ResolveProject(project, Engram.Store.Scopes.Personal);
            var searchProjects  = new List<string> { teamProject, personalProject };

            results = await store.SearchAsync(query, searchProjects, new SearchOptions
            {
                Type  = type,
                Limit = clampedLimit,
            });
        }
        else
        {
            // Explicit scope or local mode: single namespace
            var resolvedScope   = scope ?? Engram.Store.Scopes.Personal;
            var resolvedProject = ResolveProject(project, resolvedScope);

            results = await store.SearchAsync(query, new SearchOptions
            {
                Type    = type,
                Project = resolvedProject,
                Scope   = scope,   // pass through to filter by scope column if explicit
                Limit   = clampedLimit,
            });
        }

        if (results.Count == 0)
            return $"No memories found for: \"{query}\"";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {results.Count} memories:");
        sb.AppendLine();

        bool anyTruncated = false;
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i].Observation;
            var projectDisplay = r.Project is not null ? $" | project: {r.Project}" : "";
            var preview        = Truncate(r.Content, 300);
            if (r.Content.Length > 300) { anyTruncated = true; preview += " [preview]"; }
            sb.AppendLine($"[{i + 1}] #{r.Id} ({r.Type}) — {r.Title}");
            sb.AppendLine($"    {preview}");
            sb.AppendLine($"    {r.CreatedAt}{projectDisplay} | scope: {r.Scope}");
            sb.AppendLine();
        }

        if (anyTruncated)
            sb.AppendLine("---\nResults above are previews (300 chars). To read the full content of a specific memory, call mem_get_observation(id: <ID>).");

        return sb.ToString().TrimEnd();
    }

    // ─── mem_save ────────────────────────────────────────────────────────────

    [McpServerTool(Name = "mem_save", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("""
        Save an important observation to persistent memory. Call this PROACTIVELY after completing significant work — don't wait to be asked.

        WHEN to save (call this after each of these):
        - Architectural decisions or tradeoffs
        - Bug fixes (what was wrong, why, how you fixed it)
        - New patterns or conventions established
        - Configuration changes or environment setup
        - Important discoveries or gotchas
        - File structure changes

        FORMAT for content — use this structured format:
          **What**: [concise description of what was done]
          **Why**: [the reasoning, user request, or problem that drove it]
          **Where**: [files/paths affected, e.g. src/auth/middleware.ts, internal/store/store.go]
          **Learned**: [any gotchas, edge cases, or decisions made — omit if none]

        TITLE should be short and searchable, like: "JWT auth middleware", "FTS5 query sanitization", "Fixed N+1 in user list"
        """)]
    public async Task<string> MemSave(
        [Description("Short, searchable title (e.g. 'JWT auth middleware', 'Fixed N+1 query')")] string title,
        [Description("Structured content using **What**, **Why**, **Where**, **Learned** format")] string content,
        [Description("Category: decision, architecture, bugfix, pattern, config, discovery, learning (default: manual)")] string? type = null,
        [Description("Session ID to associate with (default: manual-save-{project})")] string? session_id = null,
        [Description("Project name")] string? project = null,
        [Description("Scope for this observation: team (shared with all devs) or personal (private). Auto-classified from type when omitted.")] string? scope = null,
        [Description("Optional topic identifier for upserts (e.g. architecture/auth-model). Reuses and updates the latest observation in same project+scope.")] string? topic_key = null)
    {
        type     ??= "manual";
        scope    ??= AutoClassifyScope(type);

        // ── Normalize project name and capture warning ──
        var (normalizedProject, normWarning) = Normalizers.NormalizeProjectWithWarning(
            string.IsNullOrEmpty(project) ? cfg.DefaultProject : project);
        project    = ResolveProject(normalizedProject, scope);
        session_id ??= DefaultSessionId(project);

        var suggestedKey = Normalizers.SuggestTopicKey(type, title, content);
        var truncated    = content.Length > store.MaxObservationLength;

        // ── Check for similar existing projects (only when this project has no observations) ──
        string? similarWarning = null;
        if (!string.IsNullOrEmpty(normalizedProject))
        {
            try
            {
                var existingNames = await store.ListProjectNamesAsync();
                var isNew = !existingNames.Contains(normalizedProject);
                if (isNew && existingNames.Count > 0)
                {
                    var matches = ProjectDetector.FindSimilar(normalizedProject, existingNames, 3);
                    if (matches.Count > 0)
                    {
                        var bestMatch = matches[0].Name;
                        var obsCount = await store.CountObservationsForProjectAsync(bestMatch);
                        similarWarning = $"⚠️ Project \"{normalizedProject}\" has no memories. Similar project found: \"{bestMatch}\" ({obsCount} memories). Consider using that name instead.";
                    }
                }
            }
            catch
            {
                // Similar project checking is best-effort — don't fail the save
            }
        }

        await store.CreateSessionAsync(session_id, project, "");
        await store.AddObservationAsync(new AddObservationParams
        {
            SessionId = session_id,
            Type      = type,
            Title     = title,
            Content   = content,
            Project   = project,
            Scope     = scope,
            TopicKey  = topic_key,
        });

        var msg = $"Memory saved: \"{title}\" ({type})";
        if (string.IsNullOrEmpty(topic_key) && !string.IsNullOrEmpty(suggestedKey))
            msg += $"\nSuggested topic_key: {suggestedKey}";
        if (truncated)
            msg += $"\n⚠ WARNING: Content was truncated to {store.MaxObservationLength} chars. Consider splitting into smaller observations.";
        if (!string.IsNullOrEmpty(normWarning))
            msg += $"\n{normWarning}";
        if (!string.IsNullOrEmpty(similarWarning))
            msg += $"\n{similarWarning}";

        return msg;
    }

    // ─── mem_update ──────────────────────────────────────────────────────────

    [McpServerTool(Name = "mem_update", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("Update an existing observation by ID. Only provided fields are changed.")]
    public async Task<string> MemUpdate(
        [Description("Observation ID to update")] long id,
        [Description("New title")] string? title = null,
        [Description("New content")] string? content = null,
        [Description("New type/category")] string? type = null,
        [Description("New project value")] string? project = null,
        [Description("New scope: project or personal")] string? scope = null,
        [Description("New topic key (normalized internally)")] string? topic_key = null)
    {
        if (id == 0) return "Error: id is required";
        if (title is null && content is null && type is null && project is null && scope is null && topic_key is null)
            return "Error: provide at least one field to update";

        var ok = await store.UpdateObservationAsync(id, new UpdateObservationParams
        {
            Title    = title,
            Content  = content,
            Type     = type,
            Project  = project,
            Scope    = scope,
            TopicKey = topic_key,
        });

        if (!ok) return $"Error: observation #{id} not found";

        var obs = await store.GetObservationAsync(id);
        if (obs is null) return $"Error: observation #{id} not found after update";

        var msg = $"Memory updated: #{obs.Id} \"{obs.Title}\" ({obs.Type}, scope={obs.Scope})";
        if (content is not null && content.Length > store.MaxObservationLength)
            msg += $"\n⚠ WARNING: Content was truncated to {store.MaxObservationLength} chars. Consider splitting into smaller observations.";

        return msg;
    }

    // ─── mem_suggest_topic_key ───────────────────────────────────────────────

    [McpServerTool(Name = "mem_suggest_topic_key", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Suggest a stable topic_key for memory upserts. Use this before mem_save when you want evolving topics (like architecture decisions) to update a single observation over time.")]
    public string MemSuggestTopicKey(
        [Description("Observation type/category, e.g. architecture, decision, bugfix")] string? type = null,
        [Description("Observation title (preferred input for stable keys)")] string? title = null,
        [Description("Observation content used as fallback if title is empty")] string? content = null)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(content))
            return "Error: provide title or content to suggest a topic_key";

        var key = Normalizers.SuggestTopicKey(type, title, content);
        return string.IsNullOrEmpty(key)
            ? "Error: could not suggest topic_key from input"
            : $"Suggested topic_key: {key}";
    }

    // ─── mem_delete ──────────────────────────────────────────────────────────

    [McpServerTool(Name = "mem_delete", ReadOnly = false, Idempotent = false, Destructive = true, OpenWorld = false)]
    [Description("Delete an observation by ID. Soft-delete by default; set hard_delete=true for permanent deletion.")]
    public async Task<string> MemDelete(
        [Description("Observation ID to delete")] long id,
        [Description("If true, permanently deletes the observation")] bool hard_delete = false)
    {
        if (id == 0) return "Error: id is required";

        var ok = await store.DeleteObservationAsync(id);
        if (!ok) return $"Error: observation #{id} not found";

        var mode = hard_delete ? "permanently deleted" : "soft-deleted";
        return $"Memory #{id} {mode}";
    }

    // ─── mem_save_prompt ─────────────────────────────────────────────────────

    [McpServerTool(Name = "mem_save_prompt", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("Save a user prompt to persistent memory. Use this to record what the user asked — their intent, questions, and requests — so future sessions have context about the user's goals.")]
    public async Task<string> MemSavePrompt(
        [Description("The user's prompt text")] string content,
        [Description("Session ID to associate with (default: manual-save-{project})")] string? session_id = null,
        [Description("Project name")] string? project = null)
    {
        project    = ResolveProject(project, Engram.Store.Scopes.Personal);
        session_id ??= DefaultSessionId(project);

        await store.CreateSessionAsync(session_id, project, "");
        await store.AddPromptAsync(new AddPromptParams
        {
            SessionId = session_id,
            Content   = content,
            Project   = project,
        });

        return $"Prompt saved: \"{Truncate(content, 80)}\"";
    }

    // ─── mem_context ─────────────────────────────────────────────────────────

    [McpServerTool(Name = "mem_context", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Get recent memory context from previous sessions. Shows recent sessions and observations to understand what was done before.")]
    public async Task<string> MemContext(
        [Description("Filter by project (omit for all projects)")] string? project = null,
        [Description("Filter observations by scope: team, personal, or omit for both")] string? scope = null,
        [Description("Number of observations to retrieve (default: 20)")] int limit = 20)
    {
        // Wide-read: fetch from both team/{project} and {user}/{project} when user is set
        var teamProject     = ResolveProject(project, Engram.Store.Scopes.Team);
        var personalProject = ResolveProject(project, Engram.Store.Scopes.Personal);

        string context;
        if (teamProject != personalProject)
        {
            // Team mode: merge both namespaces
            var projects = new List<string> { teamProject, personalProject };
            context = await store.FormatContextAsync(projects, scope);
        }
        else
        {
            // Local mode (no ENGRAM_USER): single namespace
            context = await store.FormatContextAsync(teamProject, scope);
        }

        if (string.IsNullOrEmpty(context))
            return "No previous session memories found.";

        var stats    = await store.StatsAsync();
        var projects2 = stats.Projects.Count > 0 ? string.Join(", ", stats.Projects) : "none";

        return $"{context}\n---\nMemory stats: {stats.TotalSessions} sessions, {stats.TotalObservations} observations across projects: {projects2}";
    }

    // ─── mem_stats ───────────────────────────────────────────────────────────

    [McpServerTool(Name = "mem_stats", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Show memory system statistics — total sessions, observations, and projects tracked.")]
    public async Task<string> MemStats()
    {
        var stats    = await store.StatsAsync();
        var projects = stats.Projects.Count > 0 ? string.Join(", ", stats.Projects) : "none yet";

        return $"""
            Memory System Stats:
            - Sessions: {stats.TotalSessions}
            - Observations: {stats.TotalObservations}
            - Prompts: {stats.TotalPrompts}
            - Projects: {projects}
            """;
    }

    // ─── mem_timeline ────────────────────────────────────────────────────────

    [McpServerTool(Name = "mem_timeline", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Show chronological context around a specific observation. Use after mem_search to drill into the timeline of events surrounding a search result. This is the progressive disclosure pattern: search first, then timeline to understand context.")]
    public async Task<string> MemTimeline(
        [Description("The observation ID to center the timeline on (from mem_search results)")] long observation_id,
        [Description("Number of observations to show before the focus (default: 5)")] int before = 5,
        [Description("Number of observations to show after the focus (default: 5)")] int after = 5)
    {
        if (observation_id == 0) return "Error: observation_id is required";

        var result = await store.TimelineAsync(observation_id, before, after);
        if (result is null) return $"Error: observation #{observation_id} not found";

        var sb = new StringBuilder();

        if (result.SessionInfo is not null)
        {
            var summary = result.SessionInfo.Summary is not null
                ? $" — {Truncate(result.SessionInfo.Summary, 100)}"
                : "";
            sb.AppendLine($"Session: {result.SessionInfo.Project} ({result.SessionInfo.StartedAt}){summary}");
            sb.AppendLine($"Total observations in session: {result.TotalInRange}");
            sb.AppendLine();
        }

        if (result.Before.Count > 0)
        {
            sb.AppendLine("─── Before ───");
            foreach (var e in result.Before)
                sb.AppendLine($"  #{e.Id} [{e.Type}] {e.Title} — {Truncate(e.Content, 150)}");
            sb.AppendLine();
        }

        sb.AppendLine($">>> #{result.Focus.Id} [{result.Focus.Type}] {result.Focus.Title} <<<");
        sb.AppendLine($"    {Truncate(result.Focus.Content, 500)}");
        sb.AppendLine($"    {result.Focus.CreatedAt}");
        sb.AppendLine();

        if (result.After.Count > 0)
        {
            sb.AppendLine("─── After ───");
            foreach (var e in result.After)
                sb.AppendLine($"  #{e.Id} [{e.Type}] {e.Title} — {Truncate(e.Content, 150)}");
        }

        return sb.ToString().TrimEnd();
    }

    // ─── mem_get_observation ─────────────────────────────────────────────────

    [McpServerTool(Name = "mem_get_observation", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Get the full content of a specific observation by ID. Use when you need the complete, untruncated content of an observation found via mem_search or mem_timeline.")]
    public async Task<string> MemGetObservation(
        [Description("The observation ID to retrieve")] long id)
    {
        if (id == 0) return "Error: id is required";

        var obs = await store.GetObservationAsync(id);
        if (obs is null) return $"Error: observation #{id} not found";

        var project   = obs.Project  is not null ? $"\nProject: {obs.Project}"  : "";
        var topic     = obs.TopicKey is not null ? $"\nTopic: {obs.TopicKey}"   : "";
        var toolName  = obs.ToolName is not null ? $"\nTool: {obs.ToolName}"    : "";

        return $"""
            #{obs.Id} [{obs.Type}] {obs.Title}
            {obs.Content}
            Session: {obs.SessionId}{project}
            Scope: {obs.Scope}{topic}{toolName}
            Duplicates: {obs.DuplicateCount}
            Revisions: {obs.RevisionCount}
            Created: {obs.CreatedAt}
            """;
    }

    // ─── mem_session_summary ─────────────────────────────────────────────────

    [McpServerTool(Name = "mem_session_summary", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("""
        Save a comprehensive end-of-session summary. Call this when a session is ending or when significant work is complete. This creates a structured summary that future sessions will use to understand what happened.

        FORMAT — use this exact structure in the content field:

        ## Goal
        [One sentence: what were we building/working on in this session]

        ## Instructions
        [User preferences, constraints, or context discovered during this session. Things a future agent needs to know about HOW the user wants things done. Skip if nothing notable.]

        ## Discoveries
        - [Technical finding, gotcha, or learning 1]
        - [Technical finding 2]
        - [Important API behavior, config quirk, etc.]

        ## Accomplished
        - ✅ [Completed task 1 — with key implementation details]
        - ✅ [Completed task 2 — mention files changed]
        - 🔲 [Identified but not yet done — for next session]

        ## Relevant Files
        - path/to/file.ts — [what it does or what changed]
        - path/to/other.go — [role in the architecture]

        GUIDELINES:
        - Be CONCISE but don't lose important details (file paths, error messages, decisions)
        - Focus on WHAT and WHY, not HOW (the code itself is in the repo)
        - Include things that would save a future agent time
        - The Discoveries section is the most valuable — capture gotchas and non-obvious learnings
        - Relevant Files should only include files that were significantly changed or are important for context
        """)]
    public async Task<string> MemSessionSummary(
        [Description("Full session summary using the Goal/Instructions/Discoveries/Accomplished/Files format")] string content,
        [Description("Project name")] string project,
        [Description("Session ID (default: manual-save-{project})")] string? session_id = null)
    {
        project    = ResolveProject(project, Engram.Store.Scopes.Team);
        session_id ??= DefaultSessionId(project);

        await store.CreateSessionAsync(session_id, project, "");
        await store.AddObservationAsync(new AddObservationParams
        {
            SessionId = session_id,
            Type      = "session_summary",
            Title     = $"Session summary: {project}",
            Content   = content,
            Project   = project,
            Scope     = Engram.Store.Scopes.Team,
        });

        return $"Session summary saved for project \"{project}\"";
    }

    // ─── mem_session_start ───────────────────────────────────────────────────

    [McpServerTool(Name = "mem_session_start", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Register the start of a new coding session. Call this at the beginning of a session to track activity.")]
    public async Task<string> MemSessionStart(
        [Description("Unique session identifier")] string id,
        [Description("Project name")] string project,
        [Description("Working directory")] string? directory = null)
    {
        project = ResolveProject(project, Engram.Store.Scopes.Team);
        await store.CreateSessionAsync(id, project, directory ?? "");
        return $"Session \"{id}\" started for project \"{project}\"";
    }

    // ─── mem_session_end ─────────────────────────────────────────────────────

    [McpServerTool(Name = "mem_session_end", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Mark a coding session as completed with an optional summary.")]
    public async Task<string> MemSessionEnd(
        [Description("Session identifier to close")] string id,
        [Description("Summary of what was accomplished")] string? summary = null)
    {
        await store.EndSessionAsync(id, summary ?? "");
        return $"Session \"{id}\" completed";
    }

    // ─── mem_capture_passive ─────────────────────────────────────────────────

    [McpServerTool(Name = "mem_capture_passive", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("""
        Extract and save structured learnings from text output. Use this at the end of a task to capture knowledge automatically.

        The tool looks for sections like "## Key Learnings:" or "## Aprendizajes Clave:" and extracts numbered or bulleted items. Each item is saved as a separate observation.

        Duplicates are automatically detected and skipped — safe to call multiple times with the same content.
        """)]
    public async Task<string> MemCapturePassive(
        [Description("The text output containing a '## Key Learnings:' section with numbered or bulleted items")] string content,
        [Description("Session ID (default: manual-save-{project})")] string? session_id = null,
        [Description("Project name")] string? project = null,
        [Description("Source identifier (e.g. 'subagent-stop', 'session-end')")] string? source = null)
    {
        if (string.IsNullOrEmpty(content))
            return "Error: content is required — include text with a '## Key Learnings:' section";

        project    = ResolveProject(project, Engram.Store.Scopes.Personal);
        session_id ??= DefaultSessionId(project);
        source     ??= "mcp-passive";

        await store.CreateSessionAsync(session_id, project, "");

        var learnings = PassiveCapture.ExtractLearnings(content);
        int saved = 0;

        foreach (var learning in learnings)
        {
            var title  = learning.Length > 60 ? learning[..60] + "..." : learning;
            var result = await store.AddObservationAsync(new AddObservationParams
            {
                SessionId = session_id,
                Type      = "passive",
                Title     = title,
                Content   = learning,
                Project   = project,
                Scope     = Engram.Store.Scopes.Personal,
                ToolName  = source,
            });
            if (result > 0) saved++;
        }

        return $"Passive capture complete: extracted={learnings.Count} saved={saved} duplicates={learnings.Count - saved}";
    }

    // ─── mem_merge_projects ──────────────────────────────────────────────────

    [McpServerTool(Name = "mem_merge_projects", ReadOnly = false, Idempotent = true, Destructive = true, OpenWorld = false)]
    [Description("Merge memories from multiple project name variants into one canonical name. Use when you discover project name drift (e.g. 'Engram' and 'engram' should be the same project). DESTRUCTIVE — moves all records from source names to the canonical name.")]
    public async Task<string> MemMergeProjects(
        [Description("Comma-separated list of project names to merge FROM (e.g. 'Engram,engram-memory,ENGRAM')")] string from,
        [Description("The canonical project name to merge INTO (e.g. 'engram')")] string to)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return "Error: both 'from' and 'to' are required";

        var sources = from.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                         .ToList();

        if (sources.Count == 0)
            return "Error: at least one source project name is required in 'from'";

        var result = await store.MergeProjectsAsync(sources, to);

        return $"""
            Merged {result.SourcesMerged.Count} source(s) into "{result.Canonical}":
              Observations moved: {result.ObservationsUpdated}
              Sessions moved:     {result.SessionsUpdated}
              Prompts moved:      {result.PromptsUpdated}
            """;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the final namespaced project string for storage.
    /// Scope drives the namespace prefix (team/ vs user/).
    /// </summary>
    private string ResolveProject(string? project, string scope)
    {
        var namespaced = cfg.ResolveNamespacedProject(project, scope);
        return Normalizers.NormalizeProject(namespaced);
    }

    /// <summary>
    /// Backward-compatible overload: resolves using personal scope (user/ prefix).
    /// Used by tools that don't participate in team/personal classification
    /// (mem_search without scope, mem_save_prompt, mem_session_start).
    /// </summary>
    private string ResolveProject(string? project)
        => ResolveProject(project, Engram.Store.Scopes.Personal);

    /// <summary>
    /// Auto-classifies the scope based on observation type.
    /// Team types: architecture, decision, bugfix, pattern, session_summary,
    ///             config, discovery, learning, manual
    /// Personal types: tool_use, file_change, command, file_read, search, passive
    /// null/unknown → "team" (safe default for shared knowledge)
    /// </summary>
    private static string AutoClassifyScope(string? type) => type switch
    {
        "tool_use"    or
        "file_change" or
        "command"     or
        "file_read"   or
        "search"      or
        "passive"     => Engram.Store.Scopes.Personal,
        _             => Engram.Store.Scopes.Team,
    };

    private static string DefaultSessionId(string project)
        => string.IsNullOrEmpty(project) ? "manual-save" : $"manual-save-{project}";

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "...";
}
