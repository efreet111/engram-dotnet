[← Volver al README](../README.md)

# CI/CD con GitHub Actions — Especificación Técnica

> **Estado**: DRAFT — pendiente de implementación  
> **Prioridad**: FUTURA (post-validación del deployment actual en TrueNAS)  
> **Fecha de creación**: 2026-04-11

---

## 1. Objetivo

Implementar un sistema de integración y entrega continua (CI/CD) utilizando GitHub Actions para el proyecto `engram-dotnet`. El objetivo es automatizar el ciclo de vida del desarrollo desde el push de código hasta la actualización del entorno de producción en TrueNAS SCALE. Esto incluye la compilación, pruebas, publicación de releases con binarios self-contained, build y push de imágenes Docker a `ghcr.io`, y deploy automático en el servidor.

---

## 2. Alcance

### Incluye
- Workflows de GitHub Actions para CI, release y deploy
- Compilación y pruebas automáticas en cada push/PR
- Publicación de binario `engram-linux-x64` en GitHub Releases
- Build y push de imagen Docker a GitHub Container Registry (`ghcr.io`)
- Deploy automático (o manual) a TrueNAS SCALE vía SSH
- Rollback automático si el healthcheck post-deploy falla

### No incluye
- Migración de SQLite a otro motor de base de datos
- Gestión dinámica de infraestructura fuera de TrueNAS
- Integración con sistemas de monitoreo externos (Grafana, Prometheus, etc.)
- Ambientes de staging separados (fuera del alcance actual)

---

## 3. Flujo de CI/CD propuesto

```
┌─────────────────────────────────────────────────────────┐
│  Push a cualquier rama / PR hacia main                  │
│  → ci.yml: Build + Test + Lint                          │
└─────────────────────────────────────────────────────────┘
                        │
                        ▼ (solo si tag vX.Y.Z)
┌─────────────────────────────────────────────────────────┐
│  release.yml                                            │
│  → Build binario self-contained linux-x64               │
│  → Crear GitHub Release con binario engram-linux-x64    │
│  → Build imagen Docker                                  │
│  → Push a ghcr.io/efreet111/engram-dotnet:vX.Y.Z        │
│  → Push a ghcr.io/efreet111/engram-dotnet:latest        │
└─────────────────────────────────────────────────────────┘
                        │
                        ▼ (automático o manual)
┌─────────────────────────────────────────────────────────┐
│  deploy.yml                                             │
│  → SSH a TrueNAS SCALE                                  │
│  → docker compose pull + docker compose up -d           │
│  → Verificar healthcheck en :7437/health                │
│  → Rollback a imagen anterior si healthcheck falla      │
└─────────────────────────────────────────────────────────┘
```

---

## 4. GitHub Actions — Workflows requeridos

### a) `ci.yml` — Integración continua

**Archivo**: `.github/workflows/ci.yml`  
**Trigger**: `push` a cualquier rama + `pull_request` hacia `main`  
**Secrets requeridos**: ninguno  

```yaml
# Pseudocódigo — implementar en la fase CI/CD
name: CI
on:
  push:
    branches: ["**"]
  pull_request:
    branches: [main]

jobs:
  build-and-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - run: dotnet restore engram-dotnet.slnx
      - run: dotnet build engram-dotnet.slnx -c Release --no-restore
      - run: dotnet test engram-dotnet.slnx --no-build -c Release --logger trx
      - run: dotnet format engram-dotnet.slnx --verify-no-changes
```

**Artefactos producidos**:
- Reporte de pruebas (`.trx`)
- Estado del lint/format

---

### b) `release.yml` — Publicar release

**Archivo**: `.github/workflows/release.yml`  
**Trigger**: push de tag con formato `v*.*.*`  
**Secrets requeridos**: `GHCR_TOKEN`  

```yaml
# Pseudocódigo — implementar en la fase CI/CD
name: Release
on:
  push:
    tags: ["v*.*.*"]

jobs:
  publish-binary:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - run: |
          dotnet publish src/Engram.Cli/Engram.Cli.csproj \
            -c Release -r linux-x64 --self-contained \
            -p:UseAppHost=true -o dist/
          mv dist/engram dist/engram-linux-x64
      - uses: softprops/action-gh-release@v2
        with:
          files: dist/engram-linux-x64

  publish-docker:
    runs-on: ubuntu-latest
    needs: publish-binary
    steps:
      - uses: actions/checkout@v4
      - uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GHCR_TOKEN }}
      - uses: docker/build-push-action@v5
        with:
          context: .
          file: docker/Dockerfile
          push: true
          tags: |
            ghcr.io/efreet111/engram-dotnet:${{ github.ref_name }}
            ghcr.io/efreet111/engram-dotnet:latest
```

**Artefactos producidos**:
- Binario `engram-linux-x64` adjunto al GitHub Release
- Imagen Docker en `ghcr.io/efreet111/engram-dotnet`

---

### c) `deploy.yml` — Deploy automático a TrueNAS

**Archivo**: `.github/workflows/deploy.yml`  
**Trigger**: manual (`workflow_dispatch`) o automático al completar `release.yml`  
**Secrets requeridos**: `TRUENAS_HOST`, `TRUENAS_USER`, `TRUENAS_SSH_KEY`  

```yaml
# Pseudocódigo — implementar en la fase CI/CD
name: Deploy to TrueNAS
on:
  workflow_dispatch:
  workflow_run:
    workflows: ["Release"]
    types: [completed]

jobs:
  deploy:
    runs-on: ubuntu-latest
    if: ${{ github.event.workflow_run.conclusion == 'success' || github.event_name == 'workflow_dispatch' }}
    steps:
      - name: Deploy via SSH
        uses: appleboy/ssh-action@v1
        with:
          host: ${{ secrets.TRUENAS_HOST }}
          username: ${{ secrets.TRUENAS_USER }}
          key: ${{ secrets.TRUENAS_SSH_KEY }}
          script: |
            cd /mnt/Pool_8TB/engram_data
            docker compose -f docker/docker-compose.yml pull
            docker compose -f docker/docker-compose.yml up -d
            # Esperar inicio y verificar healthcheck
            sleep 15
            wget -qO- http://localhost:7437/health || \
              (docker compose -f docker/docker-compose.yml rollback && exit 1)
```

**Nota sobre rollback**: Docker Compose no tiene rollback nativo. La estrategia recomendada es mantener el tag de la imagen anterior y hacer `docker compose up -d` con el tag previo en caso de fallo. Definir estrategia detallada durante la implementación.

---

## 5. Secrets de GitHub requeridos

| Secret | Descripción | Cómo obtenerlo |
|--------|-------------|----------------|
| `TRUENAS_HOST` | IP o hostname del servidor TrueNAS | Panel admin TrueNAS → Red |
| `TRUENAS_USER` | Usuario SSH (`truenas_admin`) | Admin TrueNAS |
| `TRUENAS_SSH_KEY` | Clave privada SSH (Ed25519 recomendado) | `ssh-keygen -t ed25519` — instalar pública en TrueNAS |
| `GHCR_TOKEN` | Personal Access Token con scope `write:packages` | GitHub → Settings → Developer Settings → PAT |

### Configurar en GitHub
`Settings → Secrets and variables → Actions → New repository secret`

---

## 6. Cambios necesarios en el proyecto para CI/CD (Opción B futura)

Cuando se implemente CI/CD completo, el Dockerfile deberá migrar de **Opción A** (compilar desde fuente) a **Opción B** (descargar binario publicado):

### `docker/Dockerfile` — migración a Opción B
```dockerfile
# ANTES (Opción A — compilar desde fuente)
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY . .
RUN dotnet restore engram-dotnet.slnx
RUN dotnet publish src/Engram.Server/... --self-contained ...

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-preview AS runtime
...
COPY --from=build /app/publish .
```

```dockerfile
# DESPUÉS (Opción B — binario de GitHub Release)
FROM debian:12-slim
ARG ENGRAM_VERSION=v1.1.0
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates wget libicu72 && rm -rf /var/lib/apt/lists/*
RUN wget -O /usr/local/bin/engram \
    "https://github.com/efreet111/engram-dotnet/releases/download/${ENGRAM_VERSION}/engram-linux-x64" \
    && chmod +x /usr/local/bin/engram
# ... resto igual (usuario, volumen, healthcheck)
ENTRYPOINT ["/usr/local/bin/engram"]
CMD ["serve"]
```

### `docker/docker-compose.yml` — con imagen de ghcr.io
```yaml
services:
  engram:
    image: ghcr.io/efreet111/engram-dotnet:latest  # ← desde registry
    # build: ya no es necesario
    ...
```

---

## 7. Estrategia de versionado

- **Formato de tags**: `vX.Y.Z` (semver estricto)
  - `X` — cambios breaking (API incompatible)
  - `Y` — features nuevas retrocompatibles
  - `Z` — bugfixes y parches
- **Rama de producción**: `main` (siempre estable, deployable)
- **Ramas de feature**: `feature/nombre-descriptivo` — base en `main`
- **Ramas de fix**: `fix/nombre-descriptivo`
- **Crear release**: tag en `main` → dispara `release.yml`

---

## 8. Consideraciones de seguridad

### Secrets
- Nunca hardcodear secrets en el código o en archivos Docker
- Rotar `GHCR_TOKEN` y `TRUENAS_SSH_KEY` cada 90 días mínimo
- `ENGRAM_JWT_SECRET` en producción: usar `openssl rand -base64 32`

### SSH Key de deploy (mínimos privilegios)
```bash
# Generar clave dedicada solo para CI/CD
ssh-keygen -t ed25519 -C "github-actions-deploy" -f ~/.ssh/engram_deploy

# Instalar solo la pública en TrueNAS
# Restringir en authorized_keys a comandos específicos:
command="cd /mnt/Pool_8TB/engram_data && docker compose -f docker/docker-compose.yml up -d",no-agent-forwarding,no-X11-forwarding ssh-ed25519 AAAA...
```

### Imagen Docker
- Siempre referenciar imágenes por digest en producción (no solo `:latest`)
- Escanear imagen con `docker scout` o `trivy` antes del push

---

## 9. Prerequisitos antes de implementar CI/CD

### En TrueNAS SCALE
- [ ] Acceso SSH habilitado desde GitHub Actions (regla de firewall si aplica)
- [ ] Clave pública del deploy key instalada en `~/.ssh/authorized_keys` de `truenas_admin`
- [ ] Docker y Docker Compose instalados y funcionales
- [ ] Directorio `/mnt/Pool_8TB/engram_data` con permisos correctos (UID 950)
- [ ] Puerto 7437 accesible localmente para healthcheck

### En GitHub
- [ ] Secrets configurados: `TRUENAS_HOST`, `TRUENAS_USER`, `TRUENAS_SSH_KEY`, `GHCR_TOKEN`
- [ ] GitHub Container Registry habilitado para el repositorio
- [ ] Protección de rama `main` habilitada (requiere PR + CI verde)
- [ ] GitHub Actions habilitado en el repositorio

---

## 10. Criterios de aceptación

- [ ] `ci.yml` se ejecuta en cada push y PR — falla si tests no pasan
- [ ] `ci.yml` falla si el código no está formateado correctamente
- [ ] `release.yml` se dispara al crear un tag `vX.Y.Z` en `main`
- [ ] El binario `engram-linux-x64` aparece adjunto en el GitHub Release
- [ ] La imagen Docker está disponible en `ghcr.io/efreet111/engram-dotnet`
- [ ] `deploy.yml` puede ejecutarse manualmente desde GitHub Actions UI
- [ ] Después del deploy, `GET /health` retorna `{"status":"ok"}` en TrueNAS
- [ ] Si el healthcheck falla, el sistema hace rollback a la versión anterior
- [ ] Ningún secret aparece en los logs de los workflows

---

## Metadata

- **Fecha de creación**: 2026-04-11
- **Estado**: DRAFT — pendiente de implementación
- **Prioridad**: FUTURA (post-validación del deployment actual en TrueNAS)
- **Relacionado con**: `docs/DEPLOYMENT.md`, `docker/Dockerfile`, `docker/docker-compose.yml`
- **Implementar cuando**: el deployment Opción A esté validado y estable en TrueNAS
