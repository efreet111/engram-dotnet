# HU-057 — Cross-functional team memory

**ENG:** ENG-487  
**Tipo:** Feature  
**Prioridad:** P2  
**Esfuerzo:** L-XL (1+ semana)  
**Estado:** Deferred  
**Origen:** ← feature ideas 2026-08 (ENGRAM-IDEA-008)

---

## Problema que resuelve

Hoy engram es **dev-focused**. Pero el mismo patrón de memoria funciona para product, design, ops.

Un equipo podría compartir memorias a través de funciones. Cada función tiene su namespace, pero las memorias core son compartidas.

**Silos de conocimiento entre dev, product, design.** La misma decisión arquitectónica, expresada en términos de dev, frecuentemente necesita re-explicación en términos de product.

---

## Propuesta de solución

Agregar **namespace support** a engram. Cada equipo/función tiene su propio memory scope, con optional cross-namespace sharing.

### Conceptos clave

1. **Namespace**: scope de memorias para una función específica
   ```
   namespace: dev
   ├── decision: usamos MediatR para CQRS
   ├── convention: commands en Application/Commands/
   └── insight: CQRS no necesita event sourcing
   
   namespace: product
   ├── decision: feature X es prioridad Q3
   ├── convention: user stories siguen formato "As a... I want... So that..."
   └── insight: users prefieren onboarding guiado vs self-service
   
   namespace: design
   ├── convention: usamos 8px grid system
   ├── decision: dark mode es default
   └── insight: users prefieren progress indicators sobre spinners
   ```

2. **Shared memories**: memorias visibles en todos los namespaces
   ```
   namespace: shared
   ├── decision: lanzamos v2.0 en Q4
   ├── convention: todos los equipos usan Slack para comunicación
   └── insight: remote-first culture requiere documentación async
   ```

3. **Cross-namespace queries**: buscar en múltiples namespaces
   ```bash
   engram search "CQRS" --namespace dev,shared
   # → busca en dev + shared
   ```

### UX propuesta

```bash
# Cambiar namespace actual
engram namespace switch dev
# → Now in namespace: dev

# Ver namespace actual
engram namespace current
# → dev

# Listar namespaces
engram namespace list
# → dev, product, design, shared

# Guardar en namespace específico
engram save --namespace product "decision: feature X es prioridad Q3"

# Buscar en múltiples namespaces
engram search "decision" --namespace dev,product,shared

# Compartir memoria con otros namespaces
engram share <memory-id> --to product,design
# → memoria ahora visible en dev, product, design
```

---

## Criterios de aceptación (visión a 6 meses)

- [ ] **Namespace management**:
  - [ ] `engram namespace switch <name>` — cambiar namespace actual
  - [ ] `engram namespace current` — mostrar namespace actual
  - [ ] `engram namespace list` — listar namespaces
  - [ ] `engram namespace create <name>` — crear nuevo namespace
  - [ ] `engram namespace delete <name>` — eliminar namespace

- [ ] **Namespace-scoped operations**:
  - [ ] `engram save` guarda en namespace actual (default: `shared`)
  - [ ] `engram search` busca en namespace actual (default: `shared`)
  - [ ] Flag `--namespace <names>` para override (ej: `--namespace dev,shared`)

- [ ] **Cross-namespace sharing**:
  - [ ] `engram share <id> --to <namespaces>` — compartir memoria con otros namespaces
  - [ ] `engram unshare <id> --from <namespaces>` — quitar acceso
  - [ ] Memorias compartidas son read-only en namespaces destino

- [ ] **Schema changes**:
  - [ ] Agregar `namespace TEXT` a tabla `observations` (si no existe ya)
  - [ ] Agregar tabla `memory_shares` (memory_id, namespace, shared_at)
  - [ ] Migración idempotente (SQLite + Postgres)

- [ ] **Tests**:
  - [ ] Namespace switching funciona
  - [ ] Scoped save/search funciona
  - [ ] Cross-namespace sharing funciona
  - [ ] ACL: memorias no compartidas no son visibles en otros namespaces
  - [ ] Backward compatibility (memorias existentes van a `shared` namespace)

- [ ] **Documentación**:
  - [ ] `docs/01-QUICK-START.md` — sección "Cross-functional memory"
  - [ ] `README.md` — mención de namespace support
  - [ ] Guide: "Setting up cross-functional memory for your team"

---

## Implementación técnica

### Fase 1 — Basic namespace support (1 semana)

1. **Schema**:
   - Agregar `namespace TEXT NOT NULL DEFAULT 'shared'` a `observations`
   - Migración idempotente

2. **CLI**:
   - `engram namespace` command (switch, current, list, create, delete)
   - `engram save --namespace <name>` flag
   - `engram search --namespace <name>` flag

3. **Store**:
   - Actualizar queries para filtrar por namespace
   - Default namespace: `shared`

### Fase 2 — Cross-namespace sharing (3-5 días)

1. **Schema**:
   - Crear tabla `memory_shares` (memory_id, namespace, shared_at, shared_by)
   - Índices para queries rápidos

2. **CLI**:
   - `engram share <id> --to <namespaces>`
   - `engram unshare <id> --from <namespaces>`

3. **Store**:
   - Métodos para compartir/descompartir
   - Queries que incluyen memorias compartidas

### Fase 3 — Advanced features (future)

1. **ACL**:
   - Per-namespace permissions (read, write, admin)
   - Team-based access control

2. **UI**:
   - Visual indicator de namespace actual
   - Multi-namespace search UI

3. **Integrations**:
   - Slack notifications cuando se comparte memoria con tu namespace
   - Email digest de nuevas memorias en tu namespace

---

## Fuera de alcance (por ahora)

- ❌ ACL granular (read/write/admin per user) — future
- ❌ UI web para explorar namespaces — future
- ❌ Integración con Slack/Email — future
- ❌ Namespace templates (pre-configured namespaces para dev/product/design) — future
- ❌ Namespace-level retention policies — future

---

## ¿Por qué está deferred?

**Resuelve un problema real pero solo importa a escala.**

Razones para deferir:

1. **Solo devs usan engram ahora** — no hay product/design/ops users aún
2. **Requiere schema changes** — namespace support es un cambio grande
3. **Requiere equipos grandes** — solo importa cuando tienes 10+ personas con múltiples funciones
4. **Complejidad de ACL** — cross-namespace sharing requiere access control robusto

**Cuándo re-visitar:**
- Cuando engram tenga equipos de 10+ personas
- Cuando recibas requests de usuarios: "¿puedo compartir memorias con product?"
- Cuando tengas product/design users interesados en engram

---

## Métricas de éxito (a 6 meses)

- **Namespaces activos**: 5+ equipos usando múltiples namespaces
- **Cross-namespace sharing**: 20%+ de memorias son compartidas entre namespaces
- **Adopción multi-función**: 30%+ de usuarios son non-dev (product, design, ops)
- **Reducción de silos**: equipos reportan "menos re-explicación de decisiones"

---

## Dependencias

- 🔴 **Requiere:** equipos de 10+ personas con múltiples funciones
- 🔴 **Requiere:** schema evolution (ENG-416) para namespace support
- 🔗 **Relacionado:** ENG-485 (onboarding flow) — onboarding podría ser namespace-aware

---

## Riesgos

1. **Complejidad de ACL**: cross-namespace sharing requiere access control robusto, es fácil introducir bugs de seguridad
2. **Schema migration**: agregar namespace support es un cambio grande, puede romper backward compatibility
3. **Adopción**: si solo devs usan engram, namespace support es over-engineering
4. **Maintenance**: múltiples namespaces incrementan complejidad de queries y testing

---

## Alternativas

**En vez de namespaces, podrías:**

1. **Tags en vez de namespaces**:
   - Usar tags existentes (`type`, `scope`) para diferenciar funciones
   - Ventaja: no requiere schema changes
   - Desventaja: menos estructurado, más propenso a errores

2. **Proyectos en vez de namespaces**:
   - Usar `project` field para diferenciar funciones
   - Ventaja: ya existe en schema
   - Desventaja: projects son para código, no para funciones

3. **Separate engram instances**:
   - Cada función tiene su propia instancia de engram
   - Ventaja: isolation, no requiere cambios
   - Desventaja: no hay sharing, duplicación de memorias

---

## Referencias

- [ENGRAM-IDEA-008](/mnt/86FC44B0FC449BF5/Proyectos/Desarrollo/engram-feature-ideas.md#engram-idea-008--cross-functional-team-memory-visionary) — idea original
- [ENG-487 en BACKLOG](../../BACKLOG.md#eng-487)
