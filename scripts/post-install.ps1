#Requires -Version 5.1
<#
.SYNOPSIS
  Registra engram-dotnet en ~/.engram/config.json tras la instalación.

.DESCRIPTION
  Invocado por el FlowForge installer después de colocar el binario.
  También puede correrse manualmente. Idempotente.

.PARAMETER Binary
  Ruta al binario engram. Si no se especifica, se busca en PATH.

.PARAMETER EngramVersion
  Versión de engram-dotnet. Si no se especifica, se obtiene corriendo
  'engram version'.

.EXAMPLE
  .\post-install.ps1
  .\post-install.ps1 -Binary "C:\Programs\FlowForge\engram.exe"
  .\post-install.ps1 -Binary "C:\Programs\FlowForge\engram.exe" -EngramVersion "0.3.0"
#>
param(
    [string] $Binary        = "",
    [string] $EngramVersion = ""
)
$ErrorActionPreference = "Stop"

# ── Buscar binario ───────────────────────────────────────────────────────────

if (-not $Binary) {
    $cmd = Get-Command engram -ErrorAction SilentlyContinue
    if ($cmd) {
        $Binary = $cmd.Source
    } else {
        Write-Error "engram no encontrado en PATH. Pasá -Binary C:\ruta\al\binario."
        exit 1
    }
}

if (-not (Test-Path $Binary)) {
    Write-Error "'$Binary' no existe."
    exit 1
}

# ── Obtener versión ──────────────────────────────────────────────────────────

if (-not $EngramVersion) {
    try {
        $raw = & $Binary version 2>$null
        # "engram 0.3.0" → "0.3.0"
        $EngramVersion = ($raw -replace "^engram\s+", "").Trim()
    } catch {
        Write-Error "No se pudo obtener la versión del binario: $_"
        exit 1
    }
}

if (-not $EngramVersion) {
    Write-Error "Versión vacía. Verificá que el binario responde a 'engram version'."
    exit 1
}

# ── Directorios y archivos ───────────────────────────────────────────────────

$EngramDir  = Join-Path $env:USERPROFILE ".engram"
$ConfigFile = Join-Path $EngramDir "config.json"
$LogFile    = Join-Path $EngramDir "install.log"

if (-not (Test-Path $EngramDir)) {
    New-Item -ItemType Directory -Path $EngramDir -Force | Out-Null
}

function Write-Log {
    param([string]$Level, [string]$Message)
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    "[$ts] [$Level] $Message" | Add-Content -Path $LogFile -Encoding UTF8
}

Write-Log "INFO" "post-install.ps1: engram $EngramVersion en $Binary"

# ── Actualizar config.json (idempotente) ─────────────────────────────────────

$cfg = [ordered]@{}
if (Test-Path $ConfigFile) {
    try {
        $cfg = Get-Content $ConfigFile -Encoding UTF8 -Raw | ConvertFrom-Json -AsHashtable
    } catch {
        $cfg = [ordered]@{}
    }
}

if (-not $cfg.ContainsKey("channel"))    { $cfg["channel"]    = "stable" }
if (-not $cfg.ContainsKey("auto_update")){ $cfg["auto_update"] = $false  }
if (-not $cfg.ContainsKey("flowdoc"))    { $cfg["flowdoc"]     = @{ enabled = $true } }
if (-not $cfg.ContainsKey("components")) { $cfg["components"]  = [ordered]@{} }

$cfg["components"]["engram_dotnet"] = [ordered]@{
    installed     = $true
    version       = $EngramVersion
    binary        = $Binary
    registered_at = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
}

($cfg | ConvertTo-Json -Depth 10) + "`n" | Set-Content -Path $ConfigFile -Encoding UTF8

Write-Log "INFO" "post-install.ps1: config.json actualizado correctamente"
Write-Host "engram-dotnet $EngramVersion registrado en $ConfigFile"
