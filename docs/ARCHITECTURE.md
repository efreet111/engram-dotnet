# Architecture — engram-dotnet

> Technical architecture of **engram-dotnet**, a .NET 10 port of the original [engram](https://github.com/Gentleman-Programming/engram).  
> The conceptual design (memory system, session cycle, MCP tools) originates from the upstream project.

**Design Philosophy**: engram-dotnet intentionally rejects corporate over-engineering.

* **No MediatR.** No hidden magic pipelines or reflection-heavy message buses.
* **No CQRS.** No artificial separation of reads and writes when simple, well-indexed state is what matters.
* **No Clean Architecture abstractions.** No endless onion layers or boilerplate mapping interfaces.

**Just compiled C# 10, Minimal APIs, and Dependency Injection.** Built for raw performance, explicit execution paths, and zero boilerplate.

---

## Project Structure

```
src/
├── Engram.Store/        ← Storage engine
│   ├── IStore.cs        ← Core interface (35+ methods)
│   ├── SqliteStore.cs   ← SQLite implementation (~2400 lines)
│   ├── PostgresStore.cs ← PostgreSQL implementation (~2100 lines)
│   └── HttpStore.cs     ← Remote server proxy (via HTTP)
├── Engram.Server/       ← HTTP REST API (ASP.NET Core)
│   ├── EngramServer.cs  ← 33 route handlers + DI wiring
│   └── CloudSyncEndpoints.cs ← 8 sync endpoints
├── Engram.Sync/         ← Offline-first sync engine
│   ├── SyncManager.cs   ← BackgroundService
│   └── Transport/       ← HTTP transport (IMutationTransport)
├── Engram.Mcp/          ← MCP server (27 tools, stdio transport)
├── Engram.Cli/          ← CLI entry point (System.CommandLine)
├── Engram.Diagnostics/  ← Doctor diagnostic tools
├── Engram.Obsidian/     ← Obsidian vault export
└── Engram.MdGeneration/ ← Markdown promotion
```

---

## Storage Layer (IStore)

### Interface Design

`IStore` is the core interface with 35+ methods covering all CRUD operations. Three implementations via Strategy Pattern:

| Implementation | Activated by | Use case |
|----------------|-------------|----------|
| **SqliteStore** | Default (no env vars) | Local development, single dev |
| **PostgresStore** | `ENGRAM_DB_TYPE=postgres` | Team server, production |
| **HttpStore** | `ENGRAM_URL=http://server:7437` | Remote client via HTTP proxy |

### Selection Logic

```
Is ENGRAM_URL set?
  ├─ YES → HttpStore (remote proxy)
  └─ NO  → Is ENGRAM_DB_TYPE=postgres?
              ├─ YES → PostgresStore
              └─ NO  → SqliteStore (default)
```

> **Polymorphism in action**: The client (MCP, REST handlers) only knows `IStore`. Dependency injection resolves the correct implementation at startup. Zero coupling — switching from SQLite to PostgreSQL requires only env vars, no code changes.

### Key Methods

| Category | Methods | Description |
|----------|---------|-------------|
| Sessions | `CreateSession`, `EndSession`, `GetSession`, `DeleteSession`, `RecentSessions` | Session lifecycle |
| Observations | `AddObservation`, `GetObservation`, `UpdateObservation`, `DeleteObservation`, `RecentObservations` | Memory CRUD |
| Search | `Search`, `Timeline` | FTS5 (SQLite) / tsvector (PostgreSQL) |
| Prompts | `AddPrompt`, `RecentPrompts`, `SearchPrompts`, `DeletePrompt` | User prompts |
| Sync | `PushMutations`, `PullMutations`, `EnrollProject`, `PauseProject`, `GetSyncState` | Offline-first sync |
| Projects | `ListProjects`, `MergeProjects`, `PruneProject` | Project management |
| Export/Import | `Export`, `Import` | Data portability |
| Retention | `PruneOldObservations`, `GetRetentionStats` | TTL-based cleanup |
| MD Promotion | `PromoteToMd`, `SyncMdToRepo`, `GenerateIndex` | Memory → .md files |

---

## Data Flow

### Read Path

```
Agent → MCP tool (mem_search) → EngramServer → IStore.SearchAsync()
                                                    │
                          ┌─────────────────────────┼─────────────────────────┐
                          ▼                         ▼                         ▼
                    SqliteStore                PostgresStore              HttpStore
                    (FTS5 query)               (tsvector query)          (HTTP proxy)
```

### Write Path

```
Agent → MCP tool (mem_save) → EngramServer → IStore.AddObservationAsync()
                                                   │
                                             SqliteStore / PostgresStore
                                                   │
                                             If SQLite + SyncManager:
                                                   │
                                             sync_mutations table
                                                   │
                                             SyncManager.PushAsync()
                                                   │
                                             POST /sync/mutations/push
```

---

## Web Application

### Startup

```
Program.cs (CLI entry point)
  └─ EngramServer.Build(store, config)
       ├─ WebApplication.CreateBuilder()
       ├─ DI: store, config, SyncManager, IHttpClientFactory
       ├─ app.Use(logging middleware) → logs ALL requests
       ├─ CORS (optional)
       ├─ MapRoutes(app, store) → 33 endpoints
       ├─ MapCloudSyncRoutes()  → 8 sync endpoints
       └─ return app
```

### Route Registration

All routes are **minimal APIs** — no controllers:

```csharp
app.MapGet("/health", (Func<IStore, IResult>)(HandleHealth));
app.MapPost("/sessions", async (ctx, store) => await HandleCreateSession(ctx, store));
app.MapGet("/search", async (ctx, store) => await HandleSearch(ctx, store));
```

---

## Sync Architecture

### Components

```
┌──────────────┐     ┌──────────────────┐     ┌──────────────┐
│ SqliteStore   │     │   SyncManager    │     │  PostgresStore│
│ sync_mutations│────►│ BackgroundService│────►│ cloud_mutations
│ pending_queue │     │ Push + Pull      │     │ enrolled      │
└──────────────┘     └──────────────────┘     └──────────────┘
                          │
                     IMutationTransport
                     (HTTP via IHttpClientFactory)
```

### Sync Cycle

1. **Push**: Read local `sync_mutations` → POST to server → mark as `acked`
2. **ReplayDeferred**: Retry FK-deferred mutations (up to 5 times)
3. **Pull**: GET new mutations from server → apply to local SQLite

### Consistency Model

**Last-write-wins**: If two agents modify the same observation, the most recent `occurred_at` timestamp wins. There is no semantic merge — this is deliberate. For complex conflicts, `mem_doctor` can diagnose the current state.

> **Why not CRDT or vector clocks?** Simplicity. Last-write-wins is predictable, requires no coordination, and works for 99% of agent memory use cases. If two agents overwrite the same memory, the newest one is correct by definition.

### Enrollment & Pause

- `sync_enrolled_projects` table: only enrolled projects participate in sync
- `cloud_project_controls` table: per-project pause flag
- `cloud_sync_audit_log`: audit trail for all pause/resume events

---

## MCP Server

27 tools registered via `McpServerTool` attribute:

```
Production (create/read):
  mem_save, mem_save_prompt, mem_session_start, mem_session_end
  mem_update, mem_delete, mem_capture_passive

Read-only (query):
  mem_search, mem_get_observation, mem_context, mem_session_summary
  mem_stats, mem_timeline, mem_suggest_topic_key

Diagnostics:
  mem_doctor

Promotion:
  mem_promote_to_md, mem_sync_md_to_repo, mem_generate_index

Verification:
  mem_verify_artifact, mem_traceability, mem_trace_source, mem_lineage

Retention:
  mem_retention_prune, mem_retention_stats

Projects:
  mem_merge_projects, mem_project_redirects
```

---

## Logging & Monitoring

### Request Logging Middleware

Every request is logged with:
- Method, path, status code, duration (ms)
- Error details on 5xx (message, type, stack trace, body preview)
- Log levels: 2xx = Information, 4xx = Warning, 5xx = Error

### SyncManager Logging (8 events via LoggerMessage)

> **Performance**: Uses `LoggerMessage.Define` (source-generated) instead of `ILogger.Log*`. This generates **statically-typed delegates** that avoid boxing of value types and repeated string formatting — critical for high-throughput sync cycles where logging occurs every 30 seconds per SyncManager instance.

| Event | Level | EventId |
|-------|-------|---------|
| CycleStart | Debug | 2000 |
| CycleComplete | Information | 2001 |
| CycleFailed | Error | 2002 |
| PushBatch | Debug | 2003 |
| PullBatch | Debug | 2004 |
| DeferredReplay | Information | 2005 |
| PanicExit | Critical | 2006 |
| PhaseTransition | Debug | 2007 |

---

## Multi-User Isolation

> **Security**: The `X-Engram-User` header is validated on every request. Personal scope (`personal:{user}`) is strictly isolated — there is NO mechanism for a user to access another user's personal memories. Team scope (`team/{project}`) requires explicit enrollment via `/sync/enroll`. Context leaks between projects are prevented at the query level: search results are always filtered by the requesting user's identity and enrolled projects.

### Identity Flow

```
1. Agent sets ENGRAM_USER=victor.silgado
2. MCP client sends X-Engram-User HTTP header
3. Server detects header → namespaces personal scope
4. personal → personal:victor.silgado/{project}
5. team → team/{project} (no namespacing, shared)
```

### Implementation

```csharp
// SqliteStore.NormalizeProject()
if (v scope == "personal")
{
    if (v.StartsWith("personal:") || v.StartsWith("project:"))
        return v; // Already namespaced
    return $"personal:{userId}";
}
```

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Language | C# / .NET 10 LTS |
| HTTP | ASP.NET Core Minimal API (Kestrel) |
| SQLite | Microsoft.Data.Sqlite + FTS5 |
| PostgreSQL | Npgsql + tsvector + GIN indexes |
| MCP | ModelContextProtocol NuGet (Microsoft oficial) |
| CLI | System.CommandLine |
| Auth | JWT (optional, via `ENGRAM_JWT_SECRET`) |
| Testing | xUnit, WebApplicationFactory, Testcontainers |
