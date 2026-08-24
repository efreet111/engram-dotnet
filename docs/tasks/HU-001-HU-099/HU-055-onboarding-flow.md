# HU-055 — Onboarding flow para teams

**ENG:** ENG-485  
**Tipo:** Feature  
**Prioridad:** P2  
**Esfuerzo:** L (1-2 días)  
**Estado:** Idea  
**Origen:** ← feature ideas 2026-08 (ENGRAM-IDEA-004)

---

## Problema que resuelve

Cuando un desarrollador se une a un equipo, pasa **semanas aprendiendo**: decisiones arquitectónicas, convenciones, "¿por qué hicimos X así?", gotchas conocidos, insights recientes.

Este conocimiento está en engram (si el equipo lo usa) pero **no está surfaceado como un flow de onboarding**.

**Onboarding es caro** (30K-50K USD por desarrollador en fully-loaded cost) y **lento** (semanas a meses para full productivity). Existing solutions (wikis, Notion docs) están desactualizados y no surfacean el conocimiento "vivido" del equipo.

**Vender engram a equipos es más fácil si resuelve un problema real y caro: onboarding.**

---

## Propuesta de solución

Un comando team-mode que genera un **resumen de onboarding** para un nuevo desarrollador:

```bash
engram onboard --user <new-dev-handle>
```

### Output generado

```markdown
# Welcome to the team! 🎉

Here's what you need to know based on our team memory:

## 🏗️ Top 10 Architectural Decisions
1. **ADR-007: Sync recovery** (2026-06-29) — Usamos server-side mutation apply...
2. **ADR-002: Sync mutation application** (2026-06-15) — ID mapping strategy...
3. ...

## 📏 Active Conventions
- **Code style**: Usamos MediatR para CQRS en todos los proyectos
- **Error handling**: Result<T> pattern en vez de exceptions
- **Testing**: xUnit + Testcontainers para integration tests
- ...

## 🚧 Known Blockers / Gotchas
- **ENG-473**: `relations add` FK constraint violation — workaround: crear session primero
- **ENG-458**: Mutaciones con project="" bloquean sync — fix en PR #20
- ...

## 💡 Recent Insights (last 30 days)
- **2026-08-10**: Descubrimos que Postgres index overflow con contenido >2704 bytes — fix en ENG-475
- **2026-08-05**: Sync-on-demand push implementado — memorias se sincronizan inmediatamente después de mem_save
- ...

## 🗺️ Where to Start
- Most-referenced files: `src/Engram.Store/SqliteStore.cs`, `src/Engram.Sync/SyncManager.cs`
- Key concepts: sync mutations, project identity, memory relations
- Recommended reading: ADR-002, ADR-007, ADR-008

---
Generated from 156 memories across 3 projects.
Last updated: 2026-08-12
```

---

## Criterios de aceptación

- [ ] `engram onboard --user <handle>`:
  - [ ] Genera resumen markdown con secciones:
    - [ ] **Top 10 Architectural Decisions**: memorias de `type = decision`, ordenadas por "importancia" (recencia + referencias)
    - [ ] **Active Conventions**: memorias de `type = convention`, ordenadas por recencia
    - [ ] **Known Blockers / Gotchas**: memorias de `type = blocker` o `gotcha`
    - [ ] **Recent Insights**: memorias de últimos 30 días, todos los tipos
    - [ ] **Where to Start**: archivos/conceptos más referenciados en memorias
  - [ ] Output formateado en markdown (piped a file o stdout)
  - [ ] Flag `--format markdown|json` para output machine-readable
  - [ ] Flag `--days <N>` para customizar ventana de "recent" (default: 30)
  - [ ] Flag `--output <file>` para guardar a archivo (default: stdout)

- [ ] **Memory importance signal** (heuristic simple):
  - [ ] Recencia: memorias de últimos 90 días pesan más
  - [ ] Type weight: `decision` > `insight` > `note` > `blocker`
  - [ ] Reference count: memorias referenciadas por otras memorias (via `mem_relations`) pesan más
  - [ ] Access count (future, requiere ENG-482 v2): memorias más leídas pesan más

- [ ] **Tests**:
  - [ ] Onboarding con SQLite (in-memory)
  - [ ] Onboarding con Postgres (Testcontainers)
  - [ ] Output markdown válido
  - [ ] Output JSON válido
  - [ ] Edge case: equipo sin memorias (0 memories)
  - [ ] Edge case: equipo con pocas memorias (<10)

- [ ] **Documentación**:
  - [ ] `docs/01-QUICK-START.md` — sección "Onboarding new team members"
  - [ ] `README.md` — mención de `engram onboard`
  - [ ] Blog post / case study: "How we reduced onboarding time by 50% with engram"

---

## Implementación técnica

### Dónde tocar código

1. **`src/Engram.Cli/Program.cs`**:
   - Nuevo comando `onboard`
   - Lógica de generación de resumen:
     - Query top decisions (ORDER BY recencia + type weight)
     - Query active conventions (ORDER BY recencia)
     - Query blockers/gotchas
     - Query recent insights (WHERE created_at > date('now', '-30 days'))
     - Query most-referenced files/symbols (COUNT references from `mem_relations`)
   - Formatear output en markdown o JSON

2. **`src/Engram.Store/ILocalSyncStore.cs`**:
   - Agregar métodos:
     - `GetTopDecisionsAsync(int limit = 10)`
     - `GetActiveConventionsAsync(int limit = 20)`
     - `GetBlockersAsync()`
     - `GetRecentInsightsAsync(int days = 30)`
     - `GetMostReferencedConceptsAsync(int limit = 10)`

3. **`src/Engram.Store/SqliteStore.cs`** y **`PostgresStore.cs`**:
   - Implementar métodos anteriores con queries SQL

4. **Tests**:
   - `tests/Engram.Cli.Tests/OnboardTests.cs` — 5-7 tests

### Edge cases a manejar

- ¿Qué pasa si el equipo no tiene memorias?
  - Output: "No memories yet. Start capturing with `engram save` or `engram init` (git hooks)."

- ¿Qué pasa si hay pocas memorias (<10)?
  - Output: "Only 5 memories found. Keep capturing to improve onboarding quality."
  - Mostrar todas las memorias disponibles

- ¿Qué pasa con memorias de proyectos diferentes?
  - Default: mostrar memorias de todos los proyectos del equipo
  - Flag `--project <name>` para filtrar por proyecto específico

- ¿Qué pasa con memorias soft-deleted?
  - NO incluirlas en el onboarding (solo activas)

---

## Fuera de alcance

- ❌ UI web interactiva para onboarding — solo CLI por ahora
- ❌ Personalización por rol (dev vs product vs design) — future enhancement
- ❌ Integración con Slack/Email para enviar onboarding automáticamente — future enhancement
- ❌ Onboarding progress tracking (qué leyó el nuevo dev) — future enhancement

---

## Cómo probarlo

```bash
# 1. Build
dotnet build -c Release

# 2. Generar onboarding
./src/Engram.Cli/bin/Release/net10.0/engram onboard --user new-dev
# → debe mostrar resumen markdown en stdout

# 3. Guardar a archivo
./src/Engram.Cli/bin/Release/net10.0/engram onboard --user new-dev --output onboarding.md
# → debe crear onboarding.md

# 4. Output JSON
./src/Engram.Cli/bin/Release/net10.0/engram onboard --user new-dev --format json
# → debe mostrar JSON válido

# 5. Customizar ventana de "recent"
./src/Engram.Cli/bin/Release/net10.0/engram onboard --user new-dev --days 60
# → debe mostrar insights de últimos 60 días

# 6. Tests
dotnet test tests/Engram.Cli.Tests/ --filter "Onboard"
```

---

## Métricas de éxito

- **Time-to-onboarding**: nuevo desarrollador puede correr `engram onboard` y tener resumen en <10 segundos
- **Quality**: nuevo desarrollador identifica 2+ cosas que habría aprendido the hard way en semana 1
- **Regenerability**: resumen es regenerable y refleja estado actual de team memory (no snapshot)
- **Business case**: equipo reporta "esto nos ahorró X semanas de onboarding"

---

## Dependencias

- ✅ Ninguna — se puede implementar inmediatamente
- 🔗 Relacionado: ENG-480 (quick-capture), ENG-481 (git hooks)
- ⚠️ **Recomendación:** Implementar después de ENG-480/481 para que teams tengan memorias
- 🔗 Beneficia de ENG-412 (Memory taxonomy) para filtrar por tipo, pero no lo requiere

---

## ¿Por qué esto importa estratégicamente?

**Este es el killer feature para team adoption.**

- **Solo devs adoptan engram** para utilidad personal
- **Equipos adoptan engram** para utilidad compartida
- **Onboarding flow es el puente** que hace la utilidad compartida obvia para non-technical stakeholders (managers, HR)

**Argumento de venta:** "engram reduces your onboarding time from 4 weeks to 1 week. That's $20K-30K savings per new hire."

---

## Referencias

- [ENGRAM-IDEA-004](/mnt/86FC44B0FC449BF5/Proyectos/Desarrollo/engram-feature-ideas.md#engram-idea-004--onboarding-flow-for-new-team-members-engram-onboard) — idea original
- [ENG-485 en BACKLOG](../../BACKLOG.md#eng-485)
