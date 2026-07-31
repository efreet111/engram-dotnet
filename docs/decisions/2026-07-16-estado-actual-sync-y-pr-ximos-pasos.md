---
observation_id: 43
type: "decision"
title: "Estado actual sync y próximos pasos"
created_at: "2026-07-16 02:48:50"
topic_key: "sync-status-next-steps-2026-07-16"
project: "team/engram-dotnet"
scope: "personal"
generated_at: "2026-07-21T22:00:59.5722905Z"
---

# Estado actual sync y próximos pasos

**What**: Estado actual del sistema de sync y próximos pasos

**Current state** (2026-07-16):
- Servidor remoto 192.168.0.178:7437: v1.1.0 (desactualizado)
- Cliente local: v1.0.0 (binario ~/.local/bin/engram)
- Código repo: v1.3.0 (commit 2731dac, tiene todos los fixes)
- 3 proyectos enrolados en servidor: team/engram-dotnet, team/flowforge, engram-dotnet
- 3 proyectos enrolados en cliente local (SQLite): mismos
- 38 mutaciones pendientes de push (20 team/engram-dotnet + 18 team/flowforge)
- 3 mutaciones huérfanas con project="" borradas manualmente

**Blockers**:
1. Servidor remoto sin fix ENG-428 (session_id null)
2. Binarios desactualizados (servidor v1.1.0, cliente v1.0.0)
3. ENG-458 no resuelto (mutaciones project="" bloquean sync)

**Próximos pasos**:
1. ENG-458: Arreglar CountPendingNonEnrolledAsync (S, rápido)
2. ENG-459: Añadir feedback visible (M, más largo)
3. Actualizar servidor remoto a v1.3.0
4. Reconstruir binarios locales (dotnet publish)
5. Verificar sync end-to-end

**Dependencias**:
- ENG-453 (FlowForge installer) → PR Open en FlowForge
- ENG-455 (flowforge sync connect) → Pendiente
- ENG-302 (Wizard gráfico) → Pendiente
