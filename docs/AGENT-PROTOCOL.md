# Engram Agent Protocol — Sync Collaboration

> **Versión**: 1.0  
> **Propósito**: Protocolo para que agentes de IA usen correctamente el sync multi-usuario.  
> **Lee esto ANTES de guardar o buscar memorias en un entorno de equipo.**

---

## 1. ¿Por Qué Existe Esto?

**Problema**: Cuando 5 desarrolladores trabajan con Engram, cada uno tiene su SQLite local. Las memorias de Victor no las ve Juan, las decisiones de Ana no las ve Pedro. El equipo no tiene una "memoria compartida".

**Solución**: Offline-First Sync. Cada developer tiene SQLite local (rápido, offline), y un servidor PostgreSQL compartido sincroniza las memorias de equipo automáticamente.

---

## 2. Reglas de Oro para Agentes

### Regla #1: Siempre especificá `scope`

```markdown
// ✅ Scope TEAM: todos lo ven
mem_save(title="Decisión: ORM", ..., project="team/mi-api", scope="team")

// ✅ Scope PERSONAL: solo vos lo ves  
mem_save(title="Debug temporal", ..., project="team/mi-api", scope="personal")
```

| Scope | Visibilidad | Sync |
|-------|-------------|------|
| `team` | Todo el equipo | ✅ Se sincroniza al servidor |
| `personal` | Solo el usuario actual | ❌ Se queda en SQLite local |

### Regla #2: En proyectos, usá prefijo `team/`

```markdown
✅ project: "team/mi-api"      → Sincronizado, visible para todos
✅ project: "team/flowforge"   → Sincronizado, visible para todos
❌ project: "mi-api"           → NO sincronizado (no tiene prefijo team/)
✅ project: "personal:debug"   → NO sincronizado (prefijo personal:)
```

### Regla #3: Antes de cerrar sesión, verificá sync

```markdown
// Verificar que las memorias se sincronizaron
mem_sync_status()

// Si hay errores:
// 1. Verificar servidor: GET /health
// 2. Verificar enrolled: GET /sync/enroll
// 3. Verificar pause: GET /sync/status → .paused_projects
```

### Regla #4: Si no encontrás memoria, buscá en el equipo

```markdown
// Buscar SOLO en tu proyecto
mem_search(query="arquitectura")

// Buscar en TODO el equipo (si tenés acceso)
mem_search(query="decisión ORM", project="team/mi-api")
```

---

## 3. Protocolo de Comunicación

### 3.1 Modelo de Datos

```
Observación = {
  id: long,              // Auto-incremental
  session_id: string,     // Sesión donde se creó
  title: string,          // Título searchable
  content: string,        // **What**...**Why**...**Where**...**Learned**
  type: string,           // decision | architecture | bugfix | pattern | learning | discovery | config
  project: string,        // team/{proyecto} o personal:{user}/{proyecto}
  scope: string,          // team | personal
  topic_key: string|null, // Para upserts (mismo topic_key = actualizar)
  created_at: string,     // ISO timestamp
}
```

### 3.2 Almacenamiento Estratificado

```
┌──────────────────────────────────────────────┐
│              NIVEL 1: OPERATIVA               │
│  Qué: sessions, debugging, tool_use, command  │
│  Dónde: SQLite local + PostgreSQL server      │
│  TTL: 30-90 días según tipo                   │
│  Quién escribe: el agente automáticamente     │
├──────────────────────────────────────────────┤
│              NIVEL 2: ESTRUCTURADA             │
│  Qué: decisiones, arquitecturas, patrones     │
│  Dónde: .md versionados en el repo + metadatos │
│  TTL: permanente (se borra con PR)            │
│  Quién escribe: Memory Agent (Fase 4) + humano │
└──────────────────────────────────────────────┘
```

### 3.3 Flujo de Sync (SyncManager)

```
1. El agente llama mem_save()
2. La observación se guarda en SQLite LOCAL (inmediato, offline-safe)
3. SyncManager detecta nueva mutación (cada 30s o por debounce)
4. SyncManager hace POST /sync/mutations/push al servidor
5. Servidor guarda en PostgreSQL (cloud_mutations)
6. Otros developers: GET /sync/mutations/pull → reciben la mutación
7. Apply local: la observación aparece en sus SQLite locales
```

### 3.4 Conflict Resolution

- **Last-write-wins**: Si dos developers guardan la misma observación, gana la más reciente
- **FK deferral**: Si una mutación falla por FK (ej: sesión no existe), se guarda en `sync_apply_deferred` y se reintenta hasta 5 veces
- **Manualmente**: Si hay conflicto, `mem_doctor` puede diagnosticar

---

## 4. Herramientas MCP (las que el agente llama)

| Herramienta | Cuándo usarla | Ejemplo |
|-------------|---------------|---------|
| `mem_save` | **Siempre** que descubrís algo | Guardar decisión, patrón, bugfix |
| `mem_search` | **Antes** de empezar una tarea | Buscar contexto de sesiones anteriores |
| `mem_context` | **Al inicio** de sesión | Recuperar sesiones previas del proyecto |
| `mem_session_summary` | **Al cerrar** sesión | Documentar lo que se hizo |
| `mem_sync_status` | **Antes de cerrar** | Verificar que el sync esté sano |
| `mem_doctor` | **Cuando algo falla** | Diagnosticar DB, HTTP, MCP |

### Ejemplo completo de flujo de agente:

```markdown
// 1. Inicio de sesión: recuperar contexto
mem_context(project="team/mi-api")

// 2. Buscar si ya hay decisiones similares
mem_search(query="autenticación JWT", project="team/mi-api")

// 3. Hacer el trabajo...
// 4. Guardar la decisión
mem_save(
  title="Decisión: usar JWT con refresh tokens",
  content="**What**: ... **Why**: ... **Where**: ... **Learned**: ...",
  type="decision",
  project="team/mi-api",
  scope="team",
  topic_key="architecture/auth-model"
)

// 5. Verificar sync
mem_sync_status()

// 6. Verificar que el equipo pueda verlo
// El SyncManager se encarga automáticamente del push al servidor
```

---

## 5. Operaciones Humanas (SysAdmin)

Para operaciones administrativas, los humanos usan:

| Operación | Comando |
|-----------|---------|
| Ver estado del sync | `engram sync status` o `curl /sync/status` |
| Ver enrollemos | `curl -H "X-Engram-User: X" /sync/enroll` |
| Enrollar proyecto | `POST /sync/enroll` |
| Pausar sync | `POST /sync/pause` |
| Diagnosticar | `engram doctor --server http://servidor:7437` |
| Ver logs | `docker logs -f engram` o `journalctl -u engram-server` |

---

## 6. Troubleshooting para Agentes

| Síntoma | Qué hacer |
|---------|-----------|
| `mem_search` no devuelve memorias del equipo | Verificar que la búsqueda incluya `project` correcto |
| `mem_sync_status` muestra errores | Ejecutar `mem_doctor` para diagnóstico completo |
| `curl /sync/enroll` devuelve 409 | El proyecto ya está inscripto — continuar normalmente |
| `curl /sync/status` devuelve `sync_enabled: false` | El SyncManager no está activo — las memorias SOLO son locales |
| Error 500 en POST | Ver logs del servidor — el error ahora se loguea completo |
