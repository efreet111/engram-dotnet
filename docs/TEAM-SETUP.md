[← Volver al README](../README.md)

# Guía para IT — Deploy del servidor

> Esta guía explica cómo instalar y configurar el servidor engram-dotnet para el equipo.
> Hay dos opciones: TrueNAS SCALE con Docker (recomendado) o Linux tradicional con systemd.

## Índice

- [¿Qué es esto?](#qué-es-esto)
- [Arquitectura del equipo](#arquitectura-del-equipo)
- [Opción 1 — TrueNAS SCALE con Docker](#opción-1--truenas-scale-con-docker)
- [Opción 2 — Linux tradicional con systemd](#opción-2--linux-tradicional-con-systemd)
- [Distribuir configuración a los desarrolladores](#distribuir-configuración-a-los-desarrolladores)
- [Variables de entorno](#variables-de-entorno)
- [Verificación del servidor](#verificación-del-servidor)
- [Backup y mantenimiento](#backup-y-mantenimiento)

---

## ¿Qué es esto?

**engram-dotnet** es un servidor de memoria persistente para agentes de IA (Claude Code, Cursor, OpenCode, Gemini CLI, etc.). En lugar de que cada desarrollador tenga su propia instancia local, el equipo comparte una sola instancia centralizada.

Cada desarrollador tiene su identidad (`ENGRAM_USER`) que namespacea automáticamente sus memorias — no hay colisión de datos entre desarrolladores.

---

## Arquitectura del equipo

```
┌────────────────────────────────────────────┐
│         Servidor (Docker o systemd)        │
│                                            │
│   engram-dotnet (puerto 7437)              │
│   SQLite en /app/database/engram.db        │
│                                            │
└──────────────────────┬─────────────────────┘
                       │ HTTP REST / MCP stdio
        ┌──────────────┼──────────────┐
        ▼              ▼              ▼
   Dev: victor     Dev: maria     Dev: juan
   ENGRAM_USER=    ENGRAM_USER=   ENGRAM_USER=
   victor.silgado  maria.garcia   juan.perez
   ENGRAM_URL=http://servidor.interno:7437
```

Cada agente (Cursor, Claude Code, etc.) corre en la máquina del desarrollador. El binario `engram mcp` actúa como **proxy HTTP** hacia el servidor centralizado — no toca SQLite localmente.

---

## Opción 1 — TrueNAS SCALE con Docker

### Prerequisitos

- TrueNAS SCALE con Docker y Docker Compose habilitados
- Git disponible en el sistema
- Acceso SSH al servidor

### Paso 1 — Clonar el repositorio

```bash
git clone https://github.com/efreet111/engram-dotnet.git /mnt/Pool_8TB/engram_data
cd /mnt/Pool_8TB/engram_data
```

### Paso 2 — Configurar datos y entorno

Crear el directorio de datos y ajustar permisos para el usuario del contenedor (UID/GID 950):

```bash
mkdir -p /mnt/Pool_8TB/engram_data/database
chown -R 950:950 /mnt/Pool_8TB/engram_data/database
```

Crear el archivo de entorno a partir del ejemplo:

```bash
cp docker/.env.example docker/.env
```

Editar `docker/.env` con la ruta real:

```env
ENGRAM_DATA_PATH=/mnt/Pool_8TB/engram_data/database
ENGRAM_HOST_PORT=7437
```

> **Nota**: `ENGRAM_DATA_PATH` es la ruta en el **host** que Docker monta dentro del contenedor. Dentro del contenedor la app lo ve como `/app/database`.

### Paso 3 — Build y deploy

```bash
docker compose -f docker/docker-compose.yml build
docker compose -f docker/docker-compose.yml up -d
```

### Paso 4 — Verificar

```bash
wget -qO- http://localhost:7437/health
# → {"status":"ok","service":"engram","version":"1.0.0"}
```

Ver logs:

```bash
docker compose -f docker/docker-compose.yml logs -f
```

### Actualizar a nueva versión

```bash
docker compose -f docker/docker-compose.yml down
git pull
docker compose -f docker/docker-compose.yml build
docker compose -f docker/docker-compose.yml up -d
```

---

## Opción 2 — Linux tradicional con systemd

### Prerequisitos

- Linux x64 (Ubuntu 20.04+, Debian 11+, RHEL 8+, etc.)
- Acceso root o sudo

### Paso 1 — Descargar el binario

```bash
sudo mkdir -p /opt/engram

# Descargar el binario linux-x64
curl -L https://github.com/efreet111/engram-dotnet/releases/latest/download/engram-linux-x64 \
     -o /opt/engram/engram

sudo chmod +x /opt/engram/engram

# Verificar
/opt/engram/engram version
```

Alternativamente, compilar desde fuente:

```bash
git clone https://github.com/efreet111/engram-dotnet
cd engram-dotnet
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o /opt/engram/
```

### Paso 2 — Configurar el servidor

Crear directorio de datos y usuario de servicio:

```bash
sudo mkdir -p /data/engram
sudo useradd -r -s /bin/false engram
sudo chown engram:engram /data/engram
```

Crear archivo de entorno en `/etc/engram/server.env`:

```bash
sudo mkdir -p /etc/engram

sudo tee /etc/engram/server.env > /dev/null <<'EOF'
ENGRAM_DATA_DIR=/data/engram
ENGRAM_PORT=7437

# Opcional — habilitar autenticación JWT
# ENGRAM_JWT_SECRET=cambia_esto_por_un_secreto_seguro

# Opcional — CORS si el dashboard web se accede desde otro origen
# ENGRAM_CORS_ORIGINS=http://dashboard.interno
EOF

sudo chmod 640 /etc/engram/server.env
sudo chown root:engram /etc/engram/server.env
```

### Paso 3 — Instalar como servicio systemd

```bash
sudo tee /etc/systemd/system/engram.service > /dev/null <<'EOF'
[Unit]
Description=Engram — Servidor de memoria persistente para agentes de IA
After=network.target
Documentation=https://github.com/efreet111/engram-dotnet

[Service]
Type=simple
User=engram
Group=engram
ExecStart=/opt/engram/engram serve
EnvironmentFile=/etc/engram/server.env
Restart=on-failure
RestartSec=5s

# Hardening básico
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ReadWritePaths=/data/engram

StandardOutput=journal
StandardError=journal
SyslogIdentifier=engram

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable engram
sudo systemctl start engram
sudo systemctl status engram
```

### Paso 4 — Verificar

```bash
curl http://localhost:7437/health
# → {"status":"ok","service":"engram","version":"1.0.0"}

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

## Distribuir configuración a los desarrolladores

El repositorio incluye archivos de configuración en `config/` listos para distribuir.

### Variables de entorno por desarrollador

Cada desarrollador necesita estas variables en su `.bashrc` / `.zshrc`:

```bash
# URL del servidor compartido
export ENGRAM_URL=http://10.0.0.5:7437        # ← IP/hostname del servidor

# Identidad del desarrollador (namespacea las memorias en el servidor)
export ENGRAM_USER=nombre.apellido
```

> Si `ENGRAM_JWT_SECRET` está configurado en el servidor, también hay que setear `ENGRAM_JWT_TOKEN` en la máquina del desarrollador con el JWT válido.

### Instrucciones para Cursor

1. Copiar `config/cursor/mcp.json` a `~/.cursor/mcp.json`
2. Copiar el contenido de `config/cursor/rules/` a `~/.cursor/rules/`
3. Copiar el contenido de `config/cursor/agents/` a `~/.cursor/agents/`
4. Reiniciar Cursor

### Instrucciones para VS Code

1. Copiar `config/vscode/mcp.json` a `~/.vscode/mcp.json`
2. Copiar `config/vscode/prompts/engram.instructions.md` a `~/.github/copilot-instructions.md`
3. Reiniciar VS Code

> Ver la [guía para el desarrollador](DEVELOPER-SETUP.md) para instrucciones detalladas por herramienta.

---

## Variables de entorno

### Variables del servidor

| Variable | Default | Descripción |
|---|---|---|
| `ENGRAM_DATA_DIR` | `~/.engram` | Directorio de datos (SQLite) — usado por la app dentro del contenedor o en systemd |
| `ENGRAM_PORT` | `7437` | Puerto interno del servidor |
| `ENGRAM_JWT_SECRET` | — | Si se setea, habilita auth JWT en todos los endpoints (excepto `/health`) |
| `ENGRAM_CORS_ORIGINS` | — | Orígenes CORS permitidos, separados por coma |

### Variables del host Docker (docker/.env)

Solo aplican al deployment con Docker Compose:

| Variable | Descripción | Ejemplo |
|---|---|---|
| `ENGRAM_DATA_PATH` | Ruta en el **host** al directorio de datos | `/mnt/Pool_8TB/engram_data/database` |
| `ENGRAM_HOST_PORT` | Puerto expuesto en el host (default: `7437`) | `7437` |

> `ENGRAM_DATA_PATH` (host) se monta como `/app/database` dentro del contenedor → la app lo ve como `ENGRAM_DATA_DIR=/app/database`.

### Variables del cliente (máquina del desarrollador)

| Variable | Descripción |
|---|---|
| `ENGRAM_URL` | URL del servidor compartido |
| `ENGRAM_USER` | Identidad del desarrollador (namespacea sus memorias) |

---

## Verificación del servidor

```bash
# Health check
curl http://localhost:7437/health
# → {"status":"ok","service":"engram","version":"1.0.0"}

# Estadísticas
curl http://localhost:7437/stats
# → {"sessions":0,"observations":0,"prompts":0,"projects":[]}
```

Logs según la opción de deploy:

```bash
# Docker
docker compose -f docker/docker-compose.yml logs -f

# systemd
sudo journalctl -u engram -f
```

---

## Backup y mantenimiento

### Backup de la base de datos SQLite

```bash
# Docker
cp /mnt/Pool_8TB/engram_data/database/engram.db /backups/engram-$(date +%Y%m%d).db

# systemd
cp /data/engram/engram.db /backups/engram-$(date +%Y%m%d).db

# O usando el endpoint de export
curl http://localhost:7437/export -o /backups/engram-$(date +%Y%m%d).json
```

SQLite en modo WAL es seguro para copias en caliente.

### Logs

```bash
# Docker — últimas 100 líneas
docker compose -f docker/docker-compose.yml logs --tail=100

# systemd — últimas 100 líneas
sudo journalctl -u engram -n 100

# systemd — últimas 24 horas
sudo journalctl -u engram --since "24 hours ago"
```