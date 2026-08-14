# HU-007: Logging Infrastructure

**Status**: 🟡 In Progress
**Owner**: @owner
**Created**: 2026-06-01
**Priority**: High (bloquea debugging en producción)
**Effort**: 2-3h

---

## As a user...

**As**: Developer
**I want**: que todos los requests HTTP y responses tengan logging estructurado
**To**: poder hacer debugging en producción cuando algo falla

---

## Acceptance Criteria

### MUST

- [ ] Middleware log ALL incoming HTTP requests (method, path, status, duration, client IP)
- [ ] Middleware log ALL outgoing responses
- [ ] 5xx errors include full error details (message, stack trace, exception type)
- [ ] Logs use structured JSON format
- [ ] POST body preview (first 1KB) logged on deserialization errors
- [ ] Global exception handler covers all routes

### SHOULD

- [ ] Non-blocking logging (no impact en request latency)

---

## Tasks (Implementation)

- [ ] Implementar request/response logging middleware en EngramServer
- [ ] Agregar body debug logging en CloudSyncEndpoints
- [ ] Implementar global exception handler con coverage total
- [ ] Verificar que logs salen a stdout en formato JSON estructurado
- [ ] Testear POST body preview en deserialization errors

---

## Notes

### Implementation Notes

 HU migrada de `sdd/logging-infrastructure/`. Original spec: `sdd/logging-infrastructure/specs/logging-infrastructure.md`. Relacionada con global exception handler (commit da5c431) — actualmente parcialmente implementada pero no funciona para todos los casos.

### 🔄 Migration Reference

- Original location: `sdd/logging-infrastructure/`
- Original spec: `sdd/logging-infrastructure/specs/logging-infrastructure.md`
- Current status: Migrated to FlowDoc
- See `sdd/README.md` for full migration mapping.
