# HU-007: Logging Infrastructure

**Status**: ✅ Done
**Owner**: @owner
**Created**: 2026-06-01
**Priority**: High (bloquea debugging en producción)
**Effort**: 2-3h

---

## 🎯 Intent

Structured HTTP request/response logging for production debugging — log every incoming request (method, path, status, duration, client IP) and every error with full details (message, stack trace, exception type) so failures in production can be diagnosed without guesswork.

## 📋 Scope

- Request/response logging middleware in EngramServer
- Global exception handler covering all routes, with 5xx full error details
- POST body preview (first 1KB) on deserialization errors via CloudSyncEndpoints
- Structured JSON output to stdout
- Non-blocking logging (no latency impact)

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
