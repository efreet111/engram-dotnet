[← Volver al README](../README.md)

# Arquitectura — engram-dotnet

> Este documento describe la arquitectura técnica de **engram-dotnet**, un port en .NET 10 del proyecto original [engram](https://github.com/Gentleman-Programming/engram). El diseño conceptual (cómo funciona el sistema de memoria, el ciclo de sesiones, las herramientas MCP) proviene íntegramente del proyecto original.

---

## Índice

- [Cómo funciona](#cómo-funciona)
- [Ciclo de sesión](#ciclo-de-sesión)
- [Sistema de deduplicación](#sistema-de-deduplicación)
- [Estructura del proyecto](#estructura-del-proyecto)
- [Grafo de dependencias](#grafo-de-dependencias)
- [Stack técnico](#stack-técnico)
- [Decisiones arquitecturales](#decisiones-arquitecturales)
- [Schema de base de datos](#schema-de-base-de-datos)
- [Herramientas MCP](#herramientas-mcp)

---

## Cómo funciona

```
Agente (Claude Code / OpenCode / Gemini CLI / Codex / ...)
    ↓ MCP stdio  o  HTTP REST
engram-dotnet (binario self-contained .NET 10)
    ↓
SQLite + FTS5 (~/.engram/engram.db)
```

El agente decide qué vale la pena recordar y llama a `mem_save`. Engram persiste la observación con indexación FTS5, deduplicación automática y soporte de `topic_key` para temas evolutivos.

```
1. El agente completa trabajo significativo (bugfix, decisión de arquitectura, etc.)
2. El agente llama mem_save con un resumen estructurado:
   - title: "Fixed N+1 query in user list"
   - type: "bugfix"
   - content: formato What/Why/Where/Learned
3. Engram persiste en SQLite con indexación FTS5
4. Siguiente sesión: el agente busca en memoria, obtiene contexto relevante
```

---

## Ciclo de sesión

```
Sesión inicia → Agente trabaja → Agente guarda memorias proactivamente
                                         ↓
Sesión termina → Agente escribe resumen de sesión (Goal/Discoveries/Accomplished/Files)
                                         ↓
Siguiente sesión → Contexto de la sesión anterior se inyecta automáticamente
```

---

## Sistema de deduplicación

El store implementa tres caminos de decisión en orden al guardar una observación:

### Camino 1 — topic_key upsert
Si la observación tiene `topic_key`, busca si ya existe una observación con ese mismo topic_key en el mismo proyecto+scope. Si existe, la **actualiza** en lugar de crear una nueva (incrementa `revision_count`).

```
¿Tiene topic_key? → SÍ → ¿Existe en DB? → SÍ → UPDATE (revision_count++)
                                          → NO → continúa al camino 2
```

Útil para conocimiento que evoluciona: la decisión de arquitectura de auth puede cambiar varias veces — siempre es la misma observación actualizada.

### Camino 2 — deduplicación por contenido (ventana 15 min)
Calcula `normalized_hash` del contenido (SHA-256 de contenido en minúsculas con espacios colapsados). Si el mismo hash existe en los últimos 15 minutos, incrementa `duplicate_count` en lugar de insertar.

```
¿hash existe en últimos 15 min? → SÍ → UPDATE (duplicate_count++)
                                 → NO → continúa al camino 3
```

Evita que un agente guarde la misma información repetidamente en una sesión.

### Camino 3 — INSERT nuevo
Si ninguno de los dos caminos anteriores aplica, inserta una nueva observación.

### Algoritmos de normalización

```csharp
// HashNormalized — debe ser idéntico al proyecto Go original
// Go: strings.ToLower(strings.Join(strings.Fields(content), " "))
// strings.Fields divide por CUALQUIER whitespace (\t, \n, \r, espacio)
string HashNormalized(string content)
    → lowercase + colapsar whitespace → SHA256 → hex lowercase

// NormalizeTopicKey
// Go: TrimSpace + ToLower + colapsar whitespace a "-" + truncar a 120 chars
string NormalizeTopicKey(string? topic)
    → trim + lowercase + espacios→guiones + máx 120 chars

// Ejemplo: "Auth Model" → "auth-model"
//          "architecture/auth model" → "architecture/auth-model"
```

---

## Estructura del proyecto

```
engram-dotnet/
├── src/
│   ├── Engram.Store/              ← Motor central: SQLite + FTS5 + deduplicación
│   │   ├── IStore.cs              ← Interfaz pública (22 métodos)
│   │   ├── SqliteStore.cs         ← Implementación SQLite
│   │   ├── Models.cs              ← Session, Observation, Prompt, Stats, etc.
│   │   ├── StoreConfig.cs         ← Configuración desde variables de entorno
│   │   ├── Normalizers.cs         ← HashNormalized, NormalizeTopicKey, SanitizeFts5Query
│   │   └── PassiveCapture.cs      ← Extracción de aprendizajes de texto libre
│   ├── Engram.Server/             ← HTTP REST API (ASP.NET Core Minimal API)
│   │   └── EngramServer.cs        ← 22 endpoints (rutas + middleware integrados)
│   ├── Engram.Mcp/                ← Servidor MCP (transporte stdio)
│   │   ├── EngramMcpServer.cs     ← Bootstrap y configuración del servidor MCP
│   │   └── EngramTools.cs         ← 15 herramientas registradas
│   ├── Engram.Sync/               ← Sync git-friendly (gzip + JSONL)
│   │   └── EngramSync.cs          ← Export/import de chunks comprimidos
│   └── Engram.Cli/                ← Entry point CLI + wiring DI
│       └── Program.cs             ← Comandos serve, mcp, search, export, import, etc.
└── tests/
    ├── Engram.Store.Tests/        ← Unitarios + integración + tests de paridad (51)
    ├── Engram.Server.Tests/       ← Tests HTTP con WebApplicationFactory (16)
    └── Engram.Mcp.Tests/          ← Tests de herramientas MCP (22)
```

---

## Grafo de dependencias

```
Engram.Cli ──→ Engram.Server ──→ Engram.Store
           ──→ Engram.Mcp    ──→ Engram.Store
           ──→ Engram.Sync   ──→ Engram.Store
```

`Engram.Store` no tiene dependencias de proyecto — solo NuGet. Es el único módulo que toca la base de datos.

---

## Stack técnico

| Capa | Tecnología |
|---|---|
| Lenguaje | C# / .NET 10 LTS |
| HTTP | ASP.NET Core Minimal API (Kestrel) |
| Base de datos | SQLite via `Microsoft.Data.Sqlite` + FTS5 |
| MCP | `ModelContextProtocol` NuGet (Microsoft oficial) |
| CLI | `System.CommandLine` |
| Auth | `Microsoft.AspNetCore.Authentication.JwtBearer` (opcional) |
| Tests | xUnit + WebApplicationFactory + tests de paridad |
| Deploy | Self-contained linux-x64 (binario único, sin runtime externo) |

---

## Decisiones arquitecturales

### Self-Contained vs Native AOT
Se eligió **Self-Contained** (no Native AOT) porque:
- Native AOT requiere trimming y prohíbe `Assembly.LoadFile` y `Reflection.Emit`
- El SDK oficial de MCP usa reflection internamente
- Self-Contained produce un binario único que no requiere .NET instalado en el servidor, con cero restricciones de código

```bash
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/
```

### Engram.Server como librería (no ejecutable)
`Engram.Server` usa `Microsoft.NET.Sdk` (no `Sdk.Web`) con `OutputType=Library`. El entry point es siempre `Engram.Cli`. Esto permite que el CLI arranque el servidor Kestrel embebido sin depender de un ejecutable separado.

### SQL directo (sin ORM)
El schema de SQLite debe ser **idéntico** al proyecto Go original para garantizar compatibilidad de datos en la migración. Un ORM introduciría abstracción sobre el schema que haría difícil mantener esta paridad. Toda la SQL vive en `SqliteStore.cs` como constantes `string`.

### JWT opcional
Si `ENGRAM_JWT_SECRET` no está configurado, el servidor arranca sin autenticación (comportamiento idéntico al proyecto Go original). Si está configurado, todos los endpoints excepto `/health` requieren `Authorization: Bearer <token>`.

---

## Schema de base de datos

El schema es **idéntico** al proyecto Go original para permitir migración directa de datos.

Tablas principales:
- `sessions` — sesiones de trabajo de agentes
- `observations` — memorias persistidas (con `normalized_hash`, `topic_key`, `revision_count`, `duplicate_count`)
- `user_prompts` — prompts del usuario
- `sync_chunks` — registro de chunks sincronizados (idempotencia)
- `sync_mutations` — cola de mutaciones pendientes de sync
- `sync_state` / `sync_enrolled_projects` — estado del sync

Tabla FTS5:
- `observations_fts` — índice full-text (title, content, tool_name, type, project, topic_key)

PRAGMAs aplicados al abrir la conexión:
```sql
PRAGMA journal_mode=WAL;
PRAGMA busy_timeout=5000;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;
```

---

## Herramientas MCP

Las mismas 15 herramientas del proyecto original, organizadas en perfiles:

**Perfil `agent`** (por defecto — herramientas para agentes de IA):

| Herramienta | Propósito |
|---|---|
| `mem_save` | Guardar observación estructurada |
| `mem_update` | Actualizar por ID |
| `mem_suggest_topic_key` | Sugerir clave estable para temas evolutivos |
| `mem_search` | Búsqueda full-text (resultados truncados a 300 chars con `[preview]`) |
| `mem_session_summary` | Resumen de fin de sesión |
| `mem_context` | Contexto reciente de sesiones anteriores |
| `mem_timeline` | Contexto cronológico alrededor de una observación |
| `mem_get_observation` | Contenido completo por ID (sin truncar) |
| `mem_save_prompt` | Guardar prompt del usuario |
| `mem_capture_passive` | Extraer aprendizajes de texto |
| `mem_session_start` | Registrar inicio de sesión |

**Perfil `admin`** (herramientas de administración):

| Herramienta | Propósito |
|---|---|
| `mem_delete` | Borrar observación (soft-delete por defecto) |
| `mem_stats` | Estadísticas del sistema |
| `mem_timeline` | Contexto cronológico |
| `mem_merge_projects` | Consolidar variantes de nombre de proyecto |

Selección de perfil: `engram mcp --tools=agent` (default) | `--tools=admin` | `--tools=all`
