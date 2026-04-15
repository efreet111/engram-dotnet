using System.Text.Json.Serialization;

namespace Engram.Store;

// ─── Scope constants ──────────────────────────────────────────────────────────
// Valid scope values for observations. The two-tier model namespaces storage:
//   ScopeTeam     → stored under "team/{project}"   — visible to all developers
//   ScopePersonal → stored under "{user}/{project}" — private to the developer
// Legacy value "project" is treated as ScopePersonal at runtime via NormalizeScope().
public static class Scopes
{
    public const string Team     = "team";
    public const string Personal = "personal";
}

// ─── Core entities ────────────────────────────────────────────────────────────
// Dates are stored as SQLite TEXT (ISO-8601 strings) and kept as strings here
// so they round-trip perfectly without timezone conversion.

public class Session
{
    [JsonPropertyName("id")]          public string  Id        { get; set; } = "";
    [JsonPropertyName("project")]     public string  Project   { get; set; } = "";
    [JsonPropertyName("directory")]   public string  Directory { get; set; } = "";
    [JsonPropertyName("started_at")]  public string  StartedAt { get; set; } = "";
    [JsonPropertyName("ended_at")]    public string? EndedAt   { get; set; }
    [JsonPropertyName("summary")]     public string? Summary   { get; set; }
}

public class SessionSummary
{
    [JsonPropertyName("id")]                public string  Id               { get; set; } = "";
    [JsonPropertyName("project")]           public string  Project          { get; set; } = "";
    [JsonPropertyName("started_at")]        public string  StartedAt        { get; set; } = "";
    [JsonPropertyName("ended_at")]          public string? EndedAt          { get; set; }
    [JsonPropertyName("summary")]           public string? Summary          { get; set; }
    [JsonPropertyName("observation_count")] public int     ObservationCount { get; set; }
}

public class Observation
{
    [JsonPropertyName("id")]               public long    Id             { get; set; }
    [JsonPropertyName("sync_id")]          public string  SyncId         { get; set; } = "";
    [JsonPropertyName("session_id")]       public string  SessionId      { get; set; } = "";
    [JsonPropertyName("type")]             public string  Type           { get; set; } = "";
    [JsonPropertyName("title")]            public string  Title          { get; set; } = "";
    [JsonPropertyName("content")]          public string  Content        { get; set; } = "";
    [JsonPropertyName("tool_name")]        public string? ToolName       { get; set; }
    [JsonPropertyName("project")]          public string? Project        { get; set; }
    [JsonPropertyName("scope")]            public string  Scope          { get; set; } = "project";
    [JsonPropertyName("topic_key")]        public string? TopicKey       { get; set; }
    [JsonPropertyName("revision_count")]   public int     RevisionCount  { get; set; } = 1;
    [JsonPropertyName("duplicate_count")]  public int     DuplicateCount { get; set; } = 1;
    [JsonPropertyName("last_seen_at")]     public string? LastSeenAt     { get; set; }
    [JsonPropertyName("created_at")]       public string  CreatedAt      { get; set; } = "";
    [JsonPropertyName("updated_at")]       public string  UpdatedAt      { get; set; } = "";
    [JsonPropertyName("deleted_at")]       public string? DeletedAt      { get; set; }
}

public class TimelineEntry
{
    [JsonPropertyName("id")]               public long    Id             { get; set; }
    [JsonPropertyName("session_id")]       public string  SessionId      { get; set; } = "";
    [JsonPropertyName("type")]             public string  Type           { get; set; } = "";
    [JsonPropertyName("title")]            public string  Title          { get; set; } = "";
    [JsonPropertyName("content")]          public string  Content        { get; set; } = "";
    [JsonPropertyName("tool_name")]        public string? ToolName       { get; set; }
    [JsonPropertyName("project")]          public string? Project        { get; set; }
    [JsonPropertyName("scope")]            public string  Scope          { get; set; } = "project";
    [JsonPropertyName("topic_key")]        public string? TopicKey       { get; set; }
    [JsonPropertyName("revision_count")]   public int     RevisionCount  { get; set; } = 1;
    [JsonPropertyName("duplicate_count")]  public int     DuplicateCount { get; set; } = 1;
    [JsonPropertyName("last_seen_at")]     public string? LastSeenAt     { get; set; }
    [JsonPropertyName("created_at")]       public string  CreatedAt      { get; set; } = "";
    [JsonPropertyName("updated_at")]       public string  UpdatedAt      { get; set; } = "";
    [JsonPropertyName("deleted_at")]       public string? DeletedAt      { get; set; }
    [JsonPropertyName("is_focus")]         public bool    IsFocus        { get; set; }
}

public class TimelineResult
{
    [JsonPropertyName("focus")]          public Observation         Focus        { get; set; } = new();
    [JsonPropertyName("before")]         public List<TimelineEntry> Before       { get; set; } = [];
    [JsonPropertyName("after")]          public List<TimelineEntry> After        { get; set; } = [];
    [JsonPropertyName("session_info")]   public Session?            SessionInfo  { get; set; }
    [JsonPropertyName("total_in_range")] public int                 TotalInRange { get; set; }
}

public class Prompt
{
    [JsonPropertyName("id")]          public long   Id        { get; set; }
    [JsonPropertyName("sync_id")]     public string SyncId    { get; set; } = "";
    [JsonPropertyName("session_id")]  public string SessionId { get; set; } = "";
    [JsonPropertyName("content")]     public string Content   { get; set; } = "";
    [JsonPropertyName("project")]     public string Project   { get; set; } = "";
    [JsonPropertyName("created_at")]  public string CreatedAt { get; set; } = "";
}

public class SearchResult
{
    [JsonPropertyName("observation")] public Observation Observation { get; set; } = new();
    [JsonPropertyName("rank")]        public double      Rank        { get; set; }
}

public class Stats
{
    [JsonPropertyName("total_sessions")]      public int          TotalSessions     { get; set; }
    [JsonPropertyName("total_observations")]  public int          TotalObservations { get; set; }
    [JsonPropertyName("total_prompts")]       public int          TotalPrompts      { get; set; }
    [JsonPropertyName("projects")]            public List<string> Projects          { get; set; } = [];
}

public class ExportData
{
    [JsonPropertyName("version")]      public string            Version      { get; set; } = "1.1.0";
    [JsonPropertyName("exported_at")] public string            ExportedAt   { get; set; } = "";
    [JsonPropertyName("sessions")]     public List<Session>     Sessions     { get; set; } = [];
    [JsonPropertyName("observations")] public List<Observation> Observations { get; set; } = [];
    [JsonPropertyName("prompts")]      public List<Prompt>      Prompts      { get; set; } = [];
}

public class ImportResult
{
    [JsonPropertyName("sessions_imported")]      public int SessionsImported     { get; set; }
    [JsonPropertyName("observations_imported")]  public int ObservationsImported { get; set; }
    [JsonPropertyName("prompts_imported")]       public int PromptsImported      { get; set; }
}

public class MergeResult
{
    [JsonPropertyName("canonical")]              public string       Canonical           { get; set; } = "";
    [JsonPropertyName("sources_merged")]         public List<string> SourcesMerged       { get; set; } = [];
    [JsonPropertyName("observations_updated")]   public long         ObservationsUpdated { get; set; }
    [JsonPropertyName("sessions_updated")]       public long         SessionsUpdated     { get; set; }
    [JsonPropertyName("prompts_updated")]        public long         PromptsUpdated      { get; set; }
}

// ─── Params ───────────────────────────────────────────────────────────────────

public record AddObservationParams
{
    [JsonPropertyName("session_id")] public string  SessionId { get; init; } = "";
    [JsonPropertyName("type")]       public string  Type      { get; init; } = "manual";
    [JsonPropertyName("title")]      public string  Title     { get; init; } = "";
    [JsonPropertyName("content")]    public string  Content   { get; init; } = "";
    [JsonPropertyName("tool_name")]  public string? ToolName  { get; init; }
    [JsonPropertyName("project")]    public string? Project   { get; init; }
    [JsonPropertyName("scope")]      public string? Scope     { get; init; }
    [JsonPropertyName("topic_key")]  public string? TopicKey  { get; init; }
}

public record UpdateObservationParams
{
    [JsonPropertyName("type")]      public string? Type     { get; init; }
    [JsonPropertyName("title")]     public string? Title    { get; init; }
    [JsonPropertyName("content")]   public string? Content  { get; init; }
    [JsonPropertyName("project")]   public string? Project  { get; init; }
    [JsonPropertyName("scope")]     public string? Scope    { get; init; }
    [JsonPropertyName("topic_key")] public string? TopicKey { get; init; }
}

public record SearchOptions
{
    [JsonPropertyName("type")]    public string? Type    { get; init; }
    [JsonPropertyName("project")] public string? Project { get; init; }
    [JsonPropertyName("scope")]   public string? Scope   { get; init; }
    [JsonPropertyName("limit")]   public int     Limit   { get; init; } = 10;
}

public record AddPromptParams
{
    [JsonPropertyName("session_id")] public string  SessionId { get; init; } = "";
    [JsonPropertyName("content")]    public string  Content   { get; init; } = "";
    [JsonPropertyName("project")]    public string? Project   { get; init; }
}
