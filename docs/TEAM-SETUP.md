[← Volver al README](../README.md)

# Guía de configuración para IT — engram-dotnet

> Esta guía está dirigida al equipo de IT o DevOps. Explica cómo deployar el servidor de memoria compartida y distribuir la configuración a los desarrolladores.

---

## Índice

- [¿Qué es esto?](#qué-es-esto)
- [Arquitectura del equipo](#arquitectura-del-equipo)
- [Paso 1 — Descargar el binario](#paso-1--descargar-el-binario)
- [Paso 2 — Configurar el servidor](#paso-2--configurar-el-servidor)
- [Paso 3 — Instalar como servicio systemd](#paso-3--instalar-como-servicio-systemd)
- [Paso 4 — Distribuir configuración a los desarrolladores](#paso-4--distribuir-configuración-a-los-desarrolladores)
- [Variables de entorno del servidor](#variables-de-entorno-del-servidor)
- [Verificación del servidor](#verificación-del-servidor)
- [Backup y mantenimiento](#backup-y-mantenimiento)

---

## ¿Qué es esto?

**engram-dotnet** es un servidor de memoria persistente para agentes de IA (Claude Code, Cursor, OpenCode, Gemini CLI, etc.). En lugar de que cada desarrollador tenga su propia instancia local, el equipo comparte una sola instancia centralizada en un servidor Linux.

Cada desarrollador tiene su identidad (`ENGRAM_USER`) que namespcea automáticamente sus memorias — no hay colisión de datos entre desarrolladores.

---

## Arquitectura del equipo

```
┌────────────────────────────────────────────┐
│              Servidor Linux                │
│                                            │
│   engram-dotnet (puerto 7437)              │
│   SQLite en /data/engram/engram.db         │
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

## Paso 1 — Descargar el binario

Descargar el release más reciente de GitHub:

```bash
# Crear directorio de instalación
sudo mkdir -p /opt/engram

# Descargar el binario linux-x64
curl -L https://github.com/efreet111/engram-dotnet/releases/latest/download/engram-linux-x64 \
     -o /opt/engram/engram

# Dar permisos de ejecución
sudo chmod +x /opt/engram/engram

# Verificar que funciona
/opt/engram/engram version
```

Alternativamente, compilar desde fuente:

```bash
git clone https://github.com/efreet111/engram-dotnet
cd engram-dotnet
dotnet publish src/Engram.Cli -c Release -r linux-x64 --self-contained -o /opt/engram/
```

---

## Paso 2 — Configurar el servidor

Crear el directorio de datos:

```bash
sudo mkdir -p /data/engram
sudo useradd -r -s /bin/false engram          # usuario sin login para el servicio
sudo chown engram:engram /data/engram
```

Crear el archivo de entorno en `/etc/engram/server.env`:

```bash
sudo mkdir -p /etc/engram

sudo tee /etc/engram/server.env > /dev/null <<'EOF'
ENGRAM_DATA_DIR=/data/engram
ENGRAM_PORT=7437

# Opcional — habilitar autenticación JWT
# ENGRAM_JWT_SECRET=cambia_esto_por_un_secreto_seguro

# Opcional — CORS si el dashboard web se accede desde otro origen
# ENGRAM_CORS_ORIGINS=http://dashboard.interno

# Opcional — sync git para backup distribuido
# ENGRAM_SYNC_REPO=git@github.com:tu-org/engram-sync.git
EOF

sudo chmod 640 /etc/engram/server.env
sudo chown root:engram /etc/engram/server.env
```

---

## Paso 3 — Instalar como servicio systemd

Crear el archivo de servicio en `/etc/systemd/system/engram.service`:

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

# Habilitar e iniciar el servicio
sudo systemctl daemon-reload
sudo systemctl enable engram
sudo systemctl start engram

# Verificar estado
sudo systemctl status engram
```

---

## Paso 4 — Distribuir configuración a los desarrolladores

El repositorio incluye archivos de configuración en `config/` listos para distribuir.

### Estructura del directorio `config/`

```
config/
├── cursor/
│   ├── mcp.json                    ← Configuración MCP para Cursor
│   ├── rules/
│   │   ├── engram.mdc              ← Reglas de memoria (alwaysApply: true)
│   │   └── sdd-orchestrator.md    ← Orquestador SDD
│   └── agents/
│       ├── sdd-apply.md            ← Agente: implementar cambios
│       ├── sdd-archive.md          ← Agente: archivar cambio
│       ├── sdd-design.md           ← Agente: diseño técnico
│       ├── sdd-explore.md          ← Agente: investigar codebase
│       ├── sdd-init.md             ← Agente: inicializar SDD
│       ├── sdd-propose.md          ← Agente: propuesta de cambio
│       ├── sdd-spec.md             ← Agente: especificaciones
│       ├── sdd-tasks.md            ← Agente: desglose de tareas
│       └── sdd-verify.md           ← Agente: validar implementación
└── vscode/
    ├── mcp.json                    ← Configuración MCP para VS Code
    └── prompts/
        └── engram.instructions.md  ← Instrucciones de memoria (GitHub Copilot)
```

### Variables de entorno por desarrollador

Cada desarrollador necesita estas variables en su `.bashrc` / `.zshrc` / perfil de sistema:

```bash
# URL del servidor compartido
export ENGRAM_URL=http://10.0.0.5:7437        # ← reemplazar con IP/hostname del servidor

# Identidad del desarrollador (namespaces las memorias en el servidor)
export ENGRAM_USER=nombre.apellido            # ← personalizar por desarrollador
```

> **Importante**: si `ENGRAM_JWT_SECRET` está configurado en el servidor, también hay que setear `ENGRAM_JWT_TOKEN` en la máquina del desarrollador con el JWT válido.

### Instrucciones para Cursor

1. Copiar `config/cursor/mcp.json` a `~/.cursor/mcp.json`
2. Copiar el contenido de `config/cursor/rules/` a `~/.cursor/rules/`
3. Copiar el contenido de `config/cursor/agents/` a `~/.cursor/agents/`
4. Reiniciar Cursor

### Instrucciones para VS Code

1. Copiar `config/vscode/mcp.json` a `~/.vscode/mcp.json` (o al directorio del workspace `.vscode/mcp.json`)
2. Copiar `config/vscode/prompts/engram.instructions.md` a `~/.github/copilot-instructions.md`
3. Reiniciar VS Code

> Consultar la [guía para el desarrollador](DEVELOPER-SETUP.md) para las instrucciones detalladas por herramienta.

---

## Variables de entorno del servidor

| Variable | Default | Descripción |
|---|---|---|
| `ENGRAM_DATA_DIR` | `~/.engram` | Directorio de datos (SQLite + sync chunks) |
| `ENGRAM_PORT` | `7437` | Puerto del servidor HTTP |
| `ENGRAM_JWT_SECRET` | — | Si se setea, habilita auth JWT en todos los endpoints (excepto `/health`) |
| `ENGRAM_CORS_ORIGINS` | — | Orígenes CORS permitidos, separados por coma |
| `ENGRAM_SYNC_REPO` | — | URL del repo git para sync distribuido |
| `ENGRAM_SYNC_DIR` | `~/.engram/sync` | Directorio local de chunks de sync |

> Las variables `ENGRAM_URL` y `ENGRAM_USER` son del **cliente** (máquina del desarrollador), NO del servidor.

---

## Verificación del servidor

```bash
# Health check
curl http://localhost:7437/health
# → {"status":"ok","service":"engram","version":"1.0.0"}

# Estadísticas
curl http://localhost:7437/stats
# → {"sessions":0,"observations":0,"prompts":0,"projects":[]}

# Ver logs del servicio
sudo journalctl -u engram -f
```

---

## Backup y mantenimiento

### Backup de la base de datos SQLite

```bash
# Backup diario recomendado (SQLite con WAL — el archivo está siempre en estado consistente)
cp /data/engram/engram.db /backups/engram-$(date +%Y%m%d).db

# O usando el endpoint de export de engram
curl http://localhost:7437/export -o /backups/engram-$(date +%Y%m%d).json
```

### Actualizar el binario

```bash
# Parar el servicio
sudo systemctl stop engram

# Reemplazar el binario
sudo cp nuevo-engram /opt/engram/engram
sudo chmod +x /opt/engram/engram

# Reiniciar
sudo systemctl start engram
sudo systemctl status engram
```

### Logs

```bash
# Últimas 100 líneas de log
sudo journalctl -u engram -n 100

# Seguir logs en tiempo real
sudo journalctl -u engram -f

# Logs de las últimas 24 horas
sudo journalctl -u engram --since "24 hours ago"
```
