---
observation_id: 41
type: "decision"
title: "ENG-458: Mutaciones con project vacío bloquean sync"
created_at: "2026-07-16 02:48:36"
topic_key: "eng-458-empty-project-blocks-sync"
project: "team/engram-dotnet"
scope: "personal"
generated_at: "2026-07-21T22:00:59.5731380Z"
---

# ENG-458: Mutaciones con project vacío bloquean sync

**What**: Bug crítico descubierto — mutaciones con project="" bloquean TODO el sync

**Why**: CountPendingNonEnrolledAsync en SqliteStore.cs:2050 cuenta mutaciones huérfanas con project="" como "no enroladas" y bloquea el push completo

**Where**: src/Engram.Store/SqliteStore.cs, función CountPendingNonEnrolledAsync

**Impact**: 3 mutaciones huérfanas bloquearon sync de 38 mutaciones válidas (20 team/engram-dotnet + 18 team/flowforge). Pérdida de datos silenciosa.

**Root cause**:
```csharp
// ExtractProjectFromPayload devuelve "" si no hay project
private static string ExtractProjectFromPayload(object payload)
{
    if (payload is null) return "";
    ...
}

// Query cuenta project="" como no enrolado
SELECT sm.project, COUNT(*) as count
FROM sync_mutations sm
LEFT JOIN sync_enrolled_projects ep ON sm.project = ep.project
WHERE sm.target_key = @target AND sm.acked_at IS NULL AND ep.project IS NULL
GROUP BY sm.project
```

**Fix propuesto**: Añadir `AND sm.project != ''` al WHERE clause

**Status**: ENG-458 creada en BACKLOG.md, P0, Ready, Effort S
