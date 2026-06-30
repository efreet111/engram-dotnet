#Requires -Version 5.1
<#
.SYNOPSIS
  Wizard de configuración MCP para engram-dotnet (agnóstico de editor).

.DESCRIPTION
  Tras clonar el repo: elige modo local o offline-first sync,
  compila el binario (opcional) y escribe mcp.json para Cursor u otro cliente.
#>
$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    $here = $PSScriptRoot
    if (Test-Path (Join-Path $here "..\src\Engram.Cli\Engram.Cli.csproj")) {
        return (Resolve-Path (Join-Path $here "..")).Path
    }
    throw "Ejecutá este script desde el repo engram-dotnet (scripts\setup.ps1)."
}

function Expand-EngramCommand {
    param([string]$RepoRoot)
    $candidates = @(
        (Join-Path $RepoRoot "dist\win-x64-fixed\engram.exe"),
        (Join-Path $RepoRoot "dist\win-x64\engram.exe"),
        (Join-Path $RepoRoot "dist\engram.exe")
    )
    foreach ($p in $candidates) {
        if (Test-Path $p) { return $p }
    }
    $cmd = Get-Command engram -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return "engram"
}

function New-EditorWrapper {
    param(
        [string]$Format,
        [hashtable]$EngramServer
    )
    switch ($Format) {
        "vscode" { return @{ servers = @{ engram = $EngramServer } } }
        "opencode" {
            return @{
                mcp = @{
                    engram = @{
                        type        = "local"
                        enabled     = $true
                        command     = @($EngramServer.command, "mcp")
                        environment = $EngramServer.env
                    }
                }
            }
        }
        default { return @{ mcpServers = @{ engram = $EngramServer } } }
    }
}

function Write-JsonFile {
    param([string]$Path, [object]$Obj)
    $dir = Split-Path $Path -Parent
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    ($Obj | ConvertTo-Json -Depth 10) + "`n" | Set-Content -Path $Path -Encoding UTF8
}

function Write-AllGeneratedConfigs {
    param(
        [string]$GenDir,
        [hashtable]$EngramServer,
        [string]$ModeLabel,
        [hashtable]$CopyTargets
    )
    if (-not (Test-Path $GenDir)) { New-Item -ItemType Directory -Path $GenDir -Force | Out-Null }

    Write-JsonFile -Path (Join-Path $GenDir "cursor.mcp.json") -Obj (New-EditorWrapper "cursor" $EngramServer)
    Write-JsonFile -Path (Join-Path $GenDir "claude-desktop.mcp.json") -Obj (New-EditorWrapper "cursor" $EngramServer)
    Write-JsonFile -Path (Join-Path $GenDir "vscode.mcp.json") -Obj (New-EditorWrapper "vscode" $EngramServer)
    Write-JsonFile -Path (Join-Path $GenDir "opencode.mcp.json") -Obj (New-EditorWrapper "opencode" $EngramServer)
    Write-JsonFile -Path (Join-Path $GenDir "engram.server.json") -Obj $EngramServer

    $readme = @"
# Configuraciones MCP generadas — engram-dotnet

Modo: **$ModeLabel**
Generado: $(Get-Date -Format "yyyy-MM-dd HH:mm")

## Archivos

| Archivo | Copiar a |
|---------|----------|
| ``cursor.mcp.json`` | ``$($CopyTargets.Cursor)`` |
| ``claude-desktop.mcp.json`` | ``$($CopyTargets.Claude)`` |
| ``vscode.mcp.json`` | Extensión MCP de VS Code (clave ``servers``) |
| ``opencode.mcp.json`` | ``$($CopyTargets.OpenCode)`` (revisá formato en tu versión) |
| ``engram.server.json`` | Solo el bloque ``engram`` si el editor pide un fragmento |

## Pasos

1. Elegí el archivo del editor que uses **hoy**.
2. Copiá el contenido al path de la tabla (o fusioná con tu JSON existente).
3. Recargá el editor (Cursor: Developer → Reload Window).
4. Si cambiás de IDE mañana, repetí con otro archivo de esta carpeta.

Guía completa: ``config/mcp/INSTALL.md``

"@
    $readme | Set-Content -Path (Join-Path $GenDir "README.md") -Encoding UTF8
    Write-Host "  Generados en: $GenDir" -ForegroundColor Green
}

function Write-McpConfig {
    param(
        [string]$Path,
        [hashtable]$EngramServer
    )
    $dir = Split-Path $Path -Parent
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

    $mcpServers = @{}
    if (Test-Path $Path) {
        $existing = Get-Content $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($existing.mcpServers) {
            $existing.mcpServers.PSObject.Properties | ForEach-Object {
                if ($_.Name -ne "engram") { $mcpServers[$_.Name] = $_.Value }
            }
        }
    }
    $mcpServers["engram"] = $EngramServer
    (@{ mcpServers = $mcpServers } | ConvertTo-Json -Depth 10) + "`n" | Set-Content -Path $Path -Encoding UTF8
    Write-Host "  Escrito: $Path" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== engram-dotnet — configuración MCP ===" -ForegroundColor Cyan
Write-Host ""

$repo = Get-RepoRoot
Set-Location $repo

# --- Modo ---
Write-Host "Modo de uso:" -ForegroundColor Yellow
Write-Host "  [1] Solo local (SQLite, sin sync con servidor)"
Write-Host "  [2] Offline-first sync (SQLite local + servidor remoto)"
Write-Host ""
$mode = Read-Host "Elegí 1 o 2 (default 1)"
if ([string]::IsNullOrWhiteSpace($mode)) { $mode = "1" }

$syncEnabled = $mode -eq "2"
$serverUrl = ""

if ($syncEnabled) {
    $defaultUrl = "http://192.168.0.178:7437"
    $serverUrl = Read-Host "URL del servidor ENGRAM_SERVER_URL [$defaultUrl]"
    if ([string]::IsNullOrWhiteSpace($serverUrl)) { $serverUrl = $defaultUrl }
    $serverUrl = $serverUrl.TrimEnd('/')
    try {
        $r = Invoke-WebRequest -Uri "$serverUrl/health" -UseBasicParsing -TimeoutSec 5
        Write-Host "  Servidor OK: $($r.StatusCode)" -ForegroundColor Green
    } catch {
        Write-Host "  Advertencia: no se pudo contactar $serverUrl/health — revisá red o Docker." -ForegroundColor DarkYellow
    }
}

# --- Usuario y datos ---
$userDefault = "$env:USERNAME@local.dev"
$engramUser = Read-Host "ENGRAM_USER (identidad) [$userDefault]"
if ([string]::IsNullOrWhiteSpace($engramUser)) { $engramUser = $userDefault }

$dataDefault = Join-Path $env:USERPROFILE ".engram"
$dataDir = Read-Host "ENGRAM_DATA_DIR [$dataDefault]"
if ([string]::IsNullOrWhiteSpace($dataDir)) { $dataDir = $dataDefault }
if (-not (Test-Path $dataDir)) { New-Item -ItemType Directory -Path $dataDir -Force | Out-Null }

# --- Binario ---
Write-Host ""
$build = Read-Host "¿Compilar engram ahora? (S/n)"
if ($build -match '^[sSyY]' -or [string]::IsNullOrWhiteSpace($build)) {
    Write-Host "  Compilando (win-x64)..." -ForegroundColor Gray
    dotnet publish src\Engram.Cli\Engram.Cli.csproj -c Release -r win-x64 --self-contained false -o dist\win-x64-fixed --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish falló." }
}

$engramCmd = Expand-EngramCommand -RepoRoot $repo
Write-Host "  Comando MCP: $engramCmd" -ForegroundColor Gray

$envBlock = @{
    ENGRAM_DATA_DIR     = $dataDir
    ENGRAM_USER         = $engramUser
    ENGRAM_SYNC_ENABLED = if ($syncEnabled) { "true" } else { "false" }
}
if ($syncEnabled) { $envBlock["ENGRAM_SERVER_URL"] = $serverUrl }

$server = @{
    type    = "stdio"
    command = $engramCmd
    args    = @("mcp")
    env     = $envBlock
}

# --- Salida: todos los editores en config/mcp/generated/ ---
$genDir = Join-Path $repo "config\mcp\generated"
$modeLabel = if ($syncEnabled) { "offline-first sync" } else { "solo local" }
$copyTargets = @{
    Cursor   = Join-Path $env:USERPROFILE ".cursor\mcp.json"
    Claude   = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
    OpenCode = Join-Path $env:USERPROFILE ".config\opencode\opencode.json"
}
Write-AllGeneratedConfigs -GenDir $genDir -EngramServer $server -ModeLabel $modeLabel -CopyTargets $copyTargets

# --- Editor (instalar además en un path del sistema) ---
Write-Host ""
Write-Host "¿Instalar también en un editor ahora?" -ForegroundColor Yellow
Write-Host "  [1] No — solo usar config/mcp/generated/ (varios editores, copiar a mano)"
Write-Host "  [2] Sí — Cursor  ($($copyTargets.Cursor))"
Write-Host "  [3] Sí — Claude Desktop"
Write-Host "  [4] Mostrar JSON en pantalla"
Write-Host ""
$editor = Read-Host "Elegí 1-4 (default 1)"
if ([string]::IsNullOrWhiteSpace($editor)) { $editor = "1" }

switch ($editor) {
    "2" { Write-McpConfig -Path $copyTargets.Cursor -EngramServer $server }
    "3" { Write-McpConfig -Path $copyTargets.Claude -EngramServer $server }
    "4" { New-EditorWrapper "cursor" $server | ConvertTo-Json -Depth 10 }
}

# Copia de referencia en ~/.engram
$refPath = Join-Path $dataDir "mcp.config.json"
(@{ mcpServers = @{ engram = $server } } | ConvertTo-Json -Depth 10) + "`n" | Set-Content -Path $refPath -Encoding UTF8
Write-Host "  Copia de referencia: $refPath" -ForegroundColor Gray

Write-Host ""
Write-Host "Listo. Recargá tu editor (Cursor: Developer → Reload Window)." -ForegroundColor Cyan
if ($syncEnabled) {
    Write-Host "Sync: tras el primer mem_save, enrollá el proyecto si el push queda bloqueado:"
    Write-Host "  docs/SYNC-SETUP.md — sección Client Setup"
}
Write-Host ""
