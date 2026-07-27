---
observation_id: 42
type: "decision"
title: "ENG-459: Sin feedback cuando sync falla"
created_at: "2026-07-16 02:48:41"
topic_key: "eng-459-sync-failure-feedback"
project: "team/engram-dotnet"
scope: "personal"
generated_at: "2026-07-21T22:00:59.5728334Z"
---

# ENG-459: Sin feedback cuando sync falla

**What**: Feature crítica — sin notificación visible cuando sync falla repetidamente

**Why**: Usuario trabajó días creando memorias pensando que se sincronizaban. Al verificar manualmente, descubrió que sync estaba bloqueado desde el primer día. Pérdida de datos silenciosa.

**Where**: SyncManager.cs (BackgroundService), engram sync status CLI, /sync/status endpoint

**Escenarios de fallo silencioso**:
1. ENGRAM_SERVER_URL no configurado → SyncManager deshabilitado
2. Proyectos no enrolados → push bloqueado
3. Servidor sin fixes (ej: ENG-428) → push falla con 500
4. Red caída → timeout
5. Credenciales inválidas → 401/403

**Solución propuesta**:
1. engram sync status muestra error claro + acción sugerida
2. /sync/status incluye campo suggested_action
3. MCP server loggea warning si sync bloqueado
4. Archivo ~/.engram/sync-notifications.log

**Status**: ENG-459 creada en BACKLOG.md, P0, Ready, Effort M
