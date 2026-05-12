# Proposal: Offline-First Sync with PostgreSQL Server

**Change**: `offline-first-sync`
**Version**: 0.1.0
**Author**: Victor Silgado
**Date**: 2026-05-12
**Status**: Draft

---

## Problem Statement

El proyecto engram-dotnet necesita un sistema de sincronización para equipos de trabajo donde:
- Múltiples desarrolladores comparten una base de memoria centralizada
- La red puede no estar disponible (offline-first)
- El servidor PostgreSQL en TrueNAS (`192.168.0.178:7437`) actúa como fuente de verdad
- Cuando la red está disponible, los cambios se sincronizan bidireccionalmente

**Estado actual**:
- Local (`~/.engram/engram.db`): 579 sessions, 2126 observations — **MÁS ACTUAL**
- TrueNAS PostgreSQL: 217 sessions, 564 observations — datos desactualizados
- .NET solo tiene sync de archivos (export/import local), **NO sync de red**

---

## Goals

1. **Offline-first**: El desarrollador siempre puede leer y escribir localmente
2. **Network sync**: Cuando hay conectividad, los cambios fluyen al servidor
3. **Bidirectional**: Cambios del equipo se descargan al local
4. **Conflict-free**: Last-write-wins por timestamp para el MVP
5. **Per-project enrollment**: Cada proyecto decide si sincroniza o no
6. **Same format**: Mantener compatibilidad con Go engram (gzip JSONL chunks)

## Non-Goals

1. **Semantic conflict resolution**: No LLM judge por ahora (v2)
2. **Cross-device sync para un mismo usuario**: Eso requiere cuentas (v2)
3. **Real-time collaboration**: Los cambios son event-driven, no live cursors

---

## Architecture

```
┌─────────────────────┐          ┌──────────────────────────┐
│  Developer Machine  │   HTTPS  │   TrueNAS Server          │
│                     │  push/pull│                          │
│  ┌───────────────┐ │ ────────► │  ┌──────────────────────┐ │
│  │  SQLite       │ │           │  │  PostgreSQL           │ │
│  │  (local)      │ │           │  │  (source of truth)    │ │
│  │               │ │  ◄────────│  │                      │ │
│  │  sync_state   │ │           │  │  sync_mutations       │ │
│  │  sync_chunks  │ │           │  │  sync_chunks          │ │
│  │  sync_mutation│ │           │  │  sync_enrolled_proj   │ │
│  └───────────────┘ │           │  └──────────────────────┘ │
│         │         │           │           ▲               │
│         ▼         │           │           │               │
│  ┌───────────────┐│           │  ┌───────┴──────────────┐ │
│  │ Engram.Sync   ││           │  │ Engram.Server         │ │
│  │ SyncManager   │├───────────┤  │ /sync/mutations/push  │ │
│  │ MutationTrans ││           │  │ /sync/mutations/pull  │ │
│  └───────────────┘│           │  └──────────────────────┘ │
└─────────────────────┘           └──────────────────────────┘
```

### Data Flow

1. **Write path**: `EngramTools` → `WriteQueue` → `SqliteStore` → graba en local + registra `sync_mutations`
2. **Push**: `SyncManager` → `MutationTransport.PushAsync()` → `POST /sync/mutations/push`
3. **Pull**: `SyncManager` → `MutationTransport.PullAsync()` → `GET /sync/mutations/pull?since_seq=N`
4. **Apply**: mutations bajadas → se aplican al SQLite local, se marca `last_pulled_seq`

---

## Data Model

### SQLite (Local) — Nuevas tablas

```sql
-- Seguimiento de sincronización
CREATE TABLE sync_state (
    target          TEXT PRIMARY KEY,    -- 'cloud' o 'local'
    last_pushed_seq INTEGER,
    last_pulled_seq INTEGER,
    last_error      TEXT,
    reason_code     TEXT,
    updated_at      TEXT DEFAULT (datetime('now'))
);

-- Cola de mutaciones pendientes de push
CREATE TABLE sync_mutations (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    entity     TEXT NOT NULL,           -- 'observation', 'session', 'prompt'
    entity_id   TEXT NOT NULL,
    op          TEXT NOT NULL,           -- 'upsert', 'delete'
    payload     TEXT NOT NULL,          -- JSON del registro
    seq         INTEGER NOT NULL,       -- secuencia global (autoincrement)
    created_at  TEXT DEFAULT (datetime('now'))
);

-- Registro de chunks ya sincronizados
CREATE TABLE sync_chunks (
    chunk_id    TEXT PRIMARY KEY,
    created_by  TEXT,
    created_at  TEXT,
    synced_at   TEXT
);
```

### PostgreSQL (Server) — Nuevas tablas

```sql
-- Cola de mutaciones del servidor (idéntica a SQLite)
CREATE TABLE sync_mutations (
    id          SERIAL PRIMARY KEY,
    entity      TEXT NOT NULL,
    entity_id   TEXT NOT NULL,
    op          TEXT NOT NULL,
    payload     JSONB NOT NULL,
    seq         BIGINT NOT NULL UNIQUE,
    created_at  TIMESTAMPTZ DEFAULT now()
);

-- Índice para pull por secuencia
CREATE INDEX idx_sync_mutations_seq ON sync_mutations(seq);

-- Proyectos enrolados para sync
CREATE TABLE sync_enrolled_projects (
    project     TEXT PRIMARY KEY,
    enrolled_at TIMESTAMPTZ DEFAULT now(),
    paused_at   TIMESTAMPTZ,
    pause_reason TEXT
);

-- Sequencer global
CREATE TABLE sync_sequencer (
    id    INTEGER PRIMARY KEY DEFAULT 1 CHECK (id = 1),
    value BIGINT NOT NULL DEFAULT 0
);
INSERT INTO sync_sequencer (id, value) VALUES (1, 0);
```

---

## API Contract

### Server Endpoints (EngramServer.cs)

```
POST /sync/mutations/push
  Body: { "mutations": [ { "entity", "entity_id", "op", "payload" }, ... ] }
  Returns: { "accepted_seqs": [1, 2, 3], "rejected": [] }

GET /sync/mutations/pull?since_seq={N}&project={project}&limit={100}
  Returns: { "mutations": [...], "next_seq": 456 }

POST /sync/enroll
  Body: { "project": "engram" }
  Returns: { "enrolled": true }

POST /sync/pause
  Body: { "project": "engram", "reason": "upgrade" }
  Returns: { "paused": true }

GET /sync/status?project={project}
  Returns: { "enrolled": true, "paused": false, "last_seq": 1234 }
```

### Client MutationTransport

```csharp
public interface IMutationTransport
{
    Task<PullResult> PullAsync(long sinceSeq, string? project, int limit = 100);
    Task<PushResult> PushAsync(IList<Mutation> mutations);
    Task EnrollAsync(string project);
    Task PauseAsync(string project, string reason);
    Task<SyncStatus> GetStatusAsync(string project);
}

public record Mutation(string Entity, string EntityId, string Op, string Payload);
public record PullResult(IList<Mutation> Mutations, long NextSeq);
public record PushResult(IList<long> AcceptedSeqs, IList<RejectedMutation> Rejected);
```

---

## Phases

### Phase 1: Mutation Journal + Server Endpoints (MVP)

> **Esfuerzo**: 6-8h
> **Goal**: Push mutations from local SQLite to PostgreSQL server

| # | Task | Files |
|---|------|-------|
| 1.1 | Add `sync_mutations` table to SqliteStore | SqliteStore.cs |
| 1.2 | Add `sync_state` table to SqliteStore | SqliteStore.cs |
| 1.3 | Wrap writes in `withSyncMutation()` — record upsert/delete | SqliteStore.cs |
| 1.4 | Implement `GetSyncedChunksAsync` / `RecordSyncedChunkAsync` in SqliteStore | SqliteStore.cs |
| 1.5 | Add sync tables to PostgresStore (same schema) | PostgresStore.cs |
| 1.6 | Add `/sync/mutations/push` endpoint | EngramServer.cs |
| 1.7 | Add `/sync/mutations/pull` endpoint | EngramServer.cs |
| 1.8 | Basic `MutationTransport` (HTTP client, no auth yet) | Engram.Sync/ |
| 1.9 | Tests for mutation tracking on writes | SyncMutationTests.cs |

**Out of scope**: Background worker, debounce, conflict resolution.

---

### Phase 2: Autosync Manager + Debounce

> **Esfuerzo**: 8-10h
> **Goal**: Background sync loop with debounce and backoff

| # | Task | Files |
|---|------|-------|
| 2.1 | `SyncManager` class with `RunAsync(CancellationToken)` | Engram.Sync/SyncManager.cs |
| 2.2 | Debounced dirty notifications (channel, 500ms coalescing) | SyncManager.cs |
| 2.3 | Push cycle: drain `sync_mutations`, send to server | SyncManager.cs |
| 2.4 | Pull cycle: fetch server mutations, apply to local | SyncManager.cs |
| 2.5 | Exponential backoff on errors (1s base, 5min max, jitter) | SyncManager.cs |
| 2.6 | Lease acquisition for single-writer (SQLite advisory lock) | SyncManager.cs |
| 2.7 | Register `SyncManager` in DI (singleton, background host) | Program.cs |
| 2.8 | Tests for SyncManager (mock transport) | SyncManagerTests.cs |

---

### Phase 3: Enrollment + Conflict Resolution

> **Esfuerzo**: 4-6h
> **Goal**: Per-project enrollment and basic conflict handling

| # | Task | Files |
|---|------|-------|
| 3.1 | Add `sync_enrolled_projects` table to PostgresStore | PostgresStore.cs |
| 3.2 | Add `/sync/enroll` and `/sync/pause` endpoints | EngramServer.cs |
| 3.3 | `IMutationTransport.EnrollAsync()` / `PauseAsync()` | MutationTransport.cs |
| 3.4 | Apply remote mutations with conflict check (last-write-wins) | SyncManager.cs |
| 3.5 | Tests for enrollment flow | EnrollmentTests.cs |

---

### Phase 4: Cloud Dashboard Integration

> **Esfuerzo**: 4-6h
> **Goal**: Dashboard status, manual sync trigger, observability

| # | Task | Files |
|---|------|-------|
| 4.1 | `/sync/status` endpoint with enrolled/paused/last_seq | EngramServer.cs |
| 4.2 | `IMutationTransport.GetStatusAsync()` | MutationTransport.cs |
| 4.3 | SyncManager exposes metrics (push count, pull count, errors) | SyncManager.cs |
| 4.4 | CLI: `engram sync status` showing enrolled projects | Program.cs (CLI) |
| 4.5 | Docs: update DEPLOYMENT.md with sync setup guide | docs/DEPLOYMENT.md |
| 4.6 | Update ROADMAP.md | docs/ROADMAP.md |

---

## References

### Go Implementation (Source of Truth)

| Go File | .NET Port |
|---------|-----------|
| `internal/sync/sync.go` | `Engram.Sync/SyncManager.cs` |
| `internal/cloud/remote/transport.go` | `Engram.Sync/MutationTransport.cs` |
| `internal/cloud/autosync/manager.go` | SyncManager background loop |
| `internal/store/store.go` (sync tables) | `SqliteStore.cs` / `PostgresStore.cs` |
| `internal/cloud/cloudserver/mutations.go` | `EngramServer.cs` /sync endpoints |

### Chunk Format Compatibility

El formato de chunks (gzip JSONL + manifest SHA256) ya es compatible:
- Go: `.engram/chunks/{sha256}.jsonl.gz`
- .NET: mismo formato — intercambio directo posible

---

## Rollback

- Si la migración de schema falla: `AddColumnIfNotExists` para todas las columnas nuevas
- Si el sync loop causa deadlocks: feature flag `ENGRAM_SYNC_ENABLED=false` desactiva el manager
- Datos en PostgreSQL siempre source of truth — local puede truncarse si es necesario

---

## Success Criteria

1. Phase 1: `POST /sync/mutations/push` acepta mutations del CLI y las persiste en PostgreSQL
2. Phase 2: SyncManager corre en background, push cada 30s cuando hay dirty flags
3. Phase 3: Proyectos pueden enrolar/pausar sync; remote mutations se aplican correctamente
4. Phase 4: `engram sync status` muestra estado de sync por proyecto