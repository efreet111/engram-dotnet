# ADR-015: Memory Taxonomy & Lifecycle Status

**Fecha:** 2026-09-04  
**Estado:** Accepted  
**Feature:** ENG-412  
**Autores:** Victor, Kaito (forge-arch), forge-dev

---

## Contexto

El sistema de memoria de Engram no tenía forma de distinguir entre decisiones vigentes y decisiones obsoletas. Cuando una decisión arquitectónica evolucionaba (ej: de "controlador monolítico" a "microservicio"), ambas versiones aparecían como "activas" en las búsquedas, causando confusión para los agentes de IA y desarrolladores.

**Problemas identificados:**
1. No había forma de marcar una decisión como obsoleta
2. Las búsquedas devolvían todas las versiones sin distinguir cuál era la vigente
3. No había panorama general del estado de las decisiones por proyecto
4. Los agentes de IA no sabían cuándo deprecar una decisión antigua

## Decisión

Implementar un sistema de lifecycle status con tres estados:

### Estados
- **`active`**: Decisión vigente, visible por defecto en búsquedas
- **`deprecated`**: Decisión obsoleta, solo visible con flag explícito
- **`deleted`**: Soft-delete, solo visible por ID directo

### Implementación

#### 1. Campo `status` en tabla `observations`
```sql
ALTER TABLE observations ADD COLUMN status TEXT NOT NULL DEFAULT 'active';
CREATE INDEX idx_obs_topic_status ON observations(topic_key, status);
```

#### 2. Validación de enum cerrado
```csharp
public static class StatusValidator
{
    public static readonly string[] ValidStatuses = ["active", "deprecated", "deleted"];
    public static bool IsValid(string? status) => 
        status is not null && ValidSet.Contains(status);
}
```

#### 3. Herramientas MCP extendidas
- **`mem_update`**: Acepta parámetro `status` para cambiar estado
- **`mem_search`**: Filtra por `status='active'` por defecto, con flags `include_deprecated` y `grouped`
- **`mem_decision_tree`**: Nuevo tool para panorama de decisiones agrupadas por topic_key

#### 4. Helper compartido `TopicKeyGrouper`
```csharp
public static class TopicKeyGrouper
{
    public static Dictionary<string, List<Observation>> GroupByTopicKey(
        IEnumerable<Observation> observations);
    
    public static List<(Observation Head, int DeprecatedCount, int TotalCount)> GetGroupHeads(
        IEnumerable<Observation> observations);
}
```

### Convenciones de `topic_key`

#### Evolución (mismo tema, cambios incrementales)
```
topic_key: "architecture/auth-model"
→ Actualiza la misma observación (upsert)
→ Mantiene el historial de revisiones
```

#### Reemplazo (decisión completamente nueva)
```
topic_key: "decision/cliente-microservice"
→ Crea nueva observación
→ Deprecar la antigua: mem_update(id: old, status: "deprecated")
→ Opcionalmente vincular: mem_relations(from: new, to: old, type: "supersedes")
```

## Consecuencias

### Positivas
✅ Los agentes de IA pueden distinguir decisiones vigentes de obsoletas  
✅ Búsquedas más limpias: solo muestran lo relevante por defecto  
✅ Panorama general con `mem_decision_tree` para onboarding y review  
✅ Trazabilidad completa: se mantiene el historial de decisiones obsoletas  
✅ Backward compatible: observaciones existentes obtienen `status='active'` automáticamente  

### Negativas
⚠️ Requiere disciplina: los agentes deben marcar `deprecated` explícitamente  
⚠️ Depende de calidad de `topic_key`: si es inconsistente, el agrupamiento falla  
⚠️ No hay consolidación automática de sesiones (quedó en Icebox)  

### Riesgos Mitigados
- **Calidad de `topic_key`**: `Normalizers.NormalizeTopicKey` + `mem_suggest_topic_key` proveen consistencia
- **Disciplina del agente**: Documentación completa en descripciones de tools MCP (FR-007)
- **Backward compatibility**: Migración idempotente con `DEFAULT 'active'`

## Testing

- **1062 tests pasando** (991 SQLite + 71 PostgreSQL)
- **11 tests de regresión** verifican backward compatibility
- **12 tests específicos de ENG-412** en PostgreSQL con Docker

## Referencias

- **Spec:** `.ai-work/eng-412-memory-taxonomy-lifecycle/spec.md`
- **Summary:** `.ai-work/eng-412-memory-taxonomy-lifecycle/summary.md`
- **Tests:** `tests/Engram.Store.Tests/RegressionTests.cs`
- **RFC relacionado:** RFC-006 (Multi-Server Pull Deduplication) — estrategia definida, implementación pendiente

## Relación con otros ADRs

- **ADR-009 (Two-Version Model):** Complementario — ADR-009 maneja versionado de schemas, ADR-015 maneja lifecycle de decisiones
- **ENG-404 (Memory Relations):** Complementario — ENG-404 provee relaciones `supersedes`, ADR-015 provee el estado `deprecated`

## Follow-up

- **RFC-002-memory-taxonomy.md:** No existe aún. Este ADR es autoritativo para v1. Escribir RFC como follow-up si es necesario.
- **Consolidación automática:** Quedó en Icebox. Evaluar si es necesario en el futuro.
- **Job nocturno de mantenimiento:** Quedó en Icebox. Evaluar si es necesario en el futuro.
