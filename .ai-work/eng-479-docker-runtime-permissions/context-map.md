# Context Map — Docker Runtime Permissions & Environment Variables

## Fecha
2026-08-03

## Problema reportado
Usuario reporta error al correr contenedor Docker:
```
SQLite Error 14: 'unable to open database file'
at Engram.Store.SqliteStore..ctor(StoreConfig cfg) in /src/src/Engram.Store/SqliteStore.cs:line 65
```

## Análisis del error

### Causa raíz
1. Dockerfile usa `USER engram` (no-root) antes de `ENTRYPOINT`
2. Cuando se monta volumen desde host (`-v /path:/data/engram`), Docker lo crea como `root`
3. Usuario `engram` no tiene permisos de escritura en `/data/engram`
4. `SqliteStore.cs:56` hace `Directory.CreateDirectory(cfg.DataDir)` — falla silenciosamente o crea dir sin permisos
5. `SqliteStore.cs:65` hace `_db.Open()` — falla con "unable to open database file"

### Variables de entorno disponibles

**Definidas en Dockerfile:**
- `ENGRAM_DATA_DIR=/data/engram` — directorio de datos
- `ENGRAM_PORT=7437` — puerto HTTP
- `ASPNETCORE_URLS=http://+:7437` — URLs ASP.NET

**Usadas en código (src/Engram.Cli/Program.cs):**
- `ENGRAM_DB_TYPE` — `sqlite` (default) o `postgres`
- `ENGRAM_PG_CONNECTION` — connection string PostgreSQL
- `ENGRAM_SERVER_URL` — URL del servidor para sync
- `ENGRAM_SYNC_ENABLED` — habilitar sync
- `ENGRAM_USER` — identidad del usuario
- `ENGRAM_AUTO_ENROLL` — auto-generar .engram-id (default: true)
- `ENGRAM_PROJECT` — project name

**No documentadas en DOCKER-VANILLA.md:**
- `ENGRAM_DB_TYPE` y `ENGRAM_PG_CONNECTION` están en §2.3 (PostgreSQL)
- `ENGRAM_SERVER_URL`, `ENGRAM_SYNC_ENABLED`, `ENGRAM_USER` no están documentadas para Docker
- `ENGRAM_AUTO_ENROLL`, `ENGRAM_PROJECT` no están documentadas

## Restricciones del entorno

- Docker 29.5 (vanilla, sin Compose)
- Servidor con acceso limitado a internet
- Usuario no-root en contenedor (seguridad)

## Patrones reusables encontrados

### 1. Docker volume permissions con usuario no-root
- **Pattern**: Usar entrypoint script que ajuste permisos antes de `exec` como usuario no-root
- **Files**: `Dockerfile`, `Dockerfile.debian`
- **Why**: Docker monta volúmenes como root por defecto. El entrypoint puede hacer `chown` antes de cambiar de usuario.

### 2. Documentación de variables de entorno
- **Pattern**: Listar todas las variables de entorno disponibles en la documentación Docker
- **Files**: `docs/DOCKER-VANILLA.md`
- **Why**: Usuarios necesitan saber qué variables pueden configurar y para qué sirven.

## Decisiones arquitectónicas pendientes

### ADR-1: Estrategia de permisos
**Opciones:**
1. **Entrypoint script**: Crear `/entrypoint.sh` que haga `chown -R engram:engram /data/engram` antes de `exec gosu engram "$@"`
2. **Documentar workaround**: Explicar cómo usar `--user $(id -u):$(id -g)` en `docker run`
3. **Ambos**: Entrypoint + documentación de fallback

**Recomendación**: Opción 3 (ambos) — entrypoint para caso común, documentación para casos especiales.

### ADR-2: Variables de entorno en documentación
**Decisión**: Agregar sección completa de variables de entorno en `docs/DOCKER-VANILLA.md` con:
- Tabla de todas las variables
- Valores por defecto
- Ejemplos de uso
- Cuándo usar cada una

## Dependencies mapeadas

### Código relevante
- `src/Engram.Store/SqliteStore.cs:47-69` — constructor que abre SQLite
- `src/Engram.Cli/Program.cs:44-1391` — parseo de variables de entorno
- `Dockerfile:45-71` — creación de usuario y entrypoint
- `Dockerfile.debian:125-153` — idem

### Documentación existente
- `docs/DOCKER-VANILLA.md` — guía Docker (no menciona permisos ni todas las variables)
- `docs/API-REFERENCE.md` — referencia de API (menciona algunas variables)

## Risks identificados

| Riesgo | Probabilidad | Impacto | Mitigación |
|--------|--------------|---------|------------|
| Entrypoint script rompe compatibilidad | Baja | Medio | Probar con y sin volumen montado |
| Documentación incompleta | Alta | Bajo | Agregar tabla completa de variables |
| Permisos en sistemas Windows/Mac | Media | Medio | Documentar diferencias de plataforma |

## Outputs esperados

- `entrypoint.sh` — script de inicio que ajuste permisos
- `Dockerfile` modificado — use entrypoint script
- `Dockerfile.debian` modificado — use entrypoint script
- `docs/DOCKER-VANILLA.md` actualizado — sección de permisos + tabla de variables
- `docs/BACKLOG.md` — ENG-479 agregado
