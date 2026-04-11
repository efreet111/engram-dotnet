[← Volver al README](../README.md)

# Deployment

---

## Opción 1 — TrueNAS SCALE con Docker

### Prerequisitos

- TrueNAS SCALE con Docker y Docker Compose habilitados
- Git disponible en el sistema
- Acceso SSH al servidor

### Pasos

#### 1. Clonar el repositorio

```bash
git clone https://github.com/efreet111/engram-dotnet.git /mnt/Pool_8TB/engram_data
cd /mnt/Pool_8TB/engram_data
```

#### 2. Configurar datos y entorno

Crear el directorio de datos y ajustar permisos:

```bash
mkdir -p /mnt/Pool_8TB/engram_data/database
chown -R 950:950 /mnt/Pool_8TB/engram_data/database
```

Crear archivo de entorno:

```bash
cp docker/.env.example docker/.env
```

Editar `docker/.env` con la ruta correcta:

```env
ENGRAM_DATA_PATH=/mnt/Pool_8TB/engram_data/database
ENGRAM_HOST_PORT=7437
```

> **Nota**: El host monta `ENGRAM_DATA_PATH`; dentro del contenedor, la app lo ve como `ENGRAM_DATA_DIR=/app/database`.

#### 3. Construir la imagen Docker

```bash
docker compose -f docker/docker-compose.yml build
```

#### 4. Levantar el servicio

```bash
docker compose -f docker/docker-compose.yml up -d
```

#### 5. Verificar el estado

```bash
wget -qO- http://localhost:7437/health
# → {"status":"ok","service":"engram","version":"1.0.0"}
```

#### 6. Logs en tiempo real

```bash
docker compose -f docker/docker-compose.yml logs -f
```

### Variables de entorno opcionales

| Variable             | Descripción                                                                 |
|----------------------|-----------------------------------------------------------------------------|
| `ENGRAM_JWT_SECRET`  | Define un secreto para habilitar autenticación JWT                         |
| `ENGRAM_CORS_ORIGINS`| Lista separada por comas con orígenes permitidos para CORS                  |

### Actualizar el servicio

```bash
docker compose -f docker/docker-compose.yml down
git pull
docker compose -f docker/docker-compose.yml build
docker compose -f docker/docker-compose.yml up -d
```

---

## Opción 2 — Linux tradicional con systemd

### Requisitos del servidor

- Linux x64 (Ubuntu 20.04+, Debian 11+, RHEL 8+, etc.)
- Acceso root o sudo

### Instalación del binario

#### Opción 1: Descargar binario precompilado

```bash
sudo mkdir -p /opt/engram
curl -L https://github.com/efreet111/engram-dotnet/releases/latest/download/engram-linux-x64 -o /opt/engram/engram
sudo chmod +x /opt/engram/engram

# Verificar instalación
/opt/engram/engram version
```

#### Opción 2: Compilar desde fuente

```bash
git clone https://github.com/efreet111/engram-dotnet
cd engram-dotnet
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/
sudo mv dist/engram /opt/engram/
sudo chmod +x /opt/engram/engram
```

### Configuración

1. Crear directorio de datos:

```bash
sudo mkdir -p /data/engram
sudo useradd -r -s /bin/false engram
sudo chown engram:engram /data/engram
```

2. Crear archivo de entorno en `/etc/engram/server.env`:

```bash
sudo mkdir -p /etc/engram
sudo tee /etc/engram/server.env > /dev/null <<'EOF'
ENGRAM_DATA_DIR=/data/engram
ENGRAM_PORT=7437

# Opcional
# ENGRAM_JWT_SECRET=secreto_seguro
# ENGRAM_CORS_ORIGINS=http://frontend.ejemplo.com
EOF
sudo chmod 640 /etc/engram/server.env
sudo chown root:engram /etc/engram/server.env
```

### Systemd

1. Crear servicio systemd en `/etc/systemd/system/engram.service`:

```ini
[Unit]
Description=Engram — Servidor de IA
After=network.target

[Service]
Type=simple
User=engram
Group=engram
ExecStart=/opt/engram/engram serve
EnvironmentFile=/etc/engram/server.env
Restart=on-failure
RestartSec=5s

# Seguridad
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ReadWritePaths=/data/engram

[Install]
WantedBy=multi-user.target
```

2. Habilitar y arrancar:

```bash
sudo systemctl daemon-reload
sudo systemctl enable engram
sudo systemctl start engram
sudo systemctl status engram
```

### Verificación del servicio

```bash
curl http://localhost:7437/health
sudo systemctl status engram
sudo journalctl -u engram -f
```

### Actualizar el binario

```bash
sudo systemctl stop engram
sudo cp nuevo-engram /opt/engram/engram
sudo chmod +x /opt/engram/engram
sudo systemctl start engram
sudo systemctl status engram
```

---

## Reverse proxy con nginx

### Configuración

```nginx
server {
    listen 80;
    server_name engram.example.com;

    location / {
        proxy_pass http://localhost:7437;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
    }
}
```

> **Nota**: Actualizar `server_name` con el dominio real.

Reiniciar nginx:

```bash
sudo nginx -t
sudo systemctl reload nginx
```

---

## Configuración en máquinas de desarrollo

Cada usuario debe configurar:

```bash
export ENGRAM_URL=http://servidor:7437
export ENGRAM_USER=your.name
```

---

## Monitoreo básico

Comandos útiles:

```bash
# Ver servicio
sudo systemctl status engram

# Logs recientes
sudo journalctl -u engram

# Health check
curl http://localhost:7437/health

# Stats del servidor
curl http://localhost:7437/stats
```

---

## Backup de datos

### SQLite

```bash
cp /data/engram/engram.db /backup/engram-$(date +%Y%m%d).db
```

### Export JSON

```bash
curl http://localhost:7437/export -o /backup/engram-$(date +%Y%m%d).json
```