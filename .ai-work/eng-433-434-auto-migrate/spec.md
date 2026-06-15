# ENG-433 / ENG-434 — Specs: Auto-generación + Migración GUID

**Status:** Ready | **Priority:** P2 (433) / P1 v1.1 (434) | **Effort:** S (433) / L (434)

---

## ENG-433 — Auto-generación de `.engram-id` en startup

### Problem

Actualmente, `.engram-id` debe generarse manualmente. Si un nuevo miembro clona el repo antes de que el archivo exista, o si el archivo se pierde, el proyecto queda sin identidad hasta que alguien lo genere explícitamente.

### Requirements

**REQ-433-001:** Flag `ENGRAM_AUTO_ENROLL=true` (o `engram mcp --auto-enroll`) habilita la auto-generación al iniciar.

**REQ-433-002:** Por defecto OFF — no generar archivos sin consentimiento del usuario.

**REQ-433-003:** Al detectar git repo sin `.engram-id`:
- Calcular UUID v5 determinista
- Guardar `.engram-id`
- Log: `Generated project identity: 00e340cd-...`

**REQ-433-004:** No hacer commit automático (el usuario decide).

### Scenarios
1. `ENGRAM_AUTO_ENROLL=true` + git repo sin `.engram-id` → genera
2. `ENGRAM_AUTO_ENROLL=true` + `.engram-id` existe → no hace nada
3. Sin flag → nunca genera

### Files affected
- `src/Engram.Cli/Program.cs` — flag en startup
- `src/Engram.Mcp/EngramMcpServer.cs` — flag en MCP startup

### Out of scope
- Commit automático
- Push automático

---

## ENG-434 — Migración `project` string → GUID canónico (v1.1)

### Problem

Hoy el `project` string (nombre de carpeta) es la key canónica en el store. El `.engram-id` existe pero no se usa para almacenar/recuperar memorias. Si alguien renombra la carpeta, las memorias "desaparecen" porque la key cambió.

### Requirements (v1.1)

**REQ-434-001:** Store acepta `project_id` GUID como parámetro junto con `project` string (backward compat).

**REQ-434-002:** Search/save/context operan con GUID como canonical. `project` string pasa a ser metadata.

**REQ-434-003:** Migración automática: memorias existentes con `project` string se asocian al GUID al primer acceso.

**REQ-434-004:** Guía de migración para equipos con datos existentes.

### Dependencies
- **ENG-404** (memory relations) — necesario para referencias cross-project
- **ENG-410** (project identity) — YA IMPLEMENTADO ✅

### Scenarios
1. Usuario existente con datos → migración automática en primer acceso
2. Usuario nuevo → usa GUID desde el inicio
3. Renombrar carpeta → memorias se mantienen (GUID no cambia)

### Effort: L (~2 sesiones)
### Target: v1.1 (post-MVP)
