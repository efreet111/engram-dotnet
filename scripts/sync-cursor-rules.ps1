# Sync project Cursor rules: config/cursor/rules -> .cursor/rules
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$src = Join-Path $root "config\cursor\rules"
$dst = Join-Path $root ".cursor\rules"

if (-not (Test-Path $src)) {
    Write-Error "Missing: $src"
}

New-Item -ItemType Directory -Force -Path $dst | Out-Null
Copy-Item -Path (Join-Path $src "*.mdc") -Destination $dst -Force
Write-Host "Synced rules to $dst"
Get-ChildItem $dst -Filter "*.mdc" | ForEach-Object { Write-Host "  - $($_.Name)" }
