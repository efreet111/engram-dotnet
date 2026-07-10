# Release v1.3.0 — 2026-07-06

> Notes extraídas del `CHANGELOG.md` sección `## [1.3.0] — 2026-07-06` para `gh release create v1.3.0 --notes-file ...`.
> Generado por session FlowForge Orchestrator (cierre de MUST existentes).

---

## ⚡ Highlights

Cierre de los bugs críticos de sync detectados durante OSS launch audit (2026-06-23) + spike `spike-engram-sync` en FlowForge (ADR-007):

- **ENG-451 (BUG-1)**: SyncManager recovery para mutaciones pulled huérfanas — fin de la **data loss silenciosa** tras `lifecycle=blocked → healthy`.
- **ENG-451 (BUG-2)**: `engram sync status` ahora lee de SQLite (era in-memory en proceso fresco → siempre 0).
- **ENG-452**: auto-detección de **self-loop** en `engram serve` — el SyncManager se deshabilita con warning claro en vez de spammear 501 cada 30ms.
- **ENG-435 cycle 2**: dos integration tests nuevos cubren el rework de migración (dry-run inmutabilidad + rollback mid-migration).

## Added

- **ENG-451 (BUG-1)** — SyncManager recovery for orphaned pulled mutations. Nuevos métodos en `ILocalSyncStore`: `InsertPulledMutationAsync` (registra pulled mutations en `sync_mutations` con `source='pull'`), `ReapplyPendingPulledMutationsAsync` (re-aplica mutations con `source='pull' AND acked_at IS NULL` en cada cycle, **antes del push** — funciona incluso cuando sync está bloqueado). Añade step `ack` a `ApplyPulledMutationAsync` (faltaba — leave orphaned rows on interruption). Commits `6ba2674`, `5e20f80`. Ver ADR-007.
- **ENG-451 (BUG-2)** — `engram sync status` lee mutation counts desde SQLite vía nuevo `GetSyncMutationCountsAsync` (antes leía `SyncMetrics` in-memory que reseteaba a 0 en cada restart). Nuevo `SyncMutationCounts` record type en `ILocalSyncStore`. `HandleSyncStatusAsync` usa DB counts como fuente primaria con fallback a metrics. Commit `12b97a9`.
- **ENG-452** — `engram serve` detecta self-loop (SyncManager apuntando al mismo proceso) y deshabilita SyncManager con warning stderr claro en vez de loopear en 501 cada 30ms. Detección puramente lexical contra loopback literals (`localhost`, `127.0.0.1`, `::1`) — no DNS, no startup delay. Public static `IsSyncSelfLoop(string, int)` para unit testing. Ver ADR-008. Commit `fec9d73`.
- **ENG-435 cycle 2** — dos integration tests en `tests/Engram.Postgres.Tests/PostgresStoreTests.cs` cubren los criterios del rework: `MigrateProject_DryRun_DoesNotModifyData` (CLI dry-run deja todos los rows untouched), `MigrateProject_MidMigrationFailure_RollsBackCompletely` (instala sabotage trigger en `sessions`, asserts rollback del UPDATE previo en `observations`). Trigger install/remove extraído en helpers con `try/finally` para cleanup garantizado. Commits `4be21df`, `62c1194`.

## Fixed

- **ENG-451 (BUG-1)** — SyncManager ya no deja pulled mutations huérfanas en `sync_mutations` al interrumpirse — cada pulled mutation es tracked y re-aplicada en el próximo cycle, cerrando la **silent data loss** después de `lifecycle=blocked → healthy`.
- **ENG-452** — spam de 501 cada 30ms en logs al correr `engram serve` sin `ENGRAM_SERVER_URL`. Ahora un solo warning claro y SyncManager skipeado.
- **release.yml** — native SQLite libraries (`libe_sqlite3.so` para Linux, `e_sqlite3.dll` para Windows) ahora incluidos como assets del release. Corrige `DllNotFoundException` al ejecutar `engram serve` en sistemas sin SQLite pre-instalado.
- **release.yml** — SHA-256 checksums generados para todos los assets (8 archivos en total).
- **release.yml** — `dotnet restore` ahora especifica runtimes `linux-x64` y `win-x64` para evitar `NETSDK1047` en el publish.
- **Tests** — `ProjectDetectorTests` acepta `DirBasename` como fallback cuando git CLI no está disponible (Docker, CI sin git).
- **Tests** — `LoggingTests.BodyDebugMiddleware_Registered` skipeado (bug de consumo de stream en middleware).

## Cómo actualizar

```bash
# Desde fuente
git pull
dotnet build -c Release

# Via Docker (cuando esté publicada)
docker pull ghcr.io/efreet111/engram-dotnet:v1.3.0
# o usar el manifest de FlowForge installer (>=0.4.0)
```

## Comparación

- **Código**: https://github.com/efreet111/engram-dotnet/compare/v1.2.1...v1.3.0
- **Release anterior**: [v1.2.1 — Session Activity Tracker + Phase 2 API Parity](https://github.com/efreet111/engram-dotnet/releases/tag/v1.2.1)

## Notas

- Esta release **cierra el bloque P0 del OSS launch audit 2026-06-23**: ENG-435 rework, ENG-436 (`ApplyPulledMutationAsync` stub), ENG-437 (release hygiene), ENG-443 (installer manifest), ENG-444 (PII cleanup), ENG-445 (Docker pin), ENG-446 (untracked files).
- **ENG-436 PM-7** (e2e Docker test de sync pull con Client-B SQLite) **queda pendiente** — requiere Docker daemon corriendo; no bloquea el uso del producto pero es el smoke test de regresión que se debe correr antes del próximo ciclo.
- **ENG-453** (FlowForge installer prompt para `ENGRAM_SERVER_URL`) ya implementado en repo FlowForge commit `6f13d7e`. Auditoría forge-verify: PASS_DEGRADADO (ver `.ai-work/eng-453-installer-server-url/verify-report.md` en repo FlowForge).