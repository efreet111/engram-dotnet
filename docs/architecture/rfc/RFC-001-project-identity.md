# RFC-001: Project Identity Fingerprint

**Status:** Draft  
**Date:** 2026-06-05  
**Author:** Victor Silgado  
**Source:** PRD — Memoria Semántica v1.1, punto #1

---

## Problem

El identificador actual de proyecto (`project`) deriva del nombre de carpeta o ruta. Esto es frágil:

| Escenario | Comportamiento actual | Problema |
|-----------|----------------------|----------|
| Renombrar carpeta del repo | Cambia `project` → memorias viejas "desaparecen" | Silencioso, frustrante |
| Clonar en 2 ubicaciones | IDs distintos para el mismo proyecto | Duplicación, no hay consolidación |
| Mover a otra máquina | ID nuevo → como si fuera proyecto nuevo | Pierde historial |
| Cambiar de rama | Mismo ID para ramas diferentes | Contaminación semántica |

Con 3 usuarios actuales (Victor, Crhistian, +1) el impacto es bajo hoy, pero cada día que pasa hace más difícil migrar después.

## Solution

### Fase 1 (v1.0.0 — esta semana): UUID v5 + `.engram-id`

Crear un archivo `.engram-id` en la raíz del repositorio con un UUID v5 determinista:

```csharp
// Algoritmo de identidad
var identity = new ProjectIdentity();

// 1. Si existe .engram-id, leerlo
if (File.Exists(Path.Combine(repoPath, ".engram-id")))
    return ReadGuidFromFile();

// 2. Si no, calcular huella determinista
var originUrl = NormalizeUrl(git.RemoteOrigin); // "github.com/efreet111/engram-dotnet"
var firstCommitHash = GetFirstCommitSha();       // SHA1 del commit inicial
var fingerprint = $"{originUrl}|{firstCommitHash}";

// 3. UUID v5 con namespace fijo + huella
var projectId = Guid.Create(GuidNamespaces.Url, fingerprint);

// 4. Guardar .engram-id y commitear
File.WriteAllText(Path.Combine(repoPath, ".engram-id"), projectId.ToString());
```

**Características:**
- **Determinista:** mismo repo → mismo UUID en cualquier máquina
- **Persistente:** sobrevive renames, moves, clones
- **Compatible:** si está en `.engram-id`, se usa; si no, fallback al `project` actual
- **No rompe nada:** el `project` string se sigue usando como key en el store hasta v1.1

### Fase 2 (v1.1 — post-release): Branch-aware scoping

Extender a identificador compuesto `ProjectId + BranchId`:

```
ProjectId: 550e8400-e29b-41d4-a716-446655440000
BranchId:  feat/logging-infra (normalizado: minúsculas, sanitizado)
```

**Comportamiento por defecto:** filtrar memorias por rama actual. Búsqueda explícita puede cruzar ramas.

## Impact

### Beneficios
- **Estabilidad:** el proyecto nunca "pierde" sus memorias
- **Multi-dispositivo:** todos los clones ven la misma identidad
- **Fundación:** prerrequisito para sync avanzado (point #6), branch scoping, y consolidación

### Riesgos
- **Migración:** el `project` string actual se mantiene como fallback. Migración completa a GUID en v1.1 (ver RFC-003).
- **3 usuarios hoy:** commitear `.engram-id` al repo. Si alguien más lo clona después, hereda la identidad.
- **Conflicto de merge:** si dos personas generan `.engram-id` en paralelo → el UUID v5 es determinista → mismo valor. Conflict-free.

## Implementation Plan

### ENG-410 (esta sesión)
- [ ] Implementar `ProjectFingerprint.cs` con UUID v5
- [ ] Agregar fallback: si `.engram-id` existe → usar; si no → usar `project` string actual
- [ ] Generar `.engram-id` para este repo (engram-dotnet) y commitearlo
- [ ] Tests: mismo repo en 2 ubicaciones → mismo UUID; rename carpeta → mismo UUID

### Dependencias
- Ninguna. Aditivo. No rompe APIs existentes.

### Out of scope (para v1.1)
- Branch-aware ID
- Migración de `project` string → GUID en el store
- `.engram-id` auto-generación en `engram mcp` o `engram serve` startup

---

## References

- [PRD — Memoria Semántica v1.1](../PRD-memory-system-v1.1.md) — fuente original del análisis
- [ENG-410](../BACKLOG.md) — implementación
- [ENG-403 (Icebox)](../BACKLOG.md) — Phase 3 quitar `project` de writes
