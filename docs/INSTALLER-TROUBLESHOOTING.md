# Troubleshooting — Instalación y configuración de engram-dotnet

Esta guía cubre problemas comunes del instalador (FlowForge), los scripts de post-instalación y el wizard MCP.

---

## 1. FlowForge Installer

### `flowforge: command not found` tras instalar

**Causa:** El binary no está en `PATH`.

**Solución:**
```bash
# Linux/macOS — verificar que está en PATH
echo $PATH | tr ':' '\n' | grep -i flowforge

# Agregar manualmente si es necesario
export PATH="$PATH:$HOME/.local/bin"  # o donde lo haya instalado

# Windows — verificar en PowerShell
Get-Command flowforge
```

---

### `flowforge install` falla al descargar engram-dotnet

**Causa:** Error de red o URL del release no accesible.

**Solución:**
1. Verificá conectividad: `curl -fsSL https://github.com/efreet111/engram-dotnet/releases/latest`
2. Si usás proxy corporativo, configurá `HTTPS_PROXY` o `HTTP_PROXY`
3. Instalación manual: descargá el binario desde [GitHub Releases](https://github.com/efreet111/engram-dotnet/releases/latest) y luego ejecutá `post-install.sh`

---

### `flowforge uninstall` no remueve todo

**Causa:** Archivos criados fuera del directorio de instalación.

**Solución:**
```bash
# Remover binarios manualmente
rm -f ~/.local/bin/flowforge ~/.local/bin/engram  # Linux/macOS

# Remover datos y configuración
rm -rf ~/.engram
rm -f ~/.config/flowforge*  # si existe

# Remover MCP configs de editores
rm -f ~/.cursor/mcp.json
rm -f ~/.config/opencode/opencode.json
```

---

## 2. Post-install Scripts (`post-install.sh` / `post-install.ps1`)

### `python3: command not found` (Linux/macOS)

**Causa:** `post-install.sh` usa Python para manipular JSON.

**Solución — instalar Python:**
```bash
# Ubuntu/Debian
sudo apt update && sudo apt install python3

# macOS
brew install python3

# Fedora
sudo dnf install python3
```

**Alternativa — instalación manual de config.json:**
```bash
mkdir -p ~/.engram
cat > ~/.engram/config.json << 'EOF'
{
  "channel": "stable",
  "auto_update": false,
  "flowdoc": { "enabled": true },
  "components": {
    "engram_dotnet": {
      "installed": true,
      "version": "TU_VERSION",
      "binary": "/ruta/al/engram",
      "registered_at": "2026-01-01T00:00:00Z"
    }
  }
}
EOF
```

---

### `engram not found in PATH`

**Causa:** El binario no está instalado o no está en el PATH.

**Solución — pasar la ruta explícitamente:**
```bash
post-install.sh --binary /usr/local/bin/engram
post-install.sh --binary /home/usuario/.local/bin/engram
```

Verificá que el binario existe y es ejecutable:
```bash
ls -la /ruta/al/engram
chmod +x /ruta/al/engram
```

---

### `engram --version` no devuelve versión

**Causa:** El binario está corrupto o no es el binario correcto.

**Solución:**
```bash
# Verificar que es el binario de engram
engram --version

# Si no responde, reinstalar
# Linux/macOS
curl -fsSL https://github.com/efreet111/engram-dotnet/releases/latest/download/engram-linux-x64.tar.gz | tar -xz -C /tmp
mv /tmp/engram ~/.local/bin/engram
chmod +x ~/.local/bin/engram

# Windows — descargar desde releases y colocar en el path
```

---

### `config.json` corrupto o malformado

**Causa:** El script encontró un archivo JSON inválido.

**Solución — hacer backup y regenerar:**
```bash
# Backup
cp ~/.engram/config.json ~/.engram/config.json.bak

# Regenerar desde cero
rm ~/.engram/config.json
post-install.sh --binary /ruta/al/engram --engram-version 0.4.0
```

**Verificar contenido:**
```bash
cat ~/.engram/config.json | python3 -m json.tool  # debe validar sin errores
```

---

### Permiso denegado al escribir `~/.engram/config.json`

**Causa:** Sin permisos de escritura en `~/.engram/`.

**Solución:**
```bash
# Crear directorio con permisos correctos
mkdir -p ~/.engram
chmod 755 ~/.engram

# Si el archivo ya existe
chmod 644 ~/.engram/config.json
```

---

## 3. Wizard MCP (`setup.sh` / `setup.ps1`)

### `dotnet: command not found`

**Causa:** No tenés .NET SDK instalado. El wizard intenta compilar el proyecto.

**Solución:**
1. [Instalar .NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
2. O elegir "No" cuando pregunta si querés compilar, y usar el binario ya publicado

```powershell
# Cuando pregunta:
# "¿Compilar engram ahora? (S/n)"
# Respondé: n
```

---

### Health check falla al probar servidor sync

```
Advertencia: health check falló
```

**Causa:** El servidor no está corriendo o la URL es incorrecta.

**Solución:**
```bash
# Verificar que el servidor está arriba
curl http://localhost:7437/health

# O si es otra URL
curl http://tu-servidor:7437/health

# Si el servidor está detrás de VPN, verificar conectividad
ping tu-servidor
```

---

### `config/mcp/generated/` está vacío

**Causa:** El wizard no tuvo permisos de escritura.

**Solución:**
```bash
# Crear el directorio manualmente
mkdir -p config/mcp/generated
chmod 755 config/mcp/generated

# Volver a correr el wizard
./scripts/setup.sh
```

---

### VS Code no reconoce el MCP

**Causa:** La extensión MCP de VS Code usa un formato de JSON diferente (`servers` en vez de `mcpServers`).

**Solución:** Asegurate de usar `config/mcp/generated/vscode.mcp.json` (ya tiene el formato correcto). Si tu extensión usa otro formato, revisá la documentación de tu extensión MCP específica.

---

### OpenCode no levanta el MCP

**Causa:** OpenCode usa clave `mcp` en vez de `mcpServers`.

**Solución:** Usá `config/mcp/generated/opencode.mcp.json`. Si seguís teniendo problemas, verificá que tu versión de OpenCode soporte el formato `environment` en lugar de `env`.

---

### Cursor/Claude Desktop no muestra tools de engram

**Solución:**
1. Verificá que el archivo JSON está en la ubicación correcta:
   - Cursor: `~/.cursor/mcp.json`
   - Claude Desktop: `~/.config/Claude/claude_desktop_config.json` (Linux) o `%APPDATA%\Claude\claude_desktop_config.json` (Windows)

2. Recargá el editor:
   - Cursor: `Developer: Reload Window`
   - Claude Desktop: Reiniciar la app

3. Verificá que no haya errores en la config:
```bash
# En terminal, probá que el MCP responde
/路径/到/engram mcp

# Debe quedarse esperando sin cerrarse ni dar errores
# Ctrl+C para salir
```

---

## 4. Problemas de sync tras instalación

### Push queda bloqueado — "project not enrolled"

**Síntoma:**
```
Push failed: project "mi-proyecto" is not enrolled on the server
```

**Solución — enrollar el proyecto:**
```bash
engram sync enroll --project mi-proyecto

# O manualmente con curl
curl -X POST http://localhost:7437/sync/enroll \
  -H "X-Engram-User: tu-usuario@equipo.com" \
  -H "Content-Type: application/json" \
  -d '{"project":"mi-proyecto"}'
```

Ver: [docs/SYNC-SETUP.md](SYNC-SETUP.md)

---

### `pending_push` no baja de cero

**Solución:**
```bash
# Ver estado del sync
engram sync status --json | jq

# Forzar un ciclo de sync
engram sync cycle

# Si el servidor está caído, las memorias quedan en cola localmente
# Se sincronizan cuando el servidor vuelve a estar disponible
```

---

## 5. Verificación post-instalación

```bash
# 1. Verificar que engram responde
engram --version

# 2. Verificar doctor
engram doctor

# 3. Probar el MCP (debe quedar esperando)
engram mcp

# 4. Verificar config.json
cat ~/.engram/config.json | python3 -m json.tool

# 5. Verificar que SyncManager corre (si sync está habilitado)
engram sync status
```

---

## Referencias

- [docs/SETUP-WIZARD.md](SETUP-WIZARD.md) — guía de configuración MCP
- [docs/SYNC-SETUP.md](SYNC-SETUP.md) — setup de sync
- [config/mcp/INSTALL.md](../config/mcp/INSTALL.md) — instalación MCP por editor
- [ADR-004 — Post-install registration](../docs/architecture/adr/ADR-004-post-install-registration.md) — cómo funcionan los scripts post-install
- [ADR-005 — Headless mode + Native libs + MCP config](https://github.com/efreet111/FlowForge/blob/main/docs/decisions/ADR-005-installer-headless-native-libs.md) — `--yes` headless, `e_sqlite3.so`/`.dll`, `ENGRAM_SERVER_URL` en MCP config, sin `--tools=agent`
- [ADR-006 — OpenCode MCP: `type: "local"` y `enabled: true`](https://github.com/efreet111/FlowForge/blob/main/docs/decisions/ADR-006-opencode-mcp-config.md) — OpenCode requiere transport local y flag enabled
