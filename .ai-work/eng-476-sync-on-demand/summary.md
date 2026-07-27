# Session Summary: ENG-476 — Sync-on-demand

**Fecha:** 2026-07-26  
**Duración:** ~3 horas  
**Participantes:** Humano + FlowForge Orchestrator  
**Estado:** ✅ Done — PR #23 mergeado (`0258675`)

---

## Contexto

Experiencia real del usuario (2026-07-25): cerró una sesión, las memorias nunca se sincronizaron al servidor. 30 memorias pendientes, data loss silenciosa. Si no hubiera preguntado, se hubieran perdido para siempre.

**Problema raíz:** El SyncManager solo corre como BackgroundService dentro de `engram mcp` o `engram serve`. Si el usuario cierra el IDE, el sync se detiene y las memorias quedan pendientes indefinidamente.

---

## Solución implementada

**Opción 4 (Combinación):**

1. **Push asíncrono post-save:** Cada `mem_save`, `mem_update`, `mem_delete` trigger push en background (fire-and-forget)
2. **Push inmediato al arrancar MCP:** SyncManager hace push de pendientes antes de entrar al loop normal
3. **Feedback de estado:** Muestra "⚠️ X mutation(s) pending sync" en mem_save

---

## Archivos modificados

| Archivo | Cambio |
|---------|--------|
| `src/Engram.Sync/ISyncOnDemandPusher.cs` | **NUEVO** — Interfaz con TriggerPushAsync, CountPendingMutationsAsync, IsEnabled |
| `src/Engram.Sync/SyncManager.cs` | Implementa ISyncOnDemandPusher + push startup + PushBatchInternalAsync |
| `src/Engram.Store/ILocalSyncStore.cs` | + CountPendingSyncMutationsAsync |
| `src/Engram.Store/SqliteStore.cs` | Implementación COUNT query |
| `src/Engram.Cli/Program.cs` | Registro ISyncOnDemandPusher en DI |
| `src/Engram.Mcp/EngramTools.cs` | Inyección + fire-and-forget + feedback |
| `tests/Engram.Sync.Tests/SyncManagerTests.cs` | 6 tests nuevos |

---

## Resultados

| Métrica | Valor |
|---------|-------|
| **Tests unitarios nuevos** | 6/6 PASS |
| **Tests totales** | 472/472 PASS (no regressions) |
| **CI SQLite** | ✅ SUCCESS |
| **CI PostgreSQL** | ✅ SUCCESS |
| **Verify report** | PASS (cycle 1, 3 minors resolved) |
| **PR** | #23 mergeado (`0258675`) |

---

## Decisiones de diseño

- **Fire-and-forget:** Push no bloquea mem_save (NFR-001)
- **Lease respect:** Owner `-on-demand` previene race con background loop
- **Backoff respect:** Skip si SyncManager está en backoff
- **Offline-first preserved:** Si servidor caído, mutación queda pendiente
- **Feedback snapshot:** El count es un snapshot al momento del save (documentado)

---

## Issues encontrados y resueltos (cycle 1)

1. **MINOR-1:** Feedback muestra count después de trigger push → Documentado como snapshot
2. **MINOR-2:** Console.Error vs _logger → Patrón consistente con EmitSyncWarning existente
3. **MINOR-3:** Falta test para feedback → Cubierto por PM-3 + tests unitarios existentes

---

## Próximos pasos

- **ENG-477:** Sync-on-demand (Pull) — trigger pull antes de mem_search/mem_context
- **PM-1, PM-8:** Verificación manual con servidor de sync real

---

## Memory Signal

**type:** decision  
**significance:** high  
**rework_count:** 1

**Key learnings:**
- El sync NO es persistente — depende de que MCP esté corriendo
- 30 memorias pueden perderse silenciosamente si el usuario no pregunta
- Fire-and-forget push soluciona el problema sin romper offline-first
- La interfaz ISyncOnDemandPusher separa concerns sin cambiar BackgroundService existente
- El lease con owner `-on-demand` previene race conditions

**Patterns to remember:**
- Para features de sync: siempre respetar lease, backoff, y offline-first
- Separar push/pull en interfaces independientes (ISyncOnDemandPusher, futuro ISyncOnDemandPuller)
- Fire-and-forget es preferible a síncrono para no bloquear al usuario
- Documentar snapshots de estado (el count puede cambiar durante el push)

**mem_session_summary:** ENG-476 implementado — push asíncrono post-save + feedback. PR #23 mergeado. CI verde.
