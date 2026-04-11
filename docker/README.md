# Docker — engram-dotnet

## Estrategia de build

El proyecto utiliza una estrategia de compilación basada en código fuente dentro del servidor de Docker, siguiendo un enfoque de construcción multietapa. Se inicia con una imagen base `mcr.microsoft.com/dotnet/sdk:10.0-preview` para compilar el código y generar el binario autocontenido `engram`. Luego, la imagen final utiliza `mcr.microsoft.com/dotnet/runtime-deps:10.0-preview` para ejecutar el contenedor en un entorno más liviano.

## Prerequisitos

Antes de comenzar, asegúrate de cumplir con los siguientes prerequisitos:

- **Herramientas necesarias:**
  - `git`
  - `docker`
  - `docker compose`
- **Configuración inicial en TrueNAS SCALE:**
  1. Clonar este repositorio en `/mnt/Pool_8TB/engram_data`.
  2. Crear el directorio para los datos de la base de datos SQLite:
     ```bash
     mkdir -p /mnt/Pool_8TB/engram_data/database
     ```
  3. Ajustar los permisos del directorio de datos para el usuario del contenedor `appuser` (UID/GID 950):
     ```bash
     chown -R 950:950 /mnt/Pool_8TB/engram_data/database
     ```

## Build y deploy (TrueNAS SCALE)

Pasos detallados para construir y desplegar el contenedor en TrueNAS SCALE:

1. **Clonar el repositorio:**
```bash
git clone https://github.com/efreet111/engram-dotnet.git /mnt/Pool_8TB/engram_data
cd /mnt/Pool_8TB/engram_data
```

2. **Configurar el archivo de entorno:**
```bash
cp docker/.env.example docker/.env
```
Editar `docker/.env` y definir la ruta al directorio de datos:
```env
ENGRAM_DATA_PATH=/mnt/Pool_8TB/engram_data/database
ENGRAM_HOST_PORT=7437
```

3. **Configurar permisos de datos:**
   ```bash
   chown -R 950:950 /mnt/Pool_8TB/engram_data/database
   ```

4. **Construir y desplegar los servicios:**
   ```bash
   docker compose -f docker/docker-compose.yml build
   docker compose -f docker/docker-compose.yml up -d
   ```

## Verificar que funciona

Después de iniciar los servicios, verifica que todo esté funcionando correctamente:

1. **Listar contenedores en ejecución:**
   ```bash
   docker ps
   ```

2. **Ver los logs del contenedor:**
   ```bash
   docker logs engram
   ```

3. **Consultar el endpoint de health check:**
   ```bash
   wget -qO- http://localhost:7437/health
   ```
   Respuesta esperada:
   ```json
   {"status":"ok","service":"engram","version":"1.0.0"}
   ```

4. **Obtener estadísticas del contenedor:**
   ```bash
   docker stats engram
   ```

## Variables del host (docker/.env)

Estas variables configuran cómo Docker mapea recursos del host al contenedor. Se definen en `docker/.env` (no commiteado) a partir de `docker/.env.example`.

| Variable | Descripción | Ejemplo |
|---|---|---|
| `ENGRAM_DATA_PATH` | Ruta en el host al directorio de datos SQLite | `/mnt/Pool_8TB/engram_data/database` |
| `ENGRAM_HOST_PORT` | Puerto expuesto en el host (default: `7437`) | `7437` |

## Variables de entorno

### Configurables

| Nombre            | Descripción                                  | Valor por defecto      | Obligatorio |
|-------------------|----------------------------------------------|------------------------|-------------|
| `ENGRAM_DATA_DIR` | Ruta al directorio de datos dentro del contenedor | `/app/database`       | No          |
| `ENGRAM_PORT`     | Puerto en el que se expone el servicio       | `7437`                | No          |
| `ENGRAM_JWT_SECRET` | Clave secreta para JWT                     | *(vacío)*              | Sí          |
| `ENGRAM_CORS_ORIGINS` | Orígenes para CORS                       | *(vacío)*              | No          |

## Volumen de datos

El contenedor utiliza un volumen persistente configurado en `/mnt/Pool_8TB/engram_data/database` dentro del servidor TrueNAS. Este almacenamiento se utiliza para guardar la base de datos SQLite.

### Backup manual

Para realizar un respaldo manual de los datos, copia el contenido del directorio:
```bash
rsync -av /mnt/Pool_8TB/engram_data/database /ruta/de/backup/
```

### Restauración manual

Para restaurar un backup, copia los datos al directorio de datos y ajusta los permisos:
```bash
rsync -av /ruta/de/backup/ /mnt/Pool_8TB/engram_data/database
chown -R 950:950 /mnt/Pool_8TB/engram_data/database
```

## Actualizar a nueva versión

Para actualizar a una nueva versión del proyecto:

1. Detener los servicios:
   ```bash
   docker compose -f docker/docker-compose.yml down
   ```

2. Descargar los últimos cambios del repositorio:
   ```bash
   git pull
   ```

3. Reconstruir y reiniciar los servicios:
   ```bash
   docker compose -f docker/docker-compose.yml build
   docker compose -f docker/docker-compose.yml up -d
   ```

## Troubleshooting

### Permisos incorrectos en el directorio de datos

Asegúrate de que el directorio de datos tenga los permisos correctos:
```bash
chown -R 950:950 /mnt/Pool_8TB/engram_data/database
```

### Puerto 7437 ya está en uso

Verifica qué servicio está ocupando el puerto:
```bash
sudo lsof -i :7437
```
Detén el servicio correspondiente o cambia el puerto en `docker-compose.yml`.

### Health check falla

1. Asegúrate de que el contenedor esté en ejecución:
   ```bash
   docker ps
   ```

2. Revisa los logs del contenedor para detectar errores:
   ```bash
   docker logs engram
   ```

3. Verifica la conectividad al puerto:
   ```bash
   wget -qO- http://localhost:7437/health
   ```

## CI/CD futuro

Se encuentra planificado implementar un pipeline de CI/CD. Para más detalles, consulta [`docs/CICD-SPEC.md`](../docs/CICD-SPEC.md).