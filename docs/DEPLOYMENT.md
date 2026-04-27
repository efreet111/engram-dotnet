[← Volver al README](../README.md)

# Deployment — engram-dotnet

---

## Opción 1 — TrueNAS SCALE con PostgreSQL externo

### Prerequisitos

- TrueNAS SCALE con Docker habilitado
- PostgreSQL instalado como App en TrueNAS (puerto 5432 mapeado al host)
- Git disponible en el sistema
- Acceso SSH al servidor

### Pasos

#### 1. Clonar el repositorio

```bash
git clone https://github.com/efreet111/engram-dotnet.git /mnt/Pool_8TB/engram_data
cd /mnt/Pool_8TB/engram_data
```

#### 2. Configurar variables de entorno

```bash
cd docker
cp .env.example .env
nano .env
```

Completar con las credenciales de tu PostgreSQL de TrueNAS:

```env
# PostgreSQL (TrueNAS Apps)
ENGRAM_PG_HOST=host.docker.internal    # o la IP del TrueNAS
ENGRAM_PG_PORT=5432
ENGRAM_PG_DATABASE=postgres            # o la que configuraste en la app
ENGRAM_PG_USER=postgres                # o la que configuraste en la app
ENGRAM_PG_PASSWORD=tu_password_aqui

# Backend
ENGRAM_DB_TYPE=postgres

# Opcional
# ENGRAM_JWT_SECRET=secreto_seguro
# ENGRAM_CORS_ORIGINS=http://truenas.local:8080
```

#### 3. Construir y levantar

```bash
cd /mnt/Pool_8TB/engram_data/docker
sudo docker compose up -d --build
```

#### 4. Verificar

```bash
# Container corriendo
sudo docker ps --filter name=^engram$

# Logs de arranque
sudo docker compose logs engram 2>&1 | grep "starting"
# → [engram] starting HTTP server on :7437 (PostgreSQL)

# Health check
curl http://localhost:7437/health
# → {"status":"ok","service":"engram","version":"1.1.0"}

# Stats
curl http://localhost:7437/stats
```

### Actualizar el servicio

```bash
cd /mnt/Pool_8TB/engram_data
git pull
cd docker
sudo docker compose down
sudo docker compose up -d --build
```

### Migrar datos desde Engram Go (SQLite)

Si venís del Engram original en Go:

```bash
# 1. Exportar desde Go (en tu máquina local)
curl -s http://127.0.0.1:7437/export -o /tmp/engram-migration.json

# 2. Importar en el servidor .NET (desde tu máquina)
curl -X POST http://192.168.0.178:7437/import \
  -H "Content-Type: application/json" \
  -d @/tmp/engram-migration.json

# 3. Verificar en el servidor
curl http://192.168.0.178:7437/stats
```

---

## Opción 2 — Linux tradicional con systemd

### Requisitos del servidor

- Linux x64 (Ubuntu 20.04+, Debian 11+, RHEL 8+, etc.)
- Acceso root o sudo
- PostgreSQL 15+ instalado (o usar SQLite como fallback)

### Instalación del binario

```bash
# Descargar binario precompilado
sudo mkdir -p /opt/engram
curl -L https://github.com/efreet111/engram-dotnet/releases/latest/download/engram-linux-x64 -o /opt/engram/engram
sudo chmod +x /opt/engram/engram

# Verificar
/opt/engram/engram version
```

### Configuración con PostgreSQL

```bash
sudo mkdir -p /etc/engram
sudo tee /etc/engram/server.env > /dev/null <<'EOF'
ENGRAM_DB_TYPE=postgres
ENGRAM_PG_CONNECTION=Host=localhost;Database=engram;Username=engram;Password=secreto
ENGRAM_PORT=7437
EOF
sudo chmod 640 /etc/engram/server.env
```

### Systemd

```ini
[Unit]
Description=Engram — Servidor de memoria para IA
After=network.target postgresql.service

[Service]
Type=simple
User=engram
Group=engram
ExecStart=/opt/engram/engram serve
EnvironmentFile=/etc/engram/server.env
Restart=on-failure
RestartSec=5s

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable engram
sudo systemctl start engram
sudo systemctl status engram
```

---

## Configuración de clientes MCP

### VS Code (`.vscode/mcp.json` del proyecto)

```json
{
  "servers": {
    "engram-team": {
      "command": "/home/gantz/.local/bin/engram-dotnet",
      "args": ["mcp"],
      "env": {
        "ENGRAM_URL": "http://192.168.0.178:7437",
        "ENGRAM_USER": "victor.silgado"
      },
      "autoApprove": [
        "mem_save", "mem_search", "mem_context",
        "mem_get_observation", "mem_session_summary",
        "mem_update", "mem_suggest_topic_key",
        "mem_capture_passive", "mem_session_start", "mem_session_end"
      ]
    }
  }
}
```

### OpenCode Go (`~/.config/opencode/go.json` o similar)

En la sección de MCP de tu config de OpenCode:

```json
{
  "mcp": {
    "servers": {
      "engram-team": {
        "command": "/home/gantz/.local/bin/engram-dotnet",
        "args": ["mcp"],
        "env": {
          "ENGRAM_URL": "http://192.168.0.178:7437",
          "ENGRAM_USER": "victor.silgado"
        }
      }
    }
  }
}
```

### Variables de entorno del cliente

| Variable | Valor | Propósito |
|----------|-------|-----------|
| `ENGRAM_URL` | `http://192.168.0.178:7437` | URL del servidor centralizado |
| `ENGRAM_USER` | `victor.silgado` | Identidad del desarrollador (namespacing) |

> **Nota**: El binario `.NET` no soporta `--tools=agent` como el Go. Usa solo `mcp` sin flags adicionales.

---

## Monitoreo básico

```bash
# Ver servicio
sudo docker ps --filter name=engram
sudo docker compose logs -f

# Health check
curl http://localhost:7437/health

# Stats del servidor
curl http://localhost:7437/stats

# Buscar memorias
curl "http://localhost:7437/search?q=postgresql&limit=5"

# Contexto reciente
curl "http://localhost:7437/context?project=engram-dotnet"
```

---

## Backup de datos

### PostgreSQL (desde TrueNAS)

```bash
# Export completo via API
curl http://localhost:7437/export -o /backup/engram-$(date +%Y%m%d).json

# O pg_dump directo (si tenés acceso a la DB)
pg_dump -h 192.168.0.178 -p 5432 -U postgres engram > /backup/engram-$(date +%Y%m%d).sql
```

### Importar backup

```bash
curl -X POST http://localhost:7437/import \
  -H "Content-Type: application/json" \
  -d @/backup/engram-20260427.json
```
