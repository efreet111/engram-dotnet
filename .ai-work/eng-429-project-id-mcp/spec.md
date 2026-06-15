# ENG-429 — Spec: Exponer `project_id` en `mem_current_project`

**Status:** Ready | **Priority:** P1 | **Effort:** S (~30 min) | **Origen:** ENG-410 audit

## Problem

`DetectionResult.ProjectId` ya existe en `ProjectDetector.cs` (implementado en ENG-410). El campo se calcula correctamente durante `DetectProjectFull()`. Pero el MCP tool `mem_current_project` en `EngramTools.cs:963-1034` NO incluye `project_id` en su respuesta JSON. El campo está en el modelo C# pero el wrapper lo ignora al serializar la respuesta.

Esto significa que el agente FlowForge (o cualquier MCP client) ve el nombre del proyecto (`project` string) pero NO su identidad estable (`project_id` GUID). El `.engram-id` existe en disco pero es invisible para el ecosistema.

## Requirements

### REQ-429-001: Incluir project_id en respuesta
El JSON de `mem_current_project` DEBE incluir `project_id` cuando `DetectionResult.ProjectId` no es null/empty.

```json
{
  "project": "engram-dotnet",
  "project_source": "git_remote",
  "project_id": "00e340cd-ae42-5441-a0da-8117199da0a6",
  "project_path": "/path/to/repo",
  "cwd": "/path/to/repo",
  ...
}
```

Cuando no hay identidad, `project_id` DEBE ser `null` (no string vacío).

### REQ-429-002: Backward compatible
Clientes MCP existentes que no esperan el campo NO DEBEN romperse. El campo es aditivo.

### REQ-429-003: Test
Test unitario que verifica:
- Con `.engram-id` presente → `project_id` tiene GUID válido
- Sin `.engram-id` ni git → `project_id` es null

## Scenarios

### Scenario 1: Proyecto con .engram-id
**Given:** cwd es `/path/to/engram-dotnet` con `.engram-id = "00e340cd-..."`
**When:** `mem_current_project()` se invoca
**Then:** response incluye `"project_id": "00e340cd-..."`

### Scenario 2: Proyecto sin .engram-id
**Given:** cwd es un git repo sin `.engram-id`
**When:** `mem_current_project()` se invoca
**Then:** response incluye `"project_id": null`

### Scenario 3: Sin git repo
**Given:** cwd es un directorio sin git
**When:** `mem_current_project()` se invoca
**Then:** response incluye `"project_id": null`

## Files affected
- `src/Engram.Mcp/EngramTools.cs` — línea ~963: agregar `project_id` al JSON

## Out of scope
- Cambiar el comportamiento de `project` string
- Exponer `project_id` en otros endpoints REST

## Manual test
- PM-1: `mem_current_project` desde engram-dotnet (con .engram-id) → ver `project_id`
- PM-2: `mem_current_project` desde /tmp (sin git) → `project_id: null`
