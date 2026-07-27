---
observation_id: 27
type: "manual"
title: "engram-dotnet: FlowDoc v2.0 inspired enhancements — análisis deepen"
created_at: "2026-07-09 02:57:29"
topic_key: "engram-dotnet/flowdoc-v2-inspired-enhancements"
project: "team/flowforge"
scope: "team"
generated_at: "2026-07-21T22:00:59.5783108Z"
---

# engram-dotnet: FlowDoc v2.0 inspired enhancements — análisis deepen

**What**: Análisis profundo de las 4 mejoras originalmente propuestas, contrastadas contra el código real de engram-dotnet y los patrones reales de FlowDoc v2.0.

**Why**: Decidir qué llevar a ENG-XXX concretos antes del launch/next milestone. El reporteFlowDocs reveló que FlowDoc v2.0 fue un commit sobre main (no branch aparte), y que ADR-009 (sub-agent context pattern, deprecated pero conceptualmente rico) define un "Discovery block" format muy útil para un backend de memoria.

**Where**: Engram-dotnet repo (BACKLOG.md + Models.cs + Stores) / FlowDocs repo (ADR-009 + templates v2.0 + scripts flowdoc-migration.sh)

**Learned** (hallazgos clave que cambian la postura original):

1. **`hu_id` (#1) es conceptualmente redundante** con `topic_key` convención `trace/{project}/{hu-id}` (HU-005 ya estandarizó esto). Si se añade columna, justificarlo por **performance indexada** (`WHERE hu_id = 'HU-005'` indexado vs `LIKE` sobre topic_key), no por trazabilidad nueva.

2. **`tech_debt` type (#2) NO requiere migración**: `Type` es `string` libre sin enum C# ni CHECK constraint SQL. Cualquier string se acepta ya. Pero sin tocar `EngramTools.cs` (descripciones MCP), `RetentionConfig.cs` (TTL — sin entrada se podaría con TTL default), `Normalizers.cs:97-108` (InferTopicFamily para topic_key coherente), y `AutoClassifyScope` (team vs personal), el tipo "existe" pero los LLMs no lo emiten y retention lo elimina. ~S (1-2h), sin breaking.

3. **`test_ref`/`code_ref` (#3) tiene superposición con subsistema de traceability ya implementado** (HU-005 + ENG-404 memory relations tipo `depends_on` a observaciones sintéticas `artifact/{path}`). Decidir arquitectura primero: ¿campo plano o reuso del grafo? Sin FTS indexing es ~M; con FTS5 re-schema (no soporta alterar vt) es ~L.

4. **`status` lifecycle (#4) tiene superposición crítica con ENG-404 (hecho)** — `supersedes` ya existe como relación tipada en `MemoryRelation` + `mem_lineage_obs` (grafo, BFS). Dos modelos competirían: status booleano cacheado vs grafo. Mezclarlos = riesgo de divergencia. Además toca docenas de SELECTs en ambos stores (alto riesgo regresión). Enmarcar como **sub-feature de ENG-412+ENG-414 existentes**, no standalone.

5. **Flujos de migración**: NO hay `IMigration_00X.cs` versionado — todo aditivo en `Migrate()` con `AddColumnIfNotExists`. ENG-416 (migraciones versionadas) está Ready pero no implementado. La mejora #4 (backfill derivado de mem-rels) es el primer caso de uso que justifica sacar ENG-416 del Icebox.

6. **BACKLOG**: ENG-XXX más alto usado = ENG-453. Próximo libre = ENG-454.

7. **ROADMAP**: meta release original "finales junio 2026" (v1.3.0 DONE 2026-07-06). El próximo macro-bloque "Meta v1.1 — memoria semántica avanzada" (ENG-412-418, todos P2/Ready) es donde caen naturalmente estas mejoras. No son features del launch pasado, son features v1.1.

8. **Patrones FlowDoc v2.0 adicionales aprovechables** (no en las 4 originales):
   - **Discovery block format** (ADR-009, deprecated pero rico): `agent`, `category` (vocab cerrado: adr-applicable | convention | pattern | workaround | reference), `summary` (max 200 chars), `details_ref` (inline | engram:topic_key), `details`. энерг-dotnet ya tiene type=discovery (TTL 60d), pero no tiene convención de content schema. Aprovechable sin tocar schema.
   - **45-line switch rule**: hot inline (≤50 lines) vs cold engram store — tiered memory. Útil para forge-memory/forge-verify context-file generation.
   - **R2 Knowledge propagation HU→HU**: `Related HU: HU-XXX` como edge + propagar discoveries. En engram ya existe `mem_relations` tipo `related_to` — solo falta convención de uso.
   - **Topic key namespace**: FlowDoc usa `sdd/{change-name}/{phase}`. Engram ya tiene `trace/{project}/{rf-id}` y `memrel/{project}/{obsId}`. Bien cubierto.
   - **docs/observaciones/ folder pattern** (v2.0 first-class): session summaries estructurados `SESSION-*-YYYY-MM-DD.md`. En engram-dotnet existe type=session_summary pero sin convención de carpeta/template para promotion to .md.
   - **Adopción gradual L1→L2→L3** — feature gating por nivel. Para engram-dotnet esto podría mapearse a Stage 1 (memory senza metadata) → Stage 2 (+ hu_id) → Stage 3 (+ lifecycle/refs).

**Clasificación sugerida para "salir este mes"** (julio 2026):
- **MUST (salir este mes)**: ENG-454 (`tech_debt` type, ~S, no-breaking) + ENG-455 (`hu_id` field, ~S-M, no-breaking). Cierran trazabilidad mínima.
- **Calidad de vida v1.1**: ENG-456 (`test_ref`/`code_ref`, resolver superposición con traceability).
- **No standalone**: ENG-457 (status lifecycle) — enmarcar como sub-story de ENG-412+ENG-414 existentes. **No meter como feature suelta.**
- **Bonus aprovechable FlowDoc v2.0** (cero schema, pura convención): subir un ADR-005 sobre Discovery block content schema y convención `related_to` para propagate discoveries HU→HU.
