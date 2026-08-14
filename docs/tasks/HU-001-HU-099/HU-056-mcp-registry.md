# HU-056 — MCP registry (visionario)

**ENG:** ENG-486  
**Tipo:** Feature  
**Prioridad:** P2  
**Esfuerzo:** XL (>2 semanas, multi-meses)  
**Estado:** Deferred  
**Origen:** ← feature ideas 2026-08 (ENGRAM-IDEA-007)

---

## Problema que resuelve

Si engram se convierte en el **standard memory layer para AI coding tools**, el siguiente paso natural es un **registry de memorias compartidas**.

Como npm para packages, pero para memorias: desarrolladores publican patrones/anti-patrones/decisiones útiles, otros los importan.

**Los equipos reinventan los mismos patrones.** "¿Cómo estructuramos CQRS en C#?" ha sido respondido 10,000 veces. engram podría ser el medio para compartir esas respuestas.

---

## Propuesta de solución

Un **registry público de memory packages**, browseable e importable.

### Conceptos clave

1. **Memory package**: colección de memorias relacionadas
   ```
   @engram/csharp-cqrs-patterns
   ├── decision: usamos MediatR para CQRS
   ├── convention: commands en Application/Commands/
   ├── convention: queries en Application/Queries/
   ├── insight: CQRS no necesita event sourcing
   └── gotcha: no compartir command/query handlers entre bounded contexts
   ```

2. **Publisher**: desarrollador o equipo que publica packages
   - Verificación de identidad (email, GitHub OAuth)
   - Reputation system (upvotes, downloads)

3. **Consumer**: desarrollador que importa packages
   ```bash
   engram registry install @engram/csharp-cqrs-patterns
   # → importa 5 memorias a tu engram local
   ```

4. **Registry server**: backend que hostea el registry
   - API REST para publish/import/search
   - Database centralizada (Postgres)
   - CDN para distribución

### Arquitectura de alto nivel

```
┌─────────────────┐
│  engram CLI     │
│  (consumer)     │
└────────┬────────┘
         │ import
         ▼
┌─────────────────┐      ┌─────────────────┐
│  Registry API   │◄────►│  Postgres DB    │
│  (backend)      │      │  (packages)     │
└────────┬────────┘      └─────────────────┘
         │ publish
         ▼
┌─────────────────┐
│  engram CLI     │
│  (publisher)    │
└─────────────────┘
```

---

## Criterios de aceptación (visión a 1 año)

- [ ] **Registry API**:
  - [ ] `POST /packages` — publicar package
  - [ ] `GET /packages/{name}` — obtener package
  - [ ] `GET /packages?search={query}` — buscar packages
  - [ ] `GET /packages/{name}/versions` — listar versiones
  - [ ] Authentication (GitHub OAuth)
  - [ ] Rate limiting

- [ ] **CLI integration**:
  - [ ] `engram registry publish` — publicar package desde memorias locales
  - [ ] `engram registry install <package>` — importar package
  - [ ] `engram registry search <query>` — buscar packages
  - [ ] `engram registry list` — listar packages instalados

- [ ] **Web UI** (opcional):
  - [ ] Browse packages en web
  - [ ] Ver detalles de package (memorias, author, downloads)
  - [ ] Search con filtros (language, framework, topic)

- [ ] **Quality & moderation**:
  - [ ] Spam detection
  - [ ] Report system para contenido inapropiado
  - [ ] Verified publishers (badge)

- [ ] **Tests**:
  - [ ] API integration tests
  - [ ] CLI tests (publish/install/search)
  - [ ] Load testing (1K packages, 10K downloads/day)

- [ ] **Documentación**:
  - [ ] Registry API docs
  - [ ] Guide: "How to publish your first package"
  - [ ] Guide: "How to import packages"

---

## Implementación técnica

### Fase 1 — MVP (3-4 meses)

1. **Registry backend**:
   - ASP.NET Core Minimal API
   - Postgres database
   - Authentication (GitHub OAuth)
   - Basic CRUD para packages

2. **CLI integration**:
   - `engram registry publish/install/search`
   - Package format: JSON con array de memorias

3. **Deploy**:
   - Docker container
   - Hostear en TrueNAS o cloud (AWS/GCP)

### Fase 2 — Quality & scale (2-3 meses)

1. **Moderation**:
   - Spam detection
   - Report system
   - Verified publishers

2. **Search**:
   - Full-text search (Postgres FTS)
   - Filters (language, framework, topic)

3. **Analytics**:
   - Download counts
   - Popular packages
   - Trending

### Fase 3 — Web UI & ecosystem (2-3 meses)

1. **Web UI**:
   - Browse packages
   - View details
   - Search

2. **Ecosystem**:
   - SDK para otros tools (FlowForge, Cursor, etc.)
   - API pública para integraciones

---

## Fuera de alcance (por ahora)

- ❌ Monetización (paid packages, subscriptions) — future
- ❌ Private packages (team-only) — future
- ❌ Package dependencies (package A depends on package B) — future
- ❌ Versioning semántico estricto — future
- ❌ CI/CD integration (auto-publish en cada commit) — future

---

## ¿Por qué está deferred?

**Este es un proyecto multi-meses, no una feature.**

Razones para deferir:

1. **No tienes 1K usuarios activos aún** — el registry necesita network effects para ser útil
2. **Requiere infraestructura dedicada** — backend, database, CDN, moderation
3. **Requiere comunidad** — necesitas que desarrolladores quieran compartir sus memorias
4. **Competencia** — si alguien más lo hace primero, puedes integrar en vez de construir

**Cuándo re-visitar:**
- Cuando engram tenga >1K usuarios activos
- Cuando recibas requests de usuarios: "¿puedo compartir mis memorias con mi equipo?"
- Cuando tengas tiempo dedicado (no como side project)

---

## Métricas de éxito (a 1 año)

- **Packages publicados**: 100+ packages en el registry
- **Downloads**: 10K+ downloads totales
- **Active publishers**: 50+ desarrolladores publicando
- **Active consumers**: 500+ desarrolladores importando
- **Quality**: <5% de packages reportados como spam/inapropiado

---

## Dependencias

- 🔴 **Requiere:** engram con >1K usuarios activos
- 🔴 **Requiere:** infraestructura dedicada (backend, database, CDN)
- 🔴 **Requiere:** comunidad activa (publishers + consumers)
- 🔗 **Relacionado:** ENG-484 (code-context queries) — packages podrían ser code-aware

---

## Riesgos

1. **Chicken-and-egg problem**: el registry no es útil sin packages, los publishers no publican sin consumers
2. **Quality control**: moderación de contenido es caro y complicado
3. **Scaling**: si el registry crece, necesitas infraestructura robusta
4. **Monetization**: ¿cómo se sostiene económicamente? (ads? paid packages? donations?)

---

## Alternativas

**En vez de construir tu propio registry, podrías:**

1. **Integrar con npm/GitHub**:
   - Packages como npm packages
   - `npm install @engram/csharp-cqrs-patterns`
   - Ventaja: infraestructura ya existe
   - Desventaja: menos control sobre UX

2. **Integrar con GitHub**:
   - Packages como GitHub repos
   - `engram registry install github.com/user/csharp-cqrs-patterns`
   - Ventaja: GitHub ya tiene auth, search, moderation
   - Desventaja: menos integrado con engram

3. **No hacer registry**:
   - Enfocarse en ser el mejor memory server local
   - Dejar sharing a otros tools (FlowForge, etc.)
   - Ventaja: menos complejidad
   - Desventaja: pierdes network effects

---

## Referencias

- [ENGRAM-IDEA-007](/mnt/86FC44B0FC449BF5/Proyectos/Desarrollo/engram-feature-ideas.md#engram-idea-007--engram-as-a-community-mcp-registry-visionary) — idea original
- [ENG-486 en BACKLOG](../../BACKLOG.md#eng-486)
