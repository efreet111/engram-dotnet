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
//   engram sync             Export/import gzip chunks
//   engram projects         Manage projects
//   engram version          Print version

using System.CommandLine;
using System.Text.Json;
using Engram.Mcp;
using Engram.Server;
using Engram.Store;
using Engram.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

const string Version = "1.1.0";

// ─── Root command ────────────────────────────────────────────────────────────

var root = new RootCommand("Engram — persistent memory for AI coding agents");

// ─── serve ───────────────────────────────────────────────────────────────────

var serveCmd  = new Command("serve", "Start the HTTP API server");
var portOpt   = new Option<int>("--port", () => 7437, "Port to listen on (env: ENGRAM_PORT)");
serveCmd.AddOption(portOpt);
serveCmd.SetHandler(async (int port) =>
{
    var envPort = Environment.GetEnvironmentVariable("ENGRAM_PORT");
    if (!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out var p)) port = p;

    var cfg   = StoreConfig.FromEnvironment();
    using var store = new SqliteStore(cfg);

    Console.Error.WriteLine($"[engram] starting HTTP server on :{port}");
    var app = EngramServer.Build(store, cfg);
    app.Urls.Clear();
    app.Urls.Add($"http://0.0.0.0:{port}");
    await app.RunAsync();
}, portOpt);

// ─── mcp ─────────────────────────────────────────────────────────────────────

var mcpCmd       = new Command("mcp", "Start the MCP server (stdio transport)");
var mcpProjectOpt = new Option<string?>("--project", "Override detected project name");
mcpCmd.AddOption(mcpProjectOpt);
mcpCmd.SetHandler(async (string? project) =>
{
    var storeCfg = StoreConfig.FromEnvironment();

    // Project detection chain: --project → ENGRAM_PROJECT → git remote → git root → cwd basename
    var defaultProject = project
        ?? storeCfg.Project
        ?? ProjectDetector.DetectProject(Directory.GetCurrentDirectory());
    defaultProject = Normalizers.NormalizeProject(defaultProject);

    // User identity: provided by IT via ENGRAM_USER (empty in local mode)
    var user = storeCfg.User ?? "";

    // Store selection: HttpStore (team mode) vs SqliteStore (local mode)
    IStore store = storeCfg.IsRemote
        ? new HttpStore(storeCfg)
        : new SqliteStore(storeCfg);

    if (storeCfg.IsRemote)
        Console.Error.WriteLine($"[engram] mcp → remote {storeCfg.RemoteUrl} (user={user}, project={defaultProject})");
    else
        Console.Error.WriteLine($"[engram] mcp → local SQLite (project={defaultProject})");

    using var ownedStore = store;

    var mcpBuilder = EngramMcpServer.CreateBuilder(args);
    mcpBuilder.Services.AddSingleton<IStore>(store);
    mcpBuilder.Services.AddSingleton(new McpConfig
    {
        DefaultProject = defaultProject,
        User           = user,
    });

    await mcpBuilder.Build().RunAsync();
}, mcpProjectOpt);

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
    using var store = OpenStore();
    var s = await store.StatsAsync();
    var projects = s.Projects.Count > 0 ? string.Join(", ", s.Projects) : "none yet";
    Console.WriteLine($"""
        Engram Memory Stats
          Sessions:     {s.TotalSessions}
          Observations: {s.TotalObservations}
          Prompts:      {s.TotalPrompts}
          Projects:     {projects}
          Database:     {cfg.DataDir}/engram.db
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

var syncCmd       = new Command("sync", "Sync memories with git-friendly compressed chunks");
var syncImportOpt = new Option<bool>("--import", "Import new chunks from sync dir");
var syncStatusOpt = new Option<bool>("--status", "Show sync status");
syncCmd.AddOption(syncImportOpt);
syncCmd.AddOption(syncStatusOpt);
syncCmd.SetHandler(async (bool doImport, bool doStatus) =>
{
    var syncCfg = SyncConfig.FromEnvironment();
    if (!syncCfg.IsConfigured)
    {
        Console.Error.WriteLine("warning: ENGRAM_SYNC_REPO is not set. Using local sync dir only.");
    }
    using var store = OpenStore();
    var sync  = new EngramSync(store, syncCfg);

    if (doStatus)
    {
        var status = await sync.GetStatusAsync();
        Console.WriteLine($"""
            Sync status:
              Enabled:        {status.Enabled}
              Repo:           {(string.IsNullOrEmpty(status.RepoUrl) ? "(not configured)" : status.RepoUrl)}
              Branch:         {status.Branch}
              Total chunks:   {status.TotalChunks}
              Synced chunks:  {status.SyncedChunks}
              Pending import: {status.PendingChunks}
            """);
        return;
    }

    if (doImport)
    {
        var imported = await sync.ImportNewChunksAsync();
        Console.WriteLine(imported == 0
            ? "No new chunks to import."
            : $"Imported {imported} observations from new chunks.");
        return;
    }

    // Default: export
    var wrote = await sync.ExportChunkAsync();
    Console.WriteLine(wrote
        ? "New chunk exported to sync dir."
        : "Nothing new to sync — all memories already exported.");
}, syncImportOpt, syncStatusOpt);

// ─── projects ─────────────────────────────────────────────────────────────────

var projectsCmd = new Command("projects", "Manage projects");

var projectsListCmd = new Command("list", "List all projects");
projectsListCmd.SetHandler(async () =>
{
    using var store = OpenStore();
    var stats = await store.StatsAsync();
    if (stats.Projects.Count == 0) { Console.WriteLine("No projects found."); return; }
    Console.WriteLine($"Projects ({stats.Projects.Count}):");
    foreach (var p in stats.Projects)
        Console.WriteLine($"  {p}");
});

projectsCmd.AddCommand(projectsListCmd);

// ─── version ──────────────────────────────────────────────────────────────────

var versionCmd = new Command("version", "Print version");
versionCmd.SetHandler(() => Console.WriteLine($"engram {Version}"));

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
root.AddCommand(projectsCmd);
root.AddCommand(versionCmd);

return await root.InvokeAsync(args);

// ─── Helpers ─────────────────────────────────────────────────────────────────

static IStore OpenStore()
{
    var cfg = StoreConfig.FromEnvironment();
    return new SqliteStore(cfg);
}

static string Truncate(string s, int max)
    => s.Length <= max ? s : s[..max] + "...";
