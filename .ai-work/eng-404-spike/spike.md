# ENG-404 Spike — Memory Relations POC

**Date:** 2026-06-18  
**Author:** forge-dev (delegated by orchestrator)  
**Status:** IN PROGRESS

---

## Hipótesis

> ENG-404 (memory relations) puede clonarse del patrón existente en
> `Engram.Verification` (TraceRepository + LineageBuilder + RelationValidator)
> en **M effort**, no XL. El spike valida esto en 2h.

## Qué se prueba

1. ¿Se puede reusar `LineageBuilder` y `RelationValidator` tal cual para
   observations generales (no solo RF-*)?
2. ¿`TraceRepository` como template funciona si cambiamos `trace/` → `memrel/`
   en el topic_key?
3. ¿El patrón completo (save → query → traverse con cycle detection) cabe
   en **~200 líneas**?
4. ¿El BFS + MaxHops + visited set da resultados correctos en casos reales?

## Caja de tiempo

- **Asignado:** 2h
- **Stop condition:** si después de 1.5h el código no compila o el BFS da
  resultados incorrectos → cancelar y archivar learnings negativos.

## Out of scope (decisiones postergadas al feature real)

- Schema migration
- MCP tool `mem_relations` con todos los argumentos
- Sync de relations entre SQLite y PostgreSQL
- UI / visualización
- Tests exhaustivos (solo smoke test)

## Plan técnico

### Paso 1 — Clonar modelo y validador

Crear `src/Engram.Verification/MemoryRelation.cs`:

```csharp
public sealed record MemoryRelation
{
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("target_observation_id")] public long TargetObservationId { get; init; }
}
```

Reusar `RelationValidator` con los mismos 4 tipos:
`depends_on`, `supersedes`, `conflicts_with`, `related_to`.

### Paso 2 — Clonar repositorio (cambiar topic_key)

Crear `src/Engram.Verification/MemoryRelationRepository.cs`:

```csharp
public sealed class MemoryRelationRepository
{
    private readonly IStore _store;
    public MemoryRelationRepository(IStore store) => _store = store;

    public async Task SaveRelationAsync(string project, long observationId, MemoryRelation rel, string sessionId)
    {
        var topicKey = $"memrel/{project}/{observationId}";
        // Append rel to existing trace OR create new
        // (clone pattern from TraceRepository.cs:26-41)
    }

    public async Task<List<MemoryRelation>> GetRelationsAsync(string project, long observationId) { /* ... */ }
}
```

### Paso 3 — Reusar LineageBuilder tal cual

```csharp
// In EngramTools.cs
var lineage = await _lineageBuilder.BuildLineageAsync(project, $"obs-{observationId}");
```

⚠️ `LineageBuilder` espera un `requirementId` (string) y `TraceRepository`
lo busca como `trace/{project}/{reqId}`. Necesitamos un `MemoryLineageBuilder`
que use `memrel/{project}/{observationId}` y haga BFS sobre observation IDs
(long). **Esto es la única parte no trivial** — validar que el algoritmo
BFS funciona con long IDs.

### Paso 4 — Smoke test

Test que:
1. Crea 5 observations en un proyecto
2. Crea 3 relations entre ellas (depends_on, supersedes, related_to)
3. Hace un `BuildLineage` desde la observation #2
4. Verifica que ancestors y descendants se devuelven correctamente
5. Verifica cycle detection con un caso A → B → A

## Output esperado

- `src/Engram.Verification/MemoryRelation.cs` (10 líneas)
- `src/Engram.Verification/MemoryRelationRepository.cs` (80 líneas)
- `src/Engram.Verification/MemoryLineageBuilder.cs` (60 líneas)
- `tests/Engram.Verification.Tests/MemoryRelationsSpikeTests.cs` (50 líneas)
- **Total: ~200 líneas**

## Criterios de éxito

- [ ] T1 compila sin errores
- [ ] Smoke test pasa (5 memories + 3 relations → lineage correcto)
- [ ] Cycle detection funciona (A → B → A → flag)
- [ ] BFS respeta MaxHops

## Si falla

Cancelar y escribir `learnings.md` con la causa raíz. No archivar la ENG-404,
queda en Icebox.
