---
capability_matrix:
  ai_reasoning:
    - Determinar si un push post-save debe esperar o ser fire-and-forget
    - Evaluar trade-off entre feedback de estado y simplicidad
    - Decidir si push al arrancar debe ser bloqueante o async
  deterministic:
    - Trigger push solo en writes que crean mutaciones (save, update, delete)
    - Respetar lease existente (no duplicar push si background ya tiene lease)
    - Respetar backoff (no intentar push si hay failures recientes)
    - Feedback solo si hay mutaciones pendientes (no siempre)
---

# Spec: ENG-476 — Sync-on-demand (Opción 4: Combinación)

## 1. Objective and scope

**Objective:** Garantizar que las memorias creadas en Engram se sincronicen con el servidor lo antes posible, sin depender de que el usuario tenga el IDE abierto con MCP corriendo.

**Contexto de origen:** Experiencia real del usuario (2026-07-25):
1. Usuario levantó servidor, trabajó con agente, cerró sesión
2. Agente hizo mem_save → memorias quedaron pendientes en sync_mutations
3. Usuario preguntó si estaban en servidor → NO estaban
4. Agente hizo sync manual → 30 memorias pendientes
5. Si no hubiera preguntado → 30 memorias perdidas para siempre (data loss)

**Problema raíz:** El SyncManager solo corre como BackgroundService dentro de `engram mcp` o `engram serve`. Si el usuario cierra el IDE, el sync se detiene y las memorias quedan pendientes indefinidamente.

**Solución seleccionada:** Opción 4 (Combinación)
1. Push asíncrono post-save (fire-and-forget)
2. Push inmediato al arrancar MCP
3. Feedback de estado en mem_save

**In scope:**
- Trigger push después de cada mem_save, mem_update, mem_delete
- Push inmediato de pendientes al arrancar MCP (SyncManager.ExecuteAsync)
- Feedback de estado: mostrar "X mutaciones pendientes" en mem_save
- Respetar lease existente (no duplicar push)
- Respetar backoff (no intentar si hay failures recientes)

**Out of scope:**
- Daemon independiente (systemd/launchd)
- Push síncrono (rompe offline-first)
- Cambios en CLI (engram save, engram search)
- Cambios en el servidor PostgreSQL

---

## 2. Functional requirements (FR)

### FR-001 — Trigger push después de mem_save

El agente MCP debe trigger un push asíncrono después de cada operación de escritura.

- **Scenario A:** Dado que el usuario hace mem_save y hay mutaciones pendientes,
  Cuando mem_save completa exitosamente,
  Entonces se trigger un push asíncrono (fire-and-forget) al servidor.

- **Scenario B:** Dado que el usuario hace mem_save y no hay mutaciones pendientes,
  Cuando mem_save completa exitosamente,
  Entonces NO se trigger push (evitar HTTP calls innecesarios).

- **Scenario C:** Dado que el usuario hace mem_update o mem_delete,
  Cuando la operación completa exitosamente,
  Entonces se trigger un push asíncrono igual que en mem_save.

### FR-002 — Push inmediato al arrancar MCP

El SyncManager debe hacer push de mutaciones pendientes inmediatamente al arrancar, sin esperar el primer PollInterval.

- **Scenario A:** Dado que MCP arranca con 10 mutaciones pendientes,
  Cuando SyncManager.ExecuteAsync() se ejecuta,
  Entonces hace push inmediato de las 10 mutaciones antes de entrar al loop normal.

- **Scenario B:** Dado que MCP arranca sin mutaciones pendientes,
  Cuando SyncManager.ExecuteAsync() se ejecuta,
  Entonces entra al loop normal sin delay adicional.

- **Scenario C:** Dado que MCP arranca y el servidor está caído,
  Cuando el push inmediato falla,
  Entonces log warning y entra al loop normal (las mutaciones quedan pendientes).

### FR-003 — Feedback de estado en mem_save

El agente MCP debe mostrar feedback de estado de sync después de cada mem_save.

- **Scenario A:** Dado que el usuario hace mem_save y hay 3 mutaciones pendientes,
  Cuando mem_save completa exitosamente,
  Entonces muestra: "⚠️ 3 mutations pending sync"

- **Scenario B:** Dado que el usuario hace mem_save y no hay mutaciones pendientes,
  Cuando mem_save completa exitosamente,
  Entonces NO muestra feedback de sync (evitar ruido).

- **Scenario C:** Dado que el usuario hace mem_save y el sync está deshabilitado,
  Cuando mem_save completa exitosamente,
  Entonces NO muestra feedback de sync.

### FR-004 — Respetar lease existente

El push on-demand debe respetar el lease del SyncManager background.

- **Scenario A:** Dado que SyncManager background tiene el lease activo,
  Cuando se trigger push on-demand,
  Entonces skip (no intentar adquirir lease, SyncManager background lo hará en próximo ciclo).

- **Scenario B:** Dado que SyncManager background NO tiene el lease,
  Cuando se trigger push on-demand,
  Entonces adquiere lease, hace push, libera lease.

- **Scenario C:** Dado que SyncManager background tiene el lease y hay muchas mutaciones pendientes,
  Cuando se trigger push on-demand,
  Entonces skip y log debug: "lease held by background, skipping on-demand push"

### FR-005 — Respetar backoff

El push on-demand debe respetar el backoff del SyncManager.

- **Scenario A:** Dado que SyncManager está en backoff (failures recientes),
  Cuando se trigger push on-demand,
  Entonces skip y log debug: "in backoff until {time}, skipping on-demand push"

- **Scenario B:** Dado que SyncManager NO está en backoff,
  Cuando se trigger push on-demand,
  Entonces intenta push normalmente.

---

## 3. Non-functional requirements (NFR)

### NFR-001 — Performance

El push on-demand NO debe bloquear la operación principal del usuario.

- **Medida:** mem_save debe retornar en <100ms después de guardar localmente
- **Verificación:** El push ocurre en background (Task.Run fire-and-forget)

### NFR-002 — Offline-first

El push on-demand NO debe romper el paradigma offline-first.

- **Medida:** Si el servidor está caído, mem_save debe completar exitosamente
- **Verificación:** Las mutaciones quedan pendientes para próximo ciclo exitoso

### NFR-003 — Resource usage

El push on-demand NO debe consumir recursos innecesarios.

- **Medida:** No hacer HTTP calls si no hay mutaciones pendientes
- **Verificación:** Verificar pendientes antes de intentar push

### NFR-004 — Observabilidad

El push on-demand debe ser observable para debugging.

- **Medida:** Log cuando se trigger push on-demand, cuando falla, cuando skip
- **Verificación:** Logs claros para troubleshooting

---

## 4. STRIDE analysis

### Spoofing
- **Riesgo:** Bajo. Push on-demand usa el mismo lease owner que SyncManager background.
- **Mitigación:** Lease-based exclusión previene push duplicado.

### Tampering
- **Riesgo:** Bajo. Push on-demand usa el mismo IMutationTransport que SyncManager.
- **Mitigación:** Validación en servidor (ya existe).

### Repudiation
- **Riesgo:** Bajo. Push on-demand loggea todas las acciones.
- **Mitigación:** Logs claros para auditoría.

### Information Disclosure
- **Riesgo:** Bajo. Push on-demand no expone información adicional.
- **Mitigación:** Mismo canal que SyncManager background.

### Denial of Service
- **Riesgo:** Medio. Push on-demand podría hacer HTTP calls frecuentes.
- **Mitigación:** Respetar lease, backoff, y solo hacer push si hay pendientes.

### Elevation of Privilege
- **Riesgo:** Bajo. Push on-demand no cambia permisos.
- **Mitigación:** Mismos permisos que SyncManager.

---

## 5. Developer manual tests (required — mark [x] before /flow-close)

| ID | Case / flow | Steps (summary) | Expected result | [x] |
|----|-------------|-----------------|-----------------|-----|
| PM-1 | Push post-save | Hacer mem_save, verificar que se trigger push | Push ocurre en background, mem_save no se bloquea | [ ] |
| PM-2 | Push al arrancar | Arrancar MCP con mutaciones pendientes | Push inmediato de pendientes antes del primer ciclo | [ ] |
| PM-3 | Feedback de estado | Hacer mem_save con 3 mutaciones pendientes | Muestra "⚠️ 3 mutations pending sync" | [ ] |
| PM-4 | Sin feedback | Hacer mem_save sin mutaciones pendientes | No muestra feedback de sync | [ ] |
| PM-5 | Lease respect | Trigger push mientras background tiene lease | Skip, no intenta adquirir lease | [ ] |
| PM-6 | Backoff respect | Trigger push mientras SyncManager está en backoff | Skip, no intenta push | [ ] |
| PM-7 | Offline-first | Hacer mem_save con servidor caído | mem_save completa, mutación queda pendiente | [ ] |
| PM-8 | Push on-demand exitoso | Hacer mem_save con servidor disponible | Push exitoso, mutación se acked | [ ] |

---

## 6. Acceptance summary (para CKP-1)

| Tier | Deliverables | Cierra gate? |
|------|--------------|-------------|
| **Mínimo** | FR-001, FR-002, FR-003 | ✅ Push post-save + feedback funcional |
| **Completo** | Mínimo + FR-004, FR-005, PM-1..PM-8 | ✅ Lease + backoff respect, todos los tests pasan |

---

## 7. Open decisions (resueltas)

### Pregunta 1: ¿Push post-save en mem_save, mem_update, y mem_delete?

**Decisión:** Sí, en todas las operaciones de escritura que crean mutaciones.

**Rationale:** Cualquier cambio debe sincronizarse. Si solo hacemos push en mem_save, los updates y deletes quedarían pendientes.

### Pregunta 2: ¿Cuántas mutaciones pendientes mostrar en feedback?

**Decisión:** Solo si hay pendientes (no siempre).

**Rationale:** Mostrar "✅ Sync: 0 pending" sería ruido. Solo mostramos cuando hay algo pendiente.

### Pregunta 3: ¿Push al arrancar es bloqueante o async?

**Decisión:** Async (no bloquea MCP).

**Rationale:** MCP debe arrancar rápido. El push ocurre en background después del arranque.

---

## 8. Referencias

- [Context Map](context-map.md) — Análisis completo del problema
- [RFC-003: Offline-First Sync Architecture](../../../docs/architecture/rfc/RFC-003-offline-first-sync-architecture.md)
- [ADR-008: Self-loop detection](../../../docs/architecture/adr/ADR-008-sync-self-loop-detection.md)
- [BACKLOG.md - ENG-476](../../../docs/BACKLOG.md)

---

*Spec derivada del análisis del 2026-07-25. CKP-1 pendiente de aprobación humana.*
