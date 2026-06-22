# HU-007: Logging Infrastructure

**Status**: 🟡 In Progress
**Owner**: @owner
**Created**: 2026-06-01
**Priority**: High (bloquea debugging en producción)
**Effort**: 2-3h
**Origin**: Migrated from `sdd/logging-infrastructure/`

---

## 🎯 Intent

Implementar infraestructura de logging estructurado para todos los requests HTTP y responses, permitiendo debugging en producción.

---

## 📋 Scope

### In Scope
- Request/Response logging middleware
- Structured JSON logs (machine-parseable)
- POST body error debugging (body preview on deserialization errors)
- Global exception handler (todas las rutas)

### Out of Scope
- Logging a archivo (solo stdout/console)
- Log rotation
- Log aggregation infrastructure

---

## ✅ Requirements

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

## 🧪 Scenarios

### Scenario: Request logged
- GIVEN any request to any endpoint
- WHEN the request is received
- THEN log method, path, status code, duration, and client IP

### Scenario: Error response logged
- GIVEN a request that causes a 5xx error
- WHEN the response is sent
- THEN log the full error details (message, stack trace, exception type)

### Scenario: Invalid JSON logged
- GIVEN a POST request with malformed JSON body
- WHEN deserialization fails
- THEN log the first 1KB of the raw body and the JSON error

### Scenario: All routes covered
- GIVEN any route throws an exception
- WHEN the exception is caught
- THEN the response includes `{ error, type }` JSON

---

## 📦 Affected Areas

- `src/Engram.Server/EngramServer.cs` — add logging middleware
- `src/Engram.Server/CloudSyncEndpoints.cs` — add body debug logging

---

## 🔗 Origin

Migrated from `sdd/logging-infrastructure/` (spec ready)

Original spec: `sdd/logging-infrastructure/specs/logging-infrastructure.md`

---

## 📝 Notes

Related to global exception handler (commit da5c431) — currently partially implemented but not working for all cases.

---

## 🔄 Migration Reference

Original location: `sdd/logging-infrastructure/`
Current status: Migrated to FlowDoc

See `sdd/README.md` for full migration mapping.