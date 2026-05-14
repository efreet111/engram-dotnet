# Git-Based Sync — engram-dotnet

> **Nota**: Este documento describe el sync **git-based** (chunks JSONL comprimidos). No confundir con [Offline-First Sync](OFFLINE-FIRST-SYNC.md) que es mutation-based via REST API.

---

## ¿Qué es?

El sistema de sync git-friendly permite distribuir memorias entre múltiples desarrolladores usando un repositorio git como transporte. Cada desarrollador exporta sus memorias locales como chunks comprimidos (`.jsonl.gz`) y los hace disponibles para el equipo vía git.

---

## Arquitectura

```
┌─────────────────┐
│ Dev 1 (local)   │
│ ~/.engram/      │
│   engram.db     │
│   sync/         │
│     manifest.json│
│     chunks/     │
│       abc123.jsonl.gz │
└────────┬────────┘
         │ git push
         ▼
┌─────────────────┐
│ Git Repo        │
│ (remoto)        │
│   .engram/      │
│     chunks/     │
└────────┬────────┘
         │ git pull
         ▼
┌─────────────────┐
│ Dev 2 (local)   │
│ ~/.engram/      │
│   engram.db     │
│   sync/         │
│     manifest.json│
│     chunks/     │
└─────────────────┘
```

---

## Componentes

### `EngramSync.cs`

Clase principal que maneja export/import de chunks.

**Métodos**:
- `ExportChunkAsync()` — Exporta memorias no sincronizadas a chunk comprimido
- `ImportNewChunksAsync()` — Importa chunks nuevos desde el directorio de sync
- `GetStatusAsync()` — Retorna estado (total/synced/pending chunks)

### Chunk Format

Cada chunk es un archivo JSONL comprimido con gzip:

```jsonl
{"type":"session","data":{...}}
{"type":"observation","data":{...}}
{"type":"prompt","data":{...}}
```

**Chunk ID**: SHA-256 hash del contenido (primeros 8 caracteres).

### Manifest

`manifest.json` trackea todos los chunks exportados:

```json
{
  "version": 1,
  "chunks": [
    {
      "id": "abc12345",
      "created_by": "victor.silgado",
      "created_at": "2026-05-13T22:30:00Z",
      "sessions": 5,
      "memories": 42,
      "prompts": 10
    }
  ]
}
```

---

## Configuración

### Variables de entorno

```bash
# Directorio de sync (default: ~/.engram/sync)
ENGRAM_SYNC_DIR=~/.engram/sync

# Repo git remoto (opcional — si no se setea, sync es local)
ENGRAM_SYNC_REPO=git@github.com:team/engram-memories.git

# Rama del repo (default: main)
ENGRAM_SYNC_BRANCH=main

# Auto-sync al guardar (default: false)
ENGRAM_AUTO_SYNC=false
```

---

## Uso

### Exportar chunk

```bash
# Exportar nuevas memorias como chunk
engram sync

# Ver estado
engram sync --status
```

**Output**:
```
Sync Status:
  Enabled: false (ENGRAM_SYNC_REPO not set)
  Total chunks: 15
  Synced chunks: 12
  Pending chunks: 3
```

### Importar chunks

```bash
# Importar chunks nuevos desde el directorio de sync
engram sync --import
```

**Output**:
```
Imported 3 chunks:
  abc12345: 5 sessions, 42 memories, 10 prompts
  def67890: 3 sessions, 28 memories, 5 prompts
  ghi11111: 2 sessions, 15 memories, 3 prompts

Total imported: 90 memories
```

---

## Flujo de trabajo en equipo

### 1. Configurar repo compartido

```bash
# En el servidor o repo central
mkdir engram-memories
cd engram-memories
git init --bare
# Push a remoto (GitHub, GitLab, etc.)
```

### 2. Cada desarrollador configura

```bash
# ~/.engram/sync/config.json (o variables de entorno)
{
  "repo": "git@github.com:team/engram-memories.git",
  "branch": "main"
}
```

### 3. Flujo diario

```bash
# Antes de empezar: traer chunks nuevos
engram sync --import

# Trabajar normal (memorias se guardan local)

# Al final del día: exportar y pushear
engram sync
cd ~/.engram/sync
git add chunks/ manifest.json
git commit -m "Sync: 3 new chunks"
git push
```

---

## Directorio Structure

```
~/.engram/sync/
  manifest.json           ← Índice de chunks (append-only)
  chunks/
    abc12345.jsonl.gz     ← Chunk comprimido
    def67890.jsonl.gz
    ...
  engram.db               ← DB local (git-ignored)
```

---

## Consideraciones

### Idempotencia

Cada chunk tiene un ID único (SHA-256). El sistema trackea qué chunks ya fueron importados en `sync_chunks` table. Importar el mismo chunk twice es seguro — no hay duplicación.

### Conflictos

El sync git-based es **unidireccional**: cada dev exporta SUS memorias locales. No hay merge automático de memorias de otros devs — cada uno decide cuándo importar.

### Cuándo usar git-based sync

| Escenario | Recomendación |
|-----------|---------------|
| Equipo pequeño (2-4 devs) | ✅ Git-based sync (simple, git ya está) |
| Equipo mediano (5-10 devs) | ⚠️ Considerar Offline-First Sync (mutation-based) |
| Equipo grande (10+ devs) | ✅ Offline-First Sync (mejor escalabilidad) |
| Sin conexión frecuente | ✅ Git-based sync (async por naturaleza) |
| Con servidor centralizado | ✅ Offline-First Sync (REST API) |

---

## Diferencias con Offline-First Sync

| Feature | Git-Based Sync | Offline-First Sync |
|---------|---------------|-------------------|
| Transporte | Git repo | REST API (HTTP) |
| Granularidad | Chunks (batch) | Mutations (individual) |
| Sync direction | Push/pull manual | Autosync en background |
| Conflict resolution | Manual (cada dev decide) | Last-write-wins automático |
| Infraestructura | Git repo (ya existe) | Servidor PostgreSQL + endpoints |
| Complejidad | Baja | Media-Alta |

---

## Ver también

- [Offline-First Sync](OFFLINE-FIRST-SYNC.md) — Mutation-based sync via REST API
- [TEAM-SETUP.md](TEAM-SETUP.md) — Configuración de equipo
- [DEPLOYMENT.md](DEPLOYMENT.md) — Deploy del servidor
