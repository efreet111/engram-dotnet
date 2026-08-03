# Context Map — Docker Vanilla Build Diagnosis

## Fecha
2026-08-03

## Problema
Usuario reporta error de versionado de paquete NuGet durante build Docker en servidor con Docker 29.5 que no puede descargar imágenes de `mcr.microsoft.com`.

## Commit de referencia
b512dc0 (no encontrado en historial local, HEAD main: 863701b)

## Restricciones del entorno
- Docker 29.5 (compatible)
- Servidor sin acceso a `mcr.microsoft.com`
- Acceso a internet limitado (docker.io y dot.net disponibles)

## Reusable Patterns Found

### 1. Multi-stage Docker build with SemVer ARG
- **Pattern**: Usar `ARG VERSION=0.0.0-dev` (SemVer 2.0 válido) como default + shell expansion `${VERSION#v}` para compatibilidad con tags `v1.3.0`.
- **Files**: `Dockerfile:32-39`, `Dockerfile.debian:85-93`
- **Why**: NuGet rechaza strings no-SemVer como `dev`, `latest`, `main`. El default `0.0.0-dev` es válido y el shell expansion permite pasar `v1.3.0` o `1.3.0` indistintamente.

### 2. Debian-slim .NET fallback via dotnet-install.sh
- **Pattern**: Para entornos sin acceso a `mcr.microsoft.com`, usar `debian:12-slim` + `dotnet-install.sh --version 10.0.108` en build stage y `--runtime aspnetcore` en runtime stage.
- **Files**: `Dockerfile.debian:34-153`
- **Why**: `debian:12-slim` está disponible en docker.io (accesible universalmente). El install script oficial de .NET permite instalar SDK/runtime específicos sin depender de imágenes pre-layered de Microsoft.

### 3. NuGet-compatible default version strings in Docker ARGs
- **Pattern**: Nunca usar strings no-SemVer como defaults de `ARG *_VERSION` en Dockerfiles. Usar `0.0.0-dev` (pre-release tag) que es SemVer 2.0 compliant.
- **Files**: `Dockerfile:32`, `Dockerfile.debian:85`
- **Why**: `dotnet publish -p:Version=dev` falla con `error: 'dev' is not a valid version string`. El tag `-dev` (con guión) es válido en SemVer 2.0 como pre-release identifier.

### 4. .dockerignore exclusion of secrets
- **Pattern**: Excluir `*.env`, `*.env.local`, `docker/.env` del build context para prevenir leaks accidentales de connection strings.
- **Files**: `.dockerignore:44-48`
- **Why**: El build context se envía completo al Docker daemon. Si hay archivos `.env` con `ENGRAM_PG_CONNECTION`, podrían quedar en capas de imagen o logs.

## Decisiones arquitectónicas

### ADR-1: Usar Dockerfile.debian como alternativa oficial
- **Decisión**: Mantener dos Dockerfiles paralelos (Dockerfile + Dockerfile.debian)
- **Razón**: Servidores con restricciones de red necesitan alternativa sin `mcr.microsoft.com`
- **Trade-off**: Build ~2 min más lento, pero imagen final de tamaño similar (~360 MB virtual)

### ADR-2: Default version `0.0.0-dev` en lugar de `dev`
- **Decisión**: Cambiar default de `ARG ENGRAM_VERSION` de `dev` a `0.0.0-dev`
- **Razón**: NuGet requiere SemVer 2.0 estricto. `dev` no es válido, `0.0.0-dev` sí.
- **Impacto**: Breaking change para builds que dependían del default `dev`, pero esos builds ya fallaban.

## Dependencies mapeadas

### NuGet packages críticos
- `Microsoft.Data.Sqlite 9.0.*` → transitivamente trae `SQLitePCLRaw.lib.e_sqlite3 2.1.10` (warning NU1903, vulnerabilidad conocida no explotable en nuestro uso)
- `ModelContextProtocol 1.3.0` (pineado, no usar `*-*`)

### Imágenes Docker base
- `mcr.microsoft.com/dotnet/sdk:10.0` (build stage, Path A)
- `mcr.microsoft.com/dotnet/aspnet:10.0` (runtime stage, Path A)
- `debian:12-slim` (build + runtime, Path B)

### External services
- PostgreSQL (opcional, externo al contenedor)
- api.nuget.org (requerido para `dotnet restore`)
- dot.net (requerido para `dotnet-install.sh` en Path B)

## Risks identificados

| Riesgo | Probabilidad | Impacto | Mitigación |
|--------|--------------|---------|------------|
| .NET 10 SDK es preview | Media | Medio | Usar `rollForward: latestFeature` en global.json |
| Vulnerabilidad SQLitePCLRaw | Alta | Bajo | No explotable en nuestro uso, tracked separately |
| mcr.microsoft.com bloqueado | Alta | Alto | Dockerfile.debian como alternativa |

## Outputs generados

- `spec.md` — Especificación completa con FR/NFR y STRIDE
- `plan.md` — Plan de ejecución con 6 tareas
- `verify-report.md` — Auditoría de implementación
- `rework_ticket.md` — 3 fixes menores (cycle 1/3)
- `docs/DOCKER-VANILLA.md` — Guía completa (334 líneas)
- `Dockerfile.debian` — Dockerfile alternativo para servidores restringidos
