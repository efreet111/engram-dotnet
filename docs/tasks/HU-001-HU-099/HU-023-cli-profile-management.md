# HU-023 — CLI Profile Management (show / set)

**As**: Developer / IT admin que necesita cambiar el perfil de deployment sin reinstalar
**I want**: Ver el perfil activo y cambiarlo via comandos `engram profile show` y `engram profile set`
**To**: Evitar editar archivos .env a mano y tener un way declarativo de switchear entre profiles (local ↔ offline-first ↔ remote-server ↔ desktop)

---

## Acceptance Criteria

### `engram profile show`

- [ ] Muestra el perfil activo actual: `Profile: local`
- [ ] Muestra todas las variables efectivas (las que el perfil setea + las overrideadas por el usuario): `ENGRAM_DB_TYPE=sqlite`, `ENGRAM_SYNC_ENABLED=false`, etc.
- [ ] Indica cuáles variables fueron overrideadas vs. las que vienen del perfil por defecto
- [ ] Muestra el archivo de config activo (`~/.engram/.env` si existe, si no indica que usa env vars)
- [ ] Si `ENGRAM_PROFILE` no está seteado, dice `Profile: local (default)`

### `engram profile set <profile>`

- [ ] Genera (o sobreescribe) `~/.engram/.env` con el perfil elegido
- [ ] Solo setea las vars obligatorias del perfil (no sobreescribe vars que el usuario ya tiene overrideadas a menos que sean del perfil)
- [ ] Hace backup del `.env` anterior a `.env.bak`
- [ ] El comando es idempotente: si ya estás en ese perfil, lo indica y no hace nada
- [ ] Validación: si el perfil requiere vars que no existen, las lista y sugiere cómo setearlas

### Perfiles disponibles

- [ ] `engram profile set local` — SQLite, sync disabled
- [ ] `engram profile set offline-first` — SQLite + sync, requiere `ENGRAM_SERVER_URL`
- [ ] `engram profile set remote-server` — PostgreSQL, sync disabled, security gate localhost
- [ ] `engram profile set desktop` — PostgreSQL + sync, requiere `ENGRAM_SERVER_URL`

### Experiencia de uso

- [ ] `--json` flag para output machine-readable
- [ ] `--dry-run` flag que solo muestra qué cambiaría sin tocar archivos
- [ ] Help integrado: `engram profile --help` lista los subcomandos

---

## Tasks (Implementation)

- [ ] Leer `src/Engram.Cli/Program.cs` — entender estructura actual de comandos CLI
- [ ] Leer `src/Engram.Store/DeployProfile.cs` — entender `ProfileDefaults` y `ToLabel()`
- [ ] Leer `src/Engram.Store/StoreConfig.cs` — entender `StoreConfig.FromEnvironment()`
- [ ] Agregar comando `profileCmd` en `Program.cs` con subcomandos `show` y `set`
- [ ] `profile show`: lee `ENGRAM_PROFILE` + `StoreConfig.FromEnvironment()` y renderiza
- [ ] `profile set`: genera `~/.engram/.env` con vars del perfil
- [ ] Validar que ProfileValidator valida las vars requeridas antes de generar el .env
- [ ] Tests: `ProfileShowTests`, `ProfileSetTests`
- [ ] Correr T1: `dotnet test --filter "FullyQualifiedName~Profile"`

---

## Notes

- **Scope**: No incluye "interactive wizard" de selección de perfil — eso es HU-017/HU-302 (wizard gráfico). Esto es solo CLI declarative switching.
- **Archivo de config**: `~/.engram/.env` — mismo formato que `docker/.env`. Cuando existe, se carga antes que las env vars del sistema.
- **Relación con HU-020**: `engram profile show` reutiliza `DeployProfile.FromEnvironment()` y `ProfileValidator.GetMissingVariables()` que ya existen.
- **Idempotencia**: `profile set desktop` si ya está en desktop no sobreescribe — lo indica.
- **Flujo de trabajo esperado**:
  1. `engram profile show` → ve que está en `local`
  2. `engram profile set offline-first --dry-run` → ve qué cambiaría
  3. `engram profile set offline-first` → genera `.env`
  4. `engram profile show` → ahora muestra `offline-first`
