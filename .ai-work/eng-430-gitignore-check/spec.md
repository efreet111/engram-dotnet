# ENG-430 — Spec: `.engram-id` en `.gitignore` + check

**Status:** Ready | **Priority:** P0 | **Effort:** S (~30 min) | **Origen:** ENG-410 audit

## Problem

Si `.engram-id` está listado en el `.gitignore` del proyecto (por accidente o por política), el archivo NUNCA se commitea ni se comparte con el equipo vía git. Cada miembro termina con un `.engram-id` diferente → identidades distintas para el mismo proyecto → memorias duplicadas/aisladas en sync.

El sistema actual no advierte sobre esta condición. Es un **bloqueo silencioso** del sync colaborativo: el equipo cree que comparte identidad pero en realidad cada uno tiene la suya.

## Requirements

### REQ-430-001: `.gitignore` del template no ignora `.engram-id`
El `.gitignore` generado por FlowForge/FlowDocs NO DEBE contener `.engram-id`.

### REQ-430-002: `engram doctor` check
El comando `engram doctor` DEBE incluir un check que verifica:
- Si `.engram-id` existe en el repo
- Si `.engram-id` está en `.gitignore` (vía `git check-ignore`)
- Warning si está ignorado

### REQ-430-003: Documentación
- CONTRIBUTING.md: mencionar que `.engram-id` debe commitearse
- README: sección "Project Identity" con instrucción de commit

## Scenarios

### Scenario 1: .engram-id commiteado (normal)
**Given:** `.engram-id` existe y NO está en `.gitignore`
**When:** `engram doctor` corre
**Then:** check pasa sin warning

### Scenario 2: .engram-id ignorado (bloqueo)
**Given:** `.engram-id` existe pero está en `.gitignore`
**When:** `engram doctor` corre
**Then:** warning: `.engram-id is in .gitignore — team members may have different project identities`

### Scenario 3: Sin .engram-id (nuevo proyecto)
**Given:** No hay `.engram-id`
**When:** `engram doctor` corre
**Then:** info: `No .engram-id found. Run 'engram project id --generate' to create one.`

## Files affected
- Templates: `.gitignore` (FlowDocs/FlowForge)
- `src/Engram.Diagnostics/` — nuevo check en el doctor
- `docs/CONTRIBUTING.md` — documentación

## Out of scope
- Modificar `.gitignore` automáticamente
- Forzar commit del archivo

## Manual test
- PM-1: Agregar `.engram-id` a `.gitignore`, correr `engram doctor` → warning
- PM-2: Quitar `.engram-id` de `.gitignore`, correr doctor → sin warning
