# Context Map: Obsidian Memory Graph

**Fecha:** 2026-07-23
**Estado:** Pre-spec (análisis de viabilidad completado)
**Slug:** `obsidian-memory-graph`

---

## 1. Feature Summary

Exportar las memorias de engram-dotnet a Obsidian preservando las relaciones como `[[wiki-links]]`, para que Obsidian Graph View muestre un grafo coherente del conocimiento. Incluye auto-linking para densificar el grafo (hoy es 100% manual y muy disperso).

---

## 2. Current State

### Nodos (observaciones) ✅
- Modelo: `src/Engram.Store/Models.cs:40-65`
- Campos: `id`, `sync_id`, `session_id`, `type`, `title`, `content`, `project`, `scope`, `topic_key`, `revision_count`, `created_at`, `updated_at`, `deleted_at`, `md_path`
- FTS5 index: `title`, `content`, `tool_name`, `type`, `project`, `topic_key`

### Aristas (relaciones) ✅
- Modelo: `src/Engram.Verification/MemoryRelation.cs`
- Storage: Observaciones con `type="memory_relation"`, `topic_key="memrel/{project}/{obsId}"`, `content=JSON(MemoryRelationSet)`
- 4 tipos: `depends_on`, `supersedes`, `conflicts_with`, `related_to`
- Traversal: BFS con cycle detection en `MemoryLineageBuilder.cs`
- MCP: `mem_relations` (add/get/delete) + `mem_lineage_obs` (BFS lineage)
- CLI: `engram relations` + `engram lineage`

### Export Obsidian ✅ (parcial)
- Exporter: `src/Engram.Obsidian/Exporter.cs`
- Render: `src/Engram.Obsidian/MarkdownRenderer.cs`
- Hubs: `src/Engram.Obsidian/HubGenerator.cs` (session + topic hubs)
- **NO renderiza relaciones como wiki-links** — solo links a session/topic hubs
- **NO hay auto-linking** — 0 lógica de auto-linking en todo el codebase

### Problema central
El grafo exportado sería incoherente: 100% manual, densidad casi nula.

---

## 3. Related Artifacts

### RFCs / ADRs
- RFC-001: Project Identity (`.engram-id`)
- RFC-002: Multi-User Isolation (`personal:{user}` / `team`)
- RFC-003: Offline-First Sync Architecture
- ADR-002: Sync Mutation Application (server-side apply)

### ENGs relacionados en BACKLOG
- **ENG-465** — Obsidian export mejorado (templates, jerarquía, frontmatter, backlinks desde `mem_relations`, índice auto-generado). **Idea, P1, M effort.** ← Este es el padre directo.
- **ENG-469** — Memory consolidation (fusionar N memorias → 1 canónica). Usa `mem_relations` graph.
- **ENG-404** — Memory relations (grafo de observaciones). ✅ Done.
- **ENG-464** — Project Context Storage.

### Specs existentes
- `sdd/obsidian-export/spec.md` — spec original del export
- `.ai-work/eng-404-memory-relations/spec.md` — spec de relations

---

## 4. Dependencies & CVEs

Ninguna preocupación de seguridad específica. Dependencias relevantes:
- `Microsoft.Data.Sqlite` — storage local
- `Npgsql` — storage remoto
- `ModelContextProtocol` 1.3.0 — MCP server

No se introducen dependencias nuevas si el auto-linking es determinista (sin embeddings).

---

## 5. Compliance / Regulatory

- **PII en memorias**: Las observaciones pueden contener datos sensibles. El export a Obsidian ya existe y no introduce riesgo nuevo.
- **Data export**: El usuario controla su vault de Obsidian. No hay transmisión a terceros.
- Sin impacto regulatorio adicional.

---

## 6. Cost Implications

| Estrategia de auto-linking | Costo |
|---------------------------|-------|
| Por `topic_key` prefix | Gratis (string matching) |
| Por FTS5 keyword overlap | Gratis (usa index existente) |
| Por embeddings/vector similarity | Requiere API externa (Anthropic/OpenAI) o modelo local |
| Por sesión temporal | Gratis (ya tenemos session_id) |

**Recomendación:** Estrategia híbrida sin embeddings (topic_key + FTS5 + sesión). Costo cero.

---

## 7. Risks & Unknowns

| Riesgo | Impacto | Mitigación |
|--------|---------|------------|
| Grafo demasiado denso (ruido) | Usuario no puede navegar | Umbral configurable, links por tipo con peso |
| Grafo demasiado disperso | No aporta valor | Auto-linking híbrido (3 estrategias) |
| Conflictos con relaciones manuales | Duplicación | Auto-links marcados como `auto:true` en metadata |
| Performance en vaults grandes (>1000 notas) | Export lento | Incremental (ya existe `ExportSinceAsync`) |
| `topic_key` mal nombrados | Auto-linking por prefix falla | Fallback a FTS5 + sesión |

---

## 8. Open Questions for Human — PARCIALMENTE RESPONDIDAS

1. **¿Cuántas observaciones tenés hoy?** → **41 observaciones** (15 engram-dotnet, 10 team/engram-dotnet, 7 team/flowforge, 5+4 test). Grafo pequeño, MVP viable.
2. **¿Auto-linking en core o solo en export?** → Pendiente de decidir en spec.
3. **¿MVP o feature completa?** → Pendiente de decidir en spec.
4. **¿Dónde vive la feature?** → Pendiente de decidir en spec.

### Datos reales de la DB local (verificado 2026-07-24)

| Métrica | Valor |
|---------|-------|
| Observaciones totales | 41 |
| Topic keys únicos | 23 |
| Sesiones | 8 |
| Relaciones manuales | **1** (grafo casi vacío) |
| Proyectos | 5 (`engram-dotnet`, `team/engram-dotnet`, `team/flowforge`, `test/verify`, `test/verify-fix`) |

### Prefijos compartidos (oportunidades de auto-link)

```
architecture/*          → 3 observaciones
engram-dotnet/*         → 3 observaciones
flowdoc-v2*             → 2 observaciones
```

---

## 9. Recommendations

### Para Phase 1 (forge-arch):
1. **MVP primero**: Renderizar relaciones existentes como `[[wiki-links]]` en MarkdownRenderer
2. **Auto-linking por topic_key prefix** (determinista, sin IA)
3. **Marcar auto-links** con metadata en frontmatter (`auto_linked: true`)
4. **Extender el exporter existente** — no crear módulo nuevo
5. **No requiere embeddings** — mantener costo cero

### Alcance sugerido:
- MarkdownRenderer: agregar sección "Relations" con wiki-links
- Exporter: cargar relaciones al exportar (query a MemoryRelationRepository)
- Auto-linker: nuevo componente `AutoLinker.cs` con estrategia por topic_key
- Hub generator: incluir grafo de relaciones en topic hubs
- Tests: unit + integration (export con relaciones → verificar wiki-links)

---

## Memory Signal

**Session summary (2026-07-24):** Análisis de viabilidad completado. Se auditó el codebase completo (Models, Relations, Exporter, MCP tools). Se verificó que el MCP funciona y la DB tiene 41 observaciones con 23 topic_keys. Se identificó que el grafo es 100% manual (solo 1 relación) y muy disperso. Se propusieron 3 estrategias de auto-linking. Se creó ENG-474 en el backlog.

**Descubrimiento colateral:** Durante la verificación de sync se descubrió ENG-475 (idx_obs_dedupe overflow) que fue fixado en PR #22 (`62eca98`).

**Key decisions:**
- Feature slug: `obsidian-memory-graph`
- ENG: ENG-474 (en backlog, estado: Idea)
- Estrategia recomendada: híbrida sin embeddings (costo cero)
- MVP: renderizar relaciones + auto-link por topic_key

**ENG-476 propuesto:** Sync-on-demand — cuando el sync está habilitado pero nadie lo arranca, las memorias están desincronizadas. Idea: trigger sync cycle cuando el usuario hace búsqueda vía MCP/CLI.

**Next steps:**
1. Definir prioridad real (P1/P2) según roadmap
2. forge-arch genera spec.md
3. CKP-1: aprobación humana
4. forge-plan → forge-dev
