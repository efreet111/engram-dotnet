# RFC Template

> Request for Comments — Proposal for discussion

## Template Fields

| Campo | Descripción |
|-------|-------------|
| **Status** | `Draft` / `In Review` / `Accepted` / `Rejected` / `Obsolete` |
| **Date** | YYYY-MM-DD de creación |
| **Author** | Nombre del autor |
| **Source** | PRD, HU, o decisión que originó este RFC |

---

## Required Sections

### 1. Problem

Describe el problema que este RFC busca resolver. Incluye:
- Situación actual
- Limitaciones o issues del enfoque actual
- Por qué es necesario un cambio

### 2. Solution

Propone la solución. Incluye:
- Arquitectura propuesta
- Componentes afectados
- Flujos de datos
- Interacciones entre servicios

### 3. Open Questions

Preguntas abiertas que necesitan resolución antes de aceptar el RFC.

### 4. Alternatives Considered

Alternativas evaluadas con pros/contras.

---

## Status Lifecycle

```
Draft → In Review → Accepted
                  ↘ Rejected
                  ↘ Obsolete
```

---

## Naming Convention

```
RFC-NNN-title-in-kebab-case.md
```

Ejemplos:
- `RFC-001-project-identity.md`
- `RFC-002-multi-user-isolation.md`
- `RFC-003-offline-first-sync.md`

---

## Notes

- RFCs son para decisiones __antes__ de implementar
- ADRs son para decisiones __ya tomadas__
- Un RFC aceptado debería generar un ADR o una HU
