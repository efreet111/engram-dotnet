# Development Guide — engram-dotnet

---

## Requirements

- .NET 10 SDK (`dotnet --version` should show `10.x.x`)
- Linux, macOS or Windows (production binary is linux-x64)

---

## Project Structure

```
src/
├── Engram.Store/        ← Storage engine: IStore + 3 implementations
│   ├── IStore.cs        ← Core interface (35+ methods)
│   ├── SqliteStore.cs   ← SQLite implementation
│   ├── PostgresStore.cs ← PostgreSQL implementation
│   └── HttpStore.cs     ← Remote server proxy (via HTTP)
├── Engram.Server/       ← HTTP REST API (ASP.NET Core Minimal APIs)
│   ├── EngramServer.cs  ← 33 route handlers + DI
│   └── CloudSyncEndpoints.cs ← 8 sync route handlers
├── Engram.Sync/         ← Offline-first sync engine
│   ├── SyncManager.cs   ← Background sync service
│   └── Transport/       ← HTTP transport for sync
├── Engram.Mcp/          ← MCP server (stdio transport, 27 tools)
├── Engram.Diagnostics/  ← Doctor diagnostic tools
├── Engram.Obsidian/     ← Obsidian vault export
├── Engram.MdGeneration/ ← Markdown promotion tools
└── Engram.Cli/          ← CLI entry point (System.CommandLine)

tests/
├── Engram.Store.Tests/       ← 170 store tests
├── Engram.Server.Tests/      ← 63 HTTP API tests
├── Engram.Sync.Tests/        ← 32 sync tests
├── Engram.Mcp.Tests/         ← 61 MCP tool tests
├── Engram.Postgres.Tests/    ← PostgreSQL-specific tests
└── Engram.Diagnostics.Tests/ ← Diagnostics tests
```

---

## Running Tests

```bash
# All tests (except Docker-dependent)
dotnet test --filter "Category!=RequiresDocker"

# Specific project
dotnet test tests/Engram.Store.Tests/

# Specific test
dotnet test --filter "FullyQualifiedName~Search"
```

---

## Building

```bash
# Development build
dotnet build

# Production binary (self-contained)
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/

# Run locally
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/Engram.Cli -- serve
```

---

## Adding a New Endpoint

1. Add the route handler in `EngramServer.cs`
2. Add the method to `IStore` (if store logic needed)
3. Implement in `SqliteStore.cs`, `PostgresStore.cs`, `HttpStore.cs`
4. Add tests

---

## Architecture Notes

- **No MediatR, no CQRS, no Clean Architecture** — just minimal APIs + DI
- **Strategy Pattern**: `IStore` interface with 3 implementations
- **No controllers** — all routes are minimal API lambdas
- **State is NOT shared** — each request is stateless
- The complexity is in the **features**, not the framework
