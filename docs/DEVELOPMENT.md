[← Volver al README](../README.md)

# Guía de desarrollo — engram-dotnet

---

## Requisitos

- .NET 10 SDK (`dotnet --version` debe mostrar `10.x.x`)
- Linux, macOS o Windows (el binario de producción es linux-x64)

Instalar .NET 10 SDK: https://dotnet.microsoft.com/download/dotnet/10.0

---

## Compilar

```bash
# Clonar
git clone https://github.com/efreet111/engram-dotnet
cd engram-dotnet

# Restaurar dependencias
dotnet restore

# Compilar toda la solución
dotnet build
```

---

## Correr en modo desarrollo

```bash
# Servidor HTTP en puerto 7437
dotnet run --project src/Engram.Cli -- serve

# Servidor MCP (stdio) — modo local
dotnet run --project src/Engram.Cli -- mcp

# Servidor MCP en modo proxy (apunta a un servidor remoto)
ENGRAM_URL=http://localhost:7437 ENGRAM_USER=dev.local \
  dotnet run --project src/Engram.Cli -- mcp

# Buscar
dotnet run --project src/Engram.Cli -- search "auth"

# Con variables de entorno personalizadas
ENGRAM_DATA_DIR=/tmp/engram-dev dotnet run --project src/Engram.Cli -- serve
```

---

## Tests

```bash
# Todos los tests (124 en total)
dotnet test

# Por suite
dotnet test tests/Engram.Store.Tests        # 51 — SQLite, dedup, FTS5
dotnet test tests/Engram.Server.Tests       # 16 — HTTP endpoints
dotnet test tests/Engram.Mcp.Tests          # 32 — herramientas MCP + McpConfig
dotnet test tests/Engram.HttpStore.Tests    # 25 — HttpStore end-to-end con servidor real

# Con output detallado
dotnet test --logger "console;verbosity=detailed"

# Con cobertura
dotnet test --collect:"XPlat Code Coverage"
```

### Tests de paridad (Go vs .NET)

Los tests de paridad verifican que engram-dotnet produce resultados idénticos al binario Go original para las mismas operaciones — especialmente la lógica de deduplicación.

```bash
# Requiere tener el binario Go en PATH como "engram-go"
cd tests/parity
./run_parity.sh
```

---

## Publicar binario self-contained

```bash
# Binario linux-x64 self-contained (para deploy en servidor)
dotnet publish src/Engram.Cli \
  -c Release \
  -r linux-x64 \
  --self-contained \
  -o dist/

# El binario queda en dist/engram
./dist/engram version
```

---

## Estructura de la solución

```
engram-dotnet.slnx         ← solución (9 proyectos)
src/
  Engram.Store/            ← core: SQLite + FTS5 + dedup + HttpStore proxy
  Engram.Server/           ← HTTP REST API (librería)
  Engram.Mcp/              ← MCP server (librería)
  Engram.Sync/             ← Git sync (librería)
  Engram.Cli/              ← entry point ejecutable
tests/
  Engram.Store.Tests/      ← unitarios + integración + paridad (51)
  Engram.Server.Tests/     ← HTTP con WebApplicationFactory (16)
  Engram.Mcp.Tests/        ← herramientas MCP + McpConfig (32)
  Engram.HttpStore.Tests/  ← HttpStore end-to-end con servidor real (25)
docs/                      ← documentación
config/                    ← archivos de configuración para Cursor y VS Code
```

---

## Convenciones de código

- **Nullable**: habilitado en todos los proyectos (`<Nullable>enable</Nullable>`)
- **Implicit usings**: habilitado
- **Records** para tipos inmutables (modelos de dominio)
- **Classes** para tipos mutables (params, config)
- **JSON**: `[JsonPropertyName("snake_case")]` en todos los modelos para mantener compatibilidad con la API Go
- **Async**: todos los métodos de IStore son `Task<T>` — no bloquear con `.Result` o `.Wait()`
- **SQL**: directo en `SqliteStore.cs` como constantes `string` — sin ORM

---

## Agregar un nuevo comando CLI

1. Crear `src/Engram.Cli/Commands/MiComandoCommand.cs`
2. Definir `Command BuildCommand()` que retorna un `System.CommandLine.Command`
3. Registrar en `Program.cs` con `rootCommand.AddCommand(MiComandoCommand.BuildCommand())`

---

## Convenciones de commit

Conventional commits — sin atribución de AI:

```
feat(cli): add --json flag to search command
fix(store): prevent duplicate insert on retry within window
docs(architecture): add dedup flow diagram
refactor(store): extract hash normalization to Normalizers.cs
chore(deps): bump Microsoft.Data.Sqlite to 9.0.5
```
