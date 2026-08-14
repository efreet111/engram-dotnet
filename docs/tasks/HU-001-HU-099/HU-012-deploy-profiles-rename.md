# HU-012 — Renombrar Deployment Profiles

**As**: Developer o IT Admin desplegando engram-dotnet
**I want**: Perfiles de deployment renombrados y clarificados con semántica precisa
**To**: Eliminar confusión entre `server` y `sync`, y agregar un perfil `desktop` para uso personal (desktop↔laptop sync)

---

## Acceptance Criteria

- [ ] `ENGRAM_PROFILE=local` → SQLite backend, sync deshabilitado, solo uso interno (sin cambios)
- [ ] `ENGRAM_PROFILE=remote-server` → PostgreSQL backend, sync deshabilitado, **solo conexiones externas** (localhost bloqueado)
- [ ] `ENGRAM_PROFILE=offline-first` → **SQLite backend** (bugfix — antes incorrectly usaba PostgreSQL), sync habilitado, cliente que se conecta a remote-server
- [ ] `ENGRAM_PROFILE=desktop` → PostgreSQL backend, sync habilitado, **conexiones internas + externas** (para uso personal desktop↔laptop)
- [ ] `ENGRAM_PROFILE` defaults to `local` si no está configurado (backward compatible)
- [ ] Variables individuales (`ENGRAM_DB_TYPE`, `ENGRAM_SYNC_ENABLED`, etc.) siguen sobreescribiendo defaults del perfil
- [ ] `remote-server` valida que la conexión no sea a `localhost` o `127.0.0.1` y falla con mensaje claro
- [ ] Documentación actualizada: HU-010, RFC-003, ADR-011

---

## Tasks (Implementation)

- [ ] Renombrar `DeployProfile` enum: `Local` → `Local`, `Server` → `RemoteServer`, `Sync` → `OfflineFirst`, agregar `Desktop`
- [ ] Actualizar `ProfileDefaults` con los nuevos nombres y defaults correctos:
  - `local`: SQLite, sync disabled
  - `remote-server`: PostgreSQL, sync disabled
  - `offline-first`: **SQLite** (bugfix), sync enabled
  - `desktop`: PostgreSQL, sync enabled
- [ ] Implementar validación en `remote-server` que rechaza conexiones a `localhost` / `127.0.0.1` / `::1`
- [ ] Actualizar `scripts/deploy.sh` para usar los nuevos nombres de perfil en validación
- [ ] Actualizar `docker-compose.yml` y `docker-compose.embedded.yml` con nuevos nombres de perfil
- [ ] Actualizar tests `DeployProfileTests.cs` con:
  - Nuevos nombres de perfil
  - Test: `offline-first` → SQLite backend (verificando bugfix)
  - Test: `remote-server` con `localhost` → `InvalidOperationException`
- [ ] HU-010: Marcar como superada por HU-012, agregar nota de redirección
- [ ] RFC-003: Actualizar menciones de `sync` → `offline-first` y `server` → `remote-server`
- [ ] ADR-011: Agregar nota referenciando HU-012 para el renombrado de perfiles

---

## Profile Definitions (Nuevos)

### Profile: `local` (Sin cambios)

```
ENGRAM_DB_TYPE=sqlite
ENGRAM_SYNC_ENABLED=false
```

**Cuando usar**: Developer individual, no se comparte nada, sin requerimiento offline.

---

### Profile: `remote-server` (antes `server`)

```
ENGRAM_DB_TYPE=postgres
ENGRAM_SYNC_ENABLED=false
ENGRAM_PG_CONNECTION=<required>
ENGRAM_USER=<required>
```

**Cuando usar**: Equipo de 2–5 personas, servidor PostgreSQL compartido, conexión HTTP directa (sin sync). **No permite localhost** — solo conexiones externas para evitar accidental exposure.

**Validación de seguridad**:
```csharp
// remote-server NO permite:
ENGRAM_PG_CONNECTION=Host=localhost;...
ENGRAM_PG_CONNECTION=Host=127.0.0.1;...
```

---

### Profile: `offline-first` (antes `sync`) — BUGFIX

```
ENGRAM_DB_TYPE=sqlite   ← BUGFIX: antes incorrectamente usaba postgres
ENGRAM_SYNC_ENABLED=true
ENGRAM_SYNC_POLL_SECONDS=30
ENGRAM_SERVER_URL=<required — URL del remote-server>
ENGRAM_USER=<required>
```

**Cuando usar**: Equipo de 5–20 personas, modo offline-first, cada dev tiene SQLite local + SyncManager. **El default es SQLite**, no PostgreSQL.

> ⚠️ **Bug actual**: El perfil `sync` (current) usa PostgreSQL como default. Esto es incorrecto — el caso de uso de `sync` es tener una base local SQLite que se sincroniza. PostgreSQL como backend local no tiene sentido para este perfil.

---

### Profile: `desktop` (NUEVO)

```
ENGRAM_DB_TYPE=postgres
ENGRAM_SYNC_ENABLED=true
ENGRAM_SYNC_POLL_SECONDS=30
ENGRAM_SERVER_URL=<required — URL del remote-server>
ENGRAM_USER=<required>
ENGRAM_ALLOW_LOCALHOST=true  ← Permite conexiones locales para uso personal
```

**Cuando usar**: Uso personal (desktop↔laptop sync). El usuario quiere PostgreSQL local para mayor capacidad pero también quiere ability de sync a un remote-server. **Permite conexiones localhost** porque es uso personal, no production.

**Diferencia con `offline-first`**:

| Aspect | `offline-first` | `desktop` |
|--------|-----------------|-----------|
| DB local | SQLite | PostgreSQL |
| Caso de uso | Equipo, dev individual | Personal, desktop↔laptop |
| `ENGRAM_ALLOW_LOCALHOST` | false | true |

---

## Security: Localhost Blocking en `remote-server`

### Por qué

El perfil `server` (→ `remote-server`) está diseñado para production deployments donde el PostgreSQL es externo y no debe ser accessible desde localhost. Un developer que accidentalmente configura `ENGRAM_PG_CONNECTION=Host=localhost` podría:
1. Conectarse a un PostgreSQL local wrong
2. Exponer credenciales de production a un servicio local

### Implementación

```csharp
// ProfileValidation.cs
public static void ValidateRemoteServerConnection(string pgConnection)
{
    var blockedHosts = new[] { "localhost", "127.0.0.1", "::1" };
    var uri = new Uri(pgConnection.Replace("Host=", "").Split(';')[0]);
    
    if (blockedHosts.Contains(uri.Host))
    {
        throw new InvalidOperationException(
            $"remote-server profile does not allow localhost connections. " +
            $"Use a non-localhost PostgreSQL host for production deployments.");
    }
}
```

### Excepciones

- `desktop` profile SÍ permite localhost (es uso personal)
- `local` profile usa SQLite, no tiene este issue

---

## Notes

- **Relación con HU-010**: HU-012 supersede HU-010 para la definición de perfiles. HU-010 queda como historial.
- **Breaking change**: Deployments existentes que usan `ENGRAM_PROFILE=server` o `ENGRAM_PROFILE=sync` necesitan actualizarse a `remote-server` y `offline-first`.
- **Migración documentada en**: `docs/DEPLOYMENT.md` sección Migration (HU-012)
- **ADR候选**: Considerar documentar la regla de "localhost blocking en production profiles" como ADR si resulta útil para otros contextos de security
