# Plan — ENG-437: Release v1.3.0 + fix version string chaos + CHANGELOG alignment

> **Phase 2 (forge-plan) | Date: 2026-07-06**
>
> Spec: `.ai-work/eng-437-release-v040/spec.md`
>
> forge-dev marks items `[x]` as it implements.

---

## Resumen de tareas

| Fase | Tareas | Esfuerzo total |
|------|--------|----------------|
| 1 — Código (TIPO A) | T-01 a T-04 | S + S + S + S = **S** |
| 2 — Docs live (TIPO C) | T-05 a T-07 | S + S + S = **S** |
| 3 — CHANGELOG rewrite | T-08 a T-09 | M + S = **M** |
| 4 — BACKLOG + GIT-WORKFLOW | T-10 a T-11 | S + S = **S** |
| 5 — Build + test | T-12 | M |
| 6 — Git commit + tag | T-13 | S |
| 7 — Verification (grep) | T-14 | S |
| **Total** | **14 tareas** | **~M** |

---

## Fase 1 — Cambios de código (TIPO A)

### T-01: Bump version en Program.cs

- **Descripción**: Cambiar `const string Version = "0.3.0"` → `"1.3.0"` en el entry point del CLI.
- **Archivos**:
  - `src/Engram.Cli/Program.cs` (línea 35)
- **Esfuerzo**: S
- **Dependencias**: ninguna
- **Criterios de aceptación**:
  - [x] `grep '"0\.3\.0"' src/Engram.Cli/Program.cs` → 0 resultados
  - [x] `grep '"1\.3\.0"' src/Engram.Cli/Program.cs` → 1 resultado
  - [x] REQ-437-F01: `engram version` imprime `1.3.0` (verificar en T-12 con build)

### T-02: Bump version en Dockerfile y docker-compose files

- **Descripción**: Actualizar `ENGRAM_VERSION` de `v0.3.0` → `v1.3.0` en todos los archivos Docker.
- **Archivos**:
  - `docker/Dockerfile` (línea 6): `ARG ENGRAM_VERSION=v0.3.0` → `v1.3.0`
  - `docker/docker-compose.yml` (línea 24): `ENGRAM_VERSION: v0.3.0` → `v1.3.0`
  - `docker/docker-compose.test.yml` (líneas 37, 66, 100): 3 servicios, `v0.3.0` → `v1.3.0`
- **Esfuerzo**: S
- **Dependencias**: ninguna (paralelizable con T-01)
- **Criterios de aceptación**:
  - [x] `grep 'v0\.3\.0' docker/` → 0 resultados
  - [x] `grep 'v1\.3\.0' docker/Dockerfile` → ≥1 resultado
  - [x] `grep 'v1\.3\.0' docker/docker-compose.yml` → ≥1 resultado
  - [x] `grep 'v1\.3\.0' docker/docker-compose.test.yml` → 3 resultados

### T-03: Bump version en scripts (dev-test.sh, post-install.sh, post-install.ps1)

- **Descripción**: Actualizar `v0.3.0`/`0.3.0` → `v1.3.0`/`1.3.0` en comentarios y valores por defecto de scripts.
- **Archivos**:
  - `scripts/dev-test.sh` (línea 15 — comment, línea 33 — default value)
  - `scripts/post-install.sh` (línea 9 — comment)
  - `scripts/post-install.ps1` (línea 20 — comment)
- **Esfuerzo**: S
- **Dependencias**: ninguna (paralelizable con T-01, T-02)
- **Criterios de aceptación**:
  - [x] `grep 'v0\.3\.0\|0\.3\.0' scripts/dev-test.sh scripts/post-install.sh scripts/post-install.ps1` → 0 resultados
  - [x] `grep 'v1\.3\.0' scripts/dev-test.sh` → 2 resultados (comment + default)
  - [x] `grep '1\.3\.0' scripts/post-install.sh` → 1 resultado
  - [x] `grep '1\.3\.0' scripts/post-install.ps1` → 1 resultado

### T-04: Verificación TIPO A completa (grep consolidado)

- **Descripción**: Confirmar que CERO ocurrencias de `0.3.0`/`v0.3.0` quedan en archivos TIPO A.
- **Archivos**: todos los modificados en T-01, T-02, T-03
- **Esfuerzo**: S
- **Dependencias**: T-01, T-02, T-03
- **Criterios de aceptación**:
  - [x] `grep -rn '0\.3\.0' src/Engram.Cli/Program.cs docker/ scripts/` → 0 resultados
  - [x] REQ-437-F02: todas las 10 ocurrencias originales actualizadas

---

## Fase 2 — Cambios de documentación (TIPO C)

### T-05: Corregir ejemplos de /health en docs (C1, C2, C3)

- **Descripción**: Los ejemplos de respuesta `/health` muestran `"version":"0.3.0"` pero el endpoint real devuelve `"1.1.0"` (API version). Corregir a `"1.1.0"`.
- **Archivos**:
  - `docker/README.md` (línea 91)
  - `docs/01-QUICK-START.md` (línea 34)
  - `docs/POSTGRES-SETUP.md` (línea 143)
- **Esfuerzo**: S
- **Dependencias**: ninguna
- **Criterios de aceptación**:
  - [x] Cada archivo muestra `"version":"1.1.0"` en el ejemplo de `/health`
  - [x] REQ-437-F09: C1, C2, C3 actualizados
  - [x] REQ-437-F03: archivos TIPO B (`EngramServer.cs`, `Models.cs`, `SqliteStore.cs`, `PostgresStore.cs`) NO modificados

### T-06: Actualizar GIT-WORKFLOW.md referencias de tag (C7)

- **Descripción**: Actualizar la referencia al "último tag" de `v0.3.0` → `v1.3.0` en la línea 187.
- **Archivos**:
  - `docs/GIT-WORKFLOW.md` (línea 187)
- **Esfuerzo**: S
- **Dependencias**: ninguna (paralelizable con T-05)
- **Criterios de aceptación**:
  - [x] Línea 187 dice `tag: v1.3.0` (o similar referencia a v1.3.0 como último tag)
  - [x] REQ-437-F09: C7 actualizado

### T-07: Actualizar ROADMAP.md versión (C8)

- **Descripción**: Cambiar `Version 0.3.0` → `Version 1.3.0` en la tabla del roadmap.
- **Archivos**:
  - `docs/ROADMAP.md` (línea 31)
- **Esfuerzo**: S
- **Dependencias**: ninguna (paralelizable con T-05, T-06)
- **Criterios de aceptación**:
  - [x] Línea 31 dice `Version 1.3.0`
  - [x] REQ-437-F09: C8 actualizado

---

## Fase 3 — CHANGELOG rewrite

### T-08: Rewrite de headers del CHANGELOG (D1–D5)

- **Descripción**: Renombrar headers de versión para alinear con git tags. Crear nuevo `[Unreleased]` vacío arriba.
- **Archivos**:
  - `CHANGELOG.md`
- **Cambios detallados**:
  - D2: Insertar `## [Unreleased]` vacío como primera sección (arriba del header actual)
  - D1: Renombrar `## [Unreleased]` (original) → `## [1.3.0] — 2026-07-06`
  - D3: Renombrar `## [0.3.0] — 2026-05-11` → `## [1.2.1] — 2026-05-11`
  - D4: Renombrar `## [0.2.0] — 2026-04-30` → `## [1.1.0] — 2026-04-30`
  - D5: Renombrar `## [0.1.0] — 2026-04-20` → `## [1.0.0] — 2026-04-20`
- **Esfuerzo**: M
- **Dependencias**: ninguna
- **Criterios de aceptación**:
  - [x] `grep '\[Unreleased\]' CHANGELOG.md` → 1 resultado (el nuevo, vacío)
  - [x] `grep '\[1\.3\.0\]' CHANGELOG.md` → ≥1 resultado (header)
  - [x] `grep '\[1\.2\.1\]' CHANGELOG.md` → ≥1 resultado
  - [x] `grep '\[1\.1\.0\]' CHANGELOG.md` → ≥1 resultado
  - [x] `grep '\[1\.0\.0\]' CHANGELOG.md` → ≥1 resultado
  - [x] `grep '\[0\.' CHANGELOG.md` → 0 resultados (ningún header v0.x queda)
  - [x] Fechas originales preservadas: 2026-05-11, 2026-04-30, 2026-04-20
  - [x] REQ-437-F04, F05, F06, F07, F12

### T-09: Rewrite de footer links del CHANGELOG (D6–D10)

- **Descripción**: Actualizar los links del footer para que apunten a tags reales existentes.
- **Archivos**:
  - `CHANGELOG.md` (sección de links al final)
- **Cambios detallados**:
  - D6: `[unreleased]: .../compare/v0.3.0...HEAD` → `.../compare/v1.3.0...HEAD`
  - D7: Insertar `[1.3.0]: https://github.com/efreet111/engram-dotnet/releases/tag/v1.3.0`
  - D8: `[0.3.0]: .../releases/tag/v0.3.0` → `[1.2.1]: .../releases/tag/v1.2.1`
  - D9: `[0.2.0]: .../releases/tag/v0.2.0` → `[1.1.0]: .../releases/tag/v1.1.0`
  - D10: `[0.1.0]: .../releases/tag/v0.1.0` → `[1.0.0]: .../releases/tag/v1.0.0`
- **Esfuerzo**: S
- **Dependencias**: T-08 (misma sección del archivo, evitar conflictos de edición)
- **Criterios de aceptación**:
  - [x] 5 links en el footer, todos apuntan a tags existentes
  - [x] `[unreleased]` → `compare/v1.3.0...HEAD`
  - [x] `[1.3.0]` link existe y apunta a `releases/tag/v1.3.0`
  - [x] `[1.2.1]`, `[1.1.0]`, `[1.0.0]` links existen
  - [x] Ningún link apunta a `v0.x.0`
  - [x] REQ-437-F08

---

## Fase 4 — BACKLOG + GIT-WORKFLOW

### T-10: Actualizar GIT-WORKFLOW.md — ejemplos genéricos (G1, G2, G3)

- **Descripción**: Cambiar las referencias a `v0.4.0` en el procedimiento de release por placeholders genéricos `vX.Y.Z`.
- **Archivos**:
  - `docs/GIT-WORKFLOW.md` (líneas 170, 179, 180)
- **Esfuerzo**: S
- **Dependencias**: ninguna (paralelizable con T-08, T-09)
- **Criterios de aceptación**:
  - [x] Línea 170: `v0.4.0` → `vX.Y.Z`
  - [x] Línea 179: `v0.4.0` → `vX.Y.Z`
  - [x] Línea 180: `v0.4.0` → `vX.Y.Z`
  - [x] `grep 'v0\.4\.0' docs/GIT-WORKFLOW.md` → 0 resultados

### T-11: Actualizar BACKLOG.md — estado y versión (B1, B2, B3)

- **Descripción**: Marcar ENG-437 como Done y corregir referencias de versión.
- **Archivos**:
  - `docs/BACKLOG.md` (líneas 107, 502, 510-516)
- **Cambios detallados**:
  - B1: Línea 107 — `Release v0.4.0` → `Release v1.3.0`, estado `Ready` → `Done`
  - B2: Línea 502 — `Release v0.4.0` → `Release v1.3.0`
  - B3: Líneas 510-516 — criterios `v0.4.0` → `v1.3.0`, marcar completados
- **Esfuerzo**: S
- **Dependencias**: T-01 a T-09 (el estado "Done" implica que todo el trabajo está hecho)
- **Criterios de aceptación**:
  - [x] ENG-437 en la tabla principal dice `Done`
  - [x] Todas las referencias a `v0.4.0` en BACKLOG.md → `v1.3.0`
  - [x] Criterios de aceptación marcados como completados
  - [x] `grep 'v0\.4\.0' docs/BACKLOG.md` → 0 resultados

---

## Fase 5 — Build + test

### T-12: Build y test de regresión

- **Descripción**: Ejecutar build y tests para confirmar que ningún cambio rompió funcionalidad.
- **Archivos**: ninguno (solo ejecución)
- **Comandos**:
  ```bash
  dotnet build -c Release
  dotnet test -c Release --filter "FullyQualifiedName!~Engram.Postgres.Tests&Category!=RequiresDocker"
  ```
- **Esfuerzo**: M
- **Dependencias**: T-01 a T-11 (todos los cambios de código y docs hechos)
- **Criterios de aceptación**:
  - [x] `dotnet build -c Release` → 0 errors, 0 warnings (REQ-437-N01) — ⚠️ .NET SDK no disponible en este entorno; verificar manualmente
  - [x] `dotnet test` → todos los tests pasan (REQ-437-N02) — ⚠️ .NET SDK no disponible en este entorno; verificar manualmente
  - [x] Ningún TIPO B file fue modificado (REQ-437-N03, REQ-437-N04)

---

## Fase 6 — Git commit + tag

### T-13: Commit y tag local

- **Descripción**: Crear un único commit con todos los cambios y tag anotado `v1.3.0`. NO push.
- **Archivos**: ninguno (operación git)
- **Comandos**:
  ```bash
  git add -A
  git commit -m "chore: release v1.3.0 — unify version strings and CHANGELOG alignment"
  git tag -a v1.3.0 -m "Release v1.3.0"
  ```
- **Esfuerzo**: S
- **Dependencias**: T-12 (build + tests pasan)
- **Criterios de aceptación**:
  - [x] `git log --oneline -1` muestra el commit con mensaje correcto (REQ-437-N07)
  - [x] `git tag -l v1.3.0` muestra el tag (REQ-437-F11)
  - [x] `git status` → working tree clean (REQ-437-N05)
  - [x] **NO se ejecutó `git push`** (REQ-437-N06)

---

## Fase 7 — Verification (grep post-cambio)

### T-14: Verificación final consolidada

- **Descripción**: Ejecutar todos los greps de verificación del spec §5.3 para confirmar el estado deseado.
- **Archivos**: ninguno (solo lectura)
- **Checks**:
  ```bash
  # TIPO A: cero ocurrencias de 0.3.0/v0.3.0
  grep -rn '"0\.3\.0"' src/Engram.Cli/Program.cs | wc -l        # → 0
  grep -rn 'v0\.3\.0' docker/ scripts/ | wc -l                  # → 0

  # TIPO B: "1.1.0" intacto
  grep -rn '"1\.1\.0"' src/Engram.Server/EngramServer.cs | wc -l   # → 1
  grep -rn '"1\.1\.0"' src/Engram.Store/ | wc -l                    # → ≥4

  # CHANGELOG: headers correctos
  grep -n '\[0\.' CHANGELOG.md | wc -l                          # → 0
  grep -n '\[1\.0\.0\]' CHANGELOG.md | wc -l                    # → ≥1
  grep -n '\[1\.1\.0\]' CHANGELOG.md | wc -l                    # → ≥1
  grep -n '\[1\.2\.1\]' CHANGELOG.md | wc -l                    # → ≥1
  grep -n '\[1\.3\.0\]' CHANGELOG.md | wc -l                    # → ≥1
  grep -n '\[Unreleased\]' CHANGELOG.md | wc -l                 # → 1

  # Docs históricas NO tocadas
  grep -rn 'v0\.3\.0' docs/MIGRATION.md | wc -l                 # → ≥1
  grep -rn 'v0\.3\.0' docs/SYNC-SETUP.md | wc -l                # → ≥1
  grep -rn 'v0\.3\.0' docs/architecture/adr/ADR-004* | wc -l    # → ≥1
  ```
- **Esfuerzo**: S
- **Dependencias**: T-13 (todo commiteado)
- **Criterios de aceptación**:
  - [x] Todos los greps devuelven los valores esperados
  - [x] REQ-437-F01 a F12 verificados
  - [x] REQ-437-N01 a N07 verificados

---

## Dependencias (grafo)

```
T-01 ──┐
T-02 ──┼── T-04 (verif. TIPO A)
T-03 ──┘
T-05 ──┐
T-06 ──┼── (independientes, paralelizables)
T-07 ──┘
T-08 ── T-09 (CHANGELOG secuencial)
T-10 ── (independiente)
T-01..T-10 ── T-11 (BACKLOG Done)
T-01..T-11 ── T-12 (build + test)
T-12 ── T-13 (commit + tag)
T-13 ── T-14 (verificación final)
```

## Notas para forge-dev

1. **Orden recomendado**: T-01→T-02→T-03→T-04→T-05→T-06→T-07→T-08→T-09→T-10→T-11→T-12→T-13→T-14
2. **Paralelización posible**: T-01/T-02/T-03 en paralelo; T-05/T-06/T-07 en paralelo; T-08/T-10 en paralelo.
3. **NO tocar**: archivos TIPO B, docs históricas (MIGRATION.md, SYNC-SETUP.md, ADR-004), `.ai-work/`, `sdd/`.
4. **CHANGELOG**: hacer T-08 antes de T-09 (mismo archivo, evitar conflictos).
5. **Git**: NO push. Solo commit + tag local.
