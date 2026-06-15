# ENG-431 — Spec: Validación de consistencia del GUID

**Status:** Ready | **Priority:** P2 | **Effort:** S (~30 min) | **Origen:** ENG-410 audit

## Problem

Si alguien edita `.engram-id` manualmente (error humano, merge conflict mal resuelto), el GUID almacenado ya no coincide con el cálculo determinista `UUIDv5(remote_url, first_commit)`. El sistema usa el GUID editado sin advertir que es incorrecto. Los demás miembros del equipo mantienen el GUID original → divergencia de identidad.

## Requirements

### REQ-431-001: Validación al leer .engram-id
Al leer `.engram-id`, validar contra el cálculo determinista. Si no coincide:
- Log warning: `project_id mismatch: file has X, computed Y`
- El archivo manda (no sobreescribir)

### REQ-431-002: Modo estricto (CI)
Env var `ENGRAM_STRICT_PROJECT_ID=true` convierte el mismatch en error fatal (exit 1). Para CI/CD y pre-commit hooks.

## Scenarios

### Scenario 1: GUID coincide (normal)
**Given:** `.engram-id` = UUID v5 correcto
**When:** se lee el archivo
**Then:** sin warning, se usa el GUID

### Scenario 2: GUID editado manualmente
**Given:** `.engram-id` contiene "11111111-1111-1111-1111-111111111111" (no es el determinista)
**When:** se lee el archivo
**Then:** warning en log, se usa el GUID del archivo

### Scenario 3: Modo estricto
**Given:** `ENGRAM_STRICT_PROJECT_ID=true` y GUID no coincide
**When:** se lee el archivo
**Then:** error fatal, exit 1

## Files affected
- `src/Engram.Store/ProjectIdentity.cs` — método `Validate(string guidFromFile)`

## Out of scope
- Auto-corregir el GUID (sería peligroso)
- Sincronizar GUIDs entre miembros

## Manual test
- PM-1: Editar `.engram-id` a mano, correr `engram mcp` → warning en log
