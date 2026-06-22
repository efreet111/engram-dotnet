# HU-001: Backend Configuration Switch

**Status**: 🟡 In Progress
**Owner**: @owner
**Created**: 2026-06-01
**Priority**: Medium
**Effort**: 2-3h (Nivel 1 + Nivel 3), 4-6h (Nivel 2 completo)
**Origin**: Migrated from `sdd/backend-config-switch/`

---

## 🎯 Intent

Crear claridad sobre qué backend está activo (PostgreSQL, SQLite, o HTTP remoto) y proporcionar un mecanismo simple para identificar/configurar el backend.

---

## 📋 Scope

### In Scope
- Documentación clara de decisión de backend (Nivel 1)
- Campo `backend` en responses del servidor (Nivel 3)
- Diseño del config file (Nivel 2 — solo diseño, no implementación)

### Out of Scope
- Implementación del parser de config file
- Migración automática entre backends
- UI para cambiar backend

---

## ✅ Requirements

### MUST

- [ ] Documentación incluye diagrama de decisión con los 3 backends
- [ ] `/health` incluye campo `"backend"` identificable
- [ ] `/stats` incluye campo `"backend"` identificable
- [ ] Un desarrollador nuevo puede responder "¿qué backend estoy usando?" en < 30 segundos

### SHOULD

- [ ] El diseño del config file está documentado (aunque no implementado)

---

## 🧪 Scenarios

### Scenario: Backend identification via curl
- GIVEN a user running the server
- WHEN they run `curl http://localhost:7437/stats`
- THEN they can see `"backend": "postgres"|"sqlite"|"http"` in the response

### Scenario: Backend decision tree in docs
- GIVEN a new developer reading the documentation
- WHEN they look for "what backend am I using?"
- THEN they find a clear decision tree: ENGRAM_URL → HttpStore, ENGRAM_DB_TYPE=postgres → PostgresStore, otherwise → SqliteStore

### Scenario: Backend indicator in all responses
- GIVEN any API response
- WHEN the client receives it
- THEN the response includes `{ ..., "backend": "..." }` field

---

## 📦 Affected Areas

- `docs/DEPLOYMENT.md` — document backend decision tree
- `docs/README.md` — document backend identification
- `src/Engram.Server/HealthEndpoints.cs` — add backend field to `/health`
- `src/Engram.Server/StatsEndpoints.cs` — add backend field to `/stats`

---

## 🔗 Origin

Migrated from `sdd/backend-config-switch/` (proposal ready)

Original proposal: `sdd/backend-config-switch/proposal.md`

---

## 📝 Notes

**Niveles de implementación:**

| Nivel | Description | Effort |
|-------|-------------|--------|
| 1 | Documentación clara (diagrama de decisión) | 1-2h |
| 2 | Config file `~/.engram/config.json` | 4-6h |
| 3 | Backend indicator en responses | 1-2h |

**Recomendación del proposal**: Implementar Nivel 1 + Nivel 3 ahora. Nivel 2 para cuando haya más demanda.

---

## 🔄 Migration Reference

Original location: `sdd/backend-config-switch/`
Current status: Migrated to FlowDoc

See `sdd/README.md` for full migration mapping.