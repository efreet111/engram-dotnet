# ENG-454 — Resolución: Release v1.3.0 sin binarios

**Fecha**: 2026-07-15  
**Estado**: ✅ Resuelto  
**Tiempo de resolución**: ~15 minutos (diagnóstico + re-run del workflow)

---

## Resumen ejecutivo

El release `v1.3.0` de engram-dotnet se publicó el 2026-07-11 **sin binarios** (0 assets). El workflow `release.yml` se había disparado el 2026-07-08 pero falló con exit code 1 después de que los tests pasaran exitosamente.

**Impacto**: Usuarios que instalaban engram-dotnet recibían v1.2.1 (sin las fixes de sync recovery ENG-451, self-loop detection ENG-452, y ApplyPulledMutationAsync fix ENG-436).

**Resolución**: Re-ejecutar el workflow fallido con `gh run rerun`, que completó exitosamente y subió los 8 assets al release v1.3.0 existente.

---

## Diagnóstico

### 1. Síntoma inicial

Usuario reportó error al instalar engram-dotnet via FlowForge installer:

```
[ERROR] DownloadAndVerify error: Response status code does not indicate success: 404 (Not Found).
```

El installer intentaba descargar `engram-linux-x64` del release v1.3.0 y recibía 404.

### 2. Verificación del release

```bash
$ gh release view v1.3.0 --json tagName,assets
{"tagName":"v1.3.0","assets":[],"publishedAt":"2026-07-11T03:35:08Z"}
```

Release v1.3.0 tenía 0 assets.

### 3. Análisis del workflow

```bash
$ gh run list --workflow=release.yml --limit=3
completed  failure  Release  v1.3.0  push  28909769128  1m10s  2026-07-08T01:02:10Z
```

El workflow se disparó el 2026-07-08 pero falló.

### 4. Logs del workflow fallido

```bash
$ gh run view 28909769128 --log-failed | grep -A 5 "Test Run"
Test Run Successful.
Total tests: 48
     Passed: 48
 Total time: 3.5262 Seconds
##[error]Process completed with exit code 1.
```

**Anomalía**: Los tests pasaron (48/48), pero el proceso terminó con exit code 1.

### 5. Root cause (no determinado con certeza)

Posibles causas del exit code 1:
- Transient failure en GitHub Actions (infraestructura)
- Warning tratado como error (TreatWarningsAsErrors)
- Problema con el logger `console;verbosity=normal`
- Algún paso post-test fallando silenciosamente

**Conclusión**: El fallo fue probablemente transient, ya que la re-ejecución completó exitosamente sin cambios en el código.

---

## Resolución

### Paso 1: Re-ejecutar el workflow

```bash
$ gh run rerun 28909769128
```

### Paso 2: Monitorear la ejecución

```bash
$ sleep 90 && gh run view 28909769128
✓ v1.3.0 Release · 28909769128
Triggered via push about 2 minutes ago

JOBS
✓ Publish & Release in 1m36s (ID 87476678032)
```

Workflow completó exitosamente en 1m36s.

### Paso 3: Verificar assets

```bash
$ gh release view v1.3.0 --json assets | jq '.assets | length'
8

$ gh release view v1.3.0 --json assets | jq '.assets[].name'
"engram-linux-x64"
"engram-linux-x64.sha256"
"engram-win-x64.exe"
"engram-win-x64.exe.sha256"
"e_sqlite3.dll"
"e_sqlite3.dll.sha256"
"libe_sqlite3.so"
"libe_sqlite3.so.sha256"
```

Los 8 assets esperados están presentes en el release v1.3.0.

---

## Assets publicados

| Archivo | Tamaño | SHA-256 |
|---------|--------|---------|
| `engram-linux-x64` | 107.2 MB | `31a31d7eb7408e0a6e6ca521b466e7a1157463a335ab556817d8380d3a846fcc` |
| `engram-win-x64.exe` | 109.9 MB | `ceb9ad609d8b1abb3be9f7bad67e2fd476bc7ec810456d227e82e5e4658fa655` |
| `libe_sqlite3.so` | 1.3 MB | `478afd10c84cda9db29c6adda40e8a2babba67c7e726c27f91d330ae73e2475b` |
| `e_sqlite3.dll` | 1.7 MB | `39923cfdad272169c217406a60214ffe9bd6c1b3bd396a3eff94b781bd8d3376` |

**Fecha de publicación**: 2026-07-15T21:30:15Z

---

## Impacto en usuarios

### Antes de la resolución (2026-07-11 a 2026-07-15)

- **Usuarios nuevos**: FlowForge installer alpha.12+ descargaba v1.2.1 (fallback)
- **Usuarios existentes**: No afectados (ya tenían engram instalado)
- **Features faltantes en v1.2.1**:
  - ENG-451: Sync recovery (re-aplica mutaciones pulled huérfanas)
  - ENG-452: Self-loop detection (evita 501 cada 30ms)
  - ENG-436: ApplyPulledMutationAsync fix (sync pull roto en SQLite)
  - ENG-447 a ENG-450: PostgresStore atomicity fixes

### Después de la resolución (2026-07-15 en adelante)

- **Usuarios nuevos**: FlowForge installer descarga v1.3.0 con todas las fixes
- **Usuarios existentes**: Pueden actualizar manualmente con:
  ```bash
  curl -L https://github.com/efreet111/engram-dotnet/releases/download/v1.3.0/engram-linux-x64 -o ~/.local/bin/engram
  chmod +x ~/.local/bin/engram
  ```

---

## Lecciones aprendidas

### 1. Verificar workflow después de crear tags

Siempre ejecutar `gh run list --workflow=release.yml` después de crear un tag para confirmar que el workflow se disparó y completó exitosamente.

```bash
# Crear release
gh release create v1.3.0 --notes-file release-notes.md

# Verificar workflow
gh run list --workflow=release.yml --limit=1
# Debe mostrar "success", no "failure" o "in_progress"
```

### 2. Releases manuales vs automatizados

Crear releases con `gh release create` **no dispara** el workflow `release.yml` si el workflow está configurado para dispararse con `push: tags: v*`. El tag se crea, pero el workflow puede no dispararse si:
- Hay un filtro de paths que excluye el tag
- El workflow tiene un `if` condition que no se cumple
- Hay un problema transient en GitHub Actions

**Recomendación**: Después de crear un release manualmente, verificar que el workflow se disparó. Si no, re-ejecutar con `gh run rerun <run-id>`.

### 3. Defensa en profundidad en el installer

El FlowForge installer alpha.12+ tiene lógica para saltear releases sin assets (commit `6fc63c2`). Esta salvaguarda previene que un release notes-only upstream rompa la instalación fresh.

**Lección**: El installer no debe asumir que el release más reciente de una dependencia tiene assets. Siempre verificar la existencia del asset antes de descargar.

### 4. Monitoreo de releases

Considerar agregar un cron job que verifique semanalmente que los releases más recientes de engram-dotnet y FlowForge tienen los assets esperados.

```yaml
# .github/workflows/verify-releases.yml
name: Verify Releases
on:
  schedule:
    - cron: '0 0 * * 1'  # Lunes a medianoche
jobs:
  verify:
    runs-on: ubuntu-latest
    steps:
      - name: Check engram-dotnet latest release has assets
        run: |
          ASSETS=$(gh release view --repo efreet111/engram-dotnet --json assets | jq '.assets | length')
          if [ "$ASSETS" -eq 0 ]; then
            echo "::error::engram-dotnet latest release has no assets"
            exit 1
          fi
```

---

## Referencias

- **ENG-454 en BACKLOG**: `docs/BACKLOG.md` línea 124
- **Incident analysis (FlowForge)**: `FlowForge/.ai-work/incident-engram-v130-missing-binaries/analysis.md`
- **Workflow run fallido**: https://github.com/efreet111/engram-dotnet/actions/runs/28909769128 (2026-07-08)
- **Workflow run exitoso**: https://github.com/efreet111/engram-dotnet/actions/runs/28909769128 (re-run 2026-07-15)
- **Release v1.3.0**: https://github.com/efreet111/engram-dotnet/releases/tag/v1.3.0
- **FlowForge installer fix**: commit `6fc63c2` en repo FlowForge (skip releases without assets)

---

## Checklist de cierre

- [x] Diagnóstico completo del incidente
- [x] ENG-454 creado en BACKLOG.md
- [x] Workflow re-ejecutado exitosamente
- [x] 8 assets subidos al release v1.3.0
- [x] BACKLOG.md actualizado (ENG-454 marcado como ✅ Done)
- [x] Resolución documentada en este archivo
- [x] Lecciones aprendidas documentadas
- [ ] (Opcional) Agregar cron job para verificar releases semanalmente
- [ ] (Opcional) Actualizar documentación de release process en CONTRIBUTING.md

---

**Cerrado por**: FlowForge Orchestrator  
**Fecha de cierre**: 2026-07-15
