[← Volver al README](../../README.md) | [← RFC-001](../rfcs/RFC-001-postgresql-backend.md)

# PRD-001 — PostgreSQL Backend para engram-dotnet

| Campo       | Valor |
|-------------|-------|
| **PRD**     | 001 |
| **RFC**     | [RFC-001](../rfcs/RFC-001-postgresql-backend.md) |
| **Título**  | PostgreSQL Backend (`PostgresStore`) |
| **Estado**  | Draft |
| **Audiencia** | IT / DevOps, Desarrolladores del proyecto, Contribuyentes |
| **Creado**  | 2026-04-20 |

---

## Índice

- [Problema](#problema)
- [Objetivo](#objetivo)
- [Usuarios y contexto de uso](#usuarios-y-contexto-de-uso)
- [Requisitos funcionales](#requisitos-funcionales)
- [Requisitos no funcionales](#requisitos-no-funcionales)
- [Fuera del alcance](#fuera-del-alcance)
- [Criterios de aceptación](#criterios-de-aceptación)
- [Impacto en experiencia del usuario](#impacto-en-experiencia-del-usuario)
- [Impacto en documentación](#impacto-en-documentación)
- [Impacto en CI/CD](#impacto-en-cicd)
- [Dependencias](#dependencias)
- [Plan de rollout](#plan-de-rollout)
- [Métricas de éxito](#métricas-de-éxito)

---

## Problema

### Situación actual

`engram-dotnet` usa **SQLite** como único backend de persistencia. En modo servidor compartido para equipos, el servidor central mantiene un único `SqliteStore` que recibe escrituras de todos los desarrolladores del equipo simultáneamente.

### Dolor concreto

```
Dev 1 (juan.perez)    → mem_save() ─┐
Dev 2 (ana.gomez)     → mem_save() ─┤→ SqliteStore (UN solo lock)
Dev 3 (victor.silgado)→ mem_save() ─┘
```

Con 3 devs guardando memorias al mismo tiempo, SQLite serializa las escrituras. A 10+ devs activos, la latencia de escritura aumenta perceptiblemente. A 20+ devs con herramientas de IA generando memorias en batch, aparecen errores `SQLITE_BUSY`.

### Por qué importa ahora

El backlog de `engram-dotnet` incluye casos de uso pensados para **equipos medianos y grandes** (15-50+ devs compartiendo una instancia del servidor). SQLite no fue diseñado para ese escenario.

---

## Objetivo

Permitir que `engram-dotnet` use **PostgreSQL como backend de persistencia** alternativo, manteniendo SQLite como opción predeterminada para uso local y entornos simples.

**Objetivo principal**: un equipo de 50 devs puede correr `engram serve` apuntando a un PostgreSQL managed (Neon, Supabase, RDS, Azure DB for PostgreSQL) sin degradación de performance ni errores de concurrencia.

**Objetivo secundario**: habilitar deployments de alta disponibilidad con las herramientas estándar del ecosistema PostgreSQL.

---

## Usuarios y contexto de uso

### Persona A — IT / DevOps que despliega el servidor del equipo

**Contexto**: Despliega y mantiene el servidor `engram` compartido. Actualmente corre en TrueNAS SCALE con Docker.

**Necesidad**: Poder apuntar `engram serve` a un PostgreSQL existente (ya tienen PG en infraestructura). Tener backup automático via herramientas estándar. Poder escalar sin preocuparse por el lock de SQLite.

**Interacción con la feature**:
```bash
# docker-compose.yml actualizado
environment:
  - ENGRAM_DB_TYPE=postgres
  - ENGRAM_PG_CONNECTION=Host=db;Database=engram;Username=engram;Password=secret
```

### Persona B — Dev que usa el servidor del equipo

**Contexto**: Usa Cursor o VS Code con el MCP de engram apuntando al servidor del equipo.

**Interacción con la feature**: **ninguna** — el cambio es completamente transparente. El agente sigue llamando `mem_save`, `mem_search`, etc. Ni el protocolo MCP ni las herramientas cambian.

### Persona C — Contribuyente al proyecto

**Contexto**: Quiere agregar features o corregir bugs en `engram-dotnet`.

**Necesidad**: Tests de `PostgresStore` corren en CI. Existe un patrón claro para agregar métodos a ambos stores.

---

## Requisitos funcionales

### RF-01 — Selección de backend via variable de entorno

**Como** IT que despliega el servidor,  
**quiero** poder seleccionar el backend con una variable de entorno,  
**para que** no necesite recompilar ni cambiar código.

```bash
# SQLite (default — comportamiento actual)
./engram serve

# PostgreSQL
ENGRAM_DB_TYPE=postgres \
ENGRAM_PG_CONNECTION="Host=localhost;Database=engram;Username=engram;Password=secret" \
./engram serve
```

**Criterio**: si `ENGRAM_DB_TYPE=postgres` y `ENGRAM_PG_CONNECTION` no está seteado, el servidor falla con un mensaje de error claro:
```
[engram] error: ENGRAM_PG_CONNECTION is required when ENGRAM_DB_TYPE=postgres
```

---

### RF-02 — `PostgresStore` implementa toda la interfaz `IStore`

**Como** desarrollador del proyecto,  
**quiero** que `PostgresStore` implemente los 22 métodos de `IStore`,  
**para que** sea intercambiable con `SqliteStore` sin cambios en el resto del sistema.

Los 22 métodos están documentados en [`IStore.cs`](../../src/Engram.Store/IStore.cs).

---

### RF-03 — Full-text search funcional

**Como** agente de IA usando `mem_search`,  
**quiero** que la búsqueda funcione correctamente en PostgreSQL,  
**para que** pueda recuperar memorias relevantes con las mismas queries que uso en SQLite.

**Implementación**: `tsvector` con `GIN index` y `plainto_tsquery('simple', @query)`.

**Criterio**: una query que devuelve N resultados en SQLite debe devolver resultados equivalentes en PostgreSQL (mismo contenido, no necesariamente mismo orden de ranking).

---

### RF-04 — Deduplicación equivalente a SQLite

**Como** servidor de equipo,  
**quiero** que la lógica de deduplicación funcione igual en PostgreSQL,  
**para que** no aparezcan duplicados al migrar.

Los tres caminos de deduplicación (topic_key upsert, hash window, fresh insert) deben funcionar idénticamente.

---

### RF-05 — Migración SQLite → PostgreSQL sin pérdida de datos

**Como** IT que migra un servidor existente de SQLite a PostgreSQL,  
**quiero** un proceso documentado de migración,  
**para que** pueda migrar con riesgo mínimo.

**Proceso**:
```bash
1. engram export backup.json
2. ENGRAM_DB_TYPE=postgres ENGRAM_PG_CONNECTION=... engram import backup.json
3. engram stats  # verificar conteos
```

**Criterio**: los conteos de sessions, observations y prompts deben coincidir entre SQLite y PostgreSQL después de la migración.

---

### RF-06 — Schema idempotente (migrations automáticas)

**Como** IT,  
**quiero** que el servidor aplique el schema automáticamente al arrancar,  
**para que** no tenga que ejecutar scripts SQL manualmente.

**Criterio**: `CREATE TABLE IF NOT EXISTS`, `ALTER TABLE ADD COLUMN IF NOT EXISTS` — igual que SQLite.

---

### RF-07 — Docker Compose con PostgreSQL companion

**Como** IT que usa Docker,  
**quiero** un `docker-compose.yml` con PostgreSQL como servicio companion,  
**para que** pueda levantar el stack completo con un solo comando.

```yaml
services:
  engram:
    image: engram-dotnet
    environment:
      - ENGRAM_DB_TYPE=postgres
      - ENGRAM_PG_CONNECTION=Host=db;Database=engram;Username=engram;Password=secret
    depends_on:
      db:
        condition: service_healthy

  db:
    image: postgres:16
    environment:
      POSTGRES_DB: engram
      POSTGRES_USER: engram
      POSTGRES_PASSWORD: secret
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U engram"]
```

---

## Requisitos no funcionales

### RNF-01 — Concurrencia

`PostgresStore` debe soportar al menos **50 escrituras concurrentes** sin errores. PostgreSQL con Npgsql connection pool (`MaxPoolSize=50`) debe ser suficiente para este caso de uso.

### RNF-02 — Latencia de escritura

Latencia p95 de `mem_save` en PostgreSQL local < 50ms. En PostgreSQL remoto (mismo datacenter) < 100ms.

### RNF-03 — Compatibilidad de datos

`ExportData` producido por `SqliteStore.ExportAsync()` debe poder importarse con `PostgresStore.ImportAsync()` sin pérdida ni corrupción de datos.

### RNF-04 — Sin breaking changes en IStore ni en el protocolo MCP

`IStore` no cambia. Las 15 herramientas MCP no cambian. La HTTP API no cambia. El cambio es completamente interno al backend.

### RNF-05 — Tests de paridad

La suite de tests parametrizados debe cubrir al menos el **90% de los métodos de `IStore`** corriendo contra `PostgresStore`.

### RNF-06 — Versión mínima de PostgreSQL

PostgreSQL **15 o superior** — requerido para `GENERATED ALWAYS AS` en columnas `tsvector STORED`.

### RNF-07 — Binario self-contained

El binario publicado debe seguir siendo self-contained (`dotnet publish --self-contained`). Npgsql debe incluirse en el binario — no requiere instalación separada.

---

## Fuera del alcance

| Tema | Justificación |
|------|---------------|
| pgvector / búsqueda semántica | Requiere Python o un ciclo SDD propio — mejora futura |
| Multi-tenant a nivel de schema PG | Overkill para el caso de uso actual |
| Réplica / streaming replication | Responsabilidad del operador, no de la librería |
| EF Core Migrations | Ver ADR-001 — se mantiene SQL directo |
| MySQL / MariaDB / SQL Server | No hay demand. PG es el estándar del ecosistema .NET para casos de uso similares |
| Eliminar SQLite | SQLite sigue siendo el default para modo local |

---

## Criterios de aceptación

### CA-01 — Selección de backend
- [ ] `ENGRAM_DB_TYPE=sqlite` (o ausente) → `SqliteStore`
- [ ] `ENGRAM_DB_TYPE=postgres` con `ENGRAM_PG_CONNECTION` presente → `PostgresStore`
- [ ] `ENGRAM_DB_TYPE=postgres` sin `ENGRAM_PG_CONNECTION` → error descriptivo y exit code no-0

### CA-02 — Funcionalidad completa
- [ ] `engram serve` con PG backend responde `/health` en < 500ms
- [ ] `mem_save` persiste y `mem_search` recupera en PostgreSQL
- [ ] `mem_context` devuelve contexto correcto (team + personal) desde PG
- [ ] Deduplicación por topic_key funciona en PG
- [ ] Deduplicación por hash + ventana de tiempo funciona en PG

### CA-03 — Tests de paridad
- [ ] Todos los tests de `Engram.Store.Tests` pasan con `PostgresStore`
- [ ] Tests corren en CI (GitHub Actions) con Testcontainers o servicio PG

### CA-04 — Migración
- [ ] Export de SQLite → Import en PostgreSQL → `engram stats` muestra conteos idénticos

### CA-05 — Docker
- [ ] `docker compose up` con el compose provisto levanta engram + PG
- [ ] Engram arranca correctamente después de que PG está healthy

### CA-06 — Documentación
- [ ] `docs/POSTGRES-SETUP.md` cubre: requisitos, variables de entorno, Docker Compose, migración desde SQLite, verificación
- [ ] `docs/ARCHITECTURE.md` actualizado con el nuevo backend en el diagrama

---

## Impacto en experiencia del usuario

### Dev (Persona B)

**Impacto: ninguno.** El protocolo MCP, las herramientas, y los prompts de los agentes no cambian. El dev no sabe (ni necesita saber) si el servidor usa SQLite o PostgreSQL.

### IT / DevOps (Persona A)

**Nuevo workflow opcional**:
1. Provisionar PostgreSQL (managed o self-hosted)
2. Agregar `ENGRAM_DB_TYPE=postgres` y `ENGRAM_PG_CONNECTION` al compose
3. Migrar datos (una sola vez, si ya había datos en SQLite)
4. Operar igual que antes — PG se comporta como cualquier otro servicio PG del equipo

**Sin cambio en el workflow existente**: quien no configura `ENGRAM_DB_TYPE` sigue usando SQLite sin diferencia.

---

## Impacto en documentación

| Documento | Cambio |
|-----------|--------|
| `docs/POSTGRES-SETUP.md` | **Nuevo** — guía completa para IT |
| `docs/ARCHITECTURE.md` | Actualizar diagrama, agregar sección backends |
| `docs/DEPLOYMENT.md` | Agregar sección PostgreSQL al lado de la sección SQLite existente |
| `docker/README.md` | Agregar ejemplo con PG |
| `README.md` | Agregar `ENGRAM_DB_TYPE` y `ENGRAM_PG_CONNECTION` en la tabla de variables |

---

## Impacto en CI/CD

El pipeline de GitHub Actions necesita:

```yaml
services:
  postgres:
    image: postgres:16
    env:
      POSTGRES_DB: engram_test
      POSTGRES_USER: engram
      POSTGRES_PASSWORD: test
    options: >-
      --health-cmd pg_isready
      --health-interval 10s
      --health-timeout 5s
      --health-retries 5
```

O alternativamente, usar `Testcontainers.PostgreSQL` en los tests (levantar PG en proceso durante los tests).

---

## Dependencias

| Dependencia | Tipo | Notas |
|-------------|------|-------|
| `Npgsql` NuGet | Nueva — `Engram.Store` | Versión 8.x (compatible .NET 10) |
| PostgreSQL 15+ | Infraestructura | Solo para modo PG — no afecta modo SQLite |
| `Testcontainers.PostgreSQL` (opcional) | Dev/test | Para tests de integración automáticos |

---

## Plan de rollout

### Fase 1 — Implementación (este RFC/PRD)
- `PostgresStore` implementado
- Tests de paridad
- Docker Compose actualizado
- Documentación

### Fase 2 — Hardening (siguiente iteración)
- Benchmarks de concurrencia documentados
- TTL configurable por tipo (RFC/PRD separado — mejora #3 del backlog)

### Fase 3 — HA (opcional, largo plazo)
- Guía de setup con réplica de lectura
- Integración con `postgres_exporter` para Prometheus

---

## Métricas de éxito

| Métrica | Target |
|---------|--------|
| Tests de paridad pasando en CI | 100% |
| Latencia p95 `mem_save` (PG local) | < 50ms |
| Errores de concurrencia con 20 escrituras paralelas | 0 |
| Tiempo de migración SQLite → PG (base de datos < 10k observations) | < 5 minutos |
| Issues post-release relacionados con PG backend | < 3 en los primeros 30 días |

---

*Este PRD define el qué y el para qué. Los specs formales y el task breakdown técnico se escriben en una fase posterior usando el flujo SDD.*
