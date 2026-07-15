// Engram — Persistent memory for AI coding agents (C# port)
// Usage:
//   engram serve [port]     Start HTTP + MCP server
//   engram mcp              Start MCP server only (stdio transport)
//   engram search <query>   Search memories from CLI
//   engram save <title> <content>  Save a memory from CLI
//   engram context [project]       Show recent context
//   engram stats            Show memory stats
//   engram export [file]    Export to JSON
//   engram import <file>    Import from JSON
//   engram sync status [--json]  Show mutation-based sync status
//   engram sync export       Export gzip chunk
//   engram sync import       Import gzip chunks
//   engram projects         Manage projects
//   engram obsidian-export   Export memories to Obsidian vault
//   engram version          Print version

using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Engram.Cli;
using Engram.Mcp;
using Engram.Server;
using Engram.Server.Dtos;
using Engram.Store;
using Engram.Sync;
using Engram.Sync.Transport;
using Engram.Obsidian;
using Engram.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const string Version = "1.3.0";

// ─── Root command ────────────────────────────────────────────────────────────

var root = new RootCommand("Engram — persistent memory for AI coding agents");

// ─── serve ───────────────────────────────────────────────────────────────────

var serveCmd  = new Command("serve", "Start the HTTP API server");
var portOpt   = new Option<int>("--port", () => 7437, "Port to listen on (env: ENGRAM_PORT)");
var serveNoAutoEnrollOpt = new Option<bool>("--no-auto-enroll", "Disable auto-generation of .engram-id (enabled by default; also via ENGRAM_AUTO_ENROLL=false)");
serveCmd.AddOption(portOpt);
serveCmd.AddOption(serveNoAutoEnrollOpt);
serveCmd.SetHandler(async (int port, bool noAutoEnroll) =>
{
    var envPort = Environment.GetEnvironmentVariable("ENGRAM_PORT");
    if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out var p)) port = p;

    var cfg   = StoreConfig.FromEnvironment();
    using var store = OpenStore(cfg);

    // Auto-enroll .engram-id by default (opt-out via --no-auto-enroll, ENGRAM_AUTO_ENROLL=false, or ~/.engram/config.json auto_enroll: false)
    var serveEnvAutoEnroll = Environment.GetEnvironmentVariable("ENGRAM_AUTO_ENROLL");
    var serveEnvDisabled = string.Equals(serveEnvAutoEnroll, "false", StringComparison.OrdinalIgnoreCase)
        || string.Equals(serveEnvAutoEnroll, "0", StringComparison.OrdinalIgnoreCase);
    var serveConfigDisabled = IsAutoEnrollDisabledInConfig();
    if (!noAutoEnroll && !serveEnvDisabled && !serveConfigDisabled)
    {
        var cwd = Directory.GetCurrentDirectory();
        if (ProjectIdentity.TryAutoEnroll(cwd, out var generatedId))
            Console.Error.WriteLine($"[engram] Generated project identity: {generatedId}");
    }

    var backendLabel = cfg.IsPostgres ? "PostgreSQL" : "SQLite";
    Console.Error.WriteLine($"[engram] starting HTTP server on :{port} ({backendLabel})");
    var app = EngramServer.Build(store, cfg);
    app.Urls.Clear();
    app.Urls.Add($"http://0.0.0.0:{port}");
    await app.RunAsync();
}, portOpt, serveNoAutoEnrollOpt);

// ─── mcp ─────────────────────────────────────────────────────────────────────

var mcpCmd       = new Command("mcp", "Start the MCP server (stdio transport)");
var mcpProjectOpt = new Option<string?>("--project", "Override detected project name");
var mcpAutoEnrollOpt = new Option<bool>("--no-auto-enroll", "Disable auto-generation of .engram-id (enabled by default; also via ENGRAM_AUTO_ENROLL=false)");
mcpCmd.AddOption(mcpProjectOpt);
mcpCmd.AddOption(mcpAutoEnrollOpt);
mcpCmd.SetHandler(async (string? project, bool noAutoEnroll) =>
{
    var storeCfg = StoreConfig.FromEnvironment();

    // Project detection chain: --project → ENGRAM_PROJECT → git remote → git root → cwd basename
    var defaultProject = project
        ?? storeCfg.Project
        ?? ProjectDetector.DetectProject(Directory.GetCurrentDirectory());
    defaultProject = Normalizers.NormalizeProject(defaultProject);

    // ENG-433: Auto-enroll .engram-id by default (opt-out via --no-auto-enroll, ENGRAM_AUTO_ENROLL=false, or ~/.engram/config.json auto_enroll: false)
    var envAutoEnroll = Environment.GetEnvironmentVariable("ENGRAM_AUTO_ENROLL");
    var envDisabled = string.Equals(envAutoEnroll, "false", StringComparison.OrdinalIgnoreCase)
        || string.Equals(envAutoEnroll, "0", StringComparison.OrdinalIgnoreCase);
    var configDisabled = IsAutoEnrollDisabledInConfig();
    var shouldAutoEnroll = !noAutoEnroll && !envDisabled && !configDisabled;

    if (shouldAutoEnroll)
    {
        var cwd = Directory.GetCurrentDirectory();
        if (ProjectIdentity.TryAutoEnroll(cwd, out var generatedId))
        {
            Console.Error.WriteLine($"[engram] Generated project identity: {generatedId}");
        }
    }

    // User identity: provided by IT via ENGRAM_USER (empty in local mode)
    var user = storeCfg.User ?? "";

    // Store selection: HttpStore (team mode) > PostgresStore > SqliteStore (local mode)
    IStore store = storeCfg.IsRemote
        ? new HttpStore(storeCfg)
        : OpenStore(storeCfg);

    if (storeCfg.IsRemote)
        Console.Error.WriteLine($"[engram] mcp → remote {storeCfg.RemoteUrl} (user={user}, project={defaultProject})");
    else if (storeCfg.IsPostgres)
        Console.Error.WriteLine($"[engram] mcp → PostgreSQL (project={defaultProject})");
    else
        Console.Error.WriteLine($"[engram] mcp → local SQLite (project={defaultProject})");

    using var ownedStore = store;

    var mcpBuilder = EngramMcpServer.CreateBuilder(args);
    mcpBuilder.Services.AddHttpClient("sync");
    mcpBuilder.Services.AddSingleton<IStore>(store);
    mcpBuilder.Services.AddSingleton<Engram.Mcp.WriteQueue>();
    mcpBuilder.Services.AddSingleton(new SessionActivity(TimeSpan.FromMinutes(10)));
    mcpBuilder.Services.AddSingleton(new McpConfig
    {
        DefaultProject = defaultProject,
        User           = user,
    });

    // Register verification services — lazy factory: NoOpVerifier if no API key, LlmVerifier otherwise
    mcpBuilder.Services.AddSingleton<Engram.Verification.IVerifier>(sp =>
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
            return new Engram.Verification.NoOpVerifier();
        return new Engram.Verification.LlmVerifier();
    });
    mcpBuilder.Services.AddSingleton<Engram.Verification.CycleTracker>(
        sp => new Engram.Verification.CycleTracker(sp.GetRequiredService<IStore>()));

    // Register MD promotion service
    mcpBuilder.Services.AddSingleton<Engram.MdGeneration.PromotionService>();

    // Register traceability services
    mcpBuilder.Services.AddSingleton<Engram.Verification.TraceRepository>();
    mcpBuilder.Services.AddSingleton<Engram.Verification.LineageBuilder>();

    // Register memory relation services (ENG-404)
    mcpBuilder.Services.AddSingleton<Engram.Verification.MemoryRelationRepository>();
    mcpBuilder.Services.AddSingleton<Engram.Verification.MemoryLineageBuilder>();

    // Register diagnostic service
    mcpBuilder.Services.AddSingleton<IDiagnosticService>(sp =>
    {
        var store = sp.GetRequiredService<IStore>();
        var serverUrl = Environment.GetEnvironmentVariable("ENGRAM_SERVER_URL");
        return new DiagnosticService(store, serverUrl: serverUrl);
    });

    // Register offline-first-sync services (Phase 2.4) — only when local store supports sync journal
    var syncConfig = SyncManagerConfig.FromEnvironment();
    if (syncConfig.Enabled && store is ILocalSyncStore localSyncStore)
    {
        var syncMetrics = new SyncMetrics();
        mcpBuilder.Services.AddSingleton(syncConfig);
        mcpBuilder.Services.AddSingleton(syncMetrics);
        mcpBuilder.Services.AddSingleton<IMutationTransport>(sp =>
        {
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("sync");
            var serverUrl = Environment.GetEnvironmentVariable("ENGRAM_SERVER_URL");
            var syncUrl = !string.IsNullOrEmpty(serverUrl)
                ? serverUrl.TrimEnd('/')
                : storeCfg.IsRemote
                    ? storeCfg.RemoteUrl!.TrimEnd('/')
                    : $"http://localhost:{storeCfg.Port}";
            return new MutationTransport(httpClient, syncUrl, storeCfg.User);
        });
        mcpBuilder.Services.AddSingleton<SyncManager>(sp => new SyncManager(
            localSyncStore,
            sp.GetRequiredService<IMutationTransport>(),
            syncConfig,
            sp.GetRequiredService<ILogger<SyncManager>>(),
            syncMetrics));
        mcpBuilder.Services.AddSingleton<ISyncStatusProvider>(sp => sp.GetRequiredService<SyncManager>());
        mcpBuilder.Services.AddHostedService(sp => sp.GetRequiredService<SyncManager>());
    }
    else if (syncConfig.Enabled && store is not ILocalSyncStore)
    {
        Console.Error.WriteLine("[engram] warning: ENGRAM_SYNC_ENABLED=true but store does not support offline sync (use local SQLite, not ENGRAM_URL remote mode)");
    }

    await mcpBuilder.Build().RunAsync();
}, mcpProjectOpt, mcpAutoEnrollOpt);

// ─── search ──────────────────────────────────────────────────────────────────

var searchCmd      = new Command("search", "Search memories");
var searchQueryArg = new Argument<string>("query", "Search query");
var searchTypeOpt  = new Option<string?>("--type",    "Filter by type");
var searchProjOpt  = new Option<string?>("--project", "Filter by project");
var searchScopeOpt = new Option<string?>("--scope",   "Filter by scope: team or personal (omit for both)");
var searchLimitOpt = new Option<int>("--limit", () => 10, "Max results");
searchCmd.AddArgument(searchQueryArg);
searchCmd.AddOption(searchTypeOpt);
searchCmd.AddOption(searchProjOpt);
searchCmd.AddOption(searchScopeOpt);
searchCmd.AddOption(searchLimitOpt);
searchCmd.SetHandler(async (string query, string? type, string? proj, string? scope, int limit) =>
{
    using var store = OpenStore();
    var results = await store.SearchAsync(query, new SearchOptions
    {
        Type    = type,
        Project = proj,
        Scope   = scope,
        Limit   = limit,
    });

    if (results.Count == 0) { Console.WriteLine($"No memories found for: \"{query}\""); return; }

    Console.WriteLine($"Found {results.Count} memories:\n");
    for (int i = 0; i < results.Count; i++)
    {
        var r = results[i].Observation;
        var projectDisplay = r.Project is not null ? $" | project: {r.Project}" : "";
        Console.WriteLine($"[{i+1}] #{r.Id} ({r.Type}) — {r.Title}");
        Console.WriteLine($"    {Truncate(r.Content, 300)}");
        Console.WriteLine($"    {r.CreatedAt}{projectDisplay} | scope: {r.Scope}\n");
    }
}, searchQueryArg, searchTypeOpt, searchProjOpt, searchScopeOpt, searchLimitOpt);

// ─── save ─────────────────────────────────────────────────────────────────────

var saveCmd       = new Command("save", "Save a memory");
var saveTitleArg  = new Argument<string>("title",   "Memory title");
var saveContentArg= new Argument<string>("content", "Memory content");
var saveTypeOpt   = new Option<string>("--type",    () => "manual", "Type");
var saveProjOpt   = new Option<string?>("--project", "Project name");
var saveScopeOpt  = new Option<string?>("--scope",  "Scope: team (shared with all devs) or personal (private). Default: auto-classified from --type");
var saveTopicOpt  = new Option<string?>("--topic",  "Topic key for upsert");
saveCmd.AddArgument(saveTitleArg);
saveCmd.AddArgument(saveContentArg);
saveCmd.AddOption(saveTypeOpt);
saveCmd.AddOption(saveProjOpt);
saveCmd.AddOption(saveScopeOpt);
saveCmd.AddOption(saveTopicOpt);
saveCmd.SetHandler(async (string title, string content, string type, string? proj, string? scope, string? topic) =>
{
    using var store = OpenStore();
    var sessionId = string.IsNullOrEmpty(proj) ? "manual-save" : $"manual-save-{proj}";
    await store.CreateSessionAsync(sessionId, proj ?? "", "");
    var id = await store.AddObservationAsync(new AddObservationParams
    {
        SessionId = sessionId,
        Type      = type,
        Title     = title,
        Content   = content,
        Project   = proj,
        Scope     = scope,
        TopicKey  = topic,
    });
    Console.WriteLine($"Memory saved: #{id} \"{title}\" ({type})");
}, saveTitleArg, saveContentArg, saveTypeOpt, saveProjOpt, saveScopeOpt, saveTopicOpt);

// ─── context ─────────────────────────────────────────────────────────────────

var contextCmd     = new Command("context", "Show recent memory context");
var contextProjArg = new Argument<string?>("project", () => null, "Project name (optional)");
var contextScopeOpt= new Option<string?>("--scope", "Scope filter");
contextCmd.AddArgument(contextProjArg);
contextCmd.AddOption(contextScopeOpt);
contextCmd.SetHandler(async (string? proj, string? scope) =>
{
    using var store = OpenStore();
    var ctx = await store.FormatContextAsync(proj, scope);
    Console.WriteLine(string.IsNullOrEmpty(ctx) ? "No previous session memories found." : ctx);
}, contextProjArg, contextScopeOpt);

// ─── stats ────────────────────────────────────────────────────────────────────

var statsCmd = new Command("stats", "Show memory system statistics");
statsCmd.SetHandler(async () =>
{
    var cfg = StoreConfig.FromEnvironment();
    using var store = OpenStore(cfg);
    var s = await store.StatsAsync();
    var projects = s.Projects.Count > 0 ? string.Join(", ", s.Projects) : "none yet";
    var dbLabel = cfg.IsPostgres
        ? $"PostgreSQL ({cfg.PgConnectionString?.Split(';').FirstOrDefault(p => p.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))?.Split('=').LastOrDefault() ?? "unknown"})"
        : cfg.IsRemote
            ? $"HTTP Remote ({cfg.RemoteUrl})"
            : $"{cfg.DataDir}/engram.db";
    Console.WriteLine($"""
        Engram Memory Stats
          Sessions:     {s.TotalSessions}
          Observations: {s.TotalObservations}
          Prompts:      {s.TotalPrompts}
          Projects:     {projects}
          Database:     {dbLabel}
        """);
});

// ─── export ───────────────────────────────────────────────────────────────────

var exportCmd     = new Command("export", "Export all memories to a JSON file");
var exportFileArg = new Argument<string>("file", () => "engram-export.json", "Output file");
exportCmd.AddArgument(exportFileArg);
exportCmd.SetHandler(async (string file) =>
{
    using var store = OpenStore();
    var data = await store.ExportAsync();
    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented        = true,
    });
    await File.WriteAllTextAsync(file, json);
    Console.WriteLine($"Exported to {file}");
    Console.WriteLine($"  Sessions:     {data.Sessions.Count}");
    Console.WriteLine($"  Observations: {data.Observations.Count}");
    Console.WriteLine($"  Prompts:      {data.Prompts.Count}");
}, exportFileArg);

// ─── import ───────────────────────────────────────────────────────────────────

var importCmd     = new Command("import", "Import memories from a JSON export file");
var importFileArg = new Argument<string>("file", "Input JSON file");
importCmd.AddArgument(importFileArg);
importCmd.SetHandler(async (string file) =>
{
    var json = await File.ReadAllTextAsync(file);
    var data = JsonSerializer.Deserialize<ExportData>(json, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    });
    if (data is null) { Console.Error.WriteLine("error: invalid JSON"); return; }

    using var store = OpenStore();
    var result = await store.ImportAsync(data);
    Console.WriteLine($"Imported from {file}");
    Console.WriteLine($"  Sessions:     {result.SessionsImported}");
    Console.WriteLine($"  Observations: {result.ObservationsImported}");
    Console.WriteLine($"  Prompts:      {result.PromptsImported}");
}, importFileArg);

// ─── sync ─────────────────────────────────────────────────────────────────────

var syncCmd = new Command("sync", "Sync operations");

// sync status — mutation-based sync health via HTTP
var syncStatusCmd = new Command("status", "Show mutation-based sync status");
var syncStatusJsonOpt = new Option<bool>("--json", "Output as JSON (machine-readable)");
syncStatusCmd.AddOption(syncStatusJsonOpt);
syncStatusCmd.SetHandler(async (bool json) =>
{
    var serverUrl = Environment.GetEnvironmentVariable("ENGRAM_SERVER_URL") ?? "http://localhost:7437";
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var response = await client.GetAsync($"{serverUrl}/sync/status");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();

        if (json)
        {
            Console.WriteLine(body);
            return;
        }

        var doc = JsonSerializer.Deserialize<JsonElement>(body);
        var enabled = doc.GetProperty("sync_enabled").GetBoolean();
        var phase = doc.GetProperty("phase").GetString() ?? "";
        var health = doc.GetProperty("health");
        var counts = doc.GetProperty("counts");
        var cursor = doc.GetProperty("cursor");

        Console.WriteLine($"Sync status (mutation-based):");
        Console.WriteLine($"  Enabled:              {enabled}");
        Console.WriteLine($"  Phase:                {phase}");
        Console.WriteLine($"  Health:               {health.GetProperty("status").GetString()}");
        Console.WriteLine($"  Consecutive failures: {health.GetProperty("consecutive_failures").GetInt32()}");
        Console.WriteLine($"  Backoff until:        {health.GetProperty("backoff_until").GetString() ?? "—"}");
        Console.WriteLine($"  Last sync:            {health.GetProperty("last_sync_at").GetString() ?? "—"}");
        Console.WriteLine($"  Last error:           {health.GetProperty("last_error").GetString() ?? "—"}");
        Console.WriteLine($"  Pending push:         {counts.GetProperty("pending_push").GetInt32()}");
        Console.WriteLine($"  Total pushed:         {counts.GetProperty("total_pushed").GetInt64()}");
        Console.WriteLine($"  Total pulled:         {counts.GetProperty("total_pulled").GetInt64()}");
        Console.WriteLine($"  Last pushed seq:      {cursor.GetProperty("last_pushed_seq").GetInt64()}");
        Console.WriteLine($"  Last pulled seq:      {cursor.GetProperty("last_pulled_seq").GetInt64()}");
    }
    catch (HttpRequestException)
    {
        Console.Error.WriteLine("error: No se pudo conectar al servidor — ¿está engram server corriendo?");
        Environment.Exit(1);
    }
    catch (TaskCanceledException)
    {
        Console.Error.WriteLine("error: No se pudo conectar al servidor — ¿está engram server corriendo? (timeout)");
        Environment.Exit(1);
    }
}, syncStatusJsonOpt);

// sync export — export git-friendly chunks
var syncExportCmd = new Command("export", "Export a new chunk to sync dir");
syncExportCmd.SetHandler(async () =>
{
    var syncCfg = SyncConfig.FromEnvironment();
    if (!syncCfg.IsConfigured)
        Console.Error.WriteLine("warning: ENGRAM_SYNC_REPO is not set. Using local sync dir only.");

    using var store = OpenStore();
    var sync  = new EngramSync(store, syncCfg);
    var wrote = await sync.ExportChunkAsync();
    Console.WriteLine(wrote
        ? "New chunk exported to sync dir."
        : "Nothing new to sync — all memories already exported.");
});

// sync import — import git-friendly chunks
var syncImportCmd = new Command("import", "Import new chunks from sync dir");
syncImportCmd.SetHandler(async () =>
{
    var syncCfg = SyncConfig.FromEnvironment();
    if (!syncCfg.IsConfigured)
        Console.Error.WriteLine("warning: ENGRAM_SYNC_REPO is not set. Using local sync dir only.");

    using var store = OpenStore();
    var sync  = new EngramSync(store, syncCfg);
    var imported = await sync.ImportNewChunksAsync();
    Console.WriteLine(imported == 0
        ? "No new chunks to import."
        : $"Imported {imported} observations from new chunks.");
});

// sync enroll — enroll a project for local sync push
var syncEnrollCmd = new Command("enroll", "Enroll a project for sync push");
var enrollProjectOpt = new Option<string>("--project", "Project to enroll");
syncEnrollCmd.AddOption(enrollProjectOpt);
syncEnrollCmd.SetHandler(async (string project) =>
{
    if (string.IsNullOrWhiteSpace(project))
    {
        Console.Error.WriteLine("error: --project is required");
        return;
    }
    using var store = OpenStore();
    if (store is Engram.Store.SqliteStore ss)
    {
        await ss.EnrollProjectLocalAsync(project);
        Console.WriteLine($"Project '{project}' enrolled for sync push.");
    }
    else
        Console.Error.WriteLine("enroll is only supported for local SQLite stores.");
}, enrollProjectOpt);

// sync unenroll — unenroll a project from local sync push
var syncUnenrollCmd = new Command("unenroll", "Unenroll a project from sync push");
var unenrollProjectOpt = new Option<string>("--project", "Project to unenroll");
syncUnenrollCmd.AddOption(unenrollProjectOpt);
syncUnenrollCmd.SetHandler(async (string project) =>
{
    if (string.IsNullOrWhiteSpace(project))
    {
        Console.Error.WriteLine("error: --project is required");
        return;
    }
    using var store = OpenStore();
    if (store is Engram.Store.SqliteStore ss)
    {
        await ss.UnenrollProjectLocalAsync(project);
        Console.WriteLine($"Project '{project}' unenrolled from sync push.");
    }
    else
        Console.Error.WriteLine("unenroll is only supported for local SQLite stores.");
}, unenrollProjectOpt);

syncCmd.AddCommand(syncStatusCmd);
syncCmd.AddCommand(syncExportCmd);
syncCmd.AddCommand(syncImportCmd);
syncCmd.AddCommand(syncEnrollCmd);
syncCmd.AddCommand(syncUnenrollCmd);

// ─── project id (ENG-432) ─────────────────────────────────────────────────────

var projectCmd   = new Command("project", "Project identity operations");
var projectIdCmd = new Command("id", "Show or regenerate the project identity GUID (.engram-id)");
var projectIdJsonOpt      = new Option<bool>("--json", "Output as JSON (machine-readable)");
var projectIdRegenOpt     = new Option<bool>("--regenerate", "Recompute and overwrite .engram-id with the deterministic GUID");
var projectIdSetOpt       = new Option<string?>("--set", "Set .engram-id to a specific GUID (valid UUID)");
var projectIdYesOpt       = new Option<bool>("-y", () => false, "Skip confirmation prompt (assumes yes)");
projectIdCmd.AddOption(projectIdJsonOpt);
projectIdCmd.AddOption(projectIdRegenOpt);
projectIdCmd.AddOption(projectIdSetOpt);
projectIdCmd.AddOption(projectIdYesOpt);
projectIdCmd.SetHandler(async (bool json, bool regen, string? setGuid, bool assumeYes) =>
{
    var cwd = Directory.GetCurrentDirectory();
    var fileGuid = ProjectIdentity.GetProjectId(cwd);
    var computedGuid = ProjectIdentity.TryComputeDeterministicGuid(cwd);

    // Compute GUID from deterministic formula for output / regenerate
    var computed = computedGuid?.ToString("D");

    // ─── Set custom GUID path (REQ-435-001) ─────────────────────────────────────
    if (setGuid is not null)
    {
        if (!Guid.TryParse(setGuid, out var customGuid))
        {
            Console.Error.WriteLine("error: invalid GUID format");
            Environment.Exit(1);
            return;
        }

        if (!assumeYes && fileGuid is not null)
        {
            Console.Write($"Overwrite existing project identity ({fileGuid}) with {setGuid}? [y/N] ");
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer != "y" && answer != "yes")
            {
                Console.WriteLine("Cancelled.");
                return;
            }
        }

        ProjectIdentity.SaveProjectId(cwd, customGuid);
        Console.WriteLine($"Project identity set to: {setGuid}");

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                project_id = setGuid,
                source = "manual",
            }));
        }
        return;
    }

    // ─── Regenerate path ────────────────────────────────────────────────
    if (regen)
    {
        if (computedGuid is null)
        {
            Console.Error.WriteLine("error: Cannot regenerate — no git remote or no commits in this directory.");
            Environment.Exit(1);
            return;
        }

        if (!assumeYes)
        {
            Console.Write($"Regenerate project identity? This will overwrite .engram-id with {computed}. [y/N] ");
            var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (answer != "y" && answer != "yes")
            {
                Console.WriteLine("Cancelled.");
                return;
            }
        }

        ProjectIdentity.SaveProjectId(cwd, computedGuid.Value);
        Console.WriteLine($"Project identity regenerated: {computed}");

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                project_id = computed,
                source     = "computed",
                computed   = computed,
            }));
        }
        return;
    }

    // ─── Default show path ──────────────────────────────────────────────
    var source = fileGuid is not null ? "file" : (computed is not null ? "computed" : "none");

    if (json)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            project_id = fileGuid,
            source,
            computed,
        }));
        return;
    }

    // Text output
    if (fileGuid is not null)
    {
        Console.WriteLine($"project_id: {fileGuid}");
    }
    else if (computed is not null)
    {
        Console.WriteLine($"project_id: {computed} (computed, not saved)");
    }
    else
    {
        Console.WriteLine("No project identity found.");
    }
}, projectIdJsonOpt, projectIdRegenOpt, projectIdSetOpt, projectIdYesOpt);

projectCmd.AddCommand(projectIdCmd);

// ─── project migrate (ENG-435) ────────────────────────────────────────────

var migrateCmd = new Command("migrate", "Migrate project data to a new identity");
var migrateToOpt    = new Option<string>("--to", "Target project (GUID or name) - required");
var migrateFromOpt   = new Option<string?>("--from", "Source project (defaults to .engram-id)");
var migrateYesOpt   = new Option<bool>("-y", () => false, "Skip confirmation prompt");
var migrateDryRunOpt = new Option<bool>("--dry-run", "Preview without making changes");
migrateCmd.AddOption(migrateToOpt);
migrateCmd.AddOption(migrateFromOpt);
migrateCmd.AddOption(migrateYesOpt);
migrateCmd.AddOption(migrateDryRunOpt);
migrateCmd.SetHandler(async (string to, string? from, bool assumeYes, bool dryRun) =>
{
    // Auto-detect source from .engram-id if --from not provided
    var source = from;
    if (string.IsNullOrEmpty(source))
    {
        var cwd = Directory.GetCurrentDirectory();
        var existingId = ProjectIdentity.GetProjectId(cwd);
        if (existingId is null)
        {
            Console.Error.WriteLine("error: No source project. Either specify --from or ensure .engram-id exists.");
            Environment.Exit(1);
            return;
        }
        source = existingId;
    }

    // Validate target GUID format
    if (!Guid.TryParse(to, out var targetGuid))
    {
        Console.Error.WriteLine("error: invalid target GUID format");
        Environment.Exit(1);
        return;
    }

    var target = targetGuid.ToString("D");

    // Confirmation prompt (skip with -y)
    if (!assumeYes)
    {
        Console.Write($"Migrate from '{source}' to '{target}'? This will update all associated data. [y/N] ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (answer != "y" && answer != "yes")
        {
            Console.WriteLine("Cancelled.");
            return;
        }
    }

    // Dry-run preview — use COUNT queries without modifying data
    if (dryRun)
    {
        using var store = OpenStore();
        var conn = (store is PostgresStore pg) ? pg.OpenRawConnection() : throw new NotSupportedException("Dry-run preview requires PostgreSQL backend");
        try
        {
            // Count observations
            using var cmdObs = conn.CreateCommand();
            cmdObs.CommandText = "SELECT COUNT(*) FROM observations WHERE project = @proj AND deleted_at IS NULL";
            cmdObs.Parameters.AddWithValue("@proj", source);
            var obsCount = Convert.ToInt64(cmdObs.ExecuteScalar());

            // Count sessions
            using var cmdSess = conn.CreateCommand();
            cmdSess.CommandText = "SELECT COUNT(*) FROM sessions WHERE project = @proj";
            cmdSess.Parameters.AddWithValue("@proj", source);
            var sessCount = Convert.ToInt64(cmdSess.ExecuteScalar());

            // Count prompts
            using var cmdPrompt = conn.CreateCommand();
            cmdPrompt.CommandText = "SELECT COUNT(*) FROM user_prompts WHERE project = @proj AND deleted_at IS NULL";
            cmdPrompt.Parameters.AddWithValue("@proj", source);
            var promptCount = Convert.ToInt64(cmdPrompt.ExecuteScalar());

            Console.WriteLine($"Would migrate {obsCount} observations, {sessCount} sessions, {promptCount} prompts");
        }
        finally
        {
            conn.Close();
        }
        return;
    }

    // Execute migration
    try
    {
        using var store = OpenStore();
        var result = await store.MigrateProjectAsync(source, target);
        Console.WriteLine($"Migrated {result.ObservationsMigrated} observations, {result.SessionsMigrated} sessions, {result.PromptsMigrated} prompts");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Migration failed: {ex.Message}. Rolled back.");
        Environment.Exit(1);
    }
}, migrateToOpt, migrateFromOpt, migrateYesOpt, migrateDryRunOpt);

projectCmd.AddCommand(migrateCmd);

// ─── promote ─────────────────────────────────────────────────────────────────

var promoteCmd = new Command("promote", "Promote observations to .md files");
var promoteIdOpt = new Option<long>("--id", "Observation ID to promote");
var promoteDirOpt = new Option<string?>("--md-dir", () => "docs/decisions", "Target directory for .md files");
var promoteSyncOpt = new Option<bool>("--sync", "Promote all unpromoted observations");
var promoteDryRunOpt = new Option<bool>("--dry-run", "Preview without writing");
promoteCmd.AddOption(promoteIdOpt);
promoteCmd.AddOption(promoteDirOpt);
promoteCmd.AddOption(promoteSyncOpt);
promoteCmd.AddOption(promoteDryRunOpt);
promoteCmd.SetHandler(async (long id, string? mdDir, bool sync, bool dryRun) =>
{
    using var store = OpenStore();
    var service = new Engram.MdGeneration.PromotionService(store);

    if (sync)
    {
        var result = await service.SyncAsync(mdDir ?? "docs/decisions", dryRun);
        if (dryRun)
            Console.WriteLine($"[dry-run] Would promote {result.Promoted} observations to {mdDir ?? "docs/decisions"}/");
        else
            Console.WriteLine($"Promoted {result.Promoted} observations to {mdDir ?? "docs/decisions"}/");
    }
    else if (id > 0)
    {
        var result = await service.PromoteAsync(id, mdDir ?? "docs/decisions");
        if (result == 0)
            Console.WriteLine($"Error: observation #{id} not found or already promoted");
        else
            Console.WriteLine($"Observation #{id} promoted to {mdDir ?? "docs/decisions"}/");
    }
    else
    {
        Console.Error.WriteLine("error: specify --id or --sync");
    }
}, promoteIdOpt, promoteDirOpt, promoteSyncOpt, promoteDryRunOpt);

// ─── projects ─────────────────────────────────────────────────────────────────

var projectsCmd = new Command("projects", "Manage projects");

// projects list
var projectsListCmd = new Command("list", "List all projects with stats");
projectsListCmd.SetHandler(async () =>
{
    using var store = OpenStore();
    var stats = await store.ListProjectsWithStatsAsync();
    if (stats.Count == 0) { Console.WriteLine("No projects found."); return; }

    Console.WriteLine($"Projects ({stats.Count}):");
    foreach (var p in stats)
    {
        var sessionWord = p.SessionCount == 1 ? "session" : "sessions";
        var promptWord  = p.PromptCount == 1  ? "prompt"  : "prompts";
        Console.WriteLine($"  {p.Name,-30} {p.ObservationCount,4} obs   {p.SessionCount,3} {sessionWord,-9}  {p.PromptCount,3} {promptWord}");
    }
});

// projects consolidate
var consolidateAllOpt   = new Option<bool>("--all", "Consolidate all similar projects (no interactive)");
var consolidateDryRunOpt = new Option<bool>("--dry-run", "Show what would be merged without changing anything");
var consolidateCmd = new Command("consolidate", "Merge similar project names into a canonical name");
consolidateCmd.AddOption(consolidateAllOpt);
consolidateCmd.AddOption(consolidateDryRunOpt);
consolidateCmd.SetHandler(async (bool doAll, bool dryRun) =>
{
    using var store = OpenStore();

    if (!doAll)
    {
        // Single-project mode: detect canonical project for cwd, find variants
        var canonical = ProjectDetector.DetectProject(Directory.GetCurrentDirectory());
        canonical = Normalizers.NormalizeProject(canonical);

        var allNames = await store.ListProjectNamesAsync();

        // Check if the detected canonical actually exists in the DB
        bool canonicalExists = allNames.Any(n => n == canonical);
        if (!canonicalExists)
            Console.WriteLine($"Note: \"{canonical}\" has no existing memories. Merging will move memories into this new project name.");

        // Find candidates by name similarity
        var similar = ProjectDetector.FindSimilar(canonical, allNames, 3);

        if (similar.Count == 0)
        {
            Console.WriteLine($"No similar project names found for \"{canonical}\". Nothing to consolidate.");
            return;
        }

        Console.WriteLine($"Detected project: \"{canonical}\"");
        Console.WriteLine();
        Console.WriteLine("Found similar project names:");
        for (int i = 0; i < similar.Count; i++)
        {
            var sm = similar[i];
            var obsCount = await store.CountObservationsForProjectAsync(sm.Name);
            Console.WriteLine($"  [{i + 1}] {sm.Name,-30} {obsCount,3} obs  ({sm.MatchType})");
        }

        if (dryRun)
        {
            Console.WriteLine($"\n[dry-run] Would merge {similar.Count} project(s) into \"{canonical}\"");
            return;
        }

        Console.WriteLine($"\nSelect which to merge into \"{canonical}\" (comma-separated numbers, 'all', or 'none'): ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";

        if (answer == "none" || answer == "n" || answer == "")
        {
            Console.WriteLine("Cancelled.");
            return;
        }

        var sources = new List<string>();
        if (answer == "all" || answer == "a")
        {
            sources.AddRange(similar.Select(sm => sm.Name));
        }
        else
        {
            foreach (var part in answer.Split(','))
            {
                var trimmed = part.Trim();
                if (!int.TryParse(trimmed, out var idx) || idx < 1 || idx > similar.Count)
                {
                    Console.Error.WriteLine($"Invalid selection: \"{trimmed}\" (expected 1-{similar.Count})");
                    return;
                }
                sources.Add(similar[idx - 1].Name);
            }
        }

        if (sources.Count == 0) { Console.WriteLine("Nothing selected."); return; }

        Console.WriteLine($"\nMerging {sources.Count} project(s) into \"{canonical}\"...");
        var result = await store.MergeProjectsAsync(sources, canonical);
        Console.WriteLine($"Done! Merged into \"{result.Canonical}\":");
        Console.WriteLine($"  Observations: {result.ObservationsUpdated}");
        Console.WriteLine($"  Sessions:     {result.SessionsUpdated}");
        Console.WriteLine($"  Prompts:      {result.PromptsUpdated}");
        return;
    }

    // --all mode: group all projects by similarity + shared directories
    var projects = await store.ListProjectsWithStatsAsync();
    var groups = ProjectConsolidator.GroupSimilarProjects(projects);

    if (groups.Count == 0)
    {
        Console.WriteLine("No similar project name groups found.");
        return;
    }

    Console.WriteLine($"Found {groups.Count} group(s) of similar project names:\n");

    for (int i = 0; i < groups.Count; i++)
    {
        var g = groups[i];
        Console.WriteLine($"Group {i + 1}:");
        for (int j = 0; j < g.Names.Count; j++)
        {
            var name = g.Names[j];
            var obs = projects.FirstOrDefault(p => p.Name == name)?.ObservationCount ?? 0;
            var marker = name == g.Canonical ? "→ " : "  ";
            Console.WriteLine($"  {marker}[{j + 1}] {name,-30} {obs,3} obs");
        }
        Console.WriteLine($"  Suggested canonical: \"{g.Canonical}\" (→)");

        if (dryRun)
        {
            Console.WriteLine($"  [dry-run] Would merge into \"{g.Canonical}\"\n");
            continue;
        }

        Console.WriteLine("\n  Options:");
        Console.WriteLine($"    all     — merge everything into \"{g.Canonical}\"");
        Console.WriteLine($"    1,3,... — merge only selected numbers into \"{g.Canonical}\"");
        Console.WriteLine("    rename  — choose a different canonical name");
        Console.WriteLine("    skip    — don't touch this group");
        Console.Write("  Choice: ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";

        var canonical = g.Canonical;

        if (answer == "skip" || answer == "s" || answer == "n" || answer == "")
        {
            Console.WriteLine("  Skipped.\n");
            continue;
        }

        if (answer == "rename" || answer == "r")
        {
            Console.Write("  Enter canonical name: ");
            canonical = Console.ReadLine()?.Trim() ?? "";
            if (string.IsNullOrEmpty(canonical)) { Console.WriteLine("  Empty input, skipping.\n"); continue; }
            answer = "all";
        }

        var sources = new List<string>();
        if (answer == "all" || answer == "a" || answer == "y" || answer == "yes")
        {
            foreach (var name in g.Names)
                if (name != canonical) sources.Add(name);
        }
        else
        {
            foreach (var part in answer.Split(','))
            {
                var trimmed = part.Trim();
                if (!int.TryParse(trimmed, out var idx) || idx < 1 || idx > g.Names.Count)
                {
                    Console.Error.WriteLine($"  Invalid selection: \"{trimmed}\" (expected 1-{g.Names.Count})");
                    Console.WriteLine();
                    continue;
                }
                var selected = g.Names[idx - 1];
                if (selected != canonical) sources.Add(selected);
            }
        }

        if (sources.Count == 0) { Console.WriteLine("  Nothing to merge.\n"); continue; }

        var result = await store.MergeProjectsAsync(sources, canonical);
        Console.WriteLine($"  Merged: {result.ObservationsUpdated} obs, {result.SessionsUpdated} sessions, {result.PromptsUpdated} prompts\n");
    }
}, consolidateAllOpt, consolidateDryRunOpt);

// projects prune
var pruneDryRunOpt = new Option<bool>("--dry-run", "Show what would be pruned without deleting anything");
var pruneCmd = new Command("prune", "Remove projects with 0 observations (sessions & prompts only)");
pruneCmd.AddOption(pruneDryRunOpt);
pruneCmd.SetHandler(async (bool dryRun) =>
{
    using var store = OpenStore();
    var allStats = await store.ListProjectsWithStatsAsync();

    // Find projects with 0 observations
    var candidates = allStats.Where(ps => ps.ObservationCount == 0).ToList();

    if (candidates.Count == 0)
    {
        Console.WriteLine("No empty projects to prune.");
        return;
    }

    Console.WriteLine($"Found {candidates.Count} project(s) with 0 observations:\n");
    for (int i = 0; i < candidates.Count; i++)
    {
        var ps = candidates[i];
        var sessionWord = ps.SessionCount == 1 ? "session" : "sessions";
        var promptWord  = ps.PromptCount == 1  ? "prompt"  : "prompts";
        Console.WriteLine($"  [{i + 1}] {ps.Name,-30} {ps.SessionCount,3} {sessionWord,-9}  {ps.PromptCount,3} {promptWord}");
    }

    if (dryRun)
    {
        Console.WriteLine($"\n[dry-run] Would prune {candidates.Count} project(s)");
        return;
    }

    Console.Write("\nSelect which to prune (comma-separated numbers, 'all', or 'none'): ");
    var answer = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";

    if (answer == "none" || answer == "n" || answer == "")
    {
        Console.WriteLine("Cancelled.");
        return;
    }

    var selected = new List<ProjectStats>();
    if (answer == "all" || answer == "a")
    {
        selected = candidates;
    }
    else
    {
        foreach (var part in answer.Split(','))
        {
            var trimmed = part.Trim();
            if (!int.TryParse(trimmed, out var idx) || idx < 1 || idx > candidates.Count)
            {
                Console.Error.WriteLine($"Invalid selection: \"{trimmed}\" (expected 1-{candidates.Count})");
                return;
            }
            selected.Add(candidates[idx - 1]);
        }
    }

    if (selected.Count == 0) { Console.WriteLine("Nothing selected."); return; }

    long totalSessions = 0;
    long totalPrompts = 0;
    foreach (var ps in selected)
    {
        var result = await store.PruneProjectAsync(ps.Name);
        totalSessions += result.SessionsDeleted;
        totalPrompts  += result.PromptsDeleted;
    }

    Console.WriteLine($"\nPruned {selected.Count} project(s): {totalSessions} sessions, {totalPrompts} prompts removed.");
}, pruneDryRunOpt);

projectsCmd.AddCommand(projectsListCmd);
projectsCmd.AddCommand(consolidateCmd);
projectsCmd.AddCommand(pruneCmd);

// ─── retention ────────────────────────────────────────────────────────────────

var retentionCmd = new Command("retention", "Manage memory retention and pruning");

// retention check
var retentionCheckCmd = new Command("check", "Show retention statistics");
retentionCheckCmd.SetHandler(async () =>
{
    using var store = OpenStore();
    var stats = await store.GetRetentionStatsAsync();

    Console.WriteLine($"Total observations: {stats.TotalObservations}");
    Console.WriteLine($"Without topic_key (90d): {stats.WithoutTopicKey90d}");
    Console.WriteLine();
    Console.WriteLine("Age buckets:");
    foreach (var bucket in stats.AgeBuckets)
    {
        var bar = new string('█', Math.Min(bucket.Count / 5, 40));
        Console.WriteLine($"  {bucket.Label,-15} {bucket.Count,6} {bar}");
    }
});

// retention prune
var retentionPruneCmd = new Command("prune", "Prune old observations by TTL");
var retPruneTypeOpt = new Option<string?>("--type", "Filter by observation type");
var retPruneDryRunOpt = new Option<bool>("--dry-run", "Preview without modifying");
retentionPruneCmd.AddOption(retPruneTypeOpt);
retentionPruneCmd.AddOption(retPruneDryRunOpt);
retentionPruneCmd.SetHandler(async (string? type, bool dryRun) =>
{
    using var store = OpenStore();
    var result = await store.PruneOldObservationsAsync(new RetentionPruneParams
    {
        Type   = type,
        DryRun = dryRun,
    });

    if (dryRun)
        Console.WriteLine($"[dry-run] Would prune {result.Pruned} observations");
    else
        Console.WriteLine($"Pruned {result.Pruned} observations");

    foreach (var (t, count) in result.Details)
        Console.WriteLine($"  {t}: {count}");
}, retPruneTypeOpt, retPruneDryRunOpt);

retentionCmd.AddCommand(retentionCheckCmd);
retentionCmd.AddCommand(retentionPruneCmd);

// ─── obsidian-export ──────────────────────────────────────────────────────────

var obsidianCmd          = new Command("obsidian-export", "Export memories to an Obsidian vault as markdown files");
var obsidianVaultOpt     = new Option<string>("--vault", "Path to the Obsidian vault root (required)");
var obsidianProjectOpt   = new Option<string?>("--project", "Filter export to a single project");
var obsidianPersonalOpt  = new Option<bool>("--include-personal", "Include scope=personal observations (default: team only)");
var obsidianForceOpt     = new Option<bool>("--force", "Ignore state file, do a full re-export");
var obsidianGraphOpt     = new Option<string>("--graph-config", () => "preserve", "Graph config mode: preserve|force|skip (default: preserve)");
var obsidianLimitOpt     = new Option<int>("--limit", () => 0, "Max observations to export (0 = no limit)");
var obsidianSinceOpt     = new Option<string?>("--since", "Filter by date: ISO 8601 (2025-01-01) or relative (30d, 7d, 24h, 5m)");
var obsidianWatchOpt      = new Option<bool>("--watch", "Run in watch mode (continuous export at intervals)");
var obsidianIntervalOpt  = new Option<string?>("--interval", "Watch interval: 30s, 5m, 1h (default 60s when --watch)");
obsidianCmd.AddOption(obsidianVaultOpt);
obsidianCmd.AddOption(obsidianProjectOpt);
obsidianCmd.AddOption(obsidianPersonalOpt);
obsidianCmd.AddOption(obsidianForceOpt);
obsidianCmd.AddOption(obsidianGraphOpt);
obsidianCmd.AddOption(obsidianLimitOpt);
obsidianCmd.AddOption(obsidianSinceOpt);
obsidianCmd.AddOption(obsidianWatchOpt);
obsidianCmd.AddOption(obsidianIntervalOpt);
obsidianCmd.SetHandler(async (InvocationContext context) =>
{
    // Get option values from context - need explicit type
    var vaultResult = context.ParseResult.GetValueForOption(obsidianVaultOpt);
    var vault = vaultResult ?? "";
    var project = context.ParseResult.GetValueForOption(obsidianProjectOpt);
    var includePersonal = context.ParseResult.GetValueForOption(obsidianPersonalOpt);
    var force = context.ParseResult.GetValueForOption(obsidianForceOpt);
    var graphConfig = context.ParseResult.GetValueForOption(obsidianGraphOpt) ?? "preserve";
    var limit = context.ParseResult.GetValueForOption(obsidianLimitOpt);
    var since = context.ParseResult.GetValueForOption(obsidianSinceOpt);
    var watch = context.ParseResult.GetValueForOption(obsidianWatchOpt);
    var interval = context.ParseResult.GetValueForOption(obsidianIntervalOpt);

    if (string.IsNullOrEmpty(vault))
    {
        Console.Error.WriteLine("error: --vault path is required");
        return;
    }

    GraphConfigMode graphMode;
    try
    {
        graphMode = GraphConfig.Parse(graphConfig);
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine($"error: {ex.Message}");
        return;
    }

    // Parse --since if provided
    string? sinceValue = null;
    if (!string.IsNullOrEmpty(since))
    {
        try
        {
            var parsedSince = SinceArgumentParser.Parse(since);
            sinceValue = parsedSince.ToString("yyyy-MM-ddTHH:mm:ssZ");
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return;
        }
    }

    // Default interval for watch mode
    var intervalValue = interval ?? "60s";

    var storeCfg = StoreConfig.FromEnvironment();
    using var store = OpenStore(storeCfg);
    var reader = new StoreReaderAdapter(store);

    var config = new ExportConfig
    {
        VaultPath         = vault,
        Project           = project,
        IncludePersonal   = includePersonal,
        Force             = force,
        GraphConfig       = graphMode,
        Limit             = limit,
        Since             = sinceValue ?? "",
        Watch             = watch,
        Interval         = intervalValue,
    };

    // Handle watch mode
    if (watch)
    {
        var intervalParsed = WatchIntervalParser.Parse(intervalValue);
        var watchConfig = new WatchConfig
        {
            VaultPath = vault,
            Project = project,
            Interval = intervalParsed,
            InitialSince = sinceValue != null ? SinceArgumentParser.Parse(sinceValue) : null,
            StoreReader = reader,
            IncludePersonal = includePersonal,
        };

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await WatchLoop.RunAsync(watchConfig, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        return;
    }

    // Single export (non-watch)
    var exporter = new Exporter(reader, config);
    var result = exporter.Export();

    var word = result.Created == 1 ? "file" : "files";
    Console.WriteLine($"Obsidian export complete:");
    Console.WriteLine($"  Created:  {result.Created} {word}");
    Console.WriteLine($"  Updated:  {result.Updated}");
    Console.WriteLine($"  Deleted:  {result.Deleted}");
    Console.WriteLine($"  Skipped:  {result.Skipped}");
    Console.WriteLine($"  Hubs:     {result.HubsCreated}");
    if (result.Errors.Count > 0)
    {
        Console.WriteLine($"  Errors:   {result.Errors.Count}");
        foreach (var err in result.Errors)
            Console.Error.WriteLine($"    - {err.Message}");
    }
});

// ─── version ──────────────────────────────────────────────────────────────────

var versionCmd = new Command("version", "Print version");
versionCmd.SetHandler(() => Console.WriteLine($"engram {Version}"));

// ─── doctor ───────────────────────────────────────────────────────────────────

var doctorCmd = new Command("doctor", "Run diagnostic health checks on the engram ecosystem");
var doctorServerOpt = new Option<string?>("--server", "Engram server URL (env: ENGRAM_SERVER_URL)");
doctorCmd.AddOption(doctorServerOpt);
doctorCmd.SetHandler(async (string? serverUrl) =>
{
    // Use provided URL or fall back to environment variable
    var url = serverUrl ?? Environment.GetEnvironmentVariable("ENGRAM_SERVER_URL");
    
    var storeCfg = StoreConfig.FromEnvironment();
    using var store = OpenStore(storeCfg);
    
    // Create diagnostic service
    var diagnosticService = new DiagnosticService(store, serverUrl: url);
    
    // Run diagnostics
    var result = await diagnosticService.RunDiagnosticsAsync();
    
    // Output results
    Console.WriteLine("Engram Diagnostic Report");
    Console.WriteLine("========================");
    Console.WriteLine();
    
    foreach (var (name, health) in result.Components.OrderBy(kvp => kvp.Key))
    {
        var status = health.IsHealthy ? "✓" : "✗";
        var latency = health.LatencyMs > 0 ? $" ({health.LatencyMs}ms)" : "";
        Console.WriteLine($"{status} {name,-15} {health.Message}{latency}");
    }
    
    Console.WriteLine();
    if (result.IsHealthy)
    {
        Console.WriteLine("Status: All systems operational");
        Environment.Exit(0);
    }
    else
    {
        Console.WriteLine("Status: Some components are unhealthy");
        Environment.Exit(1);
    }
}, doctorServerOpt);

// ─── relations (ENG-404) ──────────────────────────────────────────────────

var relationsCmd = new Command("relations", "Manage memory observation relations");

var relActionOpt = new Option<string>("--action", "Action: add, get, or delete (required)");
var relObsIdOpt = new Option<long>("--observation-id", "Source observation ID (required)");
var relTargetIdOpt = new Option<long>("--target-id", "Target observation ID (required for add/delete)");
var relTypeOpt = new Option<string>("--type", "Relation type: depends_on, supersedes, conflicts_with, related_to (required for add/delete)");
var relProjOpt = new Option<string?>("--project", "Project name");
relationsCmd.AddOption(relActionOpt);
relationsCmd.AddOption(relObsIdOpt);
relationsCmd.AddOption(relTargetIdOpt);
relationsCmd.AddOption(relTypeOpt);
relationsCmd.AddOption(relProjOpt);
relationsCmd.SetHandler(async (string action, long obsId, long targetId, string? relType, string? proj) =>
{
    using var store = OpenStore();
    var project = proj ?? Normalizers.NormalizeProject(ProjectDetector.DetectProject(Directory.GetCurrentDirectory()));
    var sessionId = $"rel-cli-{DateTime.UtcNow:yyyyMMdd}";
    var repo = new Engram.Verification.MemoryRelationRepository(store);

    switch (action.ToLowerInvariant())
    {
        case "get":
            var relations = await repo.GetRelationsAsync(project, obsId);
            if (relations.Count == 0) { Console.WriteLine($"No relations for obs#{obsId}"); return; }
            foreach (var r in relations)
                Console.WriteLine($"- {r.Type}: {r.TargetObservationId}");
            return;

        case "add":
            if (targetId == 0) { Console.Error.WriteLine("error: --target-id required for add"); return; }
            if (string.IsNullOrEmpty(relType)) { Console.Error.WriteLine("error: --type required for add"); return; }
            if (!Engram.Verification.RelationValidator.IsValidType(relType))
            {
                Console.Error.WriteLine($"error: invalid type '{relType}'. Valid: {string.Join(", ", Engram.Verification.RelationValidator.ValidRelationTypes)}");
                return;
            }
            await repo.SaveRelationAsync(project, obsId, new Engram.Verification.MemoryRelation { Type = relType, TargetObservationId = targetId }, sessionId);
            Console.WriteLine($"Relation {relType}:{targetId} added to obs#{obsId}");
            return;

        case "delete":
            if (targetId == 0) { Console.Error.WriteLine("error: --target-id required for delete"); return; }
            if (string.IsNullOrEmpty(relType)) { Console.Error.WriteLine("error: --type required for delete"); return; }
            var deleted = await repo.DeleteRelationAsync(project, obsId, targetId, relType, sessionId);
            Console.WriteLine(deleted ? $"Relation removed." : "No matching relation found.");
            return;

        default:
            Console.Error.WriteLine($"error: invalid action '{action}'. Valid: add, get, delete");
            return;
    }
}, relActionOpt, relObsIdOpt, relTargetIdOpt, relTypeOpt, relProjOpt);

// ──�� lineage (ENG-404) ────────────────────────────────────────────────────

var lineageCmd = new Command("lineage", "Build lineage tree for a memory observation");

var linObsIdOpt = new Option<long>("--observation-id", "Root observation ID (required)");
var linMaxHopsOpt = new Option<int>("--max-hops", () => 5, "Max traversal depth (default: 5, max: 10)");
var linProjOpt = new Option<string?>("--project", "Project name");
lineageCmd.AddOption(linObsIdOpt);
lineageCmd.AddOption(linMaxHopsOpt);
lineageCmd.AddOption(linProjOpt);
lineageCmd.SetHandler(async (long obsId, int maxHops, string? proj) =>
{
    if (obsId == 0) { Console.Error.WriteLine("error: --observation-id required"); return; }

    var clampedHops = Math.Clamp(maxHops, 1, 10);
    using var store = OpenStore();
    var project = proj ?? Normalizers.NormalizeProject(ProjectDetector.DetectProject(Directory.GetCurrentDirectory()));
    var repo = new Engram.Verification.MemoryRelationRepository(store);
    var builder = new Engram.Verification.MemoryLineageBuilder(repo, store);

    var result = await builder.BuildLineageAsync(project, obsId, clampedHops);

    Console.WriteLine($"## Lineage: obs#{obsId}");
    Console.WriteLine();

    if (result.Ancestors.Count > 0)
    {
        Console.WriteLine("### Ancestors (↑)");
        foreach (var a in result.Ancestors)
        {
            var title = string.IsNullOrEmpty(a.Title) ? "(untraced)" : $"\"{a.Title}\"";
            var lineage = a.Lineage.Count > 0 ? $" ({string.Join(", ", a.Lineage)})" : "";
            Console.WriteLine($"- obs#{a.ObservationId}: {title}{lineage}");
        }
        Console.WriteLine();
    }

    if (result.Descendants.Count > 0)
    {
        Console.WriteLine("### Descendants (↓)");
        foreach (var d in result.Descendants)
        {
            var title = string.IsNullOrEmpty(d.Title) ? "(untraced)" : $"\"{d.Title}\"";
            var lineage = d.Lineage.Count > 0 ? $" ({string.Join(", ", d.Lineage)})" : "";
            Console.WriteLine($"- obs#{d.ObservationId}: {title}{lineage}");
        }
        Console.WriteLine();
    }

    if (result.Ancestors.Count == 0 && result.Descendants.Count == 0)
        Console.WriteLine("No lineage relationships found.");

    Console.WriteLine($"Hops: {result.Hops}");
    if (result.CycleDetected) Console.WriteLine("⚠️ Cycle detected!");
}, linObsIdOpt, linMaxHopsOpt, linProjOpt);

// ─── Assemble ────────────────────────────────────────────────────────────────

root.AddCommand(serveCmd);
root.AddCommand(mcpCmd);
root.AddCommand(searchCmd);
root.AddCommand(saveCmd);
root.AddCommand(contextCmd);
root.AddCommand(statsCmd);
root.AddCommand(exportCmd);
root.AddCommand(importCmd);
root.AddCommand(syncCmd);
root.AddCommand(projectCmd);
root.AddCommand(promoteCmd);
root.AddCommand(projectsCmd);
root.AddCommand(retentionCmd);
root.AddCommand(obsidianCmd);
root.AddCommand(versionCmd);
root.AddCommand(doctorCmd);
root.AddCommand(relationsCmd);
root.AddCommand(lineageCmd);

return await root.InvokeAsync(args);

// ─── Helpers ─────────────────────────────────────────────────────────────────

static IStore OpenStore(StoreConfig? cfg = null)
{
    cfg ??= StoreConfig.FromEnvironment();

    // Remote mode: connect to server via HTTP
    if (cfg.IsRemote)
        return new HttpStore(cfg);

    // Validation: PostgreSQL requires connection string
    if (cfg.IsPostgres && string.IsNullOrWhiteSpace(cfg.PgConnectionString))
    {
        Console.Error.WriteLine("error: ENGRAM_PG_CONNECTION is required when ENGRAM_DB_TYPE=postgres");
        Environment.Exit(1);
    }

    return cfg.DbType switch
    {
        StoreDbType.Postgres => new PostgresStore(cfg),
        _ => new SqliteStore(cfg),
    };
}

/// <summary>
/// Lee ~/.engram/config.json y devuelve true si auto_enroll está explícitamente deshabilitado.
/// </summary>
static bool IsAutoEnrollDisabledInConfig()
{
    try
    {
        var configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".engram", "config.json");
        if (!File.Exists(configPath)) return false;

        var json = File.ReadAllText(configPath);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("auto_enroll", out var autoEnrollProp))
        {
            if (autoEnrollProp.ValueKind == System.Text.Json.JsonValueKind.False)
                return true;
            if (autoEnrollProp.ValueKind == System.Text.Json.JsonValueKind.Number
                && autoEnrollProp.GetInt32() == 0)
                return true;
        }
        return false;
    }
    catch { return false; }
}

static string Truncate(string s, int max)
    => s.Length <= max ? s : s[..max] + "...";
