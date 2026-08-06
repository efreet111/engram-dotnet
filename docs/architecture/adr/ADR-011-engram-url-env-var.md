# ADR-011: Estandarización de `ENGRAM_SERVER_URL` como variable canónica

**Status:** Accepted
**Date:** 2026-08-06
**Deciders:** victor
**Related:** HU-010, ENG-452, ADR-008

## Context

Durante la implementación de HU-010 (Deployment Profile System), se identificó una inconsistencia en las variables de entorno relacionadas con la URL del servidor remoto:

### Inconsistencia encontrada

| Archivo | Variable usada | Problema |
|---------|--------------|----------|
| `StoreConfig.cs:45` | `ENGRAM_URL` | Legado, no documentado |
| `EngramServer.cs:64` | `ENGRAM_SERVER_URL` | ✅ Correcto |
| `SyncManager.cs` | `ENGRAM_SERVER_URL` | ✅ Correcto |
| `EngramTools.cs:18` | `ENGRAM_URL` (comentario) | Solo documentación |
| `HttpStore.cs:12` | `ENGRAM_URL` (comentario) | Solo documentación |
| Tests y docs | Mezcla de ambas | Confusión |

El resto del codebase (85+ occurrences) usa consistentemente `ENGRAM_SERVER_URL`.

### Por qué importa

1. **Confusión**: Los usuarios ven `ENGRAM_URL` en algunos contextos y `ENGRAM_SERVER_URL` en otros
2. **Bug real**: `StoreConfig.RemoteUrl` (usado para `RemoteUrl` en sync) leía `ENGRAM_URL`, no `ENGRAM_SERVER_URL`
3. **Documentación contradictoria**: Los docs más recientes (SYNC-SETUP.md, docker/README.md) usan `ENGRAM_SERVER_URL`
4. **Inconsistencia con SyncManager**: `SyncManagerConfig` ya usa `ENGRAM_SERVER_URL` pero `StoreConfig.RemoteUrl` no

## Decision

Estandarizar en `ENGRAM_SERVER_URL` como la variable canónica para la URL del servidor remoto de sync.

### Cambio aplicado

```csharp
// StoreConfig.cs:45 — ANTES (bug)
public string? RemoteUrl { get; init; } = Environment.GetEnvironmentVariable("ENGRAM_URL");

// StoreConfig.cs:45 — DESPUÉS (fix)
public string? RemoteUrl { get; init; } = Environment.GetEnvironmentVariable("ENGRAM_SERVER_URL");
```

### Rationale

1. **`ENGRAM_SERVER_URL` es la forma dominante**: 85+ occurrences vs ~5 de `ENGRAM_URL`
2. **`ENGRAM_URL` es ambiguо**: Podría interpretarse como URL del cliente (local), no del servidor
3. **`ENGRAM_SERVER_URL` es auto-explicativo**: El nombre indica claramente que es la URL del servidor
4. **Aligns con SyncManager**: `SyncManagerConfig` ya usa `ENGRAM_SERVER_URL`

## Consequences

### Positive

1. Una sola variable canónica para la URL del servidor
2. Alineación total con `SyncManagerConfig`
3. Documentación consistente
4. Nombre más claro para nuevos usuarios

### Negative (breaking change)

**Deployments existentes que usan `ENGRAM_URL` necesitan actualizarse a `ENGRAM_SERVER_URL`**.

Scripts y configs que setean `ENGRAM_URL` van a romper. Los affected paths:

- Tests con `ENGRAM_URL` hardcoded
- Comentarios en `EngramTools.cs`, `HttpStore.cs`
- Template configs antiguos

### Mitigations

1. **Migración**: Agregar esta nota a `docs/DEPLOYMENT.md` sección Migration
2. **Backward-compat temporal**: Podríamos leer ambas variables por un período de transición, pero no es necesario porque `ENGRAM_URL` nunca estuvo documentado como oficial
3. **ADR**: Este ADR sirve como registro de la decisión para equipos que actualicen desde versiones anteriores

## Compliance

- [x] `StoreConfig.cs:45` fix verificado — ahora lee `ENGRAM_SERVER_URL`
- [x] `scripts/deploy.sh` usa `ENGRAM_SERVER_URL` consistentemente
- [x] `docker-compose.yml` y `docker-compose.embedded.yml` usan `ENGRAM_SERVER_URL`
- [x] Documentación actualizada: `docs/SYNC-SETUP.md`, `docs/DEPLOYMENT.md`
- [x] Tests de `DeployProfileTests.cs` verifican `ENGRAM_SERVER_URL`
