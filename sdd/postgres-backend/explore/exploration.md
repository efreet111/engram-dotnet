## Exploration: PostgreSQL Backend for engram-dotnet

### Current State
`engram-dotnet` currently provides two storage backends:
1. `SqliteStore` – local file-based SQLite + FTS5 (default)
2. `HttpStore` – HTTP proxy to a remote `engram serve` server (team mode)

Both implement the `IStore` interface (22 methods). The CLI selects the store in `Program.cs`:
```csharp
IStore store = config.IsRemote ? new HttpStore(config) : new SqliteStore(config);
```
where `config.IsRemote` checks `ENGRAM_URL`.

All persistence logic lives in `Engram.Store` package, specifically in `SqliteStore.cs`. The schema, queries, and deduplication logic are written in raw SQL using `Microsoft.Data.Sqlite`.

The project follows an explicit decision to avoid ORMs (ADR-001) to maintain schema parity with the Go original and full control over FTS5.

### Affected Areas
- `src/Engram.Store/IStore.cs` — interface definition (no change required)
- `src/Engram.Store/SqliteStore.cs` — existing implementation (reference for port)
- `src/Engram.Store/StoreConfig.cs` — needs new properties for PostgreSQL connection
- `src/Engram.Store/Normalizers.cs` — may need helpers for dedupe window expression
- `src/Engram.Store/PassiveCapture.cs` — unchanged (works via IStore)
- `src/Engram.Store/Models.cs` — unchanged (dates remain as strings)
- `src/Engram.Cli/Program.cs` — store selection logic must add PostgreSQL branch
- `src/Engram.Server/EngramServer.cs` — unchanged (receives IStore via DI)
- `src/Engram.Mcp/EngramMcpServer.cs` — unchanged
- `src/Engram.Sync/EngramSync.cs` — unchanged (uses IStore.Export/Import)
- `tests/` — existing test suites (`Engram.Store.Tests`, `Engram.HttpStore.Tests`) will need to run against PostgreSQL for parity
- `docker/` — may need docker-compose with postgres service
- `docs/` — RFC/PRD/ADR already written; need ARCHITECTURE.md update and new POSTGRES-SETUP.md

### Approaches

#### 1. **Separate PostgreSQL project** (`Engram.Store.Postgres`)
**Description**: Create a new `.csproj` for PostgreSQL backend, depend on Npgsql, contain `PostgresStore : IStore`.
- **Pros**:
  - Clear separation of concerns; SQLite and PostgreSQL backends are independent
  - Binary size: Npgsql only included if PostgreSQL project referenced (though CLI always references both stores? Actually CLI would need to reference both)
  - Easier to enforce that PostgreSQL code doesn't accidentally leak into SQLite
- **Cons**:
  - Requires adding a project reference in `Engram.Cli` (or dynamic loading via factory)
  - Slightly more complex build
  - Duplication of helpers (e.g., `ReadObservation`, `Normalizers`) might be harder to share
- **Effort**: Medium (project setup, reference management)

#### 2. **PostgresStore in same project** (`Engram.Store`)
**Description**: Add `PostgresStore.cs` to existing `Engram.Store` project alongside `SqliteStore.cs` and `HttpStore.cs`.
- **Pros**:
  - Simple: no extra project files, single CSPROJ
  - Easy sharing of internal helpers (`Normalizers.cs`, private methods)
  - CLI store selection is a simple extension of existing if/else
  - Matches current pattern: HttpStore already lives in same project
- **Cons**:
  - All backends compiled into same assembly (Npgsql always linked)
  - Slight risk of accidental cross-usage (mitigated by namespaces/naming)
- **Effort**: Low (add file, register in Program.cs)

#### 3. **Factory pattern with runtime discovery**
**Description**: Keep IStore, but have a factory that scans assemblies for `IStore` implementations and selects based on config.
- **Pros**:
  - Pluggable: future backends can be added without touching Program.cs
  - Decouples store selection from concrete types
- **Cons**:
  - Overkill for two backends
  - Requires assembly scanning or explicit registration
  - Adds indirection
- **Effort**: Medium-High (design factory, test)

### Recommendation
**Approach 2 – PostgresStore in same project (`Engram.Store`)**.
- Matches existing pattern (`HttpStore` is there)
- Minimal churn: one new file, ~3-line extension in `Program.cs`
- Easy to share `Normalizers.cs` and any future helper methods
- Binary size impact acceptable (Npgsql ~2MB)
- Keeps the codebase simple for contributors

### Risks
- **Schema drift**: Ensuring PostgreSQL schema stays identical in semantics to SQLite (especially FTS behavior). Mitigation: comprehensive parity tests.
- **Connection string management**: Avoid leaking secrets; ensure `ENGRAM_PG_CONNECTION` is handled like other secrets (no logging). Mitigation: treat as sensitive, mask in logs if any.
- **Date handling mismatch**: Using `TEXT` for dates in PostgreSQL requires casts for date arithmetic (e.g., dedupe window). Mitigation: centralize date conversion in a helper or use explicit casts in queries.
- **Concurrency bugs**: Npgsql connection pool misconfiguration under load. Mitigation: set sensible `MaxPoolSize` in connection string documentation and test with load.
- **Test complexity**: Running PostgreSQL in CI requires either a service (Postgres Docker) or Testcontainers. Mitigation: adopt Testcontainers.PostgreSQL for isolated parallel tests.

### Ready for Proposal
Yes — exploration complete. The team should decide on the open questions from RFC-001 (D1: location of PostgresStore — we recommend same project; D3: date storage as TEXT — recommend TEXT for v1; Testcontainers adoption; PG minimum version; sync strategy). Once those are confirmed, we can proceed to write specs (sdd-spec) and tasks.
