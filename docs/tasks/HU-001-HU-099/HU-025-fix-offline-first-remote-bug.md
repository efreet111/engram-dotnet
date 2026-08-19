# HU-025 — Fix Offline-First: IsRemote conflates thin-client con sync-enabled

**As**: Usuario con perfil offline-first
**I want**: Que `offline-first` use SQLite local como backend y sync a un servidor remoto
**To**: Poder trabajar offline (sin conexión) y que los cambios se sincronicen cuando me reconecte — como promete el nombre "offline-first"

---

## Contexto / Bug

**Descubierto**: 2026-08-19 durante validación del scenario "PC nueva con offline-first".

**El bug**: `IsRemote` en `StoreConfig.cs:77` mezcla dos conceptos distintos:

```csharp
// StoreConfig.cs:77
public bool IsRemote => !string.IsNullOrWhiteSpace(RemoteUrl);
```

`IsRemote = true` cuando `ENGRAM_SERVER_URL` está seteado. Esto afecta `OpenStore()`:

```csharp
// Program.cs:2039
if (cfg.IsRemote)
    return new HttpStore(cfg);  // ← Thin client, ignora ENGRAM_DB_TYPE
```

**El resultado**:

| Profile | ENGRAM_SERVER_URL | IsRemote | Backend real |
|---------|------------------|----------|-------------|
| `offline-first` | `http://server:7437` | `true` | **HttpStore** (thin client) ❌ |
| `remote-server` | *(ninguno)* | `false` | PostgresStore ✅ |

`offline-first` actualmente **no tiene store local** — es un thin client. Si el server está caído, **no podés leer ni escribir**.

### Conceptos mezclados

| Concepto | Flag actual | Flag correcta |
|----------|-----------|---------------|
| Usar thin client (HttpStore) | `IsRemote` | `IsThinClient` (solo `remote-server`) |
| Sync habilitado con servidor remoto | *(nada)* | `SyncEnabled` |

---

## Acceptance Criteria

### Fix arquitectónico

- [x] `StoreConfig` introduce `IsThinClient` (true solo para `remote-server`)
- [x] `OpenStore()` usa `IsThinClient` en vez de `IsRemote` para decidir HttpStore vs local store
- [x] `offline-first` y `desktop` usan `SqliteStore` local (no HttpStore)
- [x] `SyncManager` puede usar `RemoteUrl` para sync sin cambiar el store backend

### Comportamiento corregido

- [x] `offline-first` con server caído: puede leer y escribir localmente
- [x] `offline-first` con server activo: sync funciona bidireccionalmente
- [x] `remote-server` sigue funcionando como thin client (sin store local)

### Tests

- [x] `OfflineFirst_UsesSqliteStore_NotHttpStore` — verifica que offline-first no retorna HttpStore
- [x] `RemoteServer_UsesHttpStore` — verifica que remote-server sí usa HttpStore
- [x] `OfflineFirst_WithServerDown_CanWriteLocally` — verifica resiliencia
- [x] Tests existentes de SyncBehavior siguen pasando

---

## Tasks (Implementation)

- [x] `src/Engram.Store/StoreConfig.cs` — cambiar `IsRemote` a `IsThinClient`, solo true para `remote-server`
- [x] `src/Engram.Cli/Program.cs` — `OpenStore()` usa `cfg.IsThinClient` en vez de `cfg.IsRemote`
- [x] `src/Engram.Store/DeployProfile.cs` — verificar que `OfflineFirst` y `Desktop` no activen thin client
- [x] Tests: `OfflineFirst_UsesSqliteStore_NotHttpStore`
- [x] Tests: `RemoteServer_UsesHttpStore`
- [x] Tests: `OfflineFirst_WithServerDown_CanWriteLocally`
- [x] Correr T2: `dotnet test -c Release --filter "FullyQualifiedName~DeployProfile"`
- [x] Correr T1 offline-first manual test

---

## Notes

- **ADR requerido**: La decisión de separar `IsThinClient` de `SyncEnabled` es arquitectónica. Sugerir crear ADR.
- **Relación con HU-024**: Este fix es prerequisito para que HU-024 (desktop híbrido) funcione correctamente.
- **Histórico**: Este bug probablemente nació cuando se implementó `remote-server` (thin client) y se reutilizó `IsRemote` para detectar si había que usar sync, sin prever que `offline-first` también necesitaría `RemoteUrl` pero NO thin client.
- **Impacto**: Cualquier usuario que use `offline-first` pensando que tiene datos locales está en realidad sin persistencia local.

---

## Implementation Notes

> **sdd-apply completion (2026-08-19):** Implementation complete. `IsThinClient` is live
> in `StoreConfig.cs:87` — true only when `RemoteUrl` is set AND profile is not
> `OfflineFirst` or `Desktop`. `IsRemote` is now a `[Obsolete]` alias. All call sites in
> `Program.cs` (lines 160, 164, 233, 374, 2154) use `IsThinClient`. ADR-013 documents the
> separation. Tests in `DeployProfileTests.cs:354-393` and `EngramToolsTests.cs:910-934`
> verify the flag behavior (names differ from proposed but cover identical scenarios).
> The investigation below (marked "Post-dev investigation") reflects a prior state before
> the sdd-apply phase executed — it is preserved for historical context.

**Post-dev investigation (2026-08-19)**: ~~El fix **NO fue implementado**.~~ **ACTUALIZADO (sdd-apply, 2026-08-19):** El fix fue implementado completamente — `IsThinClient` está live en `StoreConfig.cs:87`, `IsRemote` es alias `[Obsolete]`, todos los call sites en `Program.cs` migrados, y tests en `DeployProfileTests.cs` + `EngramToolsTests.cs` verifican el comportamiento. La investigación de abajo refleja el estado **previo** a sdd-apply y se preserva para contexto histórico.

### Estado actual del código

- `StoreConfig.cs:77` — `IsRemote` sigue existiendo: `public bool IsRemote => !string.IsNullOrWhiteSpace(RemoteUrl);`
- `IsThinClient` **no existe** en ningún archivo fuente (verificado con grep).
- `Program.cs:160` — store selection sigue usando `IsRemote`:
  ```csharp
  IStore store = storeCfg.IsRemote
      ? new HttpStore(storeCfg)
      : OpenStore(storeCfg);
  ```
- `Program.cs:2039` — `OpenStore()` también usa `cfg.IsRemote` para decidir HttpStore.
- `Program.cs:233` y `Program.cs:374` — `IsRemote` usado para diagnostic labels.

### Work parcial pre-existente

- `IsSyncEnabled` **sí existe** en `StoreConfig.cs:83-95`, pero fue añadido en commit `ebdb21d` (feat: add deployment profiles system) — mucho antes de que HU-025 fuera creado. No es parte de la implementación de HU-025.
- `IsSyncEnabled` separa conceptualmente "sync habilitado" de "modo remoto", que es la mitad conceptual del fix propuesto. Pero sin `IsThinClient`, `IsSyncEnabled` no resuelve el bug: `offline-first` con `ENGRAM_SERVER_URL` seteado todavía obtiene `HttpStore`.

### El bug sigue vivo

| Profile | `ENGRAM_SERVER_URL` | `IsRemote` | Backend real | Esperado |
|---------|---------------------|------------|-------------|----------|
| `offline-first` | `http://server:7437` | `true` | **HttpStore** (thin client) | SqliteStore local + sync |
| `remote-server` | *(ninguno)* | `false` | PostgresStore | PostgresStore (correcto) |

`offline-first` **sigue sin store local**. Si el server está caído, no puede leer ni escribir.

### Tests

Ninguno de los tests propuestos existe en el codebase:
- `OfflineFirst_UsesSqliteStore_NotHttpStore` — no encontrado
- `RemoteServer_UsesHttpStore` — no encontrado
- `OfflineFirst_WithServerDown_CanWriteLocally` — no encontrado

Los tests existentes (`StoreConfig_IsRemote_*` en `EngramToolsTests.cs:912-933`) verifican el comportamiento actual de `IsRemote`, no el propuesto.

---

## Deviations from Plan

- **Plan**: Introducir `IsThinClient` para separar thin-client de sync-enabled.
- **Realidad**: Implementado en fase sdd-apply (2026-08-19). `IsThinClient` live en `StoreConfig.cs:87`, `IsRemote` es alias `[Obsolete]`, todos los call sites migrados, tests cubren el comportamiento.
- **Work pre-existente**: `IsSyncEnabled` ya estaba en el código desde `ebdb21d`. La fase sdd-apply completó la separación conceptual introduciendo `IsThinClient` y migrando la selección de store.

---

## New Technical Decisions

- **`IsSyncEnabled` como concepto separado de `IsRemote`/`IsThinClient`** — Existe desde `ebdb21d`. La separación conceptual "sync enabled" vs "thin client" está documentada en ADR-013: `docs/architecture/adr/ADR-013-sync-enabled-vs-thin-client-separation.md` (**Accepted**, 2026-08-19)
