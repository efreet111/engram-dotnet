# HU-019 — Fix ENGRAM_SYNC_TARGET para perfil Desktop

**As**: Developer / usuario del perfil Desktop
**I want**: Que el perfil `Desktop` tenga `ENGRAM_SYNC_TARGET=desktop` (no `"cloud"`)
**To**: Reflejar la semántica correcta — Desktop es sync peer-to-peer desktop↔laptop, no un server centralizado tipo cloud

---

## Acceptance Criteria

### Code fix

- [ ] `src/Engram.Store/DeployProfile.cs:93` cambia `ENGRAM_SYNC_TARGET = "cloud"` → `"desktop"` para `DeployProfile.Desktop`
- [ ] `tests/Engram.Store.Tests/DeployProfileTests.cs` actualiza el test `For_Desktop_HasCorrectKeysAndValues` para esperar `"desktop"` en vez de `"cloud"`
- [ ] Tests pasan: `dotnet test -c Release --filter "FullyQualifiedName~DeployProfile"`

### Doc consistency

- [ ] `docs/DEPLOYMENT.md` líneas 155 y 318 ya dicen `"desktop"` — verificar que están correctas
- [ ] `docs/01-QUICK-START.md:295` — la tabla dice `desktop` Sync = **"No"** (INCORRECTO). Debe decir `"Yes"` o `"Desktop↔Laptop"`. Corregir.
- [ ] `docs/DEVELOPMENT.md:85` — tabla ENV no lista `ENGRAM_SYNC_TARGET` para desktop explícitamente. Agregar si es necesario.

---

## Tasks (Implementation)

- [ ] Editar `src/Engram.Store/DeployProfile.cs` línea 93: `"cloud"` → `"desktop"`
- [ ] Editar `tests/Engram.Store.Tests/DeployProfileTests.cs` línea 126: expected sea `"desktop"`
- [ ] Correr tests: `dotnet test tests/Engram.Store.Tests/ -c Release --filter "FullyQualifiedName~DeployProfile"`
- [ ] Editar `docs/01-QUICK-START.md` línea 295: Sync para `desktop` = `"No"` → `"Yes"` (o `"Desktop↔Laptop"`)
- [ ] Verificar `docs/DEPLOYMENT.md` líneas 155 y 318: valor ya es `"desktop"` (no cambiar)
- [ ] Actualizar `docs/BACKLOG.md` — marcar ENG-XXX como done si existe, o agregar línea HU-019

---

## Notes

- **Semántica**: `ENGRAM_SYNC_TARGET=cloud` implica sync a server centralizado de equipo. Desktop es peer-to-peer entre dos máquinas personales (desktop↔laptop). El target key `"desktop"` refleja la topología real.
- **Relación con HU-010/HU-012**: El bug nació cuando se copió el default de `OfflineFirst` a `Desktop` sin cambiar el target key.
- **T3**: Verificar con PostgreSQL + Docker si el sync funciona correctamente con `target=desktop`.
