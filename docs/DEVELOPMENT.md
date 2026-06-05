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
├── Engram.Mcp/          ← MCP server (stdio transport, 26 tools)
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

---

## Workflow de desarrollo (T1-T5)

> **Regla**: Antes de proponer `commit` o `push`, el código DEBE haber pasado **T3**. T1 y T2 no son suficientes — SQLite no es prod.

### Capas

| # | Capa | Comando | Backend | Tiempo |
|---|------|---------|---------|--------|
| T1 | Iterar código | `dotnet run -- serve --port 7438` | SQLite (memoria) | <5s |
| T2 | Tests unitarios | `dotnet test -c Release --filter "..."` | SQLite (in-memory) | ~10s |
| **T3** | **Integración pre-commit** | `bash scripts/dev-test.sh` | **Postgres (host)** | ~30s build + 5s run |
| T4 | CI | push a GitHub | SQLite + Postgres | automático |
| T5 | Deploy | `docker compose up -d --build` (TrueNAS) | Postgres | humano |

### T3 en detalle

`scripts/dev-test.sh` automatiza:

1. `docker build -t engram-local:dev -f Dockerfile .`
2. `docker run -d` con env vars apuntando al Postgres del host (`host.docker.internal`)
3. Wait for `/health` (timeout 30s)
4. Curl `/health` + `/stats` (smoke)
5. Deja el container corriendo para inspección (`docker logs -f engram-test`)

**Requisitos para T3:**
- Docker 20.10+ (para `host-gateway`)
- Postgres local accesible en `localhost:5432` con DB `engram_dev` creada
- Variables: `PG_HOST` (default `host.docker.internal`), `PG_PORT`, `PG_DB`, `PG_USER`, `PG_PASS`

**Setup inicial (una vez):**
```bash
# Crear DB local (psql)
createdb -U postgres engram_dev
```

**Uso:**
```bash
PG_PASS=tu_password bash scripts/dev-test.sh
```

### Cuándo saltarse T3

**Nunca**, salvo:
- Cambios puramente cosméticos (typos en docs, comentarios)
- Cambios en `scripts/setup.*` (probados manualmente)
- Cambios en CI workflow (probados por el push a GitHub)

En esos casos, igual correr T1+T2 para sanity.

### Troubleshooting T3

| Síntoma | Causa probable | Fix |
|---------|----------------|-----|
| `connection refused` a `host.docker.internal` | Docker < 20.10 o sin `host-gateway` | Actualizar Docker o usar `extra_hosts` |
| Container arranca pero `/health` falla rápido | Postgres no acepta conexiones | `pg_isready -h localhost -p 5432` |
| Logs muestran `database "engram_dev" does not exist` | DB no creada | `createdb -U postgres engram_dev` |
| `ENGRAM_PG_CONNECTION` malformado | Caracteres especiales en password | URL-encode el password o usar `.pgpass` |

---

## Logging

The server uses structured JSON logging via `Microsoft.Extensions.Logging.JsonConsole`:

```bash
# Set log level (default: Information)
export ENGRAM_LOG_LEVEL=Debug

# View logs (Docker)
docker logs engram

# View logs (systemd)
journalctl -u engram -f
```

Log fields: `@timestamp`, `level`, `method`, `path`, `status`, `duration_ms`, `client_ip`
