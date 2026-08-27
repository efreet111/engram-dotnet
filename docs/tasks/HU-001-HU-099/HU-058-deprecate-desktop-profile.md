# HU-058 — Deprecación Temporal del Perfil Desktop

**As**: Developer / Mantainer
**I want**: Quitar temporalmente el perfil `desktop` del sistema de deployment profiles
**To**: Eliminar la fricción que genera un perfil incompleto mientras se resuelve la arquitectura necesaria (ADR-014)

---

## Acceptance Criteria

- [ ] El enum `DeployProfile.Desktop` existe pero está marcado como `[Obsolete]`
- [ ] Los tests unitarios de `DeployProfile.Desktop` están skipados con referencia a HU-024 y ADR-014
- [ ] La documentación de `docs/DEPLOYMENT.md` marca el perfil `desktop` como "⚠️ Postponed"
- [ ] La documentación de `docs/01-QUICK-START.md` marca el perfil `desktop` como "⚠️ Postponed"
- [ ] `engram profile set desktop` devuelve un error claro indicando que el perfil está temporalmente no disponible
- [ ] `engram profile list` o `--help` muestra `desktop` con sufijo "(deprecated)" o "⚠️"
- [ ] HU-024 permanece como referencia del trabajo faltante
- [ ] ADR-014 permanece como referencia arquitectónica del problema
- [ ] No se elimina código — solo se depreca y documenta

---

## Tasks (Implementation)

- [ ] Editar `src/Engram.Store/DeployProfile.cs`: agregar `[Obsolete("Desktop profile is temporarily suspended. See HU-058 and ADR-014.", DiagnosticId = "ENGRAM_DEPRECATED")]` a `DeployProfile.Desktop`
- [ ] Editar `src/Engram.Store/DeployProfile.cs`: hacer que `FromEnvironment("desktop")` lance `NotSupportedException` con mensaje claro
- [ ] Editar `src/Engram.Cli/Program.cs`: actualizar `--help` del argumento `profile` para marcar `desktop` como deprecated
- [ ] Editar `src/Engram.Cli/ProfileConfig.cs`: hacer que `FromEnvironment("desktop")` lance `NotSupportedException`
- [ ] Editar `tests/Engram.Store.Tests/DeployProfileTests.cs`: agregar `Skip = "Desktop profile deferred — see HU-024 and ADR-014"` a los tests que usan `DeployProfile.Desktop`
- [ ] Editar `tests/Engram.Cli.Tests/ProfileSetTests.cs`: agregar `Skip` a los tests de `profile set desktop`
- [ ] Editar `tests/Engram.Cli.Tests/ProfileShowTests.cs`: agregar `Skip` al test que usa `desktop`
- [ ] Editar `docs/DEPLOYMENT.md`: marcar fila `desktop` con "⚠️ Postponed" y link a HU-058
- [ ] Editar `docs/01-QUICK-START.md`: marcar perfil `desktop` como "⚠️ Postponed"
- [ ] Actualizar `docs/BACKLOG.md`: crear entrada ENG-XXX para la continuación del perfil desktop

---

## Notes

### Dependencias

- HU-024 — Perfil Desktop Híbrido (trabajo original incompleto)
- ADR-014 — Desktop hybrid sync requires dedicated engram server instance (análisis arquitectónico)
- HU-058 — Este documento (deprecación temporal)

### Arquitectura del problema (documentada en ADR-014)

El perfil `desktop` requiere **dos instancias separadas** de `engram serve`:
1. **Instancia local** (cliente): `engram serve --profile desktop` → SQLite local, sync habilitado, puerto 7431
2. **Instancia hub** (Docker): `engram serve --profile remote-server` → PostgreSQL, puerto 7437

Actualmente ambas roles chocan en `ENGRAM_SERVER_URL=http://localhost:7437` (misma URL para ambas), y el self-loop guard de SyncManager (ADR-008) desactiva el sync.

### Qué se necesita para retomar

Según ADR-014:
- Segunda imagen/container `engram serve --profile remote-server` apuntando al PostgreSQL
- Compose que levante ambos (local SQLite + hub PG-backed)
- `ENGRAM_SERVER_URL` del cliente apuntando al hub (no a sí mismo)
- Tests de integración multi-instancia

### Alternativas consideradas

1. **Eliminar código** — No. Rompe tests, rompe链路 de documentación, pérdida de contexto histórico.
2. **Solo comentar** — No. Genera deuda técnica silenciosa y confunde futuros desarrolladores.
3. **Deprecación con `[Obsolete]`** — Elegida. Visible en compilación, mensaje claro, reversible.

### Criteria de reincorporación

Cuando se retome el perfil desktop, antes de marcar HU-024 como completo:
- [ ] Dos instancias de `engram serve` funcionando (local SQLite + hub PG-backed)
- [ ] Sync multi-dispositivo verificado (desktop ↔ laptop)
- [ ] Tests de integración passing
- [ ] Documentación actualizada reflejando la topología correcta
