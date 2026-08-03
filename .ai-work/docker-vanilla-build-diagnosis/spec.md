# Spec: Docker Vanilla Build Diagnosis

## 1. Problem Statement

Usuario necesita compilar imagen Docker de engram-dotnet usando Docker vanilla (sin Docker Compose) para diagnosticar error de instalación en servidor remoto. El servidor solo acepta Docker Engine estándar sin plugins adicionales.

## 2. Goals

- [ ] Diagnosticar error de build Docker vanilla
- [ ] Documentar comandos `docker build`/`docker run` equivalentes a compose
- [ ] Proveer proceso de troubleshooting paso a paso
- [ ] Actualizar documentación con flujo vanilla en `docs/DOCKER-VANILLA.md`

## 3. Non-Goals

- Modificar Dockerfile existente (salvo que sea necesario para fix)
- Crear nuevo sistema de deployment
- Soportar otros orquestadores (Kubernetes, Swarm, etc.)

## 4. Functional Requirements

### FR-1: Build command

Comando `docker build` equivalente a `docker compose build`:

```bash
# Desde la raíz del repositorio
docker build -t engram-dotnet:latest -f Dockerfile .

# Con versión específica
docker build -t engram-dotnet:v1.3.0 -f Dockerfile --build-arg ENGRAM_VERSION=v1.3.0 .

# Con logs detallados para debugging
docker build --progress=plain -t engram-dotnet:latest -f Dockerfile .
```

### FR-2: Run command

Comando `docker run` equivalente a `docker compose up`:

```bash
# Con SQLite (default, más simple)
docker run -d \
  --name engram \
  -p 7437:7437 \
  -v /path/to/data:/data/engram \
  engram-dotnet:latest

# Con PostgreSQL externo
docker run -d \
  --name engram \
  -p 7437:7437 \
  -e ENGRAM_DB_TYPE=postgres \
  -e ENGRAM_PG_CONNECTION="Host=192.168.1.100;Port=5432;Database=engram;Username=engram;Password=secret" \
  -v /path/to/data:/data/engram \
  engram-dotnet:latest

# Con PostgreSQL en el mismo host (usando host.docker.internal)
docker run -d \
  --name engram \
  -p 7437:7437 \
  --add-host host.docker.internal:host-gateway \
  -e ENGRAM_DB_TYPE=postgres \
  -e ENGRAM_PG_CONNECTION="Host=host.docker.internal;Port=5432;Database=engram;Username=engram;Password=secret" \
  -v /path/to/data:/data/engram \
  engram-dotnet:latest
```

### FR-3: PostgreSQL connection

Documentar cómo conectar a PostgreSQL externo sin compose:

**Opción A: IP directa del servidor PostgreSQL**
```bash
-e ENGRAM_PG_CONNECTION="Host=192.168.1.100;Port=5432;Database=engram;Username=engram;Password=secret"
```

**Opción B: host.docker.internal (Docker 20.10+)**
```bash
--add-host host.docker.internal:host-gateway
-e ENGRAM_PG_CONNECTION="Host=host.docker.internal;Port=5432;Database=engram;Username=engram;Password=secret"
```

**Opción C: Network mode host (Linux)**
```bash
docker run --network host engram-dotnet:latest
# PostgreSQL accesible en localhost:5432
```

### FR-4: Error diagnosis

Proceso para capturar errores:

```bash
# 1. Build con logs detallados
docker build --progress=plain -t engram-dotnet:latest -f Dockerfile . 2>&1 | tee build.log

# 2. Ver logs del contenedor
docker logs engram
docker logs -f engram  # follow mode

# 3. Inspeccionar contenedor
docker inspect engram
docker exec -it engram /bin/bash

# 4. Verificar healthcheck
docker inspect --format='{{.State.Health.Status}}' engram

# 5. Probar endpoint manualmente
curl http://localhost:7437/health
```

### FR-5: Variables de entorno requeridas

| Variable | Requerida | Default | Descripción |
|----------|-----------|---------|-------------|
| `ENGRAM_DB_TYPE` | No | `sqlite` | `sqlite` o `postgres` |
| `ENGRAM_PG_CONNECTION` | Si (postgres) | - | Connection string completo |
| `ENGRAM_DATA_DIR` | No | `/data/engram` | Directorio de datos |
| `ENGRAM_PORT` | No | `7437` | Puerto HTTP |
| `ENGRAM_JWT_SECRET` | No | - | Secret para JWT auth |

## 5. Non-Functional Requirements

### NFR-1: Compatibility

Compatible con Docker Engine 20.10+ (sin compose plugin requerido)

### NFR-2: Documentation

Documentación clara en `docs/DOCKER-VANILLA.md` con:
- Prerrequisitos (Docker version, acceso a internet)
- Paso a paso de build
- Paso a paso de run (SQLite y PostgreSQL)
- Troubleshooting común
- Ejemplos de uso

## 6. Security Considerations

### STRIDE Analysis

| Threat | Mitigation |
|--------|------------|
| **Spoofing** | Validar que `ENGRAM_PG_CONNECTION` no se exponga en logs o `docker inspect` |
| **Tampering** | Verificar integridad de imagen base con `docker trust inspect` |
| **Information Disclosure** | No hardcodear secrets en Dockerfile; usar `--env-file` o Docker secrets |
| **Denial of Service** | Documentar límites de recursos: `--memory=512m --cpus=1.0` |
| **Elevation of Privilege** | Contenedor corre como usuario no-root (`engram`) |

### Buenas prácticas

- Usar `.env` file para variables sensibles:
  ```bash
  docker run --env-file .env engram-dotnet:latest
  ```
- No commitear `.env` al repositorio
- Rotar secrets periódicamente

## 7. Open Questions (RESUELTAS)

**[RESUELTO]** Error reportado: Error de versionado de paquete NuGet durante el build (sin código de error específico)

**[RESUELTO]** Docker versión: 29.5 (compatible, sin problemas)

**[RESUELTO]** Commit b512dc0: Rama principal `main` (versiones estables)

**[RESUELTO]** Acceso a internet: SÍ tiene, pero NO puede descargar imágenes de `mcr.microsoft.com` directamente (posible firewall/proxy)

## 8. Success Criteria

- [ ] Usuario puede compilar imagen con `docker build` sin errores
- [ ] Usuario puede correr contenedor con `docker run` (SQLite y PostgreSQL)
- [ ] Error original está diagnosticado y documentado
- [ ] Documentación actualizada en `docs/DOCKER-VANILLA.md`
- [ ] Healthcheck funciona correctamente
- [ ] Contenedor sobrevive a restarts (`--restart unless-stopped`)

## 9. Implementation Notes

### Estructura del Dockerfile actual

El `Dockerfile` en la raíz usa multi-stage build:
1. **Stage 1 (build)**: .NET SDK 10.0, restore + publish
2. **Stage 2 (runtime)**: ASP.NET 10.0, copia binario publicado

**Puntos críticos:**
- `PublishSingleFile=true` genera ejecutable nativo
- `InvariantGlobalization=true` reduce dependencias
- Usuario no-root (`engram`) por seguridad
- Healthcheck con curl

### Alternativa: Dockerfile de binario precompilado

`/docker/Dockerfile` descarga binario de GitHub Releases:
- No requiere .NET SDK
- Más rápido de construir
- Requiere que el release exista en GitHub

## 10. Troubleshooting Checklist

- [ ] ¿Docker puede descargar imágenes base? (`docker pull mcr.microsoft.com/dotnet/sdk:10.0`)
- [ ] ¿Hay suficiente espacio en disco? (`docker system df`)
- [ ] ¿El puerto 7437 está disponible? (`netstat -tlnp | grep 7437`)
- [ ] ¿Los permisos del volumen son correctos? (`ls -la /path/to/data`)
- [ ] ¿PostgreSQL acepta conexiones desde la IP del contenedor?
