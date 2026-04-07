[← Volver al README](../README.md)

# Deployment — engram-dotnet en servidor Linux

---

## Requisitos del servidor

- Linux x64 (Ubuntu 20.04+, Debian 11+, RHEL 8+, etc.)
- Sin .NET runtime requerido — el binario es self-contained
- Puerto 7437 disponible (o el que se configure)

---

## Compilar el binario

En una máquina con .NET 10 SDK:

```bash
git clone https://github.com/tu-usuario/engram-dotnet
cd engram-dotnet
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o dist/
```

El binario queda en `dist/engram`. Copiarlo al servidor:

```bash
scp dist/engram usuario@servidor.interno:/opt/engram/engram
ssh usuario@servidor.interno "chmod +x /opt/engram/engram"
```

---

## Verificar instalación

```bash
/opt/engram/engram version
/opt/engram/engram stats
```

---

## Servicio systemd

Crear `/etc/systemd/system/engram.service`:

```ini
[Unit]
Description=Engram — Memoria persistente para agentes de IA
After=network.target

[Service]
Type=simple
User=engram
Group=engram
WorkingDirectory=/opt/engram

# Variables de entorno
Environment=ENGRAM_DATA_DIR=/data/engram
Environment=ENGRAM_PORT=7437
# Descomentar para habilitar auth JWT:
# Environment=ENGRAM_JWT_SECRET=cambiar-por-secreto-seguro
# Descomentar para habilitar CORS (separados por coma):
# Environment=ENGRAM_CORS_ORIGINS=http://localhost:3000,http://dashboard.interno

ExecStart=/opt/engram/engram serve
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

Activar y arrancar:

```bash
# Crear usuario dedicado
sudo useradd -r -s /bin/false engram

# Crear directorio de datos
sudo mkdir -p /data/engram
sudo chown engram:engram /data/engram

# Habilitar y arrancar el servicio
sudo systemctl daemon-reload
sudo systemctl enable engram
sudo systemctl start engram
sudo systemctl status engram
```

---

## Reverse proxy con nginx

Crear `/etc/nginx/sites-available/engram`:

```nginx
upstream engram {
    server 127.0.0.1:7437;
    keepalive 32;
}

server {
    listen 80;
    server_name engram.servidor.interno;

    # Redirigir a HTTPS si se tiene certificado
    # return 301 https://$host$request_uri;

    location / {
        proxy_pass http://engram;
        proxy_http_version 1.1;
        proxy_set_header Connection "";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;

        # Timeouts generosos para operaciones largas (export, import)
        proxy_read_timeout 120s;
        proxy_connect_timeout 10s;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/engram /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

---

## Configuración en máquinas de desarrollo

En cada desarrollador, agregar a `~/.bashrc` o `~/.zshrc`:

```bash
export ENGRAM_URL=http://engram.servidor.interno
# o con IP directa:
export ENGRAM_URL=http://192.168.1.100:7437
```

Los plugins de Claude Code, OpenCode, Gemini CLI y Codex leen `ENGRAM_URL` automáticamente. **No se requieren cambios en los plugins.**

---

## Monitoreo básico

```bash
# Estado del servicio
sudo systemctl status engram

# Logs en tiempo real
sudo journalctl -u engram -f

# Verificar que responde
curl http://localhost:7437/health

# Estadísticas de memoria
curl http://localhost:7437/stats | jq .
```

---

## Backup de datos

Los datos viven en `ENGRAM_DATA_DIR/engram.db` (SQLite). Estrategia mínima:

```bash
# Backup diario con cron
0 2 * * * cp /data/engram/engram.db /backup/engram/engram-$(date +\%Y\%m\%d).db

# O usar el export JSON para backup portable
/opt/engram/engram export /backup/engram/export-$(date +%Y%m%d).json
```

SQLite en modo WAL es seguro para copias en caliente (el archivo `.db` puede copiarse mientras el proceso corre).

---

## Actualizar el binario

```bash
# Detener el servicio
sudo systemctl stop engram

# Reemplazar el binario
sudo cp /tmp/engram-nuevo /opt/engram/engram
sudo chmod +x /opt/engram/engram

# Arrancar
sudo systemctl start engram
sudo systemctl status engram
```

No se requiere migración de base de datos — las migraciones de schema se aplican automáticamente al arrancar si hay cambios pendientes.
