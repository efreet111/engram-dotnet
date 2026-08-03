---
cycle_count: 1
max_cycles: 3
status: "closed"
severity: P2
resolved_by: "verify-report.md cycle 2 — all 3 fixes verified PASS"
---
# Rework ticket — docker-vanilla-build-diagnosis

## 1. Failure Reason

**CKP-0 Violation — `context-map.md` ausente.** El SKILL `forge-verify` exige que `.ai-work/{feature-slug}/context-map.md` exista y contenga la sección `## Reusable Patterns Found` (con entradas o una línea de resultado negativo). La Phase 0 (Discovery) no produjo este artefacto. El verify agent lo trata como fallo mecánico inmediato sin excepciones.

**Issues adicionales detectados en la misma auditoría** (aprovechar el mismo ciclo):

- **V-002**: `Dockerfile.debian:28-29` dice "~300 MB larger and ~30s slower build". `plan.md` T4 y `DOCKER-VANILLA.md` §3 dicen que los tamaños son iguales (~360MB ambos). Corregir el header.

- **V-003**: `Dockerfile.debian:52` y `:119`: `ARG DOTNET_VERSION=10.0.108` declarado pero nunca usado. `dotnet-install.sh` recibe `--channel 10.0`, no `--version ${DOTNET_VERSION}`. Si alguien pasa `--build-arg DOTNET_VERSION=10.0.200`, el ARG se ignora silenciosamente.

## 2. Affected Files

- `.ai-work/docker-vanilla-build-diagnosis/context-map.md` — **crear** (no existe)
- `Dockerfile.debian` — líneas 28-29 (header comment) y líneas 52, 119 (ARG DOTNET_VERSION)

## 3. Correction Instruction

### Fix 1: Crear `context-map.md`

Crear `.ai-work/docker-vanilla-build-diagnosis/context-map.md` con al menos:

```markdown
# Context Map — Docker Vanilla Build Diagnosis

## Reusable Patterns Found

### 1. Multi-stage Docker build with SemVer ARG
- **Pattern**: Usar `ARG VERSION=0.0.0-dev` (SemVer 2.0 válido) como default + shell expansion `${VERSION#v}` para compatibilidad con tags `v1.3.0`.
- **Files**: `Dockerfile:32-39`, `Dockerfile.debian:85-93`

### 2. Debian-slim .NET fallback via dotnet-install.sh
- **Pattern**: Para entornos sin acceso a `mcr.microsoft.com`, usar `debian:12-slim` + `dotnet-install.sh --channel 10.0 --runtime aspnetcore` en runtime stage (solo shared framework, sin SDK).
- **Files**: `Dockerfile.debian:34-153`

### 3. NuGet-compatible default version strings in Docker ARGs
- **Pattern**: Nunca usar strings no-SemVer como defaults de `ARG *_VERSION` en Dockerfiles. NuGet rechaza `dev`, `latest`, `main`, etc. Usar `0.0.0-dev` (pre-release tag) que es SemVer 2.0 compliant.
- **Files**: `Dockerfile:32`

### 4. .dockerignore exclusion of secrets
- **Pattern**: Excluir `*.env`, `*.env.local`, `docker/.env` del build context para prevenir leaks accidentales de connection strings.
- **Files**: `.dockerignore:44-48`
```

### Fix 2: Corregir Dockerfile.debian header

En `Dockerfile.debian`, reemplazar líneas 28-29:

```
#   - Trade-off vs the main Dockerfile: ~300 MB larger and ~30s slower build
#     because we install the SDK from scratch instead of pulling the
#     pre-layered microsoft image.
```

Por:

```
#   - Trade-off vs the main Dockerfile: ~5 min cold build (vs ~3 min with mcr)
#     because we install the SDK from scratch instead of pulling the
#     pre-layered Microsoft image. Final image size is similar (~360 MB virtual).
```

### Fix 3: Usar o eliminar ARG DOTNET_VERSION

**Opción A (recomendada)**: Usar el ARG en el install script para que sea funcional:

```dockerfile
# Línea 52-56 (build stage):
ARG DOTNET_VERSION=10.0.108
RUN wget -qO /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --version ${DOTNET_VERSION} --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh \
    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet
```

```dockerfile
# Línea 119-124 (runtime stage):
ARG DOTNET_VERSION=10.0.108
RUN wget -qO /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --version ${DOTNET_VERSION} --runtime aspnetcore --install-dir /usr/share/dotnet \
    && rm /tmp/dotnet-install.sh \
    && ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet
```

**Opción B**: Si se prefiere `--channel` por `rollForward`, eliminar el ARG y poner el valor directamente en el comentario.

## 4. Close Criteria

- [ ] `context-map.md` creado con `## Reusable Patterns Found` y al menos 1 patrón documentado (con entry o negative-result line)
- [ ] `Dockerfile.debian` línea 28-29 corregida (tamaño consistente con plan.md)
- [ ] `Dockerfile.debian` líneas 52 y 119: `DOTNET_VERSION` ARG usado en `dotnet-install.sh` o eliminado
- [ ] `verify-report.md` re-generado por forge-verify después de los fixes
- [ ] Veredicto final `PASS` o `PASS_DEGRADADO` obtenido
