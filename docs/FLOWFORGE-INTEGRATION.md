# Guía de Integración FlowForge ↔ Engram (ENG-412)

**Fecha:** 2026-09-04  
**Feature:** ENG-412 — Memory Taxonomy & Lifecycle Status  
**Audiencia:** Agentes de FlowForge (forge-arch, forge-dev, forge-verify, forge-memory)

---

## Resumen

ENG-412 introduce un sistema de lifecycle status para decisiones arquitectónicas en Engram. Esta guía explica cómo los agentes de FlowForge deben usar las nuevas features para mantener la memoria del proyecto actualizada y coherente.

## Cambios Principales

### 1. Campo `status` en Observations
- **`active`**: Decisión vigente (default)
- **`deprecated`**: Decisión obsoleta
- **`deleted`**: Soft-delete

### 2. Nuevos Parámetros en `mem_search`
- `status`: Filtrar por estado específico
- `include_deprecated`: Incluir decisiones obsoletas
- `grouped`: Agrupar resultados por `topic_key`

### 3. Nuevo Tool: `mem_decision_tree`
Muestra panorama de decisiones agrupadas por componente/tema.

### 4. `mem_update` con `status`
Permite marcar decisiones como `deprecated` o `deleted`.

---

## Workflows para Agentes de FlowForge

### forge-arch (Phase 1: spec.md)

#### Cuando crear una nueva decisión
```
mem_save(
  title="Authentication Strategy: JWT vs Sessions",
  content="**What**: Decidimos usar JWT...\n**Why**: Stateless, escalable...",
  type="decision",
  topic_key="decision/auth-strategy",
  project="mi-proyecto"
)
```

#### Cuando una decisión reemplaza otra
```
# 1. Crear nueva decisión
new_id = mem_save(
  title="Authentication Strategy: OAuth2 + JWT",
  content="**What**: Cambiamos a OAuth2...",
  type="decision",
  topic_key="decision/auth-strategy-oauth2",
  project="mi-proyecto"
)

# 2. Deprecar decisión antigua
mem_update(id=42, status="deprecated")

# 3. (Opcional) Vincular decisiones
mem_relations(
  observation_id=new_id,
  action="add",
  target_observation_id=42,
  type="supersedes"
)
```

#### Cuando una decisión evoluciona (mismo tema)
```
# Actualizar decisión existente usando mismo topic_key
mem_save(
  title="Authentication Strategy: JWT vs Sessions (v2)",
  content="**What**: Actualizamos la decisión...",
  type="decision",
  topic_key="decision/auth-strategy",  # MISMO topic_key → upsert
  project="mi-proyecto"
)
```

### forge-dev (Phase 3: implementation)

#### Durante la implementación
```
mem_save(
  title="Database Schema: Added status column",
  content="**What**: Agregamos columna status...",
  type="decision",
  topic_key="impl/eng-412-schema",
  project="mi-proyecto"
)
```

#### Al encontrar un bug
```
mem_save(
  title="Bug Fix: PostgreSQL migration order",
  content="**What**: El índice se creaba antes...\n**Learned**: Siempre crear índices DESPUÉS...",
  type="bugfix",
  topic_key="bugfix/eng-412-pg-migration",
  project="mi-proyecto"
)
```

### forge-verify (Phase 3b: audit)

#### Al verificar la implementación
```
# Buscar decisiones relacionadas
results = mem_search(query="ENG-412", project="mi-proyecto", grouped=True)

# Ver panorama de decisiones
tree = mem_decision_tree(project="mi-proyecto", type="decision,architecture")

# Incluir decisiones obsoletas para historial completo
history = mem_search(query="authentication", project="mi-proyecto", include_deprecated=True)
```

### forge-memory (Phase 4: closure)

#### Al cerrar la feature
```
mem_save(
  title="Session Summary: ENG-412 Implementation",
  content="**What**: Implementamos lifecycle status...\n**Learned**: La paridad SQLite/PostgreSQL...",
  type="session_summary",
  topic_key="session/eng-412-implementation",
  project="mi-proyecto"
)
```

---

## Convenciones de `topic_key`

### Estructura Recomendada
```
<categoria>/<nombre-corto>
```

### Categorías Comunes
- `decision/` — Decisiones arquitectónicas
- `impl/` — Decisiones de implementación
- `pattern/` — Patrones descubiertos
- `bugfix/` — Bugs y sus soluciones
- `session/` — Resúmenes de sesión
- `architecture/` — Decisiones de arquitectura de alto nivel

### Reglas
1. **Usar kebab-case**: `auth-strategy` no `authStrategy`
2. **Ser específico**: `decision/auth-jwt` no `decision/auth`
3. **Mantener consistencia**: Usar mismas categorías en todo el proyecto
4. **Evolución vs Reemplazo**:
   - Mismo tema → mismo `topic_key` (upsert)
   - Tema diferente → nuevo `topic_key` + deprecar antiguo

---

## Cuándo Usar Cada Tool

### `mem_decision_tree`
**Cuándo usar:**
- Onboarding a un proyecto nuevo
- Antes de tomar una decisión arquitectónica
- Review de arquitectura
- Entender el estado actual de decisiones

### `mem_search` con `grouped=True`
**Cuándo usar:**
- Buscar información sobre un tema específico
- Ver evolución de decisiones sobre un tema
- Filtrar ruido de decisiones obsoletas

### `mem_search` con `include_deprecated=True`
**Cuándo usar:**
- Auditoría completa
- Entender por qué se tomó una decisión (historial)
- Debugging de problemas relacionados con decisiones antiguas

### `mem_update` con `status`
**Cuándo usar:**
- Cuando una decisión es reemplazada por otra
- Cuando una decisión ya no es relevante
- Al cerrar una feature que deprecó decisiones anteriores

---

## Errores Comunes y Cómo Evitarlos

### Error 1: No deprecar decisiones antiguas
**Problema:** Múltiples decisiones "activas" para el mismo tema  
**Solución:** Siempre deprecar la decisión antigua al crear una nueva

### Error 2: Usar mismo `topic_key` para decisiones diferentes
**Problema:** Se sobrescribe la decisión en lugar de crear una nueva  
**Solución:** Usar `topic_key` diferentes para decisiones diferentes

### Error 3: No usar `topic_key` en absoluto
**Problema:** No se puede agrupar ni rastrear evolución  
**Solución:** Siempre usar `topic_key` con convención consistente

### Error 4: Olvidar vincular decisiones con `supersedes`
**Problema:** No hay trazabilidad de qué decisión reemplaza a cuál  
**Solución:** Usar `mem_relations` con `type="supersedes"` cuando sea apropiado

---

## Recursos Adicionales

- **ADR-015:** `docs/architecture/adr/ADR-015-memory-taxonomy-lifecycle.md`
- **Spec:** `.ai-work/eng-412-memory-taxonomy-lifecycle/spec.md` (sección 10)
- **Summary:** `.ai-work/eng-412-memory-taxonomy-lifecycle/summary.md`
- **Tests:** `tests/Engram.Store.Tests/RegressionTests.cs`

---

## Preguntas Frecuentes

### ¿Cuándo debo deprecar una decisión?
Cuando:
- Una nueva decisión reemplaza completamente la anterior
- La decisión ya no es relevante para el proyecto
- Se encontró una mejor alternativa

### ¿Cuándo debo usar mismo `topic_key` vs nuevo?
- **Mismo `topic_key`**: Cuando la decisión evoluciona pero el tema es el mismo (ej: "Auth con JWT v1" → "Auth con JWT v2")
- **Nuevo `topic_key`**: Cuando la decisión cambia fundamentalmente (ej: "Auth con JWT" → "Auth con OAuth2")

### ¿Qué pasa si no uso `topic_key`?
- No podrás agrupar decisiones relacionadas
- No podrás ver la evolución de decisiones sobre un tema
- `mem_decision_tree` no podrá mostrar el panorama completo

### ¿Puedo ver decisiones obsoletas?
Sí, usa `mem_search(include_deprecated=True)` o `mem_search(status="deprecated")`.

---

**Última actualización:** 2026-09-04
