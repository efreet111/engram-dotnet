# Docker — engram-dotnet

Guía para desplegar engram-dotnet como contenedor Docker.

## Prerequisitos

- `git`
- `docker` y `docker compose`
- PostgreSQL 15+ (puede ser local, en contenedor, o remoto)

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

Edita `docker/.env` con tus valores:

```env
# Ruta donde se guardarán los datos en el host
ENGRAM_DATA_DIR_HOST=./data

# Configuración PostgreSQL
ENGRAM_PG_HOST=host.docker.internal
ENGRAM_PG_PORT=5432
ENGRAM_PG_DATABASE=engram
ENGRAM_PG_USER=engram
ENGRAM_PG_PASSWORD=your-secure-password

# Backend: postgres (default) o sqlite
ENGRAM_DB_TYPE=postgres
```

### 3. Levantar el contenedor

```bash
docker compose up -d --build
```

### 4. Verificar

```bash
curl http://localhost:7437/health
# → {"status":"ok","service":"engram","version":"...","backend":"postgres"}
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
| `ENGRAM_DATA_DIR_HOST` | Ruta en el host para datos persistentes (SQLite, exports) | `./data` o `/var/lib/engram` |
| `ENGRAM_PG_HOST` | Host de PostgreSQL | `host.docker.internal`, `postgres`, `db.example.com` |
| `ENGRAM_PG_PORT` | Puerto de PostgreSQL | `5432` |
| `ENGRAM_PG_DATABASE` | Nombre de la base de datos | `engram` |
| `ENGRAM_PG_USER` | Usuario de PostgreSQL | `engram` |
| `ENGRAM_PG_PASSWORD` | Password de PostgreSQL (obligatorio) | `your-secure-password` |
| `ENGRAM_DB_TYPE` | Backend: `postgres` o `sqlite` | `postgres` |

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

Con `ENGRAM_DB_TYPE=postgres`, el contenedor expone la API `/sync/*` para que los clientes hagan push/pull.

**No** necesitas `ENGRAM_SYNC_ENABLED` en el compose — el `SyncManager` corre en cada PC de desarrollo con `engram mcp` + SQLite local.

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
