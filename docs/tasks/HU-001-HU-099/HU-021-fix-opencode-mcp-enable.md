# HU-021 — Fix MCP Config: `enabled: true` faltante en `mcp.engram`

**As**: Usuario de OpenCode con perfil `local`
**I want**: Que al instalar con perfil `local`, la configuración MCP de OpenCode incluya `enabled: true` dentro de `mcp.engram`
**To**: Evitar el error "missing key mcp.engram.enable" al iniciar OpenCode

---

## Acceptance Criteria

### Root cause

La config activa `~/.config/opencode/opencode.json` tiene:
```json
"mcp": {
  "engram": {
    "command": ["engram", "mcp", "--tools=agent"],
    "type": "local"
    // ← falta "enabled": true
  }
}
```

Y debería tener:
```json
"mcp": {
  "engram": {
    "enabled": true,
    "command": ["engram", "mcp", "--tools=agent"],
    "type": "local"
  }
}
```

### Fix en generator

- [x] `scripts/setup.sh` líneas 92-101 — verificar que genera `"enabled": true` (ya lo hace)
- [x] `scripts/setup.ps1` líneas 42-52 — verificar que genera `"enabled": true` (ya lo hace)
- [x] `config/mcp/editors/opencode.mcp.json` — verificar que tiene `"enabled": true` (ya lo tiene)

### Fix en install.sh (modo desktop/late-binding)

- [x] `scripts/install.sh` usa `write_mcp_config()` para generar la config — NO incluía `enabled: true` ni `type: local` para opencode; corregido
- [x] Para `opencode` específicamente: el bloque `mcp.engram` ahora incluye `enabled: true`, `type: local` y `environment` (antes usaba `env`, que OpenCode ignora)

### Fix en config activa del usuario

- [x] La HU no puede regenerar la config del usuario automáticamente (archivo es del usuario)
- [x] Documentar el fix manual en `docs/INSTALLER-TROUBLESHOOTING.md`: el usuario debe editar `~/.config/opencode/opencode.json` y agregar `"enabled": true` dentro de `mcp.engram`
- [x] O re-run `scripts/setup.sh` y copiar el resultado

### Tests

- [x] `scripts/test-install-wizard.sh` — agregar aserción: verificar que el JSON generado para opencode contiene `"enabled": true`, `"type": "local"` y `"environment"`

---

## Tasks (Implementation)

- [x] Auditar `scripts/setup.sh` — confirmar que línea ~96 tiene `"enabled": true` para opencode
- [x] Auditar `scripts/setup.ps1` — confirmar que línea ~48 tiene `"enabled": true` para opencode
- [x] Auditar `config/mcp/editors/opencode.mcp.json` — confirmar que línea 5 tiene `"enabled": true`
- [x] Auditar `scripts/install.sh` función `write_mcp_config()` — confirmar que pasa `enabled: true` al bloque engram para opencode (NO lo hacía; corregido)
- [x] Buscar si hay otro generator que NO incluya `enabled: true`: `grep -r "opencode\|mcp.*engram" scripts/ --include="*.sh" -A5`
- [x] Editar `docs/INSTALLER-TROUBLESHOOTING.md` — agregar sección "OpenCode missing mcp.engram.enable" con fix manual
- [x] Correr `scripts/test-install-wizard.sh` — verificar que pasa

---

## Notes

- **`install.sh` SÍ estaba roto**: la función `write_mcp_config()` generaba el bloque opencode sin `enabled: true`, sin `type: local` y con `env` en lugar de `environment`. OpenCode (schema `McpLocalConfig`) requiere `type: local` y lee `environment`; `env` lo ignora silenciosamente. Corregido en esta HU.
- **`setup.sh` / `setup.ps1` / plantilla `opencode.mcp.json`**: ya generaban bien (`enabled: true`, `type: local`, `environment`).
- **El mensaje de error exacto**: "missing key mcp.engram.enable" — probablemente sea un typo de OpenCode (debería decir `enabled`, no `enable`), pero el fix del lado usuario es agregar `enabled: true`.
