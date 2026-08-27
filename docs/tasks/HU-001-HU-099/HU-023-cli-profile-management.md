# HU-023 — CLI Profile Management (show / set)

**As**: Developer / IT admin que necesita cambiar el perfil de deployment sin reinstalar
**I want**: Ver el perfil activo y cambiarlo via comandos `engram profile show` y `engram profile set`
**To**: Evitar editar archivos .env a mano y tener un way declarativo de switchear entre profiles (local ↔ offline-first ↔ remote-server ↔ desktop)

---

## Acceptance Criteria

### `engram profile show`

- [x] Muestra el perfil activo actual: `Profile: local`
- [x] Muestra todas las variables efectivas (las que el perfil setea + las overrideadas por el usuario): `ENGRAM_DB_TYPE=sqlite`, `ENGRAM_SYNC_ENABLED=false`, etc.
- [x] Indica cuáles variables fueron overrideadas vs. las que vienen del perfil por defecto
- [x] Muestra el archivo de config activo (`~/.engram/.env` si existe, si no indica que usa env vars)
- [x] Si `ENGRAM_PROFILE` no está seteado, dice `Profile: local (default)`

### `engram profile set <profile>`

- [x] Genera (o sobreescribe) `~/.engram/.env` con el perfil elegido
- [x] Solo setea las vars obligatorias del perfil (no sobreescribe vars que el usuario ya tiene overrideadas a menos que sean del perfil)
- [x] Hace backup del `.env` anterior a `.env.bak`
- [x] El comando es idempotente: si ya estás en ese perfil, lo indica y no hace nada
- [x] Validación: si el perfil requiere vars que no existen, las lista y sugiere cómo setearlas

### Perfiles disponibles

- [x] `engram profile set local` — SQLite, sync disabled
- [x] `engram profile set offline-first` — SQLite + sync, requiere `ENGRAM_SERVER_URL`
- [x] `engram profile set remote-server` — PostgreSQL, sync disabled, security gate localhost
- [x] `engram profile set desktop` — PostgreSQL + sync, requiere `ENGRAM_SERVER_URL`
  > **Post-HU-024:** Desktop profile uses SQLite local store + sync (not PostgreSQL).

### Experiencia de uso

- [x] `--json` flag para output machine-readable
- [x] `--dry-run` flag que solo muestra qué cambiaría sin tocar archivos
- [x] Help integrado: `engram profile --help` lista los subcomandos

---

## Tasks (Implementation)

- [x] Leer `src/Engram.Cli/Program.cs` — entender estructura actual de comandos CLI
- [x] Leer `src/Engram.Store/DeployProfile.cs` — entender `ProfileDefaults` y `ToLabel()`
- [x] Leer `src/Engram.Store/StoreConfig.cs` — entender `StoreConfig.FromEnvironment()`
- [x] Agregar comando `profileCmd` en `Program.cs` con subcomandos `show` y `set`
- [x] `profile show`: lee `ENGRAM_PROFILE` + `StoreConfig.FromEnvironment()` y renderiza
- [x] `profile set`: genera `~/.engram/.env` con vars del perfil
- [x] Validar que ProfileValidator valida las vars requeridas antes de generar el .env
- [x] Tests: `ProfileShowTests`, `ProfileSetTests`
- [x] Correr T1: `dotnet test --filter "FullyQualifiedName~Profile"`

---

## Notes

- **Scope**: No incluye "interactive wizard" de selección de perfil — eso es HU-017/HU-302 (wizard gráfico). Esto es solo CLI declarative switching.
- **Archivo de config**: `~/.engram/.env` — mismo formato que `docker/.env`. Cuando existe, se carga antes que las env vars del sistema.
- **Relación con HU-020**: `engram profile show` reutiliza `DeployProfile.FromEnvironment()` y `ProfileValidator.GetMissingVariables()` que ya existen.
- **Idempotencia**: `profile set desktop` si ya está en desktop no sobreescribe — lo indica.
- **`.env` overwrite behavior**: `profile set` sobreescribe `~/.engram/.env` completo con
  las vars del perfil. Los overrides previos del usuario se preservan en `~/.engram/.env.bak`
  (backup automático antes de sobreescribir — ver `ProfileConfig.WriteProfile`).
- **Flujo de trabajo esperado**:
  1. `engram profile show` → ve que está en `local`
  2. `engram profile set offline-first --dry-run` → ve qué cambiaría
  3. `engram profile set offline-first` → genera `.env`
  4. `engram profile show` → ahora muestra `offline-first`

### Implementation Notes

> **sdd-apply completion (2026-08-19):** Implementation complete. `profile show` and
> `profile set` commands are live in `Program.cs:1619-1731` with `--json`, `--dry-run`,
> backup to `.env.bak`, idempotency check, and missing-vars validation. `ProfileConfig.cs`
> handles file I/O. Tests in `ProfileShowTests.cs` and `ProfileSetTests.cs`. The
> investigation below (marked "Post-dev review") reflects a prior state before the
> sdd-apply phase executed — it is preserved for historical context.

**Post-dev review (2026-08-19) — SUPERSEDED by sdd-apply:**
El feature YA fue implementado en la fase sdd-apply (ver nota de completion arriba).
El texto a continuación refleja el estado pre-implementación y se conserva como historial.

~~El feature NO fue implementado.~~ El commit `86ece96`
("feat: Add CLI profile management commands...") fue misleading — solo creó este
documento HU, agregó un test de backend (`GetMissingVariables_RemoteServerLocalhostPg`),
y modificó `scripts/install.sh`. No agregó ningún comando `profile` a `Program.cs`.
La implementación real llegó después con sdd-apply.

**Lo que SÍ existe (foundation de HU-012/HU-020, no de HU-023):**

- `src/Engram.Store/DeployProfile.cs` — enum `DeployProfile` (Local, RemoteServer,
  OfflineFirst, Desktop), `FromEnvironment()`, `ToLabel()`, `ProfileDefaults.For()`,
  `ProfileValidator.GetMissingVariables()` / `Validate()`.
- `src/Engram.Store/StoreConfig.cs:111` — `FromEnvironment()` con merge pattern de
  3 capas: `explicit env var > profile default > hardcoded default`.
- `src/Engram.Cli/Program.cs:91` — `serve` muestra `Profile: {label}` al arranque.
- `src/Engram.Cli/Program.cs:1551-1565` — `doctor` muestra `Profile: {label}` +
  missing vars warnings (HU-020).
- `tests/Engram.Store.Tests/DeployProfileTests.cs` — 345 líneas de tests del backend
  (DeployProfileExtensions, ProfileDefaults, ProfileValidator). No hay
  `ProfileShowTests` ni `ProfileSetTests`.

**Lo que NO existe (todo HU-023):**

- No hay comando `profile` registrado en el root command (`Program.cs:1742-1760`
  lista 19 comandos — ninguno es `profile`).
- No hay subcomandos `profile show` ni `profile set`.
- No hay lógica de escritura de `~/.engram/.env`.
- No hay backup `.env.bak`.
- No hay `--dry-run` ni `--json` flags para profile.
- No hay idempotency check.
- No hay `ProfileShowTests` ni `ProfileSetTests`.

### Deviations from Plan

- **Commit misleading**: `86ece96` dice "Add CLI profile management commands" pero
  no agregó ningún comando CLI. Solo creó el HU doc + 1 test de backend + install.sh
  changes. Esto genera confusión en el histórico de git.
- **Backlog state**: HU-023 sigue marcado como "Ready" (no "Done" ni "In Progress")
  en `docs/BACKLOG.md:102`, consistente con la falta de implementación.
- **Foundation ya cubierta**: Las tasks de "leer" (1-3) sí se completaron — el
  codebase tiene el backend de perfiles. Pero las tasks de implementación (4-9)
  no se tocaron.

### New Technical Decisions

*(Ninguna — el feature no fue implementado, no hay decisiones técnicas nuevas de
HU-023. El foundation de profiles tiene ADR-012 para localhost blocking y
ADR-011 para ENGRAM_SERVER_URL, pero el merge pattern de 3 capas en sí no tiene
ADR — eso es un gap de HU-012, no de HU-023.)*
