# Docker — engram-dotnet

Guía para desplegar engram-dotnet como contenedor Docker.

## Prerequisitos

- `git`
- `docker` y `docker compose`
- PostgreSQL 15+ (puede ser local, en contenedor, o remoto)

---

## Perfiles de despliegue (`ENGRAM_PROFILE`)

En lugar de configurar `ENGRAM_DB_TYPE`, `ENGRAM_SYNC_ENABLED` y otras variables una por una, usá `ENGRAM_PROFILE` para elegir tu modo de despliegue en una sola línea:

| Perfil | Para quién | Backend | Sync | Variables requeridas |
|--------|-----------|---------|------|---------------------|
| `local` (default) | Desarrollador solo | SQLite | ❌ | *(ninguna)* |
| `remote-server` | Equipo pequeño (2-5), BD compartida | PostgreSQL | ❌ | `ENGRAM_PG_CONNECTION`, `ENGRAM_USER` |
| `offline-first` | Equipo grande (5-20), offline-first | SQLite (local) + PostgreSQL (server) | ✅ | `ENGRAM_SERVER_URL`, `ENGRAM_USER` |
| `desktop` | Uso personal/workstation compartida | PostgreSQL | ❌ | `ENGRAM_PG_CONNECTION`, `ENGRAM_USER` |

Cada perfil define valores por defecto que podés sobrescribir individualmente:

| Variable | `local` | `remote-server` | `offline-first` | `desktop` |
|----------|---------|----------------|-----------------|-----------|
| `ENGRAM_DB_TYPE` | `sqlite` | `postgres` | `sqlite` | `postgres` |
| `ENGRAM_SYNC_ENABLED` | `false` | `false` | `true` | `false` |
| `ENGRAM_SYNC_POLL_SECONDS` | — | — | `30` | — |
| `ENGRAM_SYNC_TARGET` | — | — | `cloud` | — |

**Precedencia**: variable explícita > valor por defecto del perfil > valor hardcodeado. Si ponés `ENGRAM_PROFILE=remote-server` pero también `ENGRAM_DB_TYPE=sqlite`, SQLite gana — tu override siempre tiene prioridad.

### Modo de base de datos (`ENGRAM_DB_MODE`)

Controla si PostgreSQL corre como servicio embebido junto a Engram o se conecta a una instancia externa:

| Valor | Comportamiento |
|-------|---------------|
| `external` (default) | PostgreSQL está en el host o red — pasás `ENGRAM_PG_CONNECTION` con host/puerto |
| `embedded` | Docker Compose levanta un servicio `postgres` junto a Engram — cero configuración manual de PG |

`ENGRAM_DB_MODE` solo aplica con `ENGRAM_PROFILE=remote-server` o `desktop` (ambos requieren PostgreSQL). Con `local` u `offline-first` se ignora.

```bash
# Modo embedded: PostgreSQL se levanta solo
ENGRAM_PROFILE=remote-server ENGRAM_DB_MODE=embedded docker compose up -d
```

> **Retrocompatible**: No querés usar perfiles? Todas las variables existentes (`ENGRAM_DB_TYPE`, `ENGRAM_SYNC_ENABLED`, etc.) siguen funcionando igual. Sin `ENGRAM_PROFILE` el sistema se comporta exactamente como antes.

## Quick Start

### 1. Clonar el repositorio

```bash
git clone https://github.com/efreet111/engram-dotnet.git
cd engram-dotnet
```

### 2. Configurar variables de entorno

```bash
cd docker
cp .env.example .env
```

Editá `docker/.env` con tus valores. Elegí un perfil según tu caso:

**Perfil `local` (SQLite, desarrollo solo)**:
```env
# Sin ENGRAM_PROFILE o ENGRAM_PROFILE=local — SQLite por defecto
ENGRAM_DATA_DIR_HOST=./data
```

**Perfil `remote-server` (PostgreSQL, equipo pequeño)**:
```env
ENGRAM_PROFILE=remote-server
ENGRAM_PG_HOST=host.docker.internal
ENGRAM_PG_PORT=5432
ENGRAM_PG_DATABASE=engram
ENGRAM_PG_USER=engram
ENGRAM_PG_PASSWORD=your-secure-password
ENGRAM_USER=admin
```

**Perfil `offline-first` (SQLite local + sync, equipo grande)**:
```env
ENGRAM_PROFILE=offline-first
ENGRAM_SERVER_URL=http://your-server:7437
ENGRAM_USER=your-username
```

**Perfil `desktop` (PostgreSQL, uso personal)**:
```env
ENGRAM_PROFILE=desktop
ENGRAM_PG_HOST=host.docker.internal
ENGRAM_PG_PORT=5432
ENGRAM_PG_DATABASE=engram
ENGRAM_PG_USER=engram
ENGRAM_PG_PASSWORD=your-secure-password
ENGRAM_USER=admin
```

> **Sin perfil (retrocompatible)**: Si no ponés `ENGRAM_PROFILE`, el sistema funciona como antes. Seguí usando `ENGRAM_DB_TYPE`, `ENGRAM_SYNC_ENABLED`, etc. manualmente.

### 3. Levantar el contenedor

```bash
docker compose up -d --build
```

### 4. Verificar

```bash
curl http://localhost:7437/health
# → {"status":"ok","service":"engram","version":"...","backend":"sqlite"}  (local/offline-first)
# → {"status":"ok","service":"engram","version":"...","backend":"postgres"} (remote-server/desktop)
```

---

## Backend-specific compose files

engram-dotnet supports two backends. Use the compose file that matches your choice:

| File | Backend | Volume required? |
|------|---------|-----------------|
| `docker-compose.postgres.yml` | PostgreSQL | No |
| `docker-compose.sqlite.yml` | SQLite | Yes |
| `docker-compose.yml` | Both (default: postgres) | Yes (backward-compatible) |

> **Con perfil, rara vez necesitás archivos separados.** Poné `ENGRAM_PROFILE=remote-server`, `offline-first` o `desktop` en tu `.env` y usá `docker-compose.yml` — el perfil configura PostgreSQL automáticamente. Los archivos `*-postgres.yml` y `*-sqlite.yml` existen para setups avanzados y retrocompatibilidad.

### PostgreSQL-only setup (recommended for remote-server or desktop)

```bash
cd docker
cp .env.example .env
# Editá .env: poné ENGRAM_PROFILE=remote-server y configurá las credenciales PG
# ENGRAM_DATA_DIR_HOST no es necesario con PostgreSQL — podés dejarlo o comentarlo
docker compose -f docker-compose.postgres.yml up -d --build
```

### SQLite setup

```bash
cd docker
cp .env.example .env
# Editá .env: poné ENGRAM_PROFILE=local y configurá ENGRAM_DATA_DIR_HOST
docker compose -f docker-compose.sqlite.yml up -d --build
```

---

## Ubicación del archivo `.env`

El archivo `.env` debe estar en el **mismo directorio que `docker-compose.yml`**:

```
engram-dotnet/
└── docker/
    ├── docker-compose.yml
    ├── .env.example
    └── .env              ← aquí
```

Docker Compose lee automáticamente el `.env` de su directorio actual.

---

## Variables de entorno

### Variables del `.env` (host → contenedor)

Estas variables se definen en `docker/.env` y controlan cómo Docker mapea recursos:

| Variable | Descripción | Ejemplo |
|----------|-------------|---------|
| `ENGRAM_PROFILE` | Perfil de despliegue: `local`, `remote-server`, `offline-first`, `desktop` | `remote-server` |
| `ENGRAM_DB_MODE` | Modo PostgreSQL: `external` (host/red) o `embedded` (servicio compose) | `embedded` |
| `ENGRAM_DATA_DIR_HOST` | Ruta en el host para datos persistentes (SQLite, exports) | `./data` o `/var/lib/engram` |
| `ENGRAM_PG_HOST` | Host de PostgreSQL | `host.docker.internal`, `postgres`, `db.example.com` |
| `ENGRAM_PG_PORT` | Puerto de PostgreSQL | `5432` |
| `ENGRAM_PG_DATABASE` | Nombre de la base de datos | `engram` |
| `ENGRAM_PG_USER` | Usuario de PostgreSQL | `engram` |
| `ENGRAM_PG_PASSWORD` | Password de PostgreSQL (obligatorio) | `your-secure-password` |
| `ENGRAM_USER` | Identidad del usuario (requerido para `remote-server`, `offline-first`, `desktop`) | `admin` |

### Variables opcionales

| Variable | Descripción | Default |
|----------|-------------|---------|
| `ENGRAM_JWT_SECRET` | Clave para autenticación JWT | *(vacío)* |
| `ENGRAM_CORS_ORIGINS` | Orígenes permitidos para CORS | *(vacío)* |

### Variables internas del contenedor

Estas las configura el `docker-compose.yml` automáticamente:

| Variable | Descripción | Valor |
|----------|-------------|-------|
| `ENGRAM_DATA_DIR` | Ruta interna de datos | `/data/engram` |
| `ENGRAM_PORT` | Puerto interno del servicio | `7437` |

---

## Escenarios de PostgreSQL

### Escenario A: PostgreSQL en el mismo host

Si PostgreSQL corre en tu máquina (fuera de Docker):

```env
ENGRAM_PG_HOST=host.docker.internal
```

El `docker-compose.yml` incluye `extra_hosts` para resolver `host.docker.internal` automáticamente.

**Requisitos:**
- PostgreSQL debe escuchar en todas las interfaces (`postgresql.conf`: `listen_addresses = '*'`)
- Firewall debe permitir conexiones desde Docker

### Escenario B: PostgreSQL en otro contenedor

Si PostgreSQL corre en un contenedor separado, usa el nombre del contenedor:

```env
ENGRAM_PG_HOST=postgres
```

**Requisitos:**
- Ambos contenedores deben estar en la misma red Docker

### Escenario C: PostgreSQL remoto

Si PostgreSQL está en otro servidor:

```env
ENGRAM_PG_HOST=db.example.com
# o
ENGRAM_PG_HOST=192.168.1.100
```

---

## Sync offline-first

Con `ENGRAM_PROFILE=offline-first`, el contenedor usa SQLite localmente y expone la API `/sync/*` para que los clientes hagan push/pull a un servidor remoto. El perfil `offline-first` auto-configura `ENGRAM_DB_TYPE=sqlite`, `ENGRAM_SYNC_ENABLED=true`, `ENGRAM_SYNC_POLL_SECONDS=30`, y `ENGRAM_SYNC_TARGET=cloud`.

El servidor remoto debe usar `ENGRAM_PROFILE=remote-server` (PostgreSQL, sin SyncManager local).

> **Importante**: El perfil `offline-first` NO usa PostgreSQL directamente — usa SQLite en el contenedor y sincroniza con un servidor externo que tiene PostgreSQL.

**No** necesitás `ENGRAM_SYNC_ENABLED` en el compose si usás perfil — el `SyncManager` corre en cada PC de desarrollo con `engram mcp` + SQLite local.

Cada desarrollador debe configurar en su MCP:

- `ENGRAM_SERVER_URL` — URL de este servidor (ej. `http://your-server:7437`)
- `ENGRAM_SYNC_ENABLED=true`
- `ENGRAM_USER` — identidad única (obligatorio en equipos)

Ver [docs/SYNC-SETUP.md](../docs/SYNC-SETUP.md) para más detalles.

---

## Comandos útiles

```bash
# Ver logs
docker compose logs -f

# Ver contenedores
docker compose ps

# Reiniciar
docker compose restart

# Actualizar a nueva versión
git pull
docker compose up -d --build

# Detener
docker compose down
```

---

## Troubleshooting

### Puerto 7437 ya está en uso

```bash
sudo lsof -i :7437
# Cambiar puerto en docker-compose.yml si es necesario
```

### Error de permisos en el volumen

El contenedor ajusta permisos automáticamente al iniciar. Si persiste:

```bash
sudo chown -R 1000:1000 ./data
```

### PostgreSQL no conecta

```bash
# Verificar logs
docker compose logs engram | grep -i postgres

# Verificar health
curl http://localhost:7437/health
# Debe mostrar "backend":"postgres"
```

### Health check falla

```bash
# Verificar que el contenedor está corriendo
docker compose ps

# Ver logs
docker compose logs engram

# Probar manualmente
curl http://localhost:7437/health
```

---

## Ver también

- [docs/DOCKER-VANILLA.md](../docs/DOCKER-VANILLA.md) — Docker sin Compose
- [docs/POSTGRES-SETUP.md](../docs/POSTGRES-SETUP.md) — Setup detallado de PostgreSQL
- [docs/SYNC-SETUP.md](../docs/SYNC-SETUP.md) — Configuración de sync para equipos
- [docs/API-REFERENCE.md](../docs/API-REFERENCE.md) — Referencia completa de variables y endpoints
