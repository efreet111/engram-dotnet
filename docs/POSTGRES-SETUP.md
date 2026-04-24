# PostgreSQL Setup — engram-dotnet

> Guía de configuración para ejecutar engram-dotnet con PostgreSQL como backend de persistencia.

---

## Cuándo usar PostgreSQL

| Escenario | Recomendación |
|---|---|
| 1-4 desarrolladores concurrentes | SQLite (default) |
| 5+ desarrolladores concurrentes | PostgreSQL |
| Necesidad de backups automatizados | PostgreSQL |
| Alta disponibilidad / replicación | PostgreSQL |
| Deployment en infraestructura enterprise | PostgreSQL |

PostgreSQL elimina la contención de escritura de SQLite y permite escalabilidad horizontal con connection pooling.

---

## Requisitos

| Componente | Versión mínima |
|---|---|
| PostgreSQL | 15+ (requerido para `GENERATED ALWAYS AS STORED`) |
| engram-dotnet | 1.3.0+ |
| Npgsql (NuGet) | 9.0.* (incluido en el paquete) |

Managed services compatibles: **Neon**, **Supabase**, **AWS RDS**, **Azure Database for PostgreSQL**, **Google Cloud SQL**.

---

## Configuración rápida

### 1. Variables de entorno

```bash
export ENGRAM_DB_TYPE=postgres
export ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=engram;Password=secret"
```

### 2. Iniciar el servidor

```bash
./engram serve
# Output: [engram] starting HTTP server on :7437 (PostgreSQL)
```

### 3. Verificar

```bash
curl http://localhost:7437/health
# {"status":"ok"}

./engram stats
# Engram Memory Stats
#   Sessions:     0
#   Observations: 0
#   Prompts:      0
#   Projects:     none yet
#   Database:     PostgreSQL (localhost)
```

---

## Docker Compose

### docker-compose.yml

```yaml
version: "3.8"

services:
  postgres:
    image: postgres:17-alpine
    container_name: engram-postgres
    restart: unless-stopped
    environment:
      POSTGRES_DB: engram
      POSTGRES_USER: engram
      POSTGRES_PASSWORD: ${ENGRAM_PG_PASSWORD:-engram-secret}
    volumes:
      - pg-data:/var/lib/postgresql/data
    ports:
      - "5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U engram -d engram"]
      interval: 10s
      timeout: 5s
      retries: 5

  engram:
    image: engram-dotnet:latest
    build:
      context: .
      dockerfile: Dockerfile
    container_name: engram
    restart: unless-stopped
    depends_on:
      postgres:
        condition: service_healthy
    ports:
      - "7437:7437"
    environment:
      ENGRAM_DB_TYPE: postgres
      ENGRAM_PG_CONNECTION: "Host=postgres;Database=engram;Username=engram;Password=${ENGRAM_PG_PASSWORD:-engram-secret}"
      ENGRAM_PORT: "7437"
      # Opcional — auth JWT
      # ENGRAM_JWT_SECRET: "cambia_esto_por_un_secreto_seguro"
      # Opcional — CORS
      # ENGRAM_CORS_ORIGINS: "http://truenas.local"
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:7437/health"]
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 10s

volumes:
  pg-data:
```

### Iniciar

```bash
# Con contraseña por defecto
docker compose up -d

# Con contraseña personalizada
ENGRAM_PG_PASSWORD=mi-secreto docker compose up -d
```

---

## Migración desde SQLite

### Opción A: Export/Import (recomendada)

```bash
# 1. Exportar desde SQLite
ENGRAM_DATA_DIR=~/.engram ./engram export engram-backup.json

# 2. Configurar PostgreSQL
export ENGRAM_DB_TYPE=postgres
export ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=engram;Password=secret"

# 3. Importar en PostgreSQL
./engram import engram-backup.json
```

### Opción B: Fresh start

Simplemente configura las variables de entorno y arranca. `PostgresStore` crea el schema automáticamente al conectar.

---

## Schema

`PostgresStore` crea automáticamente las siguientes tablas al conectar:

| Tabla | Propósito |
|---|---|
| `sessions` | Sesiones de trabajo |
| `observations` | Memorias persistidas |
| `user_prompts` | Prompts del usuario |
| `sync_chunks` | Registro de chunks sincronizados |
| `sync_state` | Estado del sync |
| `sync_mutations` | Cola de mutaciones pendientes |
| `sync_enrolled_projects` | Proyectos enrolados en sync |

### Full-Text Search

```sql
-- Columna generada automáticamente
ALTER TABLE observations ADD COLUMN search_vector tsvector
  GENERATED ALWAYS AS (
    to_tsvector('simple',
      coalesce(title,'') || ' ' || coalesce(content,'') || ' ' ||
      coalesce(tool_name,'') || ' ' || coalesce(type,'') || ' ' ||
      coalesce(project,'') || ' ' || coalesce(topic_key,'')
    )
  ) STORED;

-- Índice GIN para búsquedas rápidas
CREATE INDEX idx_obs_fts ON observations USING GIN(search_vector);

-- Índice parcial solo para observaciones activas
CREATE INDEX idx_obs_fts_active ON observations USING GIN(search_vector)
  WHERE deleted_at IS NULL;
```

---

## Connection Pooling

Npgsql usa connection pooling por defecto. Para ajustar:

```bash
# En la connection string
ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=engram;Password=secret;MaxPoolSize=100;MinPoolSize=5"
```

| Parámetro | Default | Recomendado |
|---|---|---|
| `MaxPoolSize` | 100 | 50-200 según carga |
| `MinPoolSize` | 0 | 5-10 para evitar cold starts |
| `Connection Idle Lifetime` | 300s | 300s (default) |
| `Connection Pruning Interval` | 10s | 10s (default) |

---

## Troubleshooting

### Error: ENGRAM_PG_CONNECTION is required

```
error: ENGRAM_PG_CONNECTION is required when ENGRAM_DB_TYPE=postgres
```

**Solución**: Asegurate de que la variable de entorno esté configurada:
```bash
export ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=engram;Password=secret"
```

### Error: Connection refused

```
Npgsql.NpgsqlException: Connection refused
```

**Solución**: Verificá que PostgreSQL esté corriendo:
```bash
docker ps | grep postgres
# o
pg_isready -h localhost -p 5432
```

### Error: GENERATED ALWAYS AS STORED no soportado

```
ERROR: syntax error at or near "GENERATED"
```

**Solución**: Tu versión de PostgreSQL es menor a 15. Actualizá a PG 15+.

### Verificar que el backend es PostgreSQL

```bash
./engram stats
# Database: PostgreSQL (localhost)  ← confirma que está usando PG
```

---

## Monitoreo

### Queries útiles

```sql
-- Observaciones por proyecto
SELECT project, COUNT(*) as count
FROM observations
WHERE deleted_at IS NULL
GROUP BY project
ORDER BY count DESC;

-- Tamaño de la base de datos
SELECT pg_size_pretty(pg_database_size('engram'));

-- Índices más grandes
SELECT indexname, pg_size_pretty(pg_relation_size(indexname::regclass)) as size
FROM pg_indexes
WHERE schemaname = 'public'
ORDER BY pg_relation_size(indexname::regclass) DESC
LIMIT 10;

-- Observaciones recientes
SELECT id, type, title, project, created_at
FROM observations
WHERE deleted_at IS NULL
ORDER BY created_at DESC
LIMIT 20;
```

---

## Seguridad

- **Nunca** commitees la connection string con contraseña en el repo
- Usá variables de entorno o secrets manager (Vault, AWS Secrets Manager, etc.)
- En producción, configurá `ENGRAM_JWT_SECRET` para autenticación
- Restringí el acceso al puerto 5432 a la red interna solamente
- Usá SSL en la connection string para conexiones remotas:
  ```
  Host=db.example.com;Database=engram;Username=engram;Password=secret;SSL Mode=Require
  ```
