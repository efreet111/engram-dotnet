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

- [ ] `StoreConfig` introduce `IsThinClient` (true solo para `remote-server`)
- [ ] `OpenStore()` usa `IsThinClient` en vez de `IsRemote` para decidir HttpStore vs local store
- [ ] `offline-first` y `desktop` usan `SqliteStore` local (no HttpStore)
- [ ] `SyncManager` puede usar `RemoteUrl` para sync sin cambiar el store backend

### Comportamiento corregido

- [ ] `offline-first` con server caído: puede leer y escribir localmente
- [ ] `offline-first` con server activo: sync funciona bidireccionalmente
- [ ] `remote-server` sigue funcionando como thin client (sin store local)

### Tests

- [ ] `OfflineFirst_UsesSqliteStore_NotHttpStore` — verifica que offline-first no retorna HttpStore
- [ ] `RemoteServer_UsesHttpStore` — verifica que remote-server sí usa HttpStore
- [ ] `OfflineFirst_WithServerDown_CanWriteLocally` — verifica resiliencia
- [ ] Tests existentes de SyncBehavior siguen pasando

---

## Tasks (Implementation)

- [ ] `src/Engram.Store/StoreConfig.cs` — cambiar `IsRemote` a `IsThinClient`, solo true para `remote-server`
- [ ] `src/Engram.Cli/Program.cs` — `OpenStore()` usa `cfg.IsThinClient` en vez de `cfg.IsRemote`
- [ ] `src/Engram.Store/DeployProfile.cs` — verificar que `OfflineFirst` y `Desktop` no activen thin client
- [ ] Tests: `OfflineFirst_UsesSqliteStore_NotHttpStore`
- [ ] Tests: `RemoteServer_UsesHttpStore`
- [ ] Tests: `OfflineFirst_WithServerDown_CanWriteLocally`
- [ ] Correr T2: `dotnet test -c Release --filter "FullyQualifiedName~DeployProfile"`
- [ ] Correr T1 offline-first manual test

---

## Notes

- **ADR requerido**: La decisión de separar `IsThinClient` de `SyncEnabled` es arquitectónica. Sugerir crear ADR.
- **Relación con HU-024**: Este fix es prerequisito para que HU-024 (desktop híbrido) funcione correctamente.
- **Histórico**: Este bug probablemente nació cuando se implementó `remote-server` (thin client) y se reutilizó `IsRemote` para detectar si había que usar sync, sin prever que `offline-first` también necesitaría `RemoteUrl` pero NO thin client.
- **Impacto**: Cualquier usuario que use `offline-first` pensando que tiene datos locales está en realidad sin persistencia local.
