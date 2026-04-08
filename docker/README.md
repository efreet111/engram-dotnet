[← Volver al README](../README.md)

# Instalar engram-dotnet en TrueNAS SCALE

> Esta guía explica cómo instalar engram-dotnet en TrueNAS SCALE usando Custom App (Docker). No se requiere .NET ni conocimientos de programación — solo seguir los pasos.

---

## Requisitos previos

- TrueNAS SCALE con Apps habilitado
- Un dataset creado para los datos de engram (ej: `tank/engram`)
- Acceso a la interfaz web de TrueNAS

---

## Paso 1 — Crear el dataset de datos

En TrueNAS SCALE, los datos del contenedor deben vivir en un dataset propio para que sobrevivan actualizaciones.

1. Ir a **Storage → Datasets**
2. Crear un dataset nuevo: `engram` (dentro de tu pool, ej: `tank/engram`)
3. Anotar el path completo: `/mnt/tank/engram` (reemplazar `tank` por el nombre de tu pool)

---

## Paso 2 — Preparar los archivos de configuración

Necesitás dos archivos en tu TrueNAS. La forma más fácil es conectarse por SSH al TrueNAS y crear una carpeta de configuración:

```bash
# Conectarse por SSH al TrueNAS
ssh admin@truenas.local

# Crear carpeta de configuración
mkdir -p /mnt/tank/engram-config
cd /mnt/tank/engram-config

# Descargar los archivos del repo
curl -L https://raw.githubusercontent.com/efreet111/engram-dotnet/main/docker/Dockerfile -o Dockerfile
curl -L https://raw.githubusercontent.com/efreet111/engram-dotnet/main/docker/docker-compose.yml -o docker-compose.yml
```

### Editar docker-compose.yml

Abrir el archivo y cambiar el path del volumen por el path real de tu dataset:

```bash
nano docker-compose.yml
```

Buscar esta línea:
```yaml
- /mnt/tank/engram:/data/engram
```

Reemplazar `tank` por el nombre de tu pool. Guardar con `Ctrl+O`, salir con `Ctrl+X`.

---

## Paso 3 — Crear la Custom App en TrueNAS

1. Ir a **Apps → Available Applications**
2. Hacer click en **Custom App** (arriba a la derecha)
3. Completar el formulario:

### Application Name
```
engram
```

### Image Configuration
- **Image Repository**: dejar vacío por ahora (vamos a usar build local)
- Alternativamente, en **Docker Compose** pegar el contenido del `docker-compose.yml`

> **Nota**: TrueNAS SCALE con Apps basado en Kubernetes (versiones anteriores) usa Helm. Si tu versión usa **Docker Compose** directamente, pegá el contenido del `docker-compose.yml` en el campo correspondiente.

### Port Forwarding
- Container Port: `7437`
- Node Port: `7437`
- Protocol: `TCP`

### Storage
- Host Path: `/mnt/tank/engram` (tu dataset)
- Mount Path: `/data/engram`

4. Hacer click en **Save** e **Install**

---

## Paso 4 — Verificar que funciona

Desde cualquier máquina en tu red:

```bash
# Reemplazar con la IP de tu TrueNAS
curl http://192.168.1.X:7437/health
```

Respuesta esperada:
```json
{"status":"ok","service":"engram","version":"1.0.0"}
```

---

## Paso 5 — Configurar los desarrolladores

Una vez que el servidor responde, cada desarrollador agrega estas variables a su `~/.bashrc` o `~/.zshrc`:

```bash
export ENGRAM_URL=http://192.168.1.X:7437    # IP de tu TrueNAS
export ENGRAM_USER=nombre.apellido            # identidad del desarrollador
```

Y sigue la [Guía para el desarrollador](../docs/DEVELOPER-SETUP.md) para configurar Cursor o VS Code.

---

## Alternativa — Correr con Docker directamente (sin TrueNAS)

Si tenés Docker instalado en cualquier Linux:

```bash
# Clonar solo el directorio docker
git clone https://github.com/efreet111/engram-dotnet
cd engram-dotnet/docker

# Crear directorio de datos
mkdir -p /data/engram

# Editar docker-compose.yml y ajustar el path del volumen
# Luego construir e iniciar
docker compose up -d --build

# Verificar
curl http://localhost:7437/health
```

---

## Solución de problemas

### El contenedor no arranca

Ver los logs:
```bash
docker logs engram
```

### Error: `libicu` not found

El binario .NET requiere `libicu`. Está incluido en el Dockerfile. Si construiste la imagen manualmente, asegurate de usar `debian:12-slim` como base.

### El health check falla

Verificar que el puerto 7437 no esté bloqueado por el firewall de TrueNAS:
- Ir a **Network → Global Configuration**
- Verificar que no haya reglas bloqueando el puerto 7437

### Los datos no persisten al reiniciar

Verificar que el volumen esté montado correctamente:
```bash
docker inspect engram | grep Mounts -A 10
```
El `Source` debe apuntar a tu dataset de TrueNAS.
