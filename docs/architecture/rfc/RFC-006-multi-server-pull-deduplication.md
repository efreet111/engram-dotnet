# RFC-006 — Multi-Server Pull Deduplication

> Request for Comments — Proposal for discussion

## Template Fields

| Campo | Valor |
|-------|-------|
| **Status** | Draft |
| **Date** | 2026-08-11 |
| **Author** | Kaito |
| **Source** | HU-014 — Smart Sync Triggers |

---

## 1. Problem

**Situación actual**: Con HU-013 (per-server denylist) y HU-014 (multi-server), un cliente puede sincronizar con N servidores. Cada servidor tiene su propia copia de mutations.

**Problema**: Al hacer pull de múltiples servidores, ¿cómo evitamos duplicados y conflictos?

```
Escenario real:
- Server A: tu servidor personal (home lab)
- Server B: servidor del equipo (oficina)

Desktop:
- mem_save → Server A (obs-1, occurred_at: 10:00)
- mem_save → Server B (obs-2, occurred_at: 10:05)

Laptop (pull de ambos):

Server A devuelve: [obs-1: "trabajo en feature X", occurred_at: 10:00]
Server B devuelve: [obs-2: "bug en Y", occurred_at: 10:03]

→ obs-1 existe solo en A
→ obs-2 existe solo en B
→ No hay conflicto directo.

PERO si editás la misma obs en ambos:

Server A: obs-X { content: "bug en Y", occurred_at: 10:05 }
Server B: obs-X { content: "bug en Y FIXED", occurred_at: 10:03 }

Cliente recibe obs-X de ambos servidores → ¿cuál gana?
```

**Decisión de diseño existente**: Last-write-wins por `occurred_at` (ADR-009). Esto aplica a nivel de mutation individual, no de sync completo multi-servidor.

---

## 2. Solution

**Estrategia**: Pull secuencial por servidor + deduplicación basada en `sync_id` + last-write-wins por `occurred_at`.

### 2.1 Tracking del cursor por servidor

**Estado actual:**
```csharp
// SyncState table — 1 fila por target_key
// target_key | last_pulled_seq
// "cloud"   | 142
```

**Estado requerido:**
```csharp
// SyncState table — 1 fila por (target_key, server_id)
// target_key | server_id    | last_pulled_seq
// "cloud"   | "server-A"  | 142
// "cloud"   | "server-B"  | 87
```

**Schema migration:**
```sql
ALTER TABLE sync_state ADD COLUMN server_id TEXT;

-- Índice único compuesto
CREATE UNIQUE INDEX idx_sync_state_server 
ON sync_state(target_key, server_id);
```

**Interfaz actualizada:**
```csharp
// ILocalSyncStore
Task<long?> GetLastPulledSeqAsync(string targetKey, string serverId, CancellationToken ct = default);
Task SetLastPulledSeqAsync(string targetKey, string serverId, long seq, CancellationToken ct = default);
```

### 2.2 PullAllServersAsync — Flujo completo

```csharp
public async Task PullAllServersAsync(CancellationToken ct = default)
{
    // 1. Obtener lista de servidores enrolados para el proyecto actual
    var servers = await _store.GetEnrolledServersForProjectAsync(_currentProject, ct);
    
    var allMutations = new List<PulledMutation>();
    var lastSeqPerServer = new Dictionary<string, long>();
    
    foreach (var server in servers)
    {
        // 2. Filtrar por denylist (HU-013)
        if (_denylist.IsDenied(_currentProject, server))
        {
            _logger.LogDebug("Skipping {Server} for {Project} — denylisted", server, _currentProject);
            continue;
        }
        
        // 3. Obtener cursor para este servidor
        var cursor = await _store.GetLastPulledSeqAsync(_currentProject, server, ct) ?? 0;
        
        // 4. Pull desde este servidor
        try
        {
            var mutations = await _transport.PullMutationsAsync(
                sinceSeq: cursor,
                limit: 100,
                server,
                ct);
            
            if (mutations.Count > 0)
            {
                allMutations.AddRange(mutations);
                lastSeqPerServer[server] = mutations.Max(m => m.Seq);
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.RequestTimeout)
        {
            // 5. Timeout: skip este servidor, continuar con el siguiente
            _logger.LogWarning("Timeout pulling from {Server}, skipping", server);
            continue;
        }
    }
    
    // 6. Deduplicar antes de aplicar
    await ApplyWithDedupAsync(allMutations, ct);
    
    // 7. Actualizar cursores
    foreach (var (server, seq) in lastSeqPerServer)
    {
        await _store.SetLastPulledSeqAsync(_currentProject, server, seq, ct);
    }
}
```

### 2.3 Deduplicación — ApplyWithDedupAsync

```csharp
public async Task ApplyWithDedupAsync(List<PulledMutation> mutations, CancellationToken ct)
{
    if (mutations.Count == 0) return;
    
    // Agrupar por sync_id (identificador canónico de cada observación)
    var grupos = mutations.GroupBy(m => m.SyncId);
    
    foreach (var grupo in grupos)
    {
        PulledMutation winner;
        
        if (grupo.Count() == 1)
        {
            // Solo viene de un servidor → aplicar directo
            winner = grupo.First();
        }
        else
        {
            // Viene de múltiples servidores → last-write-wins
            winner = grupo
                .OrderByDescending(m => m.OccurredAt)
                .ThenByDescending(m => m.ServerId)  // tiebreaker: server_id mayor gana
                .First();
            
            _logger.LogDebug(
                "Dedup conflict for {SyncId}: {Count} versions, winning from {Server} at {OccurredAt}",
                grupo.Key, grupo.Count(), winner.ServerId, winner.OccurredAt);
        }
        
        // 4. Aplicar al store local
        // ApplyObservationUpsert ya hace INSERT OR REPLACE
        // (existente en SqliteStore.cs:2357)
        await ApplyMutationAsync(winner, ct);
    }
}
```

### 2.4 ApplyMutationAsync — Cómo se aplica

```csharp
// Esto ya existe en SqliteStore.cs:2357 (ApplyObservationUpsert)
// y en PostgresStore.cs de forma similar

public async Task ApplyObservationUpsert(PulledMutation mutation, CancellationToken ct)
{
    var existing = await GetObservationBySyncIdAsync(mutation.SyncId, ct);
    
    if (existing == null)
    {
        // INSERT nuevo
        await InsertObservationAsync(mutation, ct);
    }
    else if (mutation.OccurredAt > existing.OccurredAt)
    {
        // UPDATE solo si el incoming es más reciente
        await UpdateObservationAsync(mutation, ct);
    }
    // Si mutation.OccurredAt <= existing.OccurredAt → skip (ya tenemos más reciente)
}
```

### 2.5 Flujo completo de un ciclo de sync (pull)

```
SyncManager.ExecuteAsync():
    │
    ├─► PullAllServersAsync()
    │       │
    │       ├─► GET /sync/mutations/pull?since_seq=142&server=A
    │       │       ← [obs-1, obs-X]
    │       │
    │       ├─► GET /sync/mutations/pull?since_seq=87&server=B
    │       │       ← [obs-Y, obs-X (con occurred_at menor)]
    │       │
    │       ├─► Juntar: [obs-1, obs-X (A), obs-Y, obs-X (B)]
    │       │
    │       ├─► Deduplicar:
    │       │       obs-1 → solo A
    │       │       obs-Y → solo B
    │       │       obs-X → viene de A y B → gana A (occurred_at mayor)
    │       │
    │       └─► Aplicar al store local
    │
    └─► UpdateSyncState(last_seq_per_server)
```

---

## 3. Open Questions

| # | Pregunta | Opciones |
|---|----------|---------|
| 1 | ¿Qué pasa si `occurred_at` es igual en ambos servidores? | `server_id` como tiebreaker (mayor gana) / Mantener ambos como separate entries |
| 2 | ¿Cómo se obtiene la lista de servidores enrolados? | Query a `sync_enrolled_projects` + `sync_state.server_id` / Config explícita |
| 3 | ¿El pull-all se hace en paralelo o secuencial? | Secuencial (elegida) — más simple, menos presión en red / Paralelo — más rápido |
| 4 | ¿Timeout por servidor o global? | Por servidor: 5s, skip si falla / Global: 30s total |

---

## 4. Alternatives Considered

| Alternativa | Pros | Contras |
|-------------|------|---------|
| **A) Last-write-wins por occurred_at (elegida)** | Simple, ya existe (ADR-009) | Puede perder writes si clocks desincronizados |
| **B) Vector clocks** | Resuelve conflictos genuinos | Storage extra, complexity alta |
| **C) Union semántica** | Mantiene ambas versiones | No aplica para observations |
| **D) Server priority** | Predicable | Single point of failure |

---

## 5. Risks

| Risk | Impact | Mitigation |
|------|--------|------------|
| Clocks desincronizados entre servidores | Low | NTP, tiebreaker por server_id |
| Un servidor lento bloquea o rompe pull-all | Medium | Timeout 5s por servidor, skip + continue |
| Datos duplicados | Low | `sync_id` unique constraint + INSERT OR IGNORE |
| Cursor por servidor no se actualiza si crash | Medium | Cursor se actualiza DESPUÉS de aplicar, no antes |

---

## 6. Integration Points

| Componente | Cambio necesario |
|------------|-----------------|
| `SyncState` table | Agregar `server_id` column + unique index |
| `ILocalSyncStore` | Métodos `GetLastPulledSeqAsync` y `SetLastPulledSeqAsync` con `serverId` |
| `SqliteStore` | Implementar los métodos arriba + migration |
| `PostgresStore` | Implementar los métodos arriba + migration |
| `SyncManager` | Nuevo método `PullAllServersAsync()` + integración en ciclo |
| `MutationTransport` | Método `PullMutationsAsync` acepta `serverId` + lo pasa al endpoint |

---

## 7. Migration Plan

1. Agregar columna `server_id` nullable a `sync_state` (backward compatible)
2. Actualizar `GetLastPulledSeqAsync` para usar `server_id ?? "cloud"` si no existe
3. Actualizar `SetLastPulledSeqAsync` para escribir `server_id` si se provee
4. Existing data migrates with `server_id = "cloud"` (default)
5. Nuevo index `idx_sync_state_server` creado post-migration
