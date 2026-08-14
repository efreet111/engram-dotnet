# HU-052 — Dev-facing observability mejorado (`engram stats`)

**ENG:** ENG-482  
**Tipo:** Feature  
**Prioridad:** P1  
**Esfuerzo:** M (4-6 horas)  
**Estado:** Idea  
**Origen:** ← feature ideas 2026-08 (ENGRAM-IDEA-006)

---

## Problema que resuelve

Hoy, `engram stats` muestra información muy básica:

```
Engram Memory Stats
  Sessions:     42
  Observations: 156
  Prompts:      89
  Projects:     my-project, other-project
  Database:     /path/to/engram.db
```

**No responde las preguntas que los desarrolladores realmente tienen:**
- ¿Cuántas memorias tengo de cada tipo? (decisiones, insights, blockers)
- ¿Cuáles son mis memorias más recientes?
- ¿Tengo memorias viejas que debería limpiar?
- ¿Cuánto espacio ocupa mi base de datos?
- ¿Hay duplicados?

**Sin visibilidad, no hay confianza.** Los power users no saben qué tienen ni cuándo limpiar.

---

## Propuesta de solución

`engram stats` mejorado con secciones adicionales:

```
Engram Memory Stats
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

📊 Overview
  Total observations: 156
  Total sessions:     42
  Total prompts:      89
  Projects:           3 (my-project, other-project, demo)
  Database:           /path/to/engram.db (2.3 MB)

📝 By Type
  decision:    23 (14.7%)
  insight:     45 (28.8%)
  note:        67 (42.9%)
  blocker:     12 (7.7%)
  convention:   9 (5.8%)

🕐 Recent Activity (last 30 days)
  Created: 34 memories
  Most active project: my-project (18 memories)
  Most active type: insight (12 memories)

🧊 Oldest Memories (90+ days)
  23 memories not updated in 90+ days
  Run `engram stats --prune-preview` to see candidates for cleanup

💾 Storage
  Database size: 2.3 MB
  Average memory size: 1.2 KB
  Largest memory: 8.4 KB (id: 42, "ADR-007 sync recovery")
```

### Versión simplificada (SIN schema migration)

**Importante:** Esta versión NO requiere agregar `access_count` ni `last_accessed_at` al schema. Solo usa queries sobre `created_at` y `updated_at` (que ya existen).

**Lo que SÍ incluye:**
- ✅ Breakdown por tipo (query: `GROUP BY type`)
- ✅ Recently created (últimos 30 días, query: `WHERE created_at > date('now', '-30 days')`)
- ✅ Oldest memories (90+ días sin actualizar, query: `WHERE updated_at < date('now', '-90 days')`)
- ✅ Storage size (query: `pg_database_size()` para Postgres, `PRAGMA page_count * page_size` para SQLite)

**Lo que NO incluye (future enhancement):**
- ❌ Hot/cold memories por access count (requiere `access_count` column)
- ❌ "Last accessed" tracking (requiere `last_accessed_at` column)
- ❌ Detección de duplicados (requiere algoritmo de similitud)
- ❌ Decay suggestion interactivo (requiere access tracking)

---

## Criterios de aceptación

- [ ] `engram stats` muestra:
  - [ ] **Overview**: totals (observations, sessions, prompts, projects)
  - [ ] **By Type**: breakdown con count y porcentaje
  - [ ] **Recent Activity**: memorias creadas en últimos 30 días
  - [ ] **Oldest Memories**: count de memorias 90+ días sin actualizar
  - [ ] **Storage**: database size en MB/KB
- [ ] Output formateado con emojis y separadores (como el ejemplo arriba)
- [ ] Flag `--json` para output machine-readable:
  ```json
  {
    "overview": { "observations": 156, "sessions": 42, ... },
    "by_type": { "decision": 23, "insight": 45, ... },
    "recent_30d": { "created": 34, "most_active_project": "my-project" },
    "oldest_90d": { "count": 23 },
    "storage": { "size_mb": 2.3 }
  }
  ```
- [ ] Tests:
  - [ ] Stats con SQLite (in-memory)
  - [ ] Stats con Postgres (Testcontainers)
  - [ ] Output JSON válido
  - [ ] Edge case: base de datos vacía (0 memorias)
- [ ] Documentación:
  - [ ] `docs/01-QUICK-START.md` — ejemplo de `engram stats`
  - [ ] `README.md` — mención de stats mejorado

---

## Implementación técnica

### Dónde tocar código

1. **`src/Engram.Store/ILocalSyncStore.cs`**:
   - Agregar método `GetDetailedStatsAsync()` que retorna:
     ```csharp
     public record DetailedStats(
         int TotalObservations,
         int TotalSessions,
         int TotalPrompts,
         List<string> Projects,
         Dictionary<string, int> ByType,
         int Recent30Days,
         int Oldest90Days,
         long DatabaseSizeBytes
     );
     ```

2. **`src/Engram.Store/SqliteStore.cs`**:
   - Implementar `GetDetailedStatsAsync()` con queries SQL:
     ```sql
     -- By type
     SELECT type, COUNT(*) FROM observations WHERE deleted_at IS NULL GROUP BY type;
     
     -- Recent 30 days
     SELECT COUNT(*) FROM observations WHERE created_at > date('now', '-30 days');
     
     -- Oldest 90 days
     SELECT COUNT(*) FROM observations WHERE updated_at < date('now', '-90 days');
     
     -- Database size
     PRAGMA page_count;
     PRAGMA page_size;
     ```

3. **`src/Engram.Store/PostgresStore.cs`**:
   - Implementar `GetDetailedStatsAsync()` con queries SQL:
     ```sql
     -- Database size
     SELECT pg_database_size(current_database());
     ```

4. **`src/Engram.Cli/Program.cs`**:
   - Actualizar `statsCmd.SetHandler` para llamar a `GetDetailedStatsAsync()`
   - Formatear output con emojis y separadores
   - Agregar flag `--json`

5. **Tests**:
   - `tests/Engram.Store.Tests/DetailedStatsTests.cs` — 5-7 tests
   - `tests/Engram.Postgres.Tests/PostgresDetailedStatsTests.cs` — 2-3 tests

### Edge cases a manejar

- ¿Qué pasa si la base de datos está vacía?
  - Mostrar "No memories yet" en cada sección
  - Database size = 0 MB

- ¿Qué pasa si no hay memorias de cierto tipo?
  - No mostrar ese tipo en "By Type" (o mostrar 0)

- ¿Qué pasa con memorias soft-deleted (`deleted_at IS NOT NULL`)?
  - NO contarlas en los stats (solo activas)

---

## Fuera de alcance

- ❌ Access tracking (`access_count`, `last_accessed_at`) — requiere schema migration
- ❌ Hot/cold memories — requiere access tracking
- ❌ Detección de duplicados — requiere algoritmo de similitud (ENG-414)
- ❌ Decay suggestion interactivo — future enhancement
- ❌ `engram stats --prune-preview` — future enhancement

**Nota:** Si en el futuro se necesita access tracking, se puede hacer una **v2** con schema migration (ENG-416 prerequisite).

---

## Cómo probarlo

```bash
# 1. Build
dotnet build -c Release

# 2. Stats básico
./src/Engram.Cli/bin/Release/net10.0/engram stats
# → debe mostrar output formateado con Overview, By Type, Recent Activity, etc.

# 3. Stats JSON
./src/Engram.Cli/bin/Release/net10.0/engram stats --json
# → debe mostrar JSON válido

# 4. Stats con base de datos vacía
rm /tmp/test.db
ENGRAM_DATA_DIR=/tmp ./src/Engram.Cli/bin/Release/net10.0/engram stats
# → debe mostrar "No memories yet"

# 5. Tests
dotnet test tests/Engram.Store.Tests/ --filter "DetailedStats"
dotnet test tests/Engram.Postgres.Tests/ --filter "DetailedStats"
```

---

## Métricas de éxito

- **Performance**: `engram stats` corre en <2 segundos
- **Utilidad**: 30%+ de power users corren `engram stats` al menos 1 vez por semana
- **Claridad**: un nuevo usuario puede entender qué tiene en 30 segundos

---

## Dependencias

- ✅ Ninguna — se puede implementar inmediatamente (sin schema migration)
- 🔗 Relacionado: ENG-480 (quick-capture), ENG-481 (git hooks)
- 🔗 Future: ENG-416 (schema evolution) si se quiere agregar access tracking

---

## Referencias

- [ENGRAM-IDEA-006](/mnt/86FC44B0FC449BF5/Proyectos/Desarrollo/engram-feature-ideas.md#engram-idea-006--developer-facing-observability-engram-stats) — idea original
- [ENG-482 en BACKLOG](../../BACKLOG.md#eng-482)
