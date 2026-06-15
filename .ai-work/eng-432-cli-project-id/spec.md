# ENG-432 — Spec: CLI `engram project id`

**Status:** Ready | **Priority:** P2 | **Effort:** S (~30 min) | **Origen:** ENG-410 audit

## Problem

No hay forma de ver el `project_id` desde la terminal. Para debuggear, el desarrollador tiene que abrir `.engram-id` con un editor. Tampoco hay forma de regenerarlo si se corrompió.

## Requirements

### REQ-432-001: Comando básico
```
engram project id
→ project_id: 00e340cd-ae42-5441-a0da-8117199da0a6
→ No project identity found.
```

### REQ-432-002: Output JSON
```
engram project id --json
→ {"project_id":"00e340cd-...","source":"file","computed":"00e340cd-..."}
→ {"project_id":null,"source":"none","computed":null}
```

Campos:
- `project_id`: GUID del archivo (o null)
- `source`: "file" (leyó de .engram-id), "computed" (calculado on-the-fly), "none" (sin git)
- `computed`: qué valor daría el cálculo determinista (para detectar mismatches)

### REQ-432-003: Regeneración
```
engram project id --regenerate
→ Regenerate project identity? This will overwrite .engram-id. [y/N]
→ Project identity regenerated: 00e340cd-...
```

Sin `-y`/`--yes`, pide confirmación interactiva.

## Scenarios

### Scenario 1: Proyecto con .engram-id
**Given:** `.engram-id` existe
**When:** `engram project id --json`
**Then:** `{"project_id":"...","source":"file"}`

### Scenario 2: Proyecto sin .engram-id
**Given:** git repo sin `.engram-id`
**When:** `engram project id --json`
**Then:** `{"project_id":null,"source":"none","computed":"..."}`

### Scenario 3: Regeneración
**Given:** `.engram-id` existe
**When:** `engram project id --regenerate -y`
**Then:** archivo sobreescrito con el mismo GUID (determinista)

## Files affected
- `src/Engram.Cli/Program.cs` — nuevo comando `project id`

## Out of scope
- Subcomandos para otros aspectos de proyecto
- Integración con `engram projects`

## Manual test
- PM-1: `engram project id` en engram-dotnet → muestra GUID
- PM-2: `engram project id` en /tmp → "No project identity found"
- PM-3: `engram project id --regenerate -y` → mismo GUID (determinista)
