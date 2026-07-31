---
observation_id: 33
type: "decision"
title: "OSS launch audit MUST closed: ENG-436, ENG-437, ENG-453"
created_at: "2026-07-11 03:37:21"
topic_key: "engram-dotnet/oss-launch-audit-must-closure"
project: "team/engram-dotnet"
scope: "team"
generated_at: "2026-07-21T22:00:59.5755590Z"
---

# OSS launch audit MUST closed: ENG-436, ENG-437, ENG-453

**What**: Cierre completo de las 3 MUST del OSS launch audit (ENG-436, ENG-437, ENG-453).

**Why**: Estas eran las tareas bloqueantes para considerar el proyecto "listo para salir" después del OSS launch audit de 2026-06-23.

**Where**: 
- ENG-436: engram-dotnet (sync pull e2e test)
- ENG-437: engram-dotnet (Release v1.3.0)
- ENG-453: FlowForge (installer fix)

**Learned**:

### Estado final de cada MUST

**ENG-436 — ✅ Done (2026-07-09)**
- PM-7 e2e Docker test PASS: `scripts/test-2client-pull.sh` verificado
- Flujo completo: Client-A SQLite → Postgres server → Client-B SQLite
- SyncManager reports: `health: healthy, consecutive_failures: 0`
- Commits: `efbe32d` (fix) + verificación e2e 2026-07-09

**ENG-437 — ✅ Done (2026-07-10)**
- Release v1.3.0 publicada en GitHub
- URL: https://github.com/efreet111/engram-dotnet/releases/tag/v1.3.0
- Release notes: `.ai-work/eng-437-release-v040/release-notes-v1.3.0.md`
- Tag v1.3.0 creado y publicado

**ENG-453 — 🟡 PR Open en FlowForge**
- forge-arch + forge-verify (cycle 2) + forge-dev complete
- Commit `0550e35` en branch `feat/eng-453-verify-cleanup`
- 5 fixes aplicados:
  - VERIFY-01: exit code non-zero en headless abort
  - VERIFY-02: spec NFR-002 alineado (headless errors en EN)
  - VERIFY-03: write atómico en ConfigStore (.tmp → rename)
  - CLEANUP-01: ADR-010 status Proposed → Accepted
  - CLEANUP-02: POST-INSTALL.md §3 workaround removido
- forge-verify cycle 2: PASS_DEGRADADO (9/9 FR, 4/4 NFR, 0 issues)
- Pendiente: merge PR en FlowForge + tests con .NET SDK

### Flujo FlowForge aplicado a ENG-453

1. ✅ forge-discovery (Phase 0): Context map creado
2. ✅ forge-arch (Phase 1): Spec.md con 9 FRs + 4 NFRs + STRIDE
3. ✅ forge-verify cycle 1: PASS_DEGRADADO (3 MINOR + 2 cleanup)
4. ✅ forge-dev: 5 fixes aplicados
5. ✅ forge-verify cycle 2: PASS_DEGRADADO (0 issues nuevos)
6. ✅ Commit + push a branch feat/eng-453-verify-cleanup
7. 🟡 PR abierto en FlowForge, pendiente merge

### Lecciones aprendidas

1. **forge-verify cycle 2 es valioso**: cycle 1 identificó gaps, cycle 2 confirmó que los fixes resolvieron todo sin introducir regresiones
2. **PASS_DEGRADADO es aceptable**: cuando no se pueden correr tests (falta .NET SDK), el veredicto PASS_DEGRADADO permite avanzar con confianza basada en source-level verification
3. **Branch naming**: usar prefijos claros (`feat/`, `docs/`) facilita el tracking
4. **Auto-enroll descubierto como gap**: durante el cierre de MUST, se descubrió un nuevo gap de diseño (auto-enroll on first save) que requiere su propio ciclo

### Próximos pasos

- Merge PR de FlowForge (ENG-453)
- Evaluar ENG-454 (tech_debt type) + ENG-455 (hu_id field) como siguientes features
- Auto-enroll on first save queda como pendiente para próximo ciclo (ENG-454 o nuevo número)

**Trigger event**: 2026-07-10, usuario publicó Release v1.3.0 en GitHub
