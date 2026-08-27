# ADR-014 — Desktop hybrid profile sync requires dedicated engram server instance

| Campo | Valor |
|-------|-------|
| **ADR** | 014 |
| **Estado** | Accepted |
| **Fecha** | 2026-08-19 |
| **Contexto** | Desktop hybrid profile (SQLite local + PostgreSQL sync) is architecturally incomplete — SyncManager syncs to an engram server over HTTP, not to raw PostgreSQL; the compose self-loop guard blocks the only configured sync URL |

---

## Contexto

HU-024 flipped the `desktop` deployment profile from PostgreSQL to SQLite as the local source of truth (correct for resilience). The intent was a hybrid model: SQLite local + PostgreSQL Docker acting as a "sync server" for other devices (laptop, new PC) to sync to.

However, `SyncManager` syncs via **HTTP to an `engram serve` instance**, not to a raw PostgreSQL connection. The desktop compose (`scripts/install.sh` → `generate_compose_*`) emits:

- One `engram serve` container (now SQLite-backed after HU-024)
- One PostgreSQL container
- `ENGRAM_SERVER_URL=http://localhost:7437` — which points to the **same** `engram serve` container

ADR-008's self-loop detection (`SyncManager`) identifies this as a self-loop and **disables sync entirely**. The "PostgreSQL Docker as sync server" concept is therefore non-functional in the current implementation.

Key evidence:

- `src/Engram.Store/DeployProfile.cs` — `ProfileDefaults.For(Desktop)` sets `ENGRAM_DB_TYPE=sqlite` and `ENGRAM_SERVER_URL=http://localhost:7437`
- `src/Engram.Store/StoreConfig.cs` — `FromEnvironment()` resolves `RemoteUrl` from `ENGRAM_SERVER_URL` (added in HU-024)
- `scripts/install.sh` — compose templates emit one `engram serve` + one PostgreSQL container; `ENGRAM_SERVER_URL` defaults to `http://localhost:7437`
- ADR-008 — self-loop guard logic that blocks sync when `RemoteUrl` resolves to the same instance

---

## Decisión

A coherent dual-backend desktop model requires a **second `engram serve` instance backed by PostgreSQL** that acts as the sync hub. The current implementation correctly flips Desktop to SQLite (local source of truth), but the sync-server aspect remains non-functional until this second instance is deployed and `ENGRAM_SERVER_URL` is pointed at it (not at the local container).

This ADR records the architectural finding and the required direction. Implementation is deferred to a future HU/task.

---

## Razones

- **SyncManager protocol**: Sync operates over HTTP to an `engram serve` endpoint, not to a raw DB connection. A bare PostgreSQL container cannot serve as a sync target.
- **Self-loop guard (ADR-008)**: Pointing `ENGRAM_SERVER_URL` at the same container is intentional protection — it prevents sync storms. The guard is correct; the compose configuration is wrong.
- **Separation of concerns**: The local SQLite instance is the local source of truth. The PostgreSQL-backed `engram serve` instance is the sync hub for other devices. These are two different roles requiring two different processes.
- **Offline-first resilience**: If Docker drops, the local SQLite instance remains operational — the original HU-024 goal is preserved. Sync resumes when the hub is reachable.

---

## Consecuencias

**Positivas:**

- The hybrid model becomes coherent: local SQLite for speed/resilience, PostgreSQL-backed hub for cross-device sync.
- Self-loop guard (ADR-008) continues to protect without configuration hacks.
- Clear separation: local instance = read/write local; hub instance = sync aggregation point.

**Negativas:**

- Running two `engram serve` instances increases operational complexity (two processes to manage, monitor, and restart).
- The PostgreSQL container alone is insufficient — it needs an `engram serve` process in front of it.
- Users who expected "just point at PostgreSQL" must understand the sync hub concept.

**Mitigaciones:**

- Document the required topology in `docs/DEPLOYMENT.md` (desktop hybrid section).
- Provide a compose variant that spins up both instances (local SQLite + hub PostgreSQL-backed).
- The hub instance pointing `ENGRAM_SERVER_URL` at itself is acceptable — hub self-sync is a no-op, which is fine for the aggregation node.

---

## Alternativas Consideradas

### Opción 1: Direct PostgreSQL-to-PostgreSQL sync (bypass engram serve)

- Pro: No second process needed; PostgreSQL container alone suffices.
- Contra: `SyncManager` is HTTP-based; rewriting it for direct DB sync would be a major architectural change, breaking the sync protocol abstraction and ADR-001 (SQL sin ORM boundaries).

### Opción 2: Keep self-loop and disable the guard for desktop

- Pro: Minimal code change; sync "works" (as a no-op self-sync).
- Contra: Defeats the purpose — self-sync is meaningless. Also undermines ADR-008's safety guarantee. Rejected.

### Opción 3: Multi-backend in a single `engram serve` process

- Pro: Single process, single container.
- Contra: Requires architectural rework of the store layer to support two DB backends in one process. High complexity for a niche use case. Deferred.
