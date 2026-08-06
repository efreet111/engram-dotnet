# Deployment Manual — engram-dotnet

> **Última actualización:** 2026-08-06
> **Versión mínima:** v0.4.0 (para Deployment Profiles)
> **Related:** [HU-010](tasks/HU-001-HU-099/HU-010-deploy-profile-system.md) · [ADR-011](architecture/adr/ADR-011-engram-url-env-var.md)

---

## Profiles Overview

El sistema de Deployment Profiles (`ENGRAM_PROFILE`) permite configurar engram-dotnet con un solo entorno variable en lugar de 10+.

| Profile | Backend | Sync | Caso de uso | Requisitos |
|---------|---------|------|-------------|------------|
| `local` | SQLite | ❌ | Dev individual, sin compartir | Ninguno |
| `remote-server` | PostgreSQL | ❌ | Equipo pequeño (2-5), DB compartida | `ENGRAM_PG_CONNECTION`, `ENGRAM_USER` |
| `offline-first` | PostgreSQL | ✅ | Equipo grande (5-20), offline-first | `ENGRAM_PG_CONNECTION`, `ENGRAM_SERVER_URL`, `ENGRAM_USER` |
| `desktop` | PostgreSQL | ✅ | Desktop↔laptop sync con PostgreSQL local | `ENGRAM_PG_CONNECTION`, `ENGRAM_SERVER_URL`, `ENGRAM_USER` |

### Cuándo usar cada profile

- **`local`**: Vos solo, no necesitás compartir memorias, querés máxima simplicidad
- **`remote-server`**: Equipo chico con PostgreSQL existente (ej: TrueNAS), acceso directo por HTTP
- **`offline-first`**: Equipo mediano/grande, cada dev tiene PostgreSQL local + SyncManager, offline-first
- **`desktop`**: Usuario con desktop y laptop, ambas con PostgreSQL, sync bidireccional entre máquinas

---

## Quick Start

### Usando Docker Compose

```bash
# 1. Copiar el template
cp docker/.env.example docker/.env

# 2. Editar según tu profile
nano docker/.env

# 3. Levantar
cd docker
docker compose up -d

# 4. Verificar
curl http://localhost:7437/health
```

#### Ejemplo: profile `offline-first`

```bash
# docker/.env
ENGRAM_PROFILE=offline-first
ENGRAM_DB_MODE=external
ENGRAM_PG_HOST=192.168.1.100
ENGRAM_PG_PORT=5432
ENGRAM_PG_DATABASE=engram
ENGRAM_PG_USER=engram
ENGRAM_PG_PASSWORD=tu_password_seguro
ENGRAM_SERVER_URL=http://server:7437
ENGRAM_USER=tu_nombre
```

#### Profile `remote-server` con PostgreSQL embebido

```bash
# docker/.env
ENGRAM_PROFILE=remote-server
ENGRAM_DB_MODE=embedded
ENGRAM_PG_PASSWORD=tu_password_seguro
ENGRAM_USER=tu_nombre

# Levantar (incluye PostgreSQL como servicio)
cd docker
docker compose -f docker-compose.embedded.yml up -d
```

### Deploy script

El script `scripts/deploy.sh` automatiza el workflow de Docker:

```bash
# Validar config antes de levantar
./scripts/deploy.sh validate

# Levantar
./scripts/deploy.sh start

# Ver logs
./scripts/deploy.sh logs -f

# Estado
./scripts/deploy.sh status

# Recreate (backup automático)
./scripts/deploy.sh recreate

# Backup manual
./scripts/deploy.sh backup

# Update a nueva versión
./scripts/deploy.sh update
```

---

## Manual Deployment

### Environment variables reference

#### Profile `local` (SQLite)

| Variable | Default | Descripción |
|----------|---------|-------------|
| `ENGRAM_PROFILE` | `local` | Profile a usar |
| `ENGRAM_DB_TYPE` | `sqlite` | Backend (auto-set por profile) |
| `ENGRAM_SYNC_ENABLED` | `false` | Sync enabled (auto-set por profile) |
| `ENGRAM_DATA_DIR` | `~/.engram` | Directorio de datos |
| `ENGRAM_PORT` | `7437` | Puerto del servidor |

#### Profile `remote-server` (PostgreSQL, sin sync)

| Variable | Default | Descripción |
|----------|---------|-------------|
| `ENGRAM_PROFILE` | — | Obligatorio: `remote-server` |
| `ENGRAM_DB_TYPE` | `postgres` | Backend (auto-set por profile) |
| `ENGRAM_SYNC_ENABLED` | `false` | Sync disabled (auto-set por profile) |
| `ENGRAM_PG_CONNECTION` | — | **Obligatorio**: connection string de PostgreSQL |
| `ENGRAM_USER` | — | **Obligatorio**: identificador del dev |
| `ENGRAM_PORT` | `7437` | Puerto del servidor |
| `ENGRAM_JWT_SECRET` | — | Opcional: secreto para JWT |
| `ENGRAM_CORS_ORIGINS` | — | Opcional: orígenes CORS separados por coma |

#### Profile `offline-first` (PostgreSQL + SyncManager)

| Variable | Default | Descripción |
|----------|---------|-------------|
| `ENGRAM_PROFILE` | — | Obligatorio: `offline-first` |
| `ENGRAM_DB_TYPE` | `postgres` | Backend (auto-set por profile) |
| `ENGRAM_SYNC_ENABLED` | `true` | Sync enabled (auto-set por profile) |
| `ENGRAM_SYNC_POLL_SECONDS` | `30` | Intervalo de sync (auto-set por profile) |
| `ENGRAM_SYNC_TARGET` | `cloud` | Target key (auto-set por profile) |
| `ENGRAM_PG_CONNECTION` | — | **Obligatorio**: connection string de PostgreSQL |
| `ENGRAM_SERVER_URL` | — | **Obligatorio**: URL del remote-server |
| `ENGRAM_USER` | — | **Obligatorio**: identificador del dev |
| `ENGRAM_PORT` | `7437` | Puerto del servidor |

#### Profile `desktop` (Desktop↔Laptop sync con PostgreSQL local)

| Variable | Default | Descripción |
|----------|---------|-------------|
| `ENGRAM_PROFILE` | — | Obligatorio: `desktop` |
| `ENGRAM_DB_TYPE` | `postgres` | Backend (auto-set por profile) |
| `ENGRAM_SYNC_ENABLED` | `true` | Sync enabled (auto-set por profile) |
| `ENGRAM_SYNC_POLL_SECONDS` | `30` | Intervalo de sync (auto-set por profile) |
| `ENGRAM_SYNC_TARGET` | `desktop` | Target key (auto-set por profile) |
| `ENGRAM_PG_CONNECTION` | — | **Obligatorio**: connection string de PostgreSQL local |
| `ENGRAM_SERVER_URL` | — | **Obligatorio**: URL de la otra máquina (desktop↔laptop) |
| `ENGRAM_USER` | — | **Obligatorio**: identificador del dev |
| `ENGRAM_PORT` | `7437` | Puerto del servidor |

### Merge precedence

El sistema de configuración sigue esta prioridad:

```
explicit env var > profile default > hardcoded default
```

**Ejemplo**: Si `ENGRAM_PROFILE=offline-first` y seteas `ENGRAM_DB_TYPE=sqlite`:
- El resultado es `sqlite` (explicit env > profile default)

**Ejemplo**: Si `ENGRAM_PROFILE=local` sin setear nada:
- `ENGRAM_DB_TYPE=sqlite` (profile default)
- `ENGRAM_SYNC_ENABLED=false` (profile default)

### Validation: fail-fast

`ProfileValidator.Validate()` corre al inicio y falla con mensaje claro si faltan variables obligatorias:

```
InvalidOperationException: Configuration requires: ENGRAM_PG_CONNECTION, ENGRAM_SERVER_URL. Set them in docker/.env or environment.
```

Para validar sin levantar el servidor:

```bash
./scripts/deploy.sh validate
```

---

## Profile Reference

### `local`

**Caso de uso**: Dev individual, no hay sharing, no hay offline.

**Variables**:

```bash
ENGRAM_PROFILE=local           # O omitir (es el default)
ENGRAM_DATA_DIR=~/.engram     # Opcional, default ya es ~/.engram
ENGRAM_PORT=7437              # Opcional
```

**Requirements**: Ninguno.

**Docker Compose**:

```bash
ENGRAM_PROFILE=local docker compose up -d
# o simplemente:
docker compose up -d
```

---

### `remote-server`

**Caso de uso**: Equipo 2-5, PostgreSQL compartido (ej: TrueNAS SCALE), acceso directo por HTTP, no offline-first.

**Variables**:

```bash
ENGRAM_PROFILE=remote-server
ENGRAM_DB_TYPE=postgres
ENGRAM_SYNC_ENABLED=false
ENGRAM_PG_CONNECTION=Host=my-server;Port=5432;Database=engram;Username=engram;Password=REPLACE_ME
ENGRAM_USER=tu_nombre
```

**Requirements**:
- `ENGRAM_PG_CONNECTION`: Connection string de PostgreSQL
- `ENGRAM_USER`: Tu identificador para namespacing

**Docker Compose con PostgreSQL embebido**:

```bash
ENGRAM_PROFILE=remote-server
ENGRAM_DB_MODE=embedded
ENGRAM_PG_PASSWORD=REPLACE_ME
ENGRAM_USER=tu_nombre

docker compose -f docker-compose.embedded.yml up -d
```

**Docker Compose con PostgreSQL externo (TrueNAS)**:

```bash
ENGRAM_PROFILE=remote-server
ENGRAM_DB_MODE=external
ENGRAM_PG_HOST=192.168.1.100
ENGRAM_PG_PORT=5432
ENGRAM_PG_DATABASE=engram
ENGRAM_PG_USER=engram
ENGRAM_PG_PASSWORD=REPLACE_ME
ENGRAM_USER=tu_nombre

docker compose up -d
```

---

### `offline-first`

**Caso de uso**: Equipo 5-20, offline-first, cada dev tiene PostgreSQL local + SyncManager, las memorias se sincronizan cuando hay conexión.

**Variables**:

```bash
ENGRAM_PROFILE=offline-first
ENGRAM_DB_TYPE=postgres
ENGRAM_SYNC_ENABLED=true
ENGRAM_SYNC_POLL_SECONDS=30
ENGRAM_SYNC_TARGET=cloud
ENGRAM_PG_CONNECTION=Host=my-server;Port=5432;Database=engram;Username=engram;Password=REPLACE_ME
ENGRAM_SERVER_URL=http://server:7437
ENGRAM_USER=tu_nombre
```

**Requirements**:
- `ENGRAM_PG_CONNECTION`: Connection string del servidor PostgreSQL central
- `ENGRAM_SERVER_URL`: URL del remote-server (donde corre `engram serve` en modo remote-server)
- `ENGRAM_USER`: Tu identificador

**Variables sync-specific**:

| Variable | Default | Descripción |
|----------|---------|-------------|
| `ENGRAM_SYNC_POLL_SECONDS` | `30` | Cada cuántos segundos corre el sync cycle |
| `ENGRAM_SYNC_TARGET` | `cloud` | Clave del target de sync (para multi-server) |

**Docker Compose (PostgreSQL embebido para offline-first)**:

```bash
ENGRAM_PROFILE=offline-first
ENGRAM_DB_MODE=embedded
ENGRAM_PG_PASSWORD=REPLACE_ME
ENGRAM_SERVER_URL=http://server:7437
ENGRAM_USER=tu_nombre

docker compose -f docker-compose.embedded.yml up -d
```

---

### `desktop`

**Caso de uso**: Usuario con desktop y laptop, ambas máquinas con PostgreSQL local, sync bidireccional entre ellas (ej: desktop en casa, laptop en el trabajo conectando a desktop via VPN).

**Variables**:

```bash
ENGRAM_PROFILE=desktop
ENGRAM_DB_TYPE=postgres
ENGRAM_SYNC_ENABLED=true
ENGRAM_SYNC_POLL_SECONDS=30
ENGRAM_SYNC_TARGET=desktop
ENGRAM_PG_CONNECTION=Host=localhost;Port=5432;Database=engram;Username=engram;Password=REPLACE_ME
ENGRAM_SERVER_URL=http://<ip-de-la-otra-maquina>:7437
ENGRAM_USER=tu_nombre
```

**Requirements**:
- `ENGRAM_PG_CONNECTION`: Connection string de PostgreSQL local
- `ENGRAM_SERVER_URL`: URL de la otra máquina (la que actúa como remote-server)
- `ENGRAM_USER`: Tu identificador (mismo en ambas máquinas)

**Importante**: El profile `desktop` asume que una de las dos máquinas actúa como remote-server. Ejecutá `engram serve` en la máquina que actúa como servidor.

**Docker Compose (desktop como sync client)**:

```bash
ENGRAM_PROFILE=desktop
ENGRAM_DB_MODE=embedded
ENGRAM_PG_PASSWORD=REPLACE_ME
ENGRAM_SERVER_URL=http://<ip-de-la-otra-maquina>:7437
ENGRAM_USER=tu_nombre

docker compose -f docker-compose.embedded.yml up -d
```

---

## Docker Deployment

### `docker/docker-compose.yml`

Compose principal para PostgreSQL externo (TrueNAS, servidor existente).

```bash
cd docker
cp .env.example .env
# editar .env con tus credenciales
docker compose up -d --build
```

Usa `host.docker.internal` para conectar al PostgreSQL del host.

### `docker/docker-compose.embedded.yml`

Compose para desarrollo local o deploys que quieren PostgreSQL auto-gestionado.

```bash
cd docker
cp .env.example .env
# editar .env
docker compose -f docker-compose.embedded.yml up -d --build
```

Incluye el servicio `postgres:` con su propio volumen `pgdata`.

### `docker/.env.example`

Template completo con todas las variables. Para crear tu `.env`:

```bash
cp docker/.env.example docker/.env
nano docker/.env
```

### `scripts/deploy.sh` — 10 subcommands

```bash
./scripts/deploy.sh <command>

Commands:
  start      Levanta el contenedor (docker compose up -d [--build] o --image)
  stop       Detiene el contenedor (docker compose stop)
  remove     Elimina contenedores y redes (docker compose down)
  recreate   Recrea desde cero (stop + remove + start)
  logs       Ver logs (add -f para tail)
  status     Muestra estado del contenedor
  restart    Reinicia el contenedor
  validate   Valida vars de entorno antes de deploy
  backup     Backup de datos antes de recreate/update
  update     Pull nueva imagen + recreate

Options:
  --profile  Deployment profile: local (default), remote-server, offline-first, desktop
  --image    Usa imagen pre-compilada de GHCR en vez de build local
```

**Ejemplos**:

```bash
# Levantar con build local
./scripts/deploy.sh start

# Levantar con imagen pre-compilada
./scripts/deploy.sh start --image

# Profile específico
./scripts/deploy.sh start --profile offline-first

# Validar antes de levantar
./scripts/deploy.sh validate

# Ver logs en vivo
./scripts/deploy.sh logs -f

# Backup antes de update
./scripts/deploy.sh backup

# Update a nueva versión
./scripts/deploy.sh update
```

---

## Troubleshooting

### "Configuration requires: ENGRAM_PG_CONNECTION"

Falta la connection string de PostgreSQL. Verificá que esté seteada en `.env`:

```bash
grep ENGRAM_PG_CONNECTION docker/.env
```

Si usás `ENGRAM_PROFILE=remote-server`, `offline-first` o `desktop`, necesitás setear `ENGRAM_PG_CONNECTION`.

### "Configuration requires: ENGRAM_SERVER_URL"

Falta la URL del servidor de sync. Agregala:

```bash
echo "ENGRAM_SERVER_URL=http://tu-servidor:7437" >> docker/.env
```

### SyncManager disabled — self-loop

Si ves este warning:

```
[engram] warning: SyncManager disabled — ENGRAM_SERVER_URL points to this server itself
```

Significa que `ENGRAM_SERVER_URL` apunta al mismo servidor. Esto pasa cuando:
1. Corrés `engram serve` con `ENGRAM_SYNC_ENABLED=true` y `ENGRAM_SERVER_URL=http://localhost:7437`
2. El servidor detecta que la URL apunta a sí mismo y deshabilita SyncManager

**Solución**: Configurá `ENGRAM_SERVER_URL` con la URL real del servidor de sync (no `localhost`).

### Container no levanta

```bash
# Ver logs
./scripts/deploy.sh logs

# Ver estado
./scripts/deploy.sh status

# Validar config
./scripts/deploy.sh validate
```

### PostgreSQL connection refused

```bash
# Verificar que PostgreSQL esté corriendo
docker ps | grep postgres

# Test de conexión
docker exec engram pg_isready -h postgres -U engram
```

### Validar configuración antes de startup

```bash
./scripts/deploy.sh validate
```

Salida esperada:

```
=== Validating deployment configuration ===

Profile:
  ✓ Profile: offline-first (PostgreSQL + SyncManager)

Database:
  ✓ DB mode: embedded (PostgreSQL as Docker service)

Safety:
  ✓ .env is not tracked by git (safe)

✓ Ready to deploy.
```

### Test verification

```bash
# Tests completos (T1 + T2)
bash scripts/run-tests.sh

# Tests con Postgres (T3, requiere Docker)
PG_PASS=tu_password bash scripts/dev-test.sh
```

---

## Migration

### `ENGRAM_URL` → `ENGRAM_SERVER_URL` (v0.4.0)

**Si tenés deployments existentes usando `ENGRAM_URL`**, actualizá a `ENGRAM_SERVER_URL`:

```bash
# Antes (deprecated)
export ENGRAM_URL=http://server:7437

# Después
export ENGRAM_SERVER_URL=http://server:7437
```

**Archivos a actualizar**:
- `docker/.env` → cambiar `ENGRAM_URL` a `ENGRAM_SERVER_URL`
- Scripts que setean la URL → actualizar nombre de variable
- Configs de IDE (MCP JSON) → actualizar nombre de variable

**Nota**: `ENGRAM_URL` nunca estuvo documentado como oficial. `ENGRAM_SERVER_URL` es la variable canónica desde siempre en la mayoría del codebase. El cambio afecta solo a deployments que usaron `StoreConfig.RemoteUrl` directamente con `ENGRAM_URL`.

### Upgrade a v0.4.0 desde versiones anteriores

```bash
# 1. Backup
./scripts/deploy.sh backup

# 2. Pull nueva imagen
docker pull ghcr.io/efreet111/engram-dotnet:latest

# 3. Recrear
./scripts/deploy.sh recreate
```

---

## Reference

### Merge precedence (config resolution)

```
explicit env > profile default > hardcoded default
```

Ejemplos:

| Profile | Variable seteada | Resultado |
|---------|-----------------|-----------|
| `offline-first` | nada | `ENGRAM_SYNC_ENABLED=true` (profile default) |
| `offline-first` | `ENGRAM_SYNC_ENABLED=false` | `false` (explicit > profile) |
| `remote-server` | `ENGRAM_DB_TYPE=sqlite` | `sqlite` (explicit > profile default `postgres`) |

### Profile defaults

| Variable | `local` | `remote-server` | `offline-first` | `desktop` |
|----------|---------|---------------|----------------|---------|
| `ENGRAM_DB_TYPE` | `sqlite` | `postgres` | `postgres` | `postgres` |
| `ENGRAM_SYNC_ENABLED` | `false` | `false` | `true` | `true` |
| `ENGRAM_SYNC_POLL_SECONDS` | — | — | `30` | `30` |
| `ENGRAM_SYNC_TARGET` | — | — | `cloud` | `desktop` |

### ENGRAM_DB_MODE

| Mode | Descripción | Caso de uso |
|------|-------------|-------------|
| `external` (default) | PostgreSQL existe fuera de Docker | TrueNAS, DB server existente |
| `embedded` | PostgreSQL como servicio Docker | Dev local, fullstack |

---

## See also

- [HU-010 — Deployment Profile System](tasks/HU-001-HU-099/HU-010-deploy-profile-system.md)
- [ADR-011 — Estandarización ENGRAM_SERVER_URL](architecture/adr/ADR-011-engram-url-env-var.md)
- [SYNC-SETUP.md](SYNC-SETUP.md)
- [DOCKER-VANILLA.md](DOCKER-VANILLA.md)
- [INSTALL.md](INSTALL.md)
