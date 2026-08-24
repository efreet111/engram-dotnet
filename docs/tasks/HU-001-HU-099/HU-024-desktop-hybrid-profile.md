# HU-024 — Perfil Desktop Híbrido: SQLite local + PostgreSQL Docker como sync hub

**As**: Power user con múltiples equipos
**I want**: Tener SQLite local Y un servidor PostgreSQL Docker corriendo simultáneamente en mi desktop, sirviendo a otros equipos como sync hub
**To**: Máxima resiliencia (si Docker cae, SQLite local funciona), máximo performance (lectura local), y sync para múltiples dispositivos

---

## Context

El perfil `desktop` es un **perfil híbrido** que combina:
1. **Cliente local** (`offline-first`): `engram serve --profile desktop` → SQLite local, sync habilitado
2. **Servidor de sync** (Docker): `engram serve --profile remote-server` → PostgreSQL, sirve como hub de sincronización

**Caso de uso real:**
- Desktop (PC potente): SQLite local + Docker con `remote-server` (PostgreSQL) → sirve a sí mismo y a otros equipos
- Laptop/PC nuevo: `offline-first` → SQLite local + sync al desktop via `http://<desktop-ip>:7437`
- Sync bidireccional entre equipos

---

## Acceptance Criteria

### Cliente local (Desktop profile)

- [x] `DeployProfile.Desktop` setea: `ENGRAM_DB_TYPE=sqlite`
- [x] `DeployProfile.Desktop` setea: `ENGRAM_SYNC_ENABLED=true`
- [x] `DeployProfile.Desktop` setea: `ENGRAM_SERVER_URL=http://localhost:7437` (apunta al Docker)

### Servidor de sync en Docker (Remote-server profile)

- [x] `install.sh` genera compose con `ENGRAM_PROFILE: remote-server` (no `desktop`)
- [x] `install.sh` genera compose con `ENGRAM_DB_TYPE: postgres` (no `sqlite`)
- [x] PostgreSQL corre como backend del sync hub (no como storage primario del cliente)

### Los 3 modos de PostgreSQL Docker

- [x] **all-in-one**: 1 contenedor con `engram serve --profile remote-server` + PostgreSQL embebido
- [x] **separate**: 2 contenedores — `engram serve --profile remote-server` + `postgres` raw separado
- [x] **existing**: 1 contenedor `engram serve --profile remote-server` + PostgreSQL externo del usuario

### Experiencia multi-dispositivo

- [x] Desktop sirve como "remote server" para laptop/PC nuevos
- [x] Laptop usa `offline-first` apuntando a `http://<desktop-ip>:7437`
- [x] Sync bidireccional: cambios en desktop aparecen en laptop y viceversa

### Resiliencia

- [x] Si PostgreSQL Docker no está disponible: engram local sigue funcionando con SQLite local
- [x] Sync se reanuda automáticamente cuando Docker vuelve
- [x] Sin data loss: SQLite local es source of truth

---

## Tasks (Implementation)

- [x] `src/Engram.Store/DeployProfile.cs` — Desktop: `DB_TYPE=sqlite`, `SYNC_ENABLED=true`, `SERVER_URL=http://localhost:7437`
- [x] `scripts/install.sh` — `generate_compose_allinone`: `ENGRAM_PROFILE=remote-server`, `ENGRAM_DB_TYPE=postgres`
- [x] `scripts/install.sh` — `generate_compose_separate`: `ENGRAM_PROFILE=remote-server`, `ENGRAM_DB_TYPE=postgres` para el contenedor engram
- [x] `scripts/install.sh` — `generate_compose_existing`: `ENGRAM_PROFILE=remote-server`, `ENGRAM_DB_TYPE=postgres` para el contenedor engram
- [x] `docs/DEPLOYMENT.md` — actualizar sección desktop: explicar modo híbrido (local SQLite + Docker remote-server)
- [x] `docs/01-QUICK-START.md` — tabla de perfiles actualizada
- [x] `DeployProfileTests` — Desktop defaults verifican SQLite + sync + server URL
- [x] Tests de integración: desktop híbrido + offline-first client

---

## Arquitectura esperada

```
┌──────────────────────────────────────────────────────────────┐
│  Desktop Host                                                 │
│                                                               │
│  Proceso local (cliente):                                    │
│  engram serve --profile desktop                              │
│  → SQLite local (/data/engram)                              │
│  → Puerto 7431 (serve local)                                │
│  → ENGRAM_SERVER_URL=http://localhost:7437                   │
│    (apunta al Docker)                                        │
│  → ENGRAM_DB_TYPE=sqlite                                     │
│  → ENGRAM_SYNC_ENABLED=true                                   │
│                                                               │
│  Docker Desktop:                                              │
│  engram serve --profile remote-server                        │
│  → PostgreSQL (localhost o contenedor postgres)              │
│  → Puerto 7437 (expuesto al host)                           │
│  → ENGRAM_DB_TYPE=postgres                                   │
└──────────────────────────────────────────────────────────────┘
```

---

## Notes

- **Dependencia**: HU-025 (IsRemote → IsThinClient) — el fix permite que `desktop` use SqliteStore local aunque `ENGRAM_SERVER_URL` esté seteado
- **Decisión técnica**: PostgreSQL Docker en desktop actúa como "sync hub", no como storage backend del cliente. SQLite local es siempre el storage primario del cliente.
- **Tres modos de PostgreSQL**: all-in-one (1 contenedor con PG embebido), separate (engram + postgres raw), existing (engram + PG externo del usuario)
- **Relación offline-first**: laptop usa `offline-first` apuntando al desktop. Desktop usa `desktop` (SQLite local) + Docker `remote-server` (sync hub)

---

## New Technical Decisions

- **Dual backend (SQLite local + PostgreSQL-as-sync-server)** — El perfil desktop combina un cliente SQLite local con un sync hub PostgreSQL en Docker. SQLite es el store primario del cliente; PostgreSQL solo recibe y envía cambios via SyncManager HTTP. Esto permite resiliencia (cliente funciona sin Docker) y sync multi-device. Decisión documentada en ADR-014.
