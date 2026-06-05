---
feature: critical-rest-api-bugfix
agent: forge-memory
closed_at: 2026-06-01
deploy_commit: e1a9cf9
ckp4: pending_human_deploy_decision
server: http://192.168.0.178:7437
---

# Session summary — Critical REST API Bugfixes

## What shipped

| Bug | Endpoint | Fix |
|-----|----------|-----|
| P0 | `POST /sync/mutations/push` | Null-check `body.Entries` → HTTP 400 `empty-batch` |
| P2 | `DELETE /sessions/{id}` | COUNT excluye soft-deleted (`deleted_at IS NULL`) |
| P1 | `GET /prompts/recent` | `GetUserId(ctx)` + columna `created_by` + filtro en store |
| — | Migración PostgreSQL | Índice `idx_prompts_created_by` solo tras `ALTER TABLE` |

**Deploy**: TrueNAS `git pull` → `e1a9cf9` → `docker compose up -d --build`. Health OK, backend postgres.

## FlowForge artifacts

| Phase | Artifact | Outcome |
|-------|----------|---------|
| 0 Discovery | `context-map.md` | CKP-0 passed |
| 1 Spec | `spec.md` | FR + PM-* (PM-3/4 N/A) |
| 2 Plan | `plan.md` | Implemented |
| 3 Verify | `verify-report.md` | **PASS** (code + prod) |
| 4 Close | This file | PM-* done; CKP-4 = human choice |

## ✅ Pruebas Manuales del Desarrollador

| PM | Caso | Resultado |
|----|------|-----------|
| PM-1 | Push sin `entries` | ✅ HTTP 400 |
| PM-2 | Push `entries: null` | ✅ HTTP 400 |
| PM-5 | Delete session (solo soft-deleted) | ✅ HTTP 200 |
| PM-6 | Prompts + `X-Engram-User` | ✅ scoping por usuario |
| PM-7 | `/prompts/recent` sin `project` | ✅ HTTP 200 + `[]` (comportamiento documentado) |
| PM-3 / PM-4 | Retention prune | N/A — fuera de alcance de este feature |

Verificadas en producción 2026-06-01. Script reproducible: `docs/MANUAL-TESTING-CHECKLIST.md`.

## Learnings (memoria del equipo)

1. **`CREATE TABLE IF NOT EXISTS` no migra columnas** — índices en el mismo bloque SQL fallan en PG legacy.
2. **Fix incompleto (`f8ba8f6`)** — añadir migración al final no basta si el índice sigue al inicio.
3. **TrueNAS atrasado** — `git log -1` en servidor antes de debuggear Docker; estaba en `5ae578d`.
4. **Dos Dockerfiles** — raíz = build fuente; `docker/Dockerfile` = release GitHub.
5. **DB producción** — `engram_cloud`, no `engram` del compose default.
6. **`created_by` en JSON** — filtrado OK; campo respuesta puede mostrar `""` (deuda menor).

## Docs promovidos (Git)

- `docs/SESSION-REPORT-2026-05-31-REST-API-BUGFIX.md`
- `docs/MANUAL-TESTING-CHECKLIST.md` (regression tests)
- `docs/POSTGRES-SETUP.md` (troubleshooting 42703)

## Deploy gate (CKP-4)

Feature **methodology-complete** y **desplegada en TrueNAS**. Pendiente decisión humana:

- [ ] Commit + push docs en repo (`efreet111/engram-dotnet`)
- [ ] Tag/release si aplica
- [x] Ingest Engram local: obs **#2444** (`engram save`, session_summary) — sync a TrueNAS pendiente si SyncManager no está corriendo

## Next steps (opcional)

- Fix serialización `created_by` en respuesta JSON de prompts
- PM-3 / PM-4 retention prune → feature separado
- Test automatizado migración legacy `user_prompts` sin columna `created_by`
