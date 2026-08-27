# HU-020 — Doctor Perfil-Aware: Validación Diferenciada por ENGRAM_PROFILE

**As**: Developer / IT admin corriendo `engram doctor`
**I want**: Que `doctor` valide específicamente lo que corresponde al perfil activo (`local`, `remote-server`, `offline-first`, `desktop`)
**To**: Evitar falsos positivos (ej: Local falla en `http_server` check que no aplica) y falsos negativos (ej: Desktop no valida PG_CONNECTION)

---

## Acceptance Criteria

### Profile detection

- [ ] `engram doctor` lee `ENGRAM_PROFILE` del entorno y lo muestra en el output: `Profile: local` / `desktop`, etc.
- [ ] Si `ENGRAM_PROFILE` no está seteado, assume `local` y lo indica.

### Skip irrelevant checks per profile

| Check | local | remote-server | offline-first | desktop |
|-------|-------|---------------|--------------|---------|
| `http_server` | **SKIP** (no server URL) | **SKIP** (no server URL) | RUN | RUN |
| `sync_health` | **SKIP** (sync disabled) | **SKIP** (sync disabled) | RUN | RUN |
| `database` | RUN | RUN | RUN | RUN |
| `mcp_server` | RUN | RUN | RUN | RUN |
| `project_identity` | RUN | RUN | RUN | RUN |

### ProfileValidator integration

- [ ] `doctor` usa `ProfileValidator.Validate()` para validar vars requeridas según el perfil activo
- [ ] Muestra missing vars como warnings, no corta la ejecución
- [ ] Validaciones por perfil:

| Profile | Debe validar |
|---------|-------------|
| `local` | Ninguna env requerida |
| `remote-server` | `ENGRAM_PG_CONNECTION` presente y NO localhost |
| `offline-first` | `ENGRAM_SERVER_URL` + `ENGRAM_USER` |
| `desktop` | `ENGRAM_PG_CONNECTION` + `ENGRAM_SERVER_URL` + `ENGRAM_USER` |

### Output diferenciado

- [ ] Doctor output incluye: `Profile: X | Checks: Y/Z passed | Missing: [...]` (si hay)
- [ ] Si `http_server` o `sync_health` se skippean, lo indica: `[SKIPPED - not applicable for profile 'local']`

---

## Tasks (Implementation)

- [ ] Leer `src/Engram.Cli/Program.cs` (comando doctor, líneas ~1540-1582) — entender estructura actual
- [ ] Leer `src/Engram.Diagnostics/DiagnosticService.cs` — entender checks actuales
- [ ] Modificar `DiagnosticService` para aceptar `DeployProfile` y filtrar checks según la tabla above
- [ ] Modificar `Program.cs` para pasar `DeployProfile.FromEnvironment()` al `DiagnosticService`
- [ ] Mostrar `Profile: X` en el output de doctor
- [ ] Integrar `ProfileValidator.Validate()` en doctor (modo no-fatal, solo reporta missing)
- [ ] Tests: agregar tests para doctor con cada perfil — verificar qué checks se run/skip
- [ ] Correr T1/T2: `dotnet test -c Release --filter "FullyQualifiedName~Doctor"`

---

## Notes

- **Root cause**: `doctor` no sabe en qué perfil está corriendo — corre 5 checks para todos. ProfileValidator existe pero solo se usa en `OpenStore()` (corta si falta algo).
- **Relación con HU-010**: El sistema de perfiles (DeployProfile) fue implementado pero doctor no lo consume.
- **No romper comportamiento actual**: El output de doctor debe mantener compatibilidad — solo agregar info y cambiar SKIP vs FAIL.
- **T3**: Testear doctor con cada perfil en Docker + Postgres (perfil desktop/remote-server) y local (perfil local/offline-first).
