# Spec: Docker Runtime Permissions & Environment Variables

## 1. Problem Statement

Usuario reporta error al correr contenedor Docker con volumen montado:
```
SQLite Error 14: 'unable to open database file'
at Engram.Store.SqliteStore..ctor(StoreConfig cfg) in /src/src/Engram.Store/SqliteStore.cs:line 65
```

**Causa raíz**: El contenedor corre como usuario `engram` (no-root), pero el volumen montado desde el host tiene permisos de root. El usuario `engram` no puede escribir en `/data/engram`.

**Problema secundario**: Las variables de entorno disponibles no están completamente documentadas en `docs/DOCKER-VANILLA.md`.

## 2. Goals

- [ ] Resolver error de permisos SQLite en Docker
- [ ] Crear entrypoint script que ajuste permisos antes de ejecutar como no-root
- [ ] Documentar todas las variables de entorno disponibles
- [ ] Proveer ejemplos de uso para cada variable
- [ ] Actualizar `docs/DOCKER-VANILLA.md` con sección de permisos y variables

## 3. Non-Goals

- Modificar lógica de `SqliteStore.cs` (el código está correcto)
- Cambiar estrategia de seguridad (seguimos con usuario no-root)
- Agregar nuevas variables de entorno (solo documentar las existentes)

## 4. Functional Requirements

### FR-1: Entrypoint script

Crear `entrypoint.sh` que:
1. Ajuste permisos de `/data/engram` si el directorio existe y es escribible por root
2. Ejecute el comando pasado como argumento con `exec` (para preservar señales)
3. Sea compatible con ambos Dockerfiles (estándar y debian)

**Ejemplo**:
```bash
#!/bin/bash
set -e

# Fix permissions for data directory (if mounted as root)
if [ -d "/data/engram" ] && [ ! -w "/data/engram" ]; then
    chown -R engram:engram /data/engram
fi

# Execute command as current user (engram)
exec "$@"
```

### FR-2: Dockerfile modificado

Modificar ambos Dockerfiles para:
1. Copiar `entrypoint.sh` a `/usr/local/bin/entrypoint.sh`
2. Hacerlo ejecutable
3. Cambiar `ENTRYPOINT` para usar el script
4. Mantener `USER engram` pero ejecutar entrypoint como root primero

**Ejemplo**:
```dockerfile
COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

# Entrypoint runs as root to fix permissions, then execs as engram
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["./engram", "serve"]
```

**Nota**: El entrypoint script debe hacer `exec gosu engram "$@"` o similar para cambiar de usuario. Alternativa: usar `su-exec` o `gosu`.

### FR-3: Documentación de permisos

Agregar sección en `docs/DOCKER-VANILLA.md`:

```markdown
## 8. Volume permissions

If you see `SQLite Error 14: 'unable to open database file'`, the volume
mounted from the host has incorrect permissions. The container runs as
user `engram` (non-root), but Docker creates mounted volumes as `root`.

### Solution A: Let the entrypoint fix it (recommended)

The entrypoint script automatically fixes permissions on startup:

```bash
docker run -d --name engram \
  -p 7437:7437 \
  -v /path/to/data:/data/engram \
  engram-dotnet:latest
```

The entrypoint will `chown -R engram:engram /data/engram` before starting
the application.

### Solution B: Pre-create directory with correct permissions

```bash
# Create directory on host
mkdir -p /path/to/data

# Set permissions (Linux)
sudo chown -R 1000:1000 /path/to/data

# Run container
docker run -d --name engram \
  -p 7437:7437 \
  -v /path/to/data:/data/engram \
  engram-dotnet:latest
```

### Solution C: Use `--user` flag

```bash
docker run -d --name engram \
  -p 7437:7437 \
  -v /path/to/data:/data/engram \
  --user $(id -u):$(id -g) \
  engram-dotnet:latest
```

This runs the container as your host user, so the volume has correct
permissions.
```

### FR-4: Documentación de variables de entorno

Agregar sección completa en `docs/DOCKER-VANILLA.md`:

```markdown
## 9. Environment variables reference

| Variable | Default | Description | Example |
|----------|---------|-------------|---------|
| `ENGRAM_DATA_DIR` | `/data/engram` | Data directory (SQLite DB, exports) | `/custom/path` |
| `ENGRAM_PORT` | `7437` | HTTP port | `8080` |
| `ENGRAM_DB_TYPE` | `sqlite` | Backend: `sqlite` or `postgres` | `postgres` |
| `ENGRAM_PG_CONNECTION` | — | PostgreSQL connection string (required if `ENGRAM_DB_TYPE=postgres`) | `Host=db;Port=5432;Database=engram;Username=engram;Password=secret` |
| `ENGRAM_SERVER_URL` | `http://localhost:7437` | Engram server URL (for sync) | `http://192.168.1.100:7437` |
| `ENGRAM_SYNC_ENABLED` | `false` | Enable sync (offline-first mode) | `true` |
| `ENGRAM_USER` | — | User identity (required for sync in team mode) | `user@example.com` |
| `ENGRAM_AUTO_ENROLL` | `true` | Auto-generate `.engram-id` on startup | `false` |
| `ENGRAM_PROJECT` | — | Project name (auto-detected from git if not set) | `my-project` |
| `ASPNETCORE_URLS` | `http://+:7437` | ASP.NET Core listening URLs | `http://+:8080` |
```

### FR-5: Ejemplos de uso

Agregar ejemplos completos en `docs/DOCKER-VANILLA.md`:

```markdown
## 10. Common configurations

### Local mode (SQLite, single user)

```bash
docker run -d --name engram \
  -p 7437:7437 \
  -v /path/to/data:/data/engram \
  engram-dotnet:latest
```

### Team mode (PostgreSQL, sync enabled)

```bash
docker run -d --name engram \
  -p 7437:7437 \
  -e ENGRAM_DB_TYPE=postgres \
  -e ENGRAM_PG_CONNECTION="Host=db.example.com;Port=5432;Database=engram;Username=engram;Password=secret" \
  -v /path/to/data:/data/engram \
  engram-dotnet:latest
```

### Custom port

```bash
docker run -d --name engram \
  -p 8080:8080 \
  -e ENGRAM_PORT=8080 \
  -e ASPNETCORE_URLS="http://+:8080" \
  -v /path/to/data:/data/engram \
  engram-dotnet:latest
```
```

## 5. Non-Functional Requirements

### NFR-1: Compatibility
- Compatible con Docker 20.10+
- Compatible con Linux, macOS, Windows (Docker Desktop)
- No romper compatibilidad con docker-compose.yml existente

### NFR-2: Security
- Mantener usuario no-root (`engram`) en runtime
- No introducir vulnerabilidades con entrypoint script
- No hardcodear secrets en Dockerfile

### NFR-3: Performance
- Entrypoint script no debe agregar más de 1s al startup
- `chown` solo si es necesario (verificar permisos antes)

## 6. Security Considerations

### STRIDE Analysis

| Threat | Mitigation |
|--------|------------|
| **Spoofing** | No cambiar identidad de usuario en entrypoint |
| **Tampering** | Entrypoint script debe ser ejecutable solo por root |
| **Information Disclosure** | No loggear variables de entorno con secrets |
| **Denial of Service** | `chown` recursivo puede ser lento en volúmenes grandes — verificar antes de ejecutar |
| **Elevation of Privilege** | Entrypoint corre como root brevemente, luego hace `exec` como `engram` |

### Buenas prácticas

- Usar `set -e` en entrypoint script (fallar rápido)
- Verificar permisos antes de `chown` (no ejecutar si no es necesario)
- Usar `exec` para reemplazar proceso (preservar señales)
- No loggear valores de variables de entorno

## 7. Open Questions

**[BLOCKER]** ¿Qué herramienta usar para cambiar de usuario en entrypoint?
- Opción A: `gosu` (requiere instalación en runtime stage)
- Opción B: `su-exec` (más ligero, requiere compilación)
- Opción C: `su -c` (nativo, pero menos limpio)
- Opción D: No cambiar de usuario en entrypoint, usar `--user` flag en `docker run`

**[BLOCKER]** ¿El entrypoint debe correr como root o como `engram`?
- Si corre como root: puede hacer `chown`, pero necesita `gosu` o similar
- Si corre como `engram`: no puede hacer `chown`, pero es más seguro

**Recomendación**: Usar `gosu` (opción A) — es el estándar en imágenes Docker oficiales.

## 8. Success Criteria

- [ ] Contenedor arranca sin error de permisos con volumen montado
- [ ] Entrypoint script funciona en ambos Dockerfiles
- [ ] Documentación de permisos agregada a `docs/DOCKER-VANILLA.md`
- [ ] Tabla de variables de entorno completa
- [ ] Ejemplos de uso para configuraciones comunes
- [ ] Tests manuales: SQLite, PostgreSQL, custom port
- [ ] No regressions en docker-compose.yml existente

## 9. Implementation Notes

### Estrategia recomendada

1. **Instalar `gosu`** en runtime stage (ambos Dockerfiles):
   ```dockerfile
   RUN apt-get update && apt-get install -y gosu && rm -rf /var/lib/apt/lists/*
   ```

2. **Crear `entrypoint.sh`**:
   ```bash
   #!/bin/bash
   set -e
   
   # Fix permissions if needed
   if [ -d "/data/engram" ] && [ ! -w "/data/engram" ]; then
       chown -R engram:engram /data/engram
   fi
   
   # Execute as engram user
   exec gosu engram "$@"
   ```

3. **Modificar Dockerfile**:
   ```dockerfile
   # Instalar gosu
   RUN apt-get update && apt-get install -y gosu && rm -rf /var/lib/apt/lists/*
   
   # Copiar entrypoint
   COPY entrypoint.sh /usr/local/bin/entrypoint.sh
   RUN chmod +x /usr/local/bin/entrypoint.sh
   
   # Mantener USER root (entrypoint necesita permisos)
   # USER engram  # <- eliminar o comentar
   
   # Cambiar entrypoint
   ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
   CMD ["./engram", "serve"]
   ```

4. **Actualizar documentación** con sección de permisos y tabla de variables.

### Alternativa: No usar entrypoint script

Si no queremos agregar `gosu`, podemos:
- Documentar que el usuario debe crear el directorio con permisos correctos antes de montar
- O usar `--user $(id -u):$(id -g)` en `docker run`

**Desventaja**: Menos amigable para el usuario, requiere pasos manuales.

## 10. Troubleshooting Checklist

- [ ] ¿El directorio `/data/engram` existe en el contenedor?
- [ ] ¿Quién es el propietario del directorio montado? (`ls -la /data/engram`)
- [ ] ¿El usuario `engram` puede escribir en el directorio? (`su - engram -c "touch /data/engram/test"`)
- [ ] ¿El entrypoint script se está ejecutando? (`docker logs engram`)
- [ ] ¿`gosu` está instalado? (`docker exec engram which gosu`)
