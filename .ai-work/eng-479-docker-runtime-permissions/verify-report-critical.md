# Verify Report: ENG-479 — Auditoría Crítica (Segundo Pase)

**Fecha**: 2026-08-03
**Agente**: forge-verify (segundo pase de auditoría)
**Veredicto**: `PASS_DEGRADADO`

---

## Resumen

Se ejecutó una auditoría crítica y adversarial sobre la implementación de ENG-479. Se encontraron **2 issues de severidad MEDIUM-HIGH y 5 issues de severidad MEDIUM** que deben ser documentados o corregidos. No hay issues CRITICAL que bloqueen el merge, pero los issues HIGH deben ser considerados seriamente antes del deploy en producción.

---

## 1. Auditoría de Seguridad Profunda

### 1.1 entrypoint.sh — Análisis línea por línea

```bash
#!/bin/bash                                    # ✅ Shebang correcto
set -e                                         # ⚠️ HIGH: falta set -u y set -o pipefail
                                               #    Sin set -u: variables indefinidas silenciosamente vacías
                                               #    Sin pipefail: errores en pipelines pueden perderse
                                               #    Patrón oficial: set -eEuo pipefail (PostgreSQL, Redis)
if [ -d "/data/engram" ]; then                  # ✅ Verifica existencia del directorio
    chown -R engram:engram /data/engram \       # ⚠️ HIGH: ejecución incondicional en cada arranque
        2>/dev/null || true                     #    El spec (NFR-3) pedía verificar antes de chown
                                               #    ⚠️ MEDIUM: 2>/dev/null oculta errores totalmente
                                               #    - sin logs, imposible diagnosticar fallos de permisos
fi
exec gosu engram "$@"                          # ✅ exec preserva señales, reemplaza PID 1
                                               # ⚠️ MEDIUM: si gosu falla, set -e mata el script sin mensaje
```

**Análisis de mitigación de `2>/dev/null || true`**:

| Escenario | Comportamiento | Riesgo |
|-----------|---------------|--------|
| Volumen root-owned | `chown` exitoso, app arranca bien | ✅ Correcto |
| Volumen ya correcto | `chown` ejecuta innecesariamente (no-op con `|| true`) | ⚠️ Perf regression |
| Volumen read-only (NFS) | `chown` falla, error suprimido, app usa permisos existentes | ✅ App arranca si permisos correctos |
| Volumen read-only + permisos incorrectos | `chown` falla, error suprimido, app falla con SQLite Error 14 | 🔴 Falla silenciosa — logs vacíos |
| `/data/engram` tiene 1M+ archivos | `chown -R` bloquea startup por segundos/minutos | 🔴 DoS autoinfligido |

**Preguntas críticas respondidas**:

| Pregunta | Respuesta |
|----------|-----------|
| ¿`set -e` es suficiente? | **No.** Debería ser `set -eEuo pipefail`. `set -u` evita variables indefinidas. `-o pipefail` evita que errores en pipeline se oculten. |
| ¿`chown -R` puede causar DoS? | **Sí.** En volúmenes grandes con muchos archivos, `chown -R` puede tomar minutos. No hay timeout ni limitación. |
| ¿`2>/dev/null \|\| true` oculta errores importantes? | **Sí.** Si `chown` falla por filesystem read-only, no hay indicación alguna en logs. |
| ¿`exec gosu` maneja señales? | **Sí.** `exec` reemplaza PID 1, señales (SIGTERM, SIGINT) llegan a `gosu` → `engram`. |
| ¿Qué pasa si `"$@"` está vacío? | `gosu engram` ejecutaría sin comando, saldría con 0. Bajo riesgo: Dockerfile siempre provee `CMD`. |

### 1.2 Análisis STRIDE (segundo pase)

| Threat | Análisis | Veredicto |
|--------|----------|-----------|
| **Spoofing** | `gosu` usa UID/GID reales del sistema. El proceso `engram` es real, no simulado. | ✅ PASS |
| **Tampering** | `entrypoint.sh` es parte de la imagen y no modificable en runtime. `COPY` en Dockerfile → `RUN chmod +x` en build. | ✅ PASS |
| **Repudiation** | No aplica. El entrypoint es infraestructura, no lógica de negocio. | ✅ N/A |
| **Information Disclosure** | `2>/dev/null` suprime stderr de `chown`. Variables de entorno no se loggean. **Pero**: si `$@` contiene secrets (bad practice del usuario), pasarían a `gosu` y al proceso hijo. Esto es inherente a cualquier entrypoint. | ✅ PASS |
| **Denial of Service** | `chown -R` sin límite en volumen grande puede bloquear startup. `2>/dev/null \|\| true` no detiene el script pero `chown` bloquea hasta terminar. No hay timeout. | ⚠️ HIGH |
| **Elevation of Privilege** | Entrypoint corre como root ~1ms–Nseg para `chown`, luego `exec gosu` reemplaza el proceso. El proceso de aplicación nunca tiene root. | ✅ PASS |

### 1.3 Dependency audit

```
NU1903 HIGH: SQLitePCLRaw.lib.e_sqlite3 2.1.10 → CVE preexistente, documentado en §5.4
```

**Veredicto**: No introducido por ENG-479. No blocking.

### 1.4 Secret scanning

| Check | Resultado |
|-------|-----------|
| API keys / tokens en código fuente | ✅ Ninguno |
| Connection strings con credenciales hardcodeadas | ✅ Placeholders (`Password=secret`, `Password=REPLACE_ME`) |
| Private keys | ✅ Ninguno |
| `.env` committed | ✅ `.env` en `.gitignore` |
| Credenciales en documentación | ✅ Placeholders (`secret`, `REPLACE_ME`, `secret123`) |

---

## 2. Auditoría de Dockerfile

### 2.1 Dockerfile (principal)

| # | Instrucción | Análisis |
|---|-------------|----------|
| 47 | `apt-get update && apt-get install curl` | ⚠️ Primer `apt-get update` del runtime stage |
| 51 | `useradd engram` | ✅ Crea usuario no-root |
| 52-53 | `mkdir /data/engram && chown`, `mkdir /app/docs && chown` | ✅ Directorios creados y owned por engram en build time |
| 55-56 | `COPY --from=build`, `chmod +x` | ✅ Binario copiado |
| 60-62 | `apt-get update && apt-get install gosu` | ⚠️ **MEDIUM**: Segundo `apt-get update` en runtime. Cache inconsistency si capa curl está cached pero gosu rebuild. Mejor práctica: un solo RUN de apt. |
| 62 | `gosu nobody true` | ✅ Validación correcta de gosu |
| 65-66 | `COPY entrypoint.sh`, `chmod +x` | ✅ Corregido en build (no depende de git file mode) |
| 81-82 | `ENTRYPOINT`, `CMD` | ✅ Configuración correcta |

**Problema de cache**: El `gosu` install (línea 60) está DESPUÉS de `COPY --from=build /app/publish .` (línea 55). Cambiar el código fuente invalida la cache de `COPY --from=build`, lo que obliga a re-ejecutar `apt-get install gosu` aunque no haya cambiado. Debería estar antes del COPY.

### 2.2 Dockerfile.debian

Mismos problemas que el principal, más:

| # | Issue | Severidad |
|---|-------|-----------|
| Build stage | `apt-get update && apt-get install` dependencias SDK | MEDIUM |
| Runtime stage líneas 104-113 | `apt-get update && apt-get install` dependencias runtime | MEDIUM |
| Runtime stage líneas 144-146 | `apt-get update && apt-get install gosu` | MEDIUM |
| **Total** | **3 `apt-get update` calls en runtime stage** | ⚠️ Dos RUNs separados de apt en runtime |

### 2.3 docker/docker-compose.test.yml

| Aspecto | Análisis |
|---------|----------|
| `server` service | Sin `command:` override → usa CMD por defecto. Sin `volumes:` → entrypoint hace chown en directorio build-time ya correcto (no-op benévolo). |
| `client-a`, `client-b` | `command: ["./engram", "serve"]` actualizado correctamente. |
| Test de permisos real | **No probado**: compose no monta volúmenes root-owned. El chown del entrypoint es no-op. |

### 2.4 docker/Dockerfile (legacy)

| Aspecto | Análisis |
|---------|----------|
| `USER engram` (línea 38) | **MEDIUM**: Sin entrypoint fix. Usuarios de este image con bind mounts root-owned seguirán viendo SQLite Error 14. Out-of-scope para ENG-479, pero documentado aquí. |

---

## 3. Auditoría de Documentación (`docs/DOCKER-VANILLA.md`)

### 3.1 Issues encontrados

| # | Severidad | Descripción | Ubicación |
|---|-----------|-------------|-----------|
| DOC-01 | MEDIUM | "Docker mounts volumes as `root:root`" — impreciso. Bind mounts preservan ownership del host. Named volumes creados por Docker sí son root-owned. | §8 |
| DOC-02 | MEDIUM | "UID 1000 is typically 'engram'" — no garantizado. Si `useradd` asigna otro UID (ya existe UID 1000 en la imagen), el manual fallará. Documentar cómo verificar UID real: `docker run --rm --entrypoint id engram-dotnet:latest engram` | §8 Manual fix |
| DOC-03 | MEDIUM | `sleep 5` en verification checklist (§4) — frágil en entornos lentos. Mejor: loop con `until curl -fsS http://localhost:7437/health; do sleep 2; done` | §4 |
| DOC-04 | LOW | Solución C ("--user flag") del spec fue eliminada de la documentación. El spec la listaba como opción, pero la documentación no la menciona. No es un requisito funcional, el spec la marcaba como "Solution C". | §8, spec §6.4 FR-3 |

### 3.2 Verificaciones positivas

| Check | Resultado |
|-------|-----------|
| Sección 8: Volume permissions | ✅ Explica causa raíz, solución automática, "How it works", solución manual |
| Sección 9: Tabla de variables | ✅ 10 variables documentadas con default, descripción, ejemplo |
| Ejemplos de uso | ✅ Local mode, Team mode, Custom port con comandos completos |
| Sección 10: PostgreSQL connection guide | ✅ 3 escenarios (misma máquina, otro container, remoto), errores comunes, env file |
| Links internos | ✅ Referencias a API-REFERENCE.md, DOCKER.md, MANUAL-TESTING-CHECKLIST.md |
| Comandos de ejemplo | ✅ Sintaxis correcta, ejecutables copy-paste |
| Placeholders de contraseñas | ✅ `REPLACE_ME`, `secret`, `secret123` — sin credenciales reales |

---

## 4. Auditoría de Tests

### 4.1 Resultado de ejecución

```text
Engram.Verification.Tests (51 tests): 51 passed, 0 failed, 0 skipped
```

Los 7 tests de ENG-479 (`DockerRuntimePermissionsTests`) pasan todos.

### 4.2 Análisis de cobertura de casos

| Caso | Test que lo cubre | Cobertura |
|------|-------------------|-----------|
| entrypoint tiene permisos 755 | `Entrypoint_IsExecutableOnUnix` | ✅ |
| entrypoint contiene chown | `Entrypoint_RepairsDataOwnershipAndDropsPrivileges` | ✅ |
| entrypoint contiene gosu | `Entrypoint_RepairsDataOwnershipAndDropsPrivileges` | ✅ |
| Dockerfile tiene gosu | `Dockerfile_UsesSharedRootEntrypoint` | ✅ |
| Dockerfile NO tiene USER engram | `Dockerfile_UsesSharedRootEntrypoint` (DoesNotMatch) | ✅ |
| Dockerfile tiene ENTRYPOINT correcto | `Dockerfile_UsesSharedRootEntrypoint` | ✅ |
| Dockerfile tiene CMD correcto | `Dockerfile_UsesSharedRootEntrypoint` | ✅ |
| Debian Dockerfile sin USER engram | `Dockerfile_UsesSharedRootEntrypoint` | ✅ |
| Debian runtime version ≠ SDK version | `DebianDockerfile_UsesMatchingAspNetRuntimeVersion` | ✅ |
| docker-compose.yml monta /data/engram | `DockerCompose_StillMountsTheApplicationDataDirectory` | ✅ |
| docker-compose.test.yml comando actualizado | `DockerCompose_StillMountsTheApplicationDataDirectory` | ✅ |
| Guía documenta permisos + variables | `DockerVanillaGuide_DocumentsPermissionsVariablesAndExamples` | ✅ |

### 4.3 Gaps en cobertura

| Gap | Severidad | Descripción |
|-----|-----------|-------------|
| **Volumen root-owned no probado** | MEDIUM | Ningún test (ni unit ni integration) verifica que el entrypoint arregla permisos en un directorio root-owned recién montado. Los tests son puramente de contrato (strings en archivos). |
| **gosu failure no probado** | LOW | No hay test que verifique qué pasa si `gosu` falla (e.g., usuario engram no existe). |
| **chown en gran volumen no probado** | LOW | No hay test de performance con volumen grande. |
| **entrypoint.bash syntax corner cases** | LOW | No se prueba comportamiento con `$@` vacío o caracteres especiales en argumentos. |

---

## 5. Auditoría de Compatibilidad

| Artefacto | Status | Detalle |
|-----------|--------|---------|
| `docker/docker-compose.yml` (prod) | ✅ Compatible | Sin cambios. No especifica command → usa default `CMD ["./engram", "serve"]` → pasa por entrypoint. |
| `docker/docker-compose.test.yml` | ✅ Compatible | `command` actualizado para client-a/client-b. Server no requiere cambio. |
| `scripts/` | ✅ Compatible | Ningún script referencia el viejo `ENTRYPOINT ["./engram"]` o `USER engram`. |
| `docker/Dockerfile` (legacy) | ⚠️ No actualizado | Fuera de scope. Sigue con `USER engram`. El bug persiste en este image. |
| Docker 20.10+ | ✅ Compatible | `gosu` funciona desde Docker 1.x. `exec` y señales funcionan igual. |
| macOS (Docker Desktop) | ✅ Compatible | Sin cambios en paths ni comandos. `gosu` soportado. |
| Windows (Docker Desktop) | ✅ Compatible | Entrypoint script funciona en WSL2 backend. |
| Linux (Docker Engine) | ✅ Compatible | Plataforma principal. Testeado con Docker 29.5/29.6. |

### Breaking Changes

| Cambio | Impacto | Migración |
|--------|---------|-----------|
| `ENTRYPOINT` cambió de `["./engram"]` a `["/usr/local/bin/entrypoint.sh"]` | Quienes usaban `--entrypoint` para overridear el binario deberán usar `--entrypoint /usr/local/bin/entrypoint.sh` o `--entrypoint ""`. | Documentar en release notes. |
| `CMD` cambió de `["serve"]` a `["./engram", "serve"]` | Quienes overrideaban `command:` en compose con `["serve"]` deben cambiarlo a `["./engram", "serve"]`. | Ya corregido en `docker-compose.test.yml`. |
| `USER engram` eliminado | Nadie debería depender de `USER` en el Dockerfile (es interno). | Sin impacto práctico. |

---

## 6. Lista Completa de Issues Encontrados

| # | Severidad | Descripción | Archivo | Línea | Recomendación |
|---|-----------|-------------|---------|-------|---------------|
| **I-01** | **MEDIUM-HIGH** | `entrypoint.sh` usa solo `set -e` sin `set -u` ni `set -o pipefail`. Patrón de industria (`set -eEuo pipefail`) usado por imágenes oficiales de Docker. | `entrypoint.sh` | 2 | Cambiar a `set -eEuo pipefail` |
| **I-02** | **MEDIUM-HIGH** | `chown -R` se ejecuta **incondicionalmente** en cada arranque del contenedor. El spec (NFR-3, §6) especificaba: "`chown` solo si es necesario (verificar permisos antes)". En volúmenes grandes (GBs, 1M+ archivos) esto puede causar latencia de startup de segundos o minutos. | `entrypoint.sh` | 11-13 | Agregar back `[ ! -w "/data/engram" ]` con fallback seguro, o usar `find /data/engram -maxdepth 1 -not -user engram | head -1` para verificar antes del chown. Documentar la limitación de `-w` en NFS/WSL. |
| **I-03** | **MEDIUM** | `2>/dev/null` en `chown` suprime TODOS los errores, incluso fallos que deberían conocerse. Si el volumen es read-only con permisos incorrectos, el chown falla silenciosamente y la app arranca sin saber por qué falla el SQLite. | `entrypoint.sh` | 12 | Redirigir stderr a un log o a `/proc/1/fd/2` con un prefijo identificable: `chown ... 2>/dev/stderr || echo "[entrypoint] chown failed — check volume permissions" >&2 || true` |
| **I-04** | **MEDIUM** | Dos llamadas `apt-get update` separadas en el runtime stage del `Dockerfile`. Si la capa curl está cacheada pero la capa gosu no, los índices de apt pueden ser inconsistentes. Cada `apt-get update` ~5-15s de build time extra. | `Dockerfile` | 47, 60 | Combinar en un solo RUN: `apt-get update && apt-get install -y curl gosu && rm -rf /var/lib/apt/lists/*`. Mover ANTES del `COPY --from=build`. |
| **I-05** | **MEDIUM** | `Dockerfile.debian` tiene 3 `apt-get update` separados (build deps + runtime deps + gosu). | `Dockerfile.debian` | 39, 104, 144 | Combinar runtime deps y gosu en un solo RUN. |
| **I-06** | **MEDIUM** | `gosu` install está después de `COPY --from=build /app/publish .` en ambos Dockerfiles. Cambios en código fuente invalidan innecesariamente la cache de instalación de `gosu`. | `Dockerfile` | 55, 60 | Mover `apt-get install gosu` ANTES de `COPY --from=build`. |
| **I-07** | **MEDIUM** | `docker-compose.test.yml` no prueba el escenario real de permisos (volumen root-owned). Los tests de contrato pasan pero no validan que el fix funcione en runtime. | `docker/docker-compose.test.yml` | — | Agregar servicio de test con bind mount a directorio root-owned: `docker compose -f test.yml run --rm test-volume-perms`. O documentar que requiere PM-* manual. |
| **I-08** | **MEDIUM** | ENG-479 no aparece en `docs/BACKLOG.md`. El `context-map.md` indicaba que sería agregado. | `docs/BACKLOG.md` | — | Agregar entrada ENG-479 en BACKLOG.md con estado "Done". |
| **I-09** | **MEDIUM** | Documentación dice "Docker mounts volumes as `root:root`" — impreciso para bind mounts donde ownership del host se preserva. | `docs/DOCKER-VANILLA.md` | §8 | Corregir a: "Docker named volumes are created as `root:root`. Bind mounts preserve the host's ownership. If your host directory is root-owned, the container's `engram` user cannot write to it." |
| **I-10** | **MEDIUM** | Documentación dice "UID 1000 is typically 'engram' in the container" sin indicar cómo verificarlo. | `docs/DOCKER-VANILLA.md` | §8 Manual fix | Agregar: "To find the actual UID, run: `docker run --rm --entrypoint id engram-dotnet:latest engram`" |
| **I-11** | **LOW** | Falta `set -E` para heredar traps en funciones (aunque no hay funciones actualmente). | `entrypoint.sh` | 2 | Agregar `set -E` si se añaden funciones en el futuro. |
| **I-12** | **LOW** | `sleep 5` en §4 (Verification checklist) es frágil. Un contenedor puede tardar más en arrancar. | `docs/DOCKER-VANILLA.md` | §4 | Sugerir: `until curl -fsS http://localhost:7437/health; do sleep 2; done` |
| **I-13** | **LOW** | Legacy `docker/Dockerfile` no tiene el fix de permisos. Usuarios de release binaries con bind mounts seguirán viendo SQLite Error 14. Fuera de scope pero no documentado como limitación. | `docker/Dockerfile` | — | Agregar nota en README o en el mismo Dockerfile: "For volume permissions, use the main Dockerfile or the Debian variant." |
| **I-14** | **LOW** | `dotnet-install.sh` descargado sin verificación GPG en `Dockerfile.debian`. | `Dockerfile.debian` | 53, 122 | Documentar como aceptable (HTTPS + canal oficial). Agregar `--check` si dotnet-install.sh lo soporta. |
| **I-15** | **LOW** | `entrypoint.sh` no maneja el caso de `gosu engram "$@"` con `$@` vacío (aunque improbable porque CMD está definido). | `entrypoint.sh` | 16 | Agregar: `[ $# -eq 0 ] && exec gosu engram /bin/bash` como fallback o simplemente documentar que CMD siempre debe definirse. |

---

## 7. Traceability Matrix

| Requirement | Type | File(s) | Test | Status |
|-------------|------|---------|------|--------|
| FR-1: Entrypoint script | FR | `entrypoint.sh` | `Entrypoint_RepairsDataOwnershipAndDropsPrivileges`, `Entrypoint_IsExecutableOnUnix` | ✅ PASS |
| FR-2: Dockerfile modified | FR | `Dockerfile`, `Dockerfile.debian` | `Dockerfile_UsesSharedRootEntrypoint` (×2), `DebianDockerfile_UsesMatchingAspNetRuntimeVersion` | ✅ PASS |
| FR-3: Volume permissions docs | FR | `docs/DOCKER-VANILLA.md` §8 | `DockerVanillaGuide_DocumentsPermissionsVariablesAndExamples` | ✅ PASS (con observaciones DOC-01, DOC-02) |
| FR-4: Environment variables docs | FR | `docs/DOCKER-VANILLA.md` §9 | `DockerVanillaGuide_DocumentsPermissionsVariablesAndExamples` | ✅ PASS |
| FR-5: Usage examples | FR | `docs/DOCKER-VANILLA.md` §9 examples, §10 | `DockerVanillaGuide_DocumentsPermissionsVariablesAndExamples` | ✅ PASS |
| NFR-1: Compatibility | NFR | `docker/docker-compose.yml`, `docker/docker-compose.test.yml` | `DockerCompose_StillMountsTheApplicationDataDirectory` | ✅ PASS |
| NFR-2: Security | NFR | `entrypoint.sh` (gosu), `Dockerfile` (no USER) | `Dockerfile_UsesSharedRootEntrypoint` (no USER check, gosu check) | ✅ PASS |
| NFR-3: Performance (<1s overhead) | NFR | `entrypoint.sh` | ⚠️ DEGRADED | ⚠️ SPEC DEVIATION: El spec pedía "chown solo si es necesario". La implementación ejecuta chown incondicionalmente. En volúmenes grandes puede exceder 1s. Ver I-02. |

---

## 8. Veredicto Final

## ⚠️ PASS_DEGRADADO

### Justificación

| Criterio | Evaluación |
|----------|------------|
| **Functional Requirements (FR-1 → FR-5)** | ✅ Todos satisfechos |
| **Non-Functional: Compatibility (NFR-1)** | ✅ Docker 20.10+, Linux/macOS/Windows, compose files intactos |
| **Non-Functional: Security (NFR-2)** | ✅ No secrets, no-root runtime, STRIDE mitigado |
| **Non-Functional: Performance (NFR-3)** | ⚠️ Spec deviation: `chown` se ejecuta incondicionalmente |
| **Tests: ENG-479 contract tests** | ✅ 7/7 pass |
| **Tests: Suite general** | ✅ 51/51 Verification.Tests pass. Suite SQLite limpia. |
| **Security: OWASP / secrets** | ✅ Sin vulnerabilidades introducidas |
| **Documentation** | ✅ Guía completa con 3 secciones nuevas, ejemplos funcionales |
| **Breaking changes** | ✅ Documentados en esta auditoría |
| **Especificación vs implementación** | ⚠️ NFR-3 spec deviation en `chown` incondicional |

### Razón del DEGRADADO

Hay **2 issues MEDIUM-HIGH** (I-01: `set -u`/`pipefail` faltante, I-02: `chown -R` incondicional) que constituyen desviaciones de mejores prácticas y del spec. El NFR-3 pedía explícitamente "`chown` solo si es necesario (verificar permisos antes)" y la implementación lo ejecuta siempre. Si bien esto fue justificado por problemas con `[ ! -w ]` en NFS/WSL, la solución actual:

1. Ejecuta chown innecesariamente en cada reinicio de contenedor
2. Puede causar latencia de startup >1s en volúmenes grandes (violando NFR-3)
3. No provee visibilidad de errores (I-03)

**Estos issues NO bloquean el merge** (la funcionalidad principal funciona y los tests pasan), pero **deben ser trackeados** para una iteración de mejora antes del próximo release.

### Problemas preexistentes (no blocking)

1. **NU1903 HIGH**: `SQLitePCLRaw.lib.e_sqlite3 2.1.10` — CVE preexistente, documentado en §5.4
2. **ENG-475 assertion**: `Expected: 500, Actual: 201` — preexistente, no relacionado
3. **`version` attribute obsolete**: `docker-compose.yml` usa `version: "3.8"` — preexistente

---

## 9. Recomendaciones

### Para este PR (antes de merge)

| Prioridad | Acción | Issue |
|-----------|--------|-------|
| **Recomendado** | Agregar `set -u` y `set -o pipefail` en `entrypoint.sh` | I-01 |
| **Recomendado** | Documentar el trade-off de `chown` incondicional en el plan.md | I-02 |
| **Opcional** | Combinar `apt-get` calls en un solo RUN en ambos Dockerfiles | I-04, I-05, I-06 |
| **Opcional** | Agregar stderr logging a `chown` fallido | I-03 |

### Para la próxima iteración (post-merge)

| Prioridad | Acción | Issue |
|-----------|--------|-------|
| **MEDIUM** | Agregar ENG-479 a BACKLOG.md | I-08 |
| **MEDIUM** | Implementar verificación previa de permisos con fallback robusto para NFS/WSL | I-02 |
| **MEDIUM** | Corregir imprecisiones en documentación (bind mounts, UID 1000) | I-09, I-10 |
| **LOW** | Agregar test de integración con volumen root-owned | I-07 |
| **LOW** | Documentar limitación del legacy `docker/Dockerfile` | I-13 |

---

## 10. Pending Manual Tests

> El desarrollador debe ejecutar los PM-* del spec.md antes de `/flow-close`.

### 🔍 Manual Verification Steps

1. **Smoke test con volumen root-owned**:
   ```bash
   mkdir -p /tmp/engram-test-data
   sudo chown root:root /tmp/engram-test-data
   docker run -d --name engram-verify --rm \
     -p 17437:7437 \
     -v /tmp/engram-test-data:/data/engram \
     engram-dotnet:latest
   sleep 5
   curl -fsS http://localhost:17437/health
   # Debe retornar 200 OK
   docker logs engram-verify
   # No debe mostrar "SQLite Error 14"
   docker rm -f engram-verify
   ```

2. **Verificar que el proceso corre como `engram`**:
   ```bash
   docker run -d --name engram-verify --rm -p 17437:7437 engram-dotnet:latest
   sleep 5
   docker exec engram-verify whoami
   # Debe retornar: engram
   docker exec engram-verify ps aux
   # El proceso ./engram debe ser PID 1, user=engram
   docker rm -f engram-verify
   ```

3. **Debian image smoke test**:
   ```bash
   docker build -f Dockerfile.debian --build-arg ENGRAM_VERSION=1.3.0 -t engram-dotnet:debian-verify .
   docker run -d --name engram-debian-verify --rm \
     -p 17437:7437 \
     -v /tmp/engram-test-data:/data/engram \
     engram-dotnet:debian-verify
   sleep 5
   curl -fsS http://localhost:17437/health
   docker rm -f engram-debian-verify
   ```

4. **docker-compose.yml con el nuevo entrypoint**:
   ```bash
   cd docker
   docker compose up -d --build
   sleep 10
   curl -fsS http://localhost:7437/health
   docker compose down -v
   ```

5. **NUEVO: Verificar que `chown` no causa latencia en reinicio**:
   ```bash
   # Crear volumen con datos reales (si existe)
   docker run -d --name engram-perf --rm \
     -p 17437:7437 \
     -v /path/to/existing/data:/data/engram \
     engram-dotnet:latest
   time docker logs engram-perf 2>&1 | head -20
   # El tiempo hasta "Now listening on" debe ser razonable
   docker rm -f engram-perf
   ```

---

## CKP-3 Status

| Campo | Valor |
|-------|-------|
| `cycle_count` | 0 (primer ciclo de verificación con auditoría crítica) |
| `max_cycles` | 3 |
| Veredicto | PASS_DEGRADADO |
| ¿Bloquea merge? | No — los issues MEDIUM-HIGH pueden trackearse para siguiente iteración |
| ¿Requiere rework_ticket.md? | No — PASS_DEGRADADO no genera rework |
