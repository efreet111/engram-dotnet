---
observation_id: 40
type: "decision"
title: "Diagnóstico sistema sync engram-dotnet"
created_at: "2026-07-16 02:48:29"
topic_key: "sync-diagnostic-2026-07-16"
project: "team/engram-dotnet"
scope: "personal"
generated_at: "2026-07-21T22:00:59.5736821Z"
---

# Diagnóstico sistema sync engram-dotnet

**What**: Diagnóstico completo del sistema de sincronización local → servidor remoto (192.168.0.178:7437)

**Why**: Verificar que el sync funciona correctamente entre cliente local y servidor

**Where**: engram-dotnet repo, servidor en 192.168.0.178:7437

**Findings**:
1. Servidor remoto: v1.1.0 (desactualizado, falta fix ENG-428)
2. Cliente local: v1.0.0 (binario en ~/.local/bin/engram)
3. ENGRAM_SERVER_URL no configurado → SyncManager se deshabilita (self-loop detection ADR-008)
4. Servidor tenía 0 proyectos enrolados
5. 3 mutaciones huérfanas con project="" bloqueaban push de 38 mutaciones válidas
6. Push fallaba con error 500: session_id null en Postgres (bug ENG-428 no aplicado en servidor)

**Learned**:
- SyncManager lee SOLO variable de entorno ENGRAM_SERVER_URL, NO config.json
- config.json tiene campo "remote_url" pero no se usa (engañoso)
- CountPendingNonEnrolledAsync cuenta project="" como no enrolado → bloquea todo
- ObsPayload usa snake_case correctamente, pero servidor v1.1.0 no tiene fix
- Self-loop detection funciona correctamente (ADR-008)
