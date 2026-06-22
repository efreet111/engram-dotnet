# ENG-434 Spike — Project string → GUID migration exploration

**Date:** 2026-06-18
**Status:** IN PROGRESS
**Caja de tiempo:** 2h max

---

## Hipótesis

ENG-434 (migración `project` string → GUID canónico) tiene un spec sub-especificado que necesita respuestas experimentales antes de re-speccar. El spike explora 5 incógnitas críticas con código real, no opinión.

## 5 incógnitas a responder con evidencia

| # | Pregunta | Cómo se responde |
|---|----------|-----------------|
| 1 | ¿Nueva columna `project_id` o renombrar `project`? | Probar ambos en una DB SQLite temporal, medir impacto |
| 2 | ¿Cuántos archivos/líneas tocan `project`? | Grep en `src/` y `tests/` con evidencia concreta |
| 3 | ¿Migración ONCE o LAZY? ¿Qué pasa con `topic_key`? | Migrar 3 observaciones reales, probar ambos approachs |
| 4 | ¿Performance de lookups por GUID vs string? | Benchmark con 1000 observaciones |
| 5 | ¿Qué se rompe en T2 si reemplazamos `project` string por GUID? | Correr T2 con la DB migrada |

## Scope del spike

Solo exploración. **No mergeable a main.** Código descartable.

### Dentro del scope
- Modificar `IStore.AddObservationAsync` para aceptar `project_id` GUID (junto con `project` string actual)
- Grep de todos los métodos que tocan `project` en Store/MCP/CLI/Server
- Probar migración de 3 observaciones con `topic_key` actualizado
- Comparar velocidad de lookup: `WHERE project = 'engram-dotnet'` vs `WHERE project = '00e340cd-...'`
- Correr T2 con una DB migrada para ver regresiones

### Fuera del scope
- Implementación real de la migración
- Cambios en MCP tools para aceptar `project_id`
- Cambios en CLI commands
- Sync changes
- Re-spec formal

## Plan técnico

### 1. Explore schema impact (30 min)

Crear un `MigrationExplorer.cs` (o test code) que:
```csharp
// 1. Crear DB SQLite con el schema actual
// 2. Ejecutar ALTER TABLE observations ADD COLUMN project_id TEXT;
// 3. Insertar observaciones con project="engram-dotnet" y project_id=GUID
// 4. Verificar que ambos campos son accesibles
// 5. Comparar: ALTER TABLE observations RENAME COLUMN project TO project_id;
// 6. Ver qué rompe (topic_key, scope, etc.)
```

### 2. Impact grep (15 min)

Correr y documentar:
```bash
grep -rn "\bproject\b" src/Engram.Store/ --include="*.cs" | wc -l
grep -rn "\bproject\b" src/Engram.Mcp/ --include="*.cs" | wc -l
grep -rn "\bproject\b" src/Engram.Cli/ --include="*.cs" | wc -l
grep -rn "\bproject\b" src/Engram.Server/ --include="*.cs" | wc -l
```

### 3. Migration experiment (30 min)

Crear un `MigrationExplorer.cs` que:
```csharp
// 1. Crear DB con observaciones donde project="engram-dotnet"
// 2. Migrar ONCE: UPDATE observations SET project = '00e340cd-...' WHERE project = 'engram-dotnet';
// 3. Actualizar topic_key: UPDATE observations SET topic_key = REPLACE(topic_key, 'engram-dotnet', '00e340cd-...');
// 4. Actualizar scope: UPDATE observations SET scope = REPLACE(scope, 'engram-dotnet', '00e340cd-...');
// 5. Verificar que las queries por GUID funcionan
// 6. Probar LAZY: mantener project string, agregar project_id column
```

### 4. Performance (15 min)

Benchmark simple:
```csharp
// Crear 1000 observaciones con project string
// Medir: SELECT * FROM observations WHERE project = 'string_value'
// Medir: SELECT * FROM observations WHERE project = '550e8400-e29b-...'
// Comparar tiempos
```

### 5. T2 impact (15 min)

```bash
# Ejecutar migración en la DB de test
# Correr dotnet test -c Release --filter "FullyQualifiedName!~Engram.Postgres.Tests&Category!=RequiresDocker"
# Ver cuántos tests fallan
```

## Output esperado

- `learnings.md` con las 5 preguntas respondidas con evidencia
- Código exploratorio en `tests/Engram.Store.Tests/Engram434SpikeTests.cs`
- No mergeable — es solo exploración

## Criterios de éxito
- [ ] Las 5 preguntas tienen respuesta concreta
- [ ] Evidencia de código, no opinión
- [ ] Claridad sobre si ENG-434 es XL validado o necesita particionarse
