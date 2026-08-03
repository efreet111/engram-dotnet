# Verify Report: Docker Vanilla Build Diagnosis — Re-verify (Cycle 2)

## Summary

Re-auditoría post-rework cycle 1/3. Se verificaron los 3 fixes del `rework_ticket.md` contra la implementación actual. Todos los fixes están correctamente aplicados. Spec compliance se mantiene al 100%. Sin issues nuevos detectados.

**Resultado**: **PASS** ✅

---

## Rework Fixes — Verification

### Fix 1: `context-map.md` con `## Reusable Patterns Found`

| Criterio | Estado | Evidencia |
|----------|--------|-----------|
| Archivo existe | ✅ PASS | `.ai-work/docker-vanilla-build-diagnosis/context-map.md` (82 líneas) |
| Sección `## Reusable Patterns Found` presente | ✅ PASS | Línea 17 |
| 4 patrones documentados | ✅ PASS | Líneas 19-37: (1) Multi-stage SemVer ARG, (2) Debian-slim fallback, (3) NuGet-compatible defaults, (4) .dockerignore secrets exclusion |

**Cada patrón incluye**: nombre descriptivo, descripción del pattern, archivos donde se aplica, explicación del por qué.

**Fix 1 verdict**: ✅ **PASS**

---

### Fix 2: `Dockerfile.debian` header corregido (líneas 28-30)

| Criterio | Estado | Evidencia |
|----------|--------|-----------|
| Ya no dice "~300 MB larger" | ✅ PASS | Texto anterior eliminado |
| Dice "~5 min cold build (vs ~3 min with mcr)" | ✅ PASS | `Dockerfile.debian:28` |
| Dice "Final image size is similar (~360 MB virtual)" | ✅ PASS | `Dockerfile.debian:30` |
| Consistente con `plan.md` T4 | ✅ PASS | plan.md:121: "Tamaño final ~360MB (igual que el path A)" |
| Consistente con `docs/DOCKER-VANILLA.md` §3 | ✅ PASS | Tabla comparativa: "both ~360 MB" |

**Fix 2 verdict**: ✅ **PASS**

---

### Fix 3: `DOTNET_VERSION` ARG usado con `--version ${DOTNET_VERSION}`

| Criterio | Estado | Evidencia |
|----------|--------|-----------|
| Build stage (línea 55) usa `--version ${DOTNET_VERSION}` | ✅ PASS | `Dockerfile.debian:55`: `/tmp/dotnet-install.sh --version ${DOTNET_VERSION} --install-dir /usr/share/dotnet` |
| Ya NO usa `--channel 10.0` en build stage | ✅ PASS | `--channel` reemplazado por `--version` |
| Runtime stage (línea 122) usa `--version ${DOTNET_VERSION}` | ✅ PASS | `Dockerfile.debian:122`: `/tmp/dotnet-install.sh --version ${DOTNET_VERSION} --runtime aspnetcore --install-dir /usr/share/dotnet` |
| Ya NO usa `--channel 10.0` en runtime stage | ✅ PASS | `--channel` reemplazado por `--version` |
| ARG declarado correctamente en ambos stages | ✅ PASS | Línea 52: `ARG DOTNET_VERSION=10.0.108`, línea 119: `ARG DOTNET_VERSION=10.0.108` |
| `--build-arg DOTNET_VERSION=10.0.200` ahora tendría efecto | ✅ PASS | El ARG se expande vía shell expansion `${DOTNET_VERSION}` |

**Fix 3 verdict**: ✅ **PASS**

---

## Spec Compliance (Re-validated)

| ID | Requisito | Status | Evidencia |
|----|-----------|--------|-----------|
| **FR-1** | Build command (`docker build` vanilla) | ✅ PASS | `Dockerfile.debian:16-18` header muestra comando build. `docs/DOCKER-VANILLA.md` §2.1 y §3.1. |
| **FR-2** | Run command (SQLite + PostgreSQL) | ✅ PASS | `docs/DOCKER-VANILLA.md` §2.2, §2.3, §3.2. Ambos paths documentados y verificados funcionalmente (plan T6). |
| **FR-3** | PostgreSQL connection (3 opciones) | ✅ PASS | Opción A (IP directa), B (`host.docker.internal`), C (`--network host`) en `docs/DOCKER-VANILLA.md` §2.3 + `Dockerfile.debian` header. |
| **FR-4** | Error diagnosis (5-step process) | ✅ PASS | `docs/DOCKER-VANILLA.md` §5: 5 escenarios de troubleshooting: NuGet error, mcr bloqueado, port conflict, SQLitePCLRaw CVE, build context size. |
| **FR-5** | Variables de entorno documentadas | ✅ PASS | Tabla en spec §4 FR-5. `docs/DOCKER-VANILLA.md` §2.3 menciona `ENGRAM_DB_TYPE`, `ENGRAM_PG_CONNECTION`. `Dockerfile.debian:142-144` define defaults. |
| **NFR-1** | Compatibilidad Docker 20.10+ | ✅ PASS | `docs/DOCKER-VANILLA.md` §1: "Docker Engine 20.10+ | Tested on Docker 29.5 / 29.6". Ambos Dockerfiles usan `# syntax=docker/dockerfile:1`. |
| **NFR-2** | Documentación completa | ✅ PASS | 334 líneas. Cubre: prerrequisitos, Path A (mcr), Path B (debian), verification checklist, troubleshooting (5 escenarios), image layout, referencias cruzadas. |

**FR compliance: 5/5 ✅ | NFR compliance: 2/2 ✅**

---

## Code Quality (Re-validated)

### Dockerfile.debian (153 líneas)

| Aspecto | Status | Notas |
|---------|--------|-------|
| Multi-stage build | ✅ | `build` (debian:12-slim + SDK) → `runtime` (debian:12-slim + ASP.NET only) |
| No SDK in runtime | ✅ | Runtime stage usa `--runtime aspnetcore` (no SDK, no compiler) |
| Non-root user | ✅ | `USER engram` (línea 140). Creado con `--create-home --shell /bin/bash` |
| Healthcheck | ✅ | `curl -f http://localhost:7437/health` — 30s interval, 5s timeout, 10s start-period, 3 retries |
| Layer caching | ✅ | `.csproj` files copiados antes del source code. `dotnet restore` antes de `dotnet publish` |
| `DEBIAN_FRONTEND` | ✅ | `noninteractive` en ambos stages |
| apt-get cleanup | ✅ | `rm -rf /var/lib/apt/lists/*` en cada `apt-get install` |
| Telemetry opt-out | ✅ | `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1` |
| ARG ENGRAM_VERSION | ✅ | Default `0.0.0-dev` (SemVer 2.0). Shell expansion `${ENGRAM_VERSION#v}` |
| ARG DOTNET_VERSION | ✅ | **FIXED** — ahora usado en `dotnet-install.sh --version ${DOTNET_VERSION}` (líneas 55 y 122) |
| Header consistency | ✅ | **FIXED** — "~5 min cold build", "~360 MB virtual" consistente con plan.md y DOCKER-VANILLA.md §3 |

### Dockerfile (71 líneas)

| Aspecto | Status |
|---------|--------|
| ARG ENGRAM_VERSION=0.0.0-dev | ✅ (fix original del bug) |
| Bug history comment | ✅ Documenta el error `'dev' is not a valid version string` |
| Shell expansion `${ENGRAM_VERSION#v}` | ✅ Compatible con tags `v1.3.0` |

**Code quality verdict**: ✅ **CLEAN** — los 2 issues detectados en cycle 1 (V-002, V-003) están resueltos. 0 issues nuevos.

---

## 🔒 Security Audit (Re-validated)

### STRIDE Analysis (spec §6)

| Threat | Mitigation | Status |
|--------|------------|--------|
| **Spoofing** | `ENGRAM_PG_CONNECTION` no expuesto en logs/docker inspect. `.dockerignore` excluye `*.env` | ✅ |
| **Tampering** | `docker trust inspect` documentado (responsabilidad operacional) | ⚠️ No automatizado |
| **Information Disclosure** | Sin secrets en Dockerfiles. `.env` excluded del build context | ✅ |
| **Denial of Service** | Límites de recursos documentados en spec. `--memory=512m --cpus=1.0` | ✅ |
| **Elevation of Privilege** | `USER engram` en ambos Dockerfiles | ✅ |

### SAST Scan

| Área | Status |
|------|--------|
| Secrets en código | ✅ PASS — sin API keys, tokens, passwords en Dockerfiles |
| Shell injection | ✅ PASS — `${ENGRAM_VERSION#v}` y `${DOTNET_VERSION}` usan expansión sobre ARG de build (controlado por operador) |
| Dependency audit | ⚠️ KNOWN — `SQLitePCLRaw.lib.e_sqlite3 2.1.10` CVE documentado, no explotable en uso actual |
| Image provenance | ✅ Fuentes oficiales: `mcr.microsoft.com` (Path A), `dot.net` y `docker.io` (Path B) |
| Non-root runtime | ✅ Ambos Dockerfiles ejecutan como `engram` |

**Security verdict**: PASS con 1 advertencia conocida (SQLitePCLRaw CVE — tracked separately, no bloqueante).

---

## Backlog Verification

| Criterio | Estado | Evidencia |
|----------|--------|-----------|
| ENG-478 presente en `docs/BACKLOG.md` | ✅ PASS | Línea 979: `ENG-478 \| P1 \| Doc \| **Docker vanilla build**: diagnosticar error NuGet...` |
| Estado: ✅ Done | ✅ PASS | Columna Estado: `✅ Done` |
| Link a `.ai-work/docker-vanilla-build-diagnosis/` | ✅ PASS | `Ver \`.ai-work/docker-vanilla-build-diagnosis/\`` |
| Esfuerzo: M | ✅ PASS | Columna Effort: `M` |
| Tipo: Doc | ✅ PASS | Columna Tipo: `Doc` |
| Origen documentado | ✅ PASS | `← bug report usuario` |

> **Nota menor**: `plan.md:231` referencia "ENG-477" donde debería decir "ENG-478". Es un typo en el plan (ENG-477 es "Sync-on-demand"), pero el backlog tiene la entrada correcta (ENG-478). No afecta funcionalidad ni trazabilidad.

---

## Testing

| Test | Status | Evidencia |
|------|--------|-----------|
| Build Path A (`docker build -f Dockerfile`) | ✅ | plan.md T6: "35 steps, OK" |
| Build Path B (`docker build -f Dockerfile.debian`) | ✅ | plan.md T4: "Verificado end-to-end" |
| Run Path A (SQLite) | ✅ | plan.md T6: arranca en <1s, health 200 OK |
| Run Path B (SQLite) | ✅ | plan.md T6: puerto 7438, mismo resultado OK |
| Healthcheck | ✅ | Ambos paths: `{"status":"ok","service":"engram",...}` |
| Stats endpoint | ✅ | plan.md T6: JSON válido, backend=sqlite |
| Search endpoint | ✅ | plan.md T6: `[]` (no 500) |

> ⚠️ **Tests execution**: Los tests funcionales (Docker build/run/curl) fueron ejecutados por el Dev Agent y documentados en `plan.md` T4 y T6. El Verify Agent no tiene acceso a runtime Docker para re-ejecutar. La evidencia es auto-reportada pero detallada y consistente con los resultados esperados. No hay tests unitarios de .NET específicos para esta feature (solo Dockerfiles y docs). Se recomienda re-ejecución manual de `docker build` y `docker run` antes del `/flow-close`.

---

## Issues Found

### 🔴 Cycle 1 Issues — RESUELTOS

| ID | Descripción | Estado |
|----|-------------|--------|
| **V-001** | `context-map.md` ausente (CKP-0 violation) | ✅ **FIXED** — creado con 4 patrones documentados |
| **V-002** | `Dockerfile.debian:28-29`: "~300 MB larger" inconsistente | ✅ **FIXED** — corregido a "~5 min cold build, ~360 MB virtual" |
| **V-003** | `Dockerfile.debian:52,119`: `DOTNET_VERSION` ARG no usado | ✅ **FIXED** — ahora usa `--version ${DOTNET_VERSION}` |

### 🟢 Issues Nuevos

**Ninguno.** La re-auditoría no encontró issues nuevos. Los 3 fixes del rework ticket están correctamente aplicados.

---

## Pending Manual Tests

No hay sección `PM-*` en `spec.md`. Los criterios de éxito (§8) fueron verificados funcionalmente por el Dev Agent en plan.md T6.

**Recomendación pre-close**: Re-ejecutar manualmente:
```bash
# Path A
docker build -t engram-dotnet:latest -f Dockerfile . && \
docker run -d --name engram-test -p 7437:7437 engram-dotnet:latest && \
sleep 5 && curl -fsS http://localhost:7437/health && \
docker stop engram-test && docker rm engram-test

# Path B
docker build -t engram-dotnet:debian -f Dockerfile.debian . && \
docker run -d --name engram-deb -p 7438:7437 engram-dotnet:debian && \
sleep 5 && curl -fsS http://localhost:7438/health && \
docker stop engram-deb && docker rm engram-deb
```

---

## Verdict

```
█████  PASS  █████
```

**Motivo**: Los 3 fixes del `rework_ticket.md` cycle 1 están correctamente aplicados y verificados:

1. ✅ `context-map.md` creado con `## Reusable Patterns Found` y 4 patrones documentados
2. ✅ `Dockerfile.debian` header corregido: "~5 min cold build" + "~360 MB virtual" (consistente con plan.md y docs)
3. ✅ `DOTNET_VERSION` ARG ahora se usa activamente en `dotnet-install.sh --version ${DOTNET_VERSION}` (build y runtime stages)

Spec compliance: 5/5 FR + 2/2 NFR. Code quality: limpio (2 issues previos resueltos, 0 nuevos). Security: PASS con 1 advertencia conocida (SQLitePCLRaw CVE — tracked separately). Backlog: ENG-478 ✅ Done con link al directorio de la feature.

### Cycle info

- **Cycle count**: 2 (cycle 1 fue REWORK)
- **Max cycles**: 3
- **CKP-3 status**: ABIERTO — queda 1 ciclo más antes del emergency brake

---

## Recommendations

1. **Pre-close**:
   - Re-ejecutar `docker build` + `docker run` + `curl /health` para ambos paths (ver §Pending Manual Tests)
   - Corregir typo en `plan.md:231`: `ENG-477` → `ENG-478` (ENG-477 es "Sync-on-demand", no "Docker vanilla build")

2. **Post-close**:
   - Ejecutar `/flow-close` para completar la feature
   - Programar upgrade de `Microsoft.Data.Sqlite` para resolver el CVE de `SQLitePCLRaw.lib.e_sqlite3`
