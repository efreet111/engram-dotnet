# Proposal: Requirement Traceability — Source & Lineage Tracking

## Intent

Hoy un spec.md describe qué construir pero no dice **por qué** se construye ni **de dónde** viene el requerimiento. Cuando un issue de GitHub, un bug report, o una decisión técnica genera un RF, esa conexión se pierde en el primer salto a spec.md.

Esto impide:
- Saber si un requisito sigue siendo relevante (porque el issue que lo originó se cerró o cambió)
- Rastrear el linaje de un requisito a través de ciclos de rework
- Relacionar requisitos entre sí (dependencias, reemplazos, conflictos)
- Que `mem_traceability` pueda verificar no solo cobertura sino vigencia

Esta propuesta introduce un campo de trazabilidad en spec.md y las tools MCP para preservar el linaje desde la fuente original hasta el código.

## Scope

### In Scope
- **Formato de trazabilidad** en spec.md: secciones Traceability con Source, Author, Date, Rationale, Relations
- **Tool MCP `mem_trace_source`**: persigue el origen de un RF/RNF, lo persiste en engram-dotnet con topic_key, y lo linkea al spec
- **Tool MCP `mem_lineage`**: dado un RF, retorna su linaje completo (fuente original → spec → reworks → código)
- **Persistencia**: observaciones con `topic_key: trace/{project}/{rf-id}` que mantienen el linaje
- **Relaciones entre requisitos**: `depends_on`, `supersedes`, `conflicts_with`, `related_to`
- **Actualización del formato spec.md** para incluir `## Traceability` como sección canónica

### Out of Scope
- Auto-detección de fuente (el Arch Agent declara la fuente, no se infiere automáticamente)
- Integración directa con GitHub/Jira API para fetch automático de issues (fase posterior)
- Verificación de que la fuente sigue activa (el humano decide en checkpoint)

## Capabilities

### New Capabilities
- `requirement-traceability`: Capacidad de rastrear el origen y linaje de requisitos desde la fuente original (issue, bug, decisión) hasta el código, con persistencia en engram-dotnet y tools MCP para consulta.

### Modified Capabilities
- `artifact-verification`: `mem_traceability` debe extenderse para incluir verificación de vigencia de fuente (¿el issue que originó este RF sigue abierto?).

## Approach

1. Definir formato canónico de trazabilidad en spec.md:

```markdown
## Traceability

### RF-001: Unicode email validation
- **Source**: GITHUB-ISSUE-42
- **Author**: Support Team
- **Date**: 2026-05-14
- **Rationale**: Users with ñ/ü/é in emails cannot register
- **Relations**: Supersedes RF-003 (email validation was too strict)

### RF-002: Duplicate email → 409
- **Source**: GITHUB-ISSUE-42 (same issue, separate requirement)
- **Rationale**: UX requirement — user needs to know email exists
- **Relations**: Depends on RF-001
```

2. Persistir cada RF/RNF con fuente como observación en engram-dotnet:
```bash
mem_save(
  title="RF-001: Unicode email validation",
  type="requirement",
  topic_key="trace/my-project/rf-001",
  content="**Source**: GITHUB-ISSUE-42 | **Rationale**: Unicode emails..."
)
```

3. Implementar `mem_trace_source(rf_id)` que:
   - Busca observación con `topic_key: trace/{project}/{rf_id}`
   - Retorna fuente, autor, fecha, rationale, relaciones
   - Si no existe, reporta "untraced"

4. Implementar `mem_lineage(rf_id)` que:
   - Busca el RF en la DB
   - Sigue relaciones transitivas (depends_on → supersedes → etc.)
   - Construye árbol de linaje completo

5. Actualizar `mem_traceability` para incluir verificación de vigencia:
   - ¿La fuente del RF sigue activa?
   - ¿El RF fue reemplazado por otro?

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `docs/01-engramflow-architecture.md` | Modified | Formato de spec.md actualizado con sección Traceability |
| `src/Engram.Mcp/EngramTools.cs` | Modified | Tools `mem_trace_source`, `mem_lineage` |
| `src/Engram.Verification/TraceabilityMatrix.cs` | Modified | Extender para verificar vigencia de fuente |
| `src/Engram.Verification/` | Modified | Nuevos modelos para trace entries + relations |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| El humano no completa la fuente → RFs sin tracear | Med | `mem_trace_source` reporta "untraced" visiblemente; el Verify Agent puede rechazar specs sin trazabilidad |
| Relaciones cíclicas (A depende de B, B depende de A) | Baja | Cycle detection en `mem_lineage` con límite de profundidad (max 10 hops) |
| Source enum se queda corto | Baja | `Source` es string libre, no enum — el humano escribe lo que corresponda |

## Rollback Plan

- La trazabilidad es puramente aditiva: no modifica spec.md existente (solo nuevos specs la incluyen)
- Tools `mem_trace_source` y `mem_lineage` son read-only
- Remover: desregistrar tools de MCP config

## Dependencies

- `promotion-level2`: para persistir traces como observaciones (aplica mismo patrón)
- `verification-tools`: `mem_traceability` se extiende para usar trazabilidad

## Success Criteria

- [ ] spec.md con `## Traceability` se considera canónico y el spec parser lo reconoce
- [ ] `mem_trace_source(RF-001)` retorna fuente, autor, rationale y relaciones
- [ ] `mem_lineage(RF-001)` retorna árbol de linaje (source → spec → reworks → code)
- [ ] Si un RF no tiene trace, `mem_trace_source` reporta "untraced"
- [ ] `mem_traceability` incluye warning si la fuente del RF ya no está activa
- [ ] Detección de ciclos en relaciones (max 10 hops)
