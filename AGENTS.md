# AGENTS.md — engram-dotnet

Guía para agentes de IA que trabajan en este repositorio.  
Para colaboración multi-usuario con Engram (sync, MCP, scopes), leer primero [`docs/AGENT-PROTOCOL.md`](docs/AGENT-PROTOCOL.md).

---

## 1. Proyecto

| Campo | Valor |
|-------|-------|
| **Nombre** | engram-dotnet |
| **Qué es** | Backend de memoria persistente para agentes (.NET 10) |
| **Stack** | C# / .NET 10, ASP.NET Core Minimal APIs, SQLite / PostgreSQL / HttpStore |
| **Fuente de trabajo** | [`docs/BACKLOG.md`](docs/BACKLOG.md) (orden) + [`docs/ROADMAP.md`](docs/ROADMAP.md) (visión) |

---

## 2. Estructura del repositorio

```
src/                          ← Código fuente (.NET)
tests/                        ← Tests (xUnit)
docs/                         ← Documentación activa (fuente de verdad)
├── architecture/
│   ├── adr/                  ← Decisiones arquitectónicas (ADR-XXX)
│   └── rfc/                  ← Propuestas y requisitos (RFC-XXX, PRD-XXX)
├── tasks/HU-001-HU-099/      ← Historias de usuario / features documentadas
├── BACKLOG.md                ← Cola de ejecución (ENG-XXX)
├── ROADMAP.md                ← Fases y prioridades
├── GIT-WORKFLOW.md           ← Ramas, commits, PRs
└── *.md                      ← Guías operativas (API, setup, testing, etc.)

sdd/                          ← Legacy SDD (specs por feature; migración gradual a docs/)
config/                       ← Config de editores/MCP (no lógica de producto)
.github/workflows/            ← CI (dotnet build/test) — no depende de sdd/ ni openspec/
AGENTS.md                     ← Este archivo
```

**Transición documental:** `docs/` es la ubicación objetivo. `sdd/` sigue existiendo y muchos enlaces del backlog apuntan ahí; al migrar contenido, mover a `docs/tasks/` o `docs/architecture/` y actualizar enlaces — no duplicar.

---

## 3. Sistema de documentación

### 3.1 Dónde va cada cosa

| Tipo | Ubicación | Cuándo usarlo |
|------|-----------|---------------|
| Feature / bug / refactor | `docs/tasks/HU-001-HU-099/HU-XXX-nombre.md` | Trabajo con requisitos y escenarios |
| Decisión arquitectónica | `docs/architecture/adr/ADR-XXX-titulo.md` | Decisión tomada e irreversible |
| Propuesta en discusión | `docs/architecture/rfc/RFC-XXX-titulo.md` | Antes de implementar |
| Orden de trabajo | `docs/BACKLOG.md` | Ítems ENG-XXX con estado y prioridad |
| Guías operativas | `docs/*.md` | Setup, API, testing manual, etc. |

### 3.2 Qué NO imponemos

- El ciclo de 15 días de FlowDoc (`docs/flowdoc-ciclo.md`, si existe) es **referencia opcional**, no proceso obligatorio del equipo.
- No crear documentación nueva fuera de `docs/` salvo artefactos temporales acordados en `.ai-work/`.

### 3.3 Al cerrar trabajo

Seguir [`docs/GIT-WORKFLOW.md`](docs/GIT-WORKFLOW.md) y actualizar, según aplique:

- `docs/BACKLOG.md` — estado del ítem ENG-XXX
- `docs/ROADMAP.md` — si cambia visión o fase
- `CHANGELOG` — si el cambio es user-facing
- HU o spec relacionada — marcar criterios cumplidos

---

## 4. Convenciones de trabajo

### 4.1 Git y ramas

Ver [`docs/GIT-WORKFLOW.md`](docs/GIT-WORKFLOW.md). Resumen:

| Rama | Uso |
|------|-----|
| `main` | Siempre desplegable; CI en cada push/PR |
| `feat/...` | Features |
| `fix/...` | Bugfixes |
| `docs/...` | Solo documentación |
| `chore/...` | CI, deps, tooling |

Incluir `ENG-XXX` en el cuerpo del PR cuando corresponda.

### 4.2 Commits (Conventional Commits)

```
feat: add backend field to /health and /stats
fix: resolve postgres index creation order
docs: update BACKLOG for ENG-207
test: add skipped postgres integration cases
chore: pin ModelContextProtocol to 1.3.0
```

### 4.3 Nomenclatura documental

```
HU-XXX: nombre-corto-kebab-case
ADR-XXX: titulo-de-la-decision
RFC-XXX: titulo-de-la-propuesta
ENG-XXX: titulo en BACKLOG
```

Un HU por tema. Si dos HUs cubren el mismo origen (`sdd/...`), consolidar en una sola antes de mergear.

---

## 5. Reglas del agente

### El agente NO debe

- Hacer commits, push ni merge a `main` **salvo que el humano lo pida explícitamente**
- Modificar `AGENTS.md` sin aprobación humana
- Modificar `docs/BACKLOG.md` o specs sin contexto del cambio de código
- Force-push a `main`
- Añadir abstracciones innecesarias (MediatR, CQRS, capas extra) — ver [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- Expandir el alcance más allá de lo pedido

### El agente SÍ debe

- Leer `docs/BACKLOG.md` y specs/HU relevantes antes de implementar
- **Pasar el workflow T1-T5 antes de proponer commit/push** (ver [§ Workflow T1-T5](#workflow-de-desarrollo-capas-t1-t5))
- Mantener cambios mínimos y alineados con el estilo existente
- Seguir la política de comentarios `///` en [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md#comentarios-xml-en-código) — canónica en [FlowForge ADR-002](../FlowForge/docs/decisions/ADR-002-scaffold-doc-policy.md); adopción local [ADR-003](docs/architecture/adr/ADR-003-xml-doc-comment-policy.md)
- Ejecutar tests localmente antes de proponer merge
- Documentar decisiones no obvias en el PR o en `docs/` cuando el humano lo pida
- Responder en español al usuario salvo que pida otro idioma

---

## Workflow de desarrollo (capas T1-T5)

> **Regla**: Antes de proponer `commit` o `push`, el código DEBE haber pasado **T3 (Docker + Postgres integration)**. SQLite no es prod — bugs SQLite-specific se escapan (ej: el bug pre-existente del SyncManager, encontrado el 2026-06-05 en `SqliteStore.cs:1938`).

| Capa | Qué | Backend | Por qué importa |
|------|-----|---------|-----------------|
| **T1** | Iterar código | SQLite (en memoria) | Loop rápido <5s |
| **T2** | Tests unitarios | SQLite (in-memory) | Verifica lógica pura |
| **T3** | **Pre-commit (Docker + host Postgres)** | **Postgres** | **Atrapa bugs SQLite-specific** |
| T4 | CI (GitHub Actions) | SQLite + Postgres (Testcontainers) | Automático, paralelo a T3 |
| T5 | Deploy (TrueNAS) | Postgres | Manual, humano |

**Comandos rápidos:**

```bash
# T1: Loop rápido
dotnet run --project src/Engram.Cli -c Release -- serve --port 7438

# T2: Tests
dotnet test -c Release --filter "FullyQualifiedName!~Engram.Postgres.Tests&Category!=RequiresDocker"

# T3: Pre-commit (integración con Postgres real)
PG_PASS=tu_password bash scripts/dev-test.sh
```

**Detalle completo** en [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md#workflow-de-desarrollo-t1-t5).

---

## 6. Tests

| Aspecto | Detalle |
|---------|---------|
| Framework | xUnit (`tests/*.Tests/`) |
| CI | `.github/workflows/ci.yml` — SQLite + PostgreSQL (Testcontainers) |
| Comando rápido (sin Docker) | `dotnet test -c Release --filter "FullyQualifiedName!~Engram.Postgres.Tests&Category!=RequiresDocker"` |
| Postgres (requiere Docker) | `dotnet test tests/Engram.Postgres.Tests/Engram.Postgres.Tests.csproj -c Release` |

Cada cambio de código debe incluir o actualizar tests cuando el comportamiento lo requiera.

---

## 7. Documentos clave

| Documento | Para qué |
|-----------|----------|
| [`docs/01-QUICK-START.md`](docs/01-QUICK-START.md) | Primer arranque |
| [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md) | Estructura de código y comandos dev |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Diseño del sistema |
| [`docs/API-REFERENCE.md`](docs/API-REFERENCE.md) | Endpoints REST |
| [`docs/BACKLOG.md`](docs/BACKLOG.md) | Qué hacer ahora |
| [`docs/GIT-WORKFLOW.md`](docs/GIT-WORKFLOW.md) | Flujo git/PR |
| [`docs/AGENT-PROTOCOL.md`](docs/AGENT-PROTOCOL.md) | Sync multi-usuario y MCP |
| [`docs/MANUAL-TESTING-CHECKLIST.md`](docs/MANUAL-TESTING-CHECKLIST.md) | Verificación manual de API |

---

**Última actualización:** 2026-06-05
