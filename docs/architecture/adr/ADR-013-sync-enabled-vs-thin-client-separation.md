# ADR-013: Separación `IsSyncEnabled` vs `IsRemote`/`IsThinClient`

**Status:** Accepted
**Date:** 2026-08-19
**Deciders:** victor
**Related:** HU-025, ADR-011, ADR-012, commit `ebdb21d`

---

## Context

`StoreConfig.IsRemote` (línea 77) se define como `!string.IsNullOrWhiteSpace(RemoteUrl)`: devuelve `true` siempre que `ENGRAM_SERVER_URL` esté seteado, sin importar el deployment profile. Esta flag controla la selección de store en dos puntos críticos:

- `Program.cs:160` — `IStore store = storeCfg.IsRemote ? new HttpStore(storeCfg) : OpenStore(storeCfg);`
- `Program.cs:2039` — `if (cfg.IsRemote) return new HttpStore(cfg);`

El commit `ebdb21d` (feat: add deployment profiles system) introdujo `IsSyncEnabled` (líneas 83-95) como concepto separado: representa "sync está habilitado" y devuelve `true` para los profiles `OfflineFirst` y `Desktop`. Sin embargo, **`IsSyncEnabled` nunca se conectó a la selección de store** — `IsRemote` siguió siendo la única flag que decidía si el cliente usaba `HttpStore` (thin client) o un store local.

### El bug (HU-025)

Esta confluación hace que `offline-first` y `desktop` con `ENGRAM_SERVER_URL` seteado obtengan `HttpStore` (thin client) en lugar de `SqliteStore` local + sync:

| Profile | `ENGRAM_SERVER_URL` | `IsRemote` | `IsSyncEnabled` | Backend real | Esperado |
|---------|---------------------|------------|-----------------|--------------|----------|
| `offline-first` | `http://server:7437` | `true` | `true` | **HttpStore** (thin client) | SqliteStore + sync |
| `desktop` | `http://server:7437` | `true` | `true` | **HttpStore** (thin client) | SqliteStore + sync |
| `remote-server` | *(ninguno)* | `false` | `false` | PostgresStore | PostgresStore (correcto) |

`offline-first` **no tiene store local**. Si el servidor está caído, no puede leer ni escribir — contradiciendo el nombre "offline-first".

### Conceptos mezclados

| Concepto | Significado | Flag correcta |
|----------|-------------|---------------|
| Thin client (HttpStore) | Este cliente delega todo al servidor, sin store local | `IsThinClient` (solo `remote-server`) |
| Sync habilitado | Hay un servidor remoto para sincronizar, pero el store es local | `IsSyncEnabled` |

---

## Decision

Separar explícitamente los dos conceptos:

1. **`IsSyncEnabled`** representa "sync está habilitado (hay `ENGRAM_SERVER_URL` y el profile lo soporta)". Ya existe en `StoreConfig.cs:83-95` y devuelve `true` para `OfflineFirst` y `Desktop`.

2. **`IsRemote` se restringe a significar "thin client"**: el cliente opera sin store local, delegando todo al servidor via `HttpStore`. Solo el profile `remote-server` activa este modo. En la implementación del fix (HU-025), se introduce `IsThinClient` como nombre explícito para reemplazar `IsRemote` en la lógica de selección de store.

3. **Store selection usa `IsThinClient`, no `IsSyncEnabled` ni `IsRemote`**: los profiles `offline-first` y `desktop` usan `SqliteStore` local **sin importar** si `ENGRAM_SERVER_URL` está seteado. El sync se maneja por separado via `SyncManager`, que usa `RemoteUrl` independientemente del store backend.

### Cambio aplicado (HU-025)

```csharp
// StoreConfig.cs — NUEVO
public bool IsThinClient =>
    !string.IsNullOrWhiteSpace(RemoteUrl)
    && Profile is not (DeployProfile.OfflineFirst or DeployProfile.Desktop);

// Program.cs:160 — ANTES
IStore store = storeCfg.IsRemote
    ? new HttpStore(storeCfg)
    : OpenStore(storeCfg);

// Program.cs:160 — DESPUÉS
IStore store = storeCfg.IsThinClient
    ? new HttpStore(storeCfg)
    : OpenStore(storeCfg);

// Program.cs:2039 — ANTES
if (cfg.IsRemote)
    return new HttpStore(cfg);

// Program.cs:2039 — DESPUÉS
if (cfg.IsThinClient)
    return new HttpStore(cfg);
```

> **Nota sobre `remote-server`**: El profile `remote-server` es el servidor en sí — usa `PostgresStore` como backend local, no `HttpStore`. La definición anterior (`Profile == DeployProfile.RemoteServer`) era incorrecta porque forzaba a `remote-server` a usar `HttpStore`, rompiendo el servidor. La definición correcta se basa en la presencia de `RemoteUrl`: el servidor no setea `ENGRAM_SERVER_URL` (él *es* el servidor), por lo que `IsThinClient` devuelve `false` y `OpenStore` selecciona `PostgresStore`. Los profiles `offline-first` y `desktop` se excluyen explícitamente porque, aunque tengan `RemoteUrl`, deben usar `SqliteStore` local + sync.

### Rationale

1. **Separation of concerns**: "tener un servidor de sync" y "ser un thin client" son decisiones arquitectónicas distintas que no deberían acoplarse en una sola flag.
2. **Coherencia con el modelo de profiles**: `offline-first` promete persistencia local + sync; `remote-server` promete delegación total. `IsRemote` violaba la primera promesa.
3. **`IsSyncEnabled` ya existía**: el commit `ebdb21d` reconoció la separación conceptual pero no la llevó hasta la selección de store. Este ADR formaliza lo que ese commit dejó implícito.
4. **`IsThinClient` es explícito**: el nombre `IsRemote` es ambiguo ("¿remoto respecto a qué?"). `IsThinClient` no deja duda sobre qué significa.

---

## Consequences

### Positive

1. `offline-first` y `desktop` con `ENGRAM_SERVER_URL` seteado usan `SqliteStore` local — offline real.
2. `SyncManager` puede usar `RemoteUrl` para sync bidireccional sin cambiar el store backend.
3. Separación conceptual clara: `IsSyncEnabled` (sync on/off) vs `IsThinClient` (store local vs HttpStore).
4. Los nombres reflejan el significado — menos confusión para nuevos desarrolladores.

### Negative

1. **Breaking change para código que usa `IsRemote`**: `Program.cs:164`, `Program.cs:233`, `Program.cs:374` usan `IsRemote` para diagnostic labels — necesitan migrarse a `IsThinClient`.
2. **Tests existentes**: `StoreConfig_IsRemote_*` en `EngramToolsTests.cs:912-933` verifican el comportamiento actual de `IsRemote` — deben actualizarse o renombrarse.
3. **`IsRemote` puede quedar como alias deprecated temporalmente** para no romper consumers externos, o eliminarse si no hay API pública que lo exponga.

### Mitigations

1. **Migración guiada**: HU-025 lista todos los puntos de uso de `IsRemote` en el codebase para migrarlos a `IsThinClient`.
2. **Tests nuevos**: HU-025 propone `OfflineFirst_UsesSqliteStore_NotHttpStore`, `RemoteServer_UsesHttpStore`, `OfflineFirst_WithServerDown_CanWriteLocally`.
3. **Este ADR**: Sirve como referencia para cualquier equipo que encuentre `IsRemote` y no entienda por qué cambió.

### Accepted technical debt

- `IsRemote` puede mantenerse temporalmente como alias de `IsThinClient` durante la transición, marcado `/// Obsolete. Use IsThinClient.` — eliminación completa en una versión futura.

---

## Alternatives Considered

### Opción 1: Usar `IsSyncEnabled` para store selection

- **Pro**: Reutiliza una flag que ya existe.
- **Contra**: Invierte la lógica — `IsSyncEnabled = true` debería significar "usa store local + sync", no "usa HttpStore". Conectarlo a store selection crearía una nueva confluación (negación confusa: `!IsSyncEnabled ? HttpStore : local`).
- **Descartada**: `IsSyncEnabled` describe el estado del sync, no el modo de operación del cliente.

### Opción 2: Eliminar `IsRemote` sin reemplazo

- **Pro**: Menos flags, menos superficie.
- **Contra**: La decisión "thin client vs local store" necesita una flag explícita — no se puede inferir solo del profile porque `remote-server` podría en el futuro soportar un cache local.
- **Descartada**: La selección de store merece una flag con nombre claro.

### Opción 3: Renombrar `IsRemote` a `IsThinClient` sin tocar `IsSyncEnabled`

- **Pro**: Cambio mínimo, resuelve el bug.
- **Contra**: Deja `IsSyncEnabled` sin rol claro en la arquitectura — existe pero no se conecta a nada.
- **Descartada**: Este ADR formaliza que ambos conceptos coexisten con roles distintos.

---

## Compliance

- [x] `StoreConfig.IsSyncEnabled` existe desde commit `ebdb21d` (líneas 83-95)
- [x] `StoreConfig.IsRemote` existe (línea 77) — será reemplazado por `IsThinClient`
- [x] `StoreConfig.IsThinClient` introducido (`StoreConfig.cs:87`) — implementado (HU-025)
- [x] `Program.cs:160` usa `IsThinClient` en vez de `IsRemote` — implementado (HU-025)
- [x] `Program.cs:2039` usa `IsThinClient` en vez de `IsRemote` — implementado (HU-025; línea actual: 2154)
- [x] Tests `OfflineFirst_UsesSqliteStore_NotHttpStore` — implementado como `IsThinClient_OfflineFirstWithRemoteUrl_IsFalse` (`DeployProfileTests.cs:355`) (HU-025)
- [x] Tests `RemoteServer_UsesHttpStore` — implementado como `IsThinClient_LocalWithRemoteUrl_IsTrue` (`DeployProfileTests.cs:381`) (HU-025)
- [x] Tests `OfflineFirst_WithServerDown_CanWriteLocally` — implementado como `IsThinClient_OfflineFirstWithoutRemoteUrl_IsFalse` (`DeployProfileTests.cs:390`) (HU-025)

> **Nota**: Este ADR documenta la decisión arquitectónica. La implementación del fix (HU-025) está completa — `IsThinClient` live, `IsRemote` es alias `[Obsolete]`, tests en `DeployProfileTests.cs` + `EngramToolsTests.cs`.
