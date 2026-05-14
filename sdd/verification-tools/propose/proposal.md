# Proposal: Verification Tools — Artifact Compliance & Traceability

## Intent

Actualmente engram-dotnet solo persiste y recupera memoria. No entiende el contexto de desarrollo: no sabe qué es un spec.md, un plan.md, ni cómo verificar que el código cumple con requisitos funcionales o no-funcionales.

Para que EngramFlow pueda tener un Verify Agent que valide automáticamente que el código cumple lo pedido (y escriba rework_ticket.md cuando no), engram-dotnet necesita herramientas que entiendan la semántica de los artefactos de desarrollo.

Esta propuesta introduce tools MCP que permiten verificar compliance de código contra spec.md, trazar RF/RNF, y generar reportes estructurados de verificación.

## Scope

### In Scope
- Tool MCP `mem_verify_artifact`: toma spec.md + plan.md + diff de cambios → structured compliance report
- Tool MCP `mem_traceability`: toma spec.md con RF/RNF listados → verifica cobertura vs código
- Formato de reporte estructurado (JSON + markdown)
- Integración con ciclo count para rework_ticket.md
- Soporte básico para spec.md con secciones de RF y RNF identificables

### Out of Scope
- Parsing semántico profundo de spec.md arbitrarios (solo formato canónico EngramFlow)
- Auto-fix de código basado en reportes (el Dev Agent lo hace, no engram-dotnet)
- UI visual para reportes (solo JSON + markdown por ahora)

## Capabilities

### New Capabilities
- `artifact-verification`: Capacidad de verificar compliance de código contra spec.md, identificar RF/RNF no cubiertos, y generar reportes estructurados de verificación con formato canónico de rework_ticket.md.

### Modified Capabilities
- None (no cambia comportamiento existente de memoria)

## Approach

1. Definir formato canónico de spec.md que la tool puede parsear:
   - `## Objetivo` / `## Objective`
   - `## Functional Requirements` con items numerados como `- RF-NNN: ...`
   - `## Non-Functional Requirements` con items como `- RNF-NNN: ...`
2. Implementar `mem_verify_artifact` que:
   a. Lee spec.md y extrae RF/RNF list
   b. Lee plan.md y extrae task list
   c. Toma diff de cambios (o permite pasar file list)
   d. Para cada RF/RNF, evalúa si el código lo cubre (via LLM-as-Judge con Sonnet)
   e. Genera structured report: `{ passed: [...], failed: [...], untested: [...], coverage_pct }`
3. Implementar `mem_traceability` que:
   a. A partir de spec.md, genera matriz de trazabilidad RF/RNF → código
   b. Identifica RF/RNF sin cobertura en tests
   c. Output: structured traceability matrix
4. Formato de rework_ticket.md:
   ```markdown
   # Rework Ticket — Cycle {N}/{MAX}

   ## Failed Items
   - [ ] {RF-NNN}: {razón}
   - [ ] {RNF-NNNN}: {razón}

   ## Instructions
   {instrucciones del Verify Agent para el Dev Agent}
   ```
5. cycle_count tracking: cada vez que Verify Agent rechaza, se incrementa. A los 3 intentos, el reporte incluye `escalate: true` para escalar a humano.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `src/Engram.Mcp/EngramTools.cs` | Modified | Tools `mem_verify_artifact`, `mem_traceability` |
| `src/Engram.Verification/` | **New** | Lógica de verificación: spec parser, RF/RNF extractor, report generator |
| `src/Engram.Verification/SpecParser.cs` | **New** | Parser de spec.md canónico |
| `src/Engram.Verification/ArtifactVerifier.cs` | **New** | Lógica de verificación contra spec |
| `src/Engram.Verification/TraceabilityMatrix.cs` | **New** | Matriz de trazabilidad RF/RNF |
| `src/Engram.Verification/Models.cs` | **New** | VerificationReport, TraceabilityEntry, ReworkTicket |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| LLM-as-Judge da falsos positivos/negativos | Med | Reporte incluye reasoning del judge + confidence score. Humano decide en checkpoint ③. |
| spec.md con formato no canónico | Med | Parser tolerante: busca secciones RF/RNF por keywords. Si no encuentra, reporta "unparseable" |
| cycle_count se pierde entre sesiones | Baja | Se persiste como observación en DB con topic_key `cycle-count/{change-name}` |

## Rollback Plan

- Las tools son read-only: no modifican código ni spec. Solo generan reportes.
- Desactivar: remover tools de la configuración MCP
- cycle_count se resetea si se borra la observación correspondiente

## Dependencies

- spec.md debe seguir el formato canónico EngramFlow (secciones RF/RNF identificables)
- Uso de modelo Sonnet para LLM-as-Judge (configurable via env var `ENGRAM_VERIFICATION_MODEL`)

## Success Criteria

- [ ] `mem_verify_artifact` detecta RF no implementados en código
- [ ] `mem_verify_artifact` detecta RNF violados (ej: passwords en logs)
- [ ] `mem_traceability` genera matriz RF/RNF → file paths
- [ ] Reporte incluye confidence score por item
- [ ] cycle_count escala a humano después de 3 intentos fallidos
- [ ] spec.md con formato no canónico se reporta como "unparseable" sin crash
- [ ] 280 tests existentes siguen pasando (sin regresiones)
