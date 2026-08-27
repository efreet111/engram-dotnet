# HU-022 — Fix install.sh: descarga libe_sqlite3.so para Linux

**As**: Usuario instalando engram en Linux
**I want**: Que `install.sh` descargue la librería nativa SQLite (`libe_sqlite3.so`) y cree los symlinks necesarios (`e_sqlite3.so`)
**To**: Evitar `DllNotFoundException: Unable to load shared library 'e_sqlite3'` tras la instalación

---

## Acceptance Criteria

- [x] `scripts/install.sh` función `install_release()` descarga `libe_sqlite3.so` alongside el binary
- [x] `scripts/install.sh` crea symlinks: `e_sqlite3.so -> libe_sqlite3.so` y `libe_sqlite3.so` en `~/.local/bin/`
- [x] `scripts/test-install-wizard.sh` verifica que los symlinks existen post-instalación
- [x] El release URL `https://github.com/efreet111/engram-dotnet/releases/download/{version}/libe_sqlite3.so` funciona (verificado)

---

## Tasks (Implementation)

- [x] Auditar `scripts/install.sh` — función `install_release()` (líneas ~536-548)
- [x] Modificar `install_release()` para:
  1. Descargar `libe_sqlite3.so` desde el release
  2. Guardarlo en `~/.local/bin/libe_sqlite3.so`
  3. Crear symlink: `~/.local/bin/e_sqlite3.so -> ~/.local/bin/libe_sqlite3.so`
- [x] Agregar test en `scripts/test-install-wizard.sh` que verifique:
  - `libe_sqlite3.so` existe en `~/.local/bin/` (o donde se instale)
  - `e_sqlite3.so` existe como symlink
- [x] Verificar que el URL del release existe: `curl -I https://github.com/efreet111/engram-dotnet/releases/download/v1.3.0/libe_sqlite3.so`
- [ ] Probar la instalación en Linux (si es posible)

---

## Notes

- **Causa raíz**: `install_release()` solo hacía `curl ... -o "$ENGRAM_CMD"` (el binary). El release en GitHub tiene `libe_sqlite3.so` como asset separado, pero el installer no lo descargaba.
- **Flujo del error**: `Microsoft.Data.Sqlite` → `SQLitePCLRaw` → busca `e_sqlite3.so` Y `libe_sqlite3.so`. Ninguno existía → `DllNotFoundException`.
- **FlowForge**: El installer de FlowForge también tiene el bug (lista `libe_sqlite3.so` en TARGETS pero no lo descarga). Este HU solo arregla `scripts/install.sh` de engram-dotnet. FlowForge es repo separado.
- **macOS**: macOS usa `.dylib` en vez de `.so`. Need verificar si el binary de macOS ya incluye la librería o si也需要 fix.
