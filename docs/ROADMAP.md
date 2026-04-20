[← Volver al README](../README.md)

# Roadmap — engram-dotnet

> Ideas y mejoras identificadas para las próximas versiones del proyecto. Este documento es un backlog vivo — las ideas se mueven a issues de GitHub cuando están listas para implementarse.

---

## 🟢 Alta Prioridad

### Diagramas de flujo + documentación VSCode

Agregar diagramas Mermaid al README y docs existentes para visualizar:
- Flujo completo: Developer → MCP plugin → EngramTools → HttpStore → Servidor → SQLite
- Modelo de namespaces team/personal
- Ciclo de vida de una observación

Actualizar `.vscode/settings.json` con configuración recomendada para el proyecto.

**Por qué**: Bajo costo, alto impacto en onboarding de nuevos desarrolladores.

---

### PostgreSQL backend

Implementar `PostgresStore` como tercer implementor de `IStore`, junto a `SqliteStore` e `HttpStore`.

**Por qué**: SQLite tiene límites reales de escritura concurrente. Con 5+ desarrolladores activos en simultáneo se genera contención. La interfaz `IStore` ya está diseñada para soportar esto — el trabajo está acotado.

**Cuándo**: Cuando el equipo supere los 5 desarrolladores activos o se detecte degradación en tiempos de escritura.

**Documentación de diseño**:
- [RFC-001 — PostgreSQL Backend](rfcs/RFC-001-postgresql-backend.md) — motivación, diseño técnico, riesgos
- [PRD-001 — PostgreSQL Backend](rfcs/PRD-001-postgresql-backend.md) — requisitos, criterios de aceptación
- [ADR-001 — SQL directo sin ORM](adr/ADR-001-no-orm.md) — decisión de no usar EF Core / Dapper

---

## 🟡 Media Prioridad

### TTL configurable por tipo de observación

Permitir configurar expiración automática por tipo:

| Tipo | TTL sugerido |
|------|-------------|
| `tool_use`, `file_change`, `command` | 30 días |
| `decision`, `architecture` | Nunca |
| `bugfix`, `pattern` | 90 días |
| `learning`, `discovery` | 60 días |

**Por qué**: Las observaciones se acumulan indefinidamente. Contexto viejo contamina búsquedas y reduce la calidad de las respuestas del agente.

---

### Herramienta administrativa acotada

Interfaz simple (TUI o CLI) para gestión de emergencia:
- Listar observaciones con filtros (project, scope, type, rango de fechas)
- Eliminar por ID o por criterio (proyecto entero, rango de fechas)
- Ver duplicados y observaciones con bajo valor

**Por qué**: El agente decide qué guardar en el flujo normal, pero hay casos donde se necesita limpieza manual — sesiones rotas, datos incorrectos, limpieza antes de archivar un proyecto.

**Alcance**: No es una UI de gestión cotidiana. Es una herramienta de emergencia.

---

### Observability básica

Endpoint `/metrics` con:
- Tiempos de respuesta por operación (search, save, context)
- Tamaño actual de la base de datos
- Queries por segundo
- Observaciones por proyecto/scope

**Por qué**: Actualmente no hay señales tempranas de degradación. Con PostgreSQL o SQLite bajo carga, saber cuándo hay un problema antes de que impacte a los agentes es crítico.

---

## 🟠 Fase Posterior

### Export Obsidian — Fase A (sin IA)

Nuevo endpoint: `GET /export/obsidian?project=X&scope=team`

Genera un `.zip` con estructura de vault de Obsidian navegable:

```
vault/
├── Architecture/        ← type: architecture, decision
├── Business Logic/      ← type: pattern, learning
├── Bugs & Fixes/        ← type: bugfix
├── Critical Points/     ← observaciones con flag CRITICAL
└── Config/              ← type: config
```

Cada observación → un archivo `.md` con frontmatter YAML.

**Consideración de seguridad**: `scope=personal` nunca se exporta sin permiso explícito del usuario. Solo `scope=team` por defecto.

**Por qué**: Hace que las memorias sean auditables por humanos, no solo por agentes. Útil para onboarding, auditorías, y documentación viva del proyecto.

---

### Export Obsidian — Fase B (con IA)

Agente especializado que toma observaciones relacionadas y genera documentos de conocimiento sintetizados:
- Agrupa bugfixes relacionados → *"Patrones de error comunes en el módulo X"*
- Agrupa decisiones de arquitectura → *"Evolución del diseño del sistema de memoria"*
- Genera glosario técnico del proyecto

**Consideración**: Requiere LLM, tiene costo de tokens. Justifica su propio ciclo SDD completo.

---

### Python port

Port del servidor HTTP a Python para equipos con stack Python-first.

**Cuándo tiene sentido**:
- Si el equipo quiere embeddings semánticos (buscar por similitud conceptual, no solo keywords)
- Si el equipo ya trabaja con Python y no quiere introducir .NET

**Cuándo NO tiene sentido**:
- Como port 1:1 del servidor HTTP actual — el beneficio es marginal
- Si el caso de uso es solo almacenamiento y búsqueda FTS5

---

## Contribuciones

Si alguna de estas ideas te interesa, abrí un issue en GitHub describiendo tu caso de uso. Las ideas con más tracción de la comunidad se convierten en issues formales y entran al pipeline de desarrollo.
