# Cursor — configuración de **este proyecto**

Todo lo de esta carpeta y de `.cursor/` en la raíz del repo es **local al workspace engram-dotnet**. No sustituye reglas globales de usuario en `~/.cursor/`; solo aplica cuando abrís este repositorio en Cursor.

## Estructura

| Ruta | Uso |
|------|-----|
| `config/cursor/rules/` | Fuente versionada de reglas (`.mdc`) |
| `.cursor/rules/` | Copia activa que lee Cursor (sincronizar con el script) |
| `.cursor/skills/` | Skills del proyecto (p. ej. `engram-docs-on-done`) |
| `config/cursor/agents/` | Agentes SDD |
| `config/cursor/mcp.json` | Ejemplo MCP del repo |

## Reglas de flujo (alwaysApply)

| Regla | Qué hace |
|-------|----------|
| `engram-git-workflow.mdc` | Ramas, commits, PRs, tags — ver [docs/GIT-WORKFLOW.md](../../docs/GIT-WORKFLOW.md) |
| `engram-docs-on-done.mdc` | CHANGELOG, BACKLOG, ROADMAP al cerrar ítems |
| `engram.mdc` | Protocolo memoria Engram (MCP) |

## Después de clonar o editar reglas en `config/cursor/rules/`

```powershell
.\scripts\sync-cursor-rules.ps1
```

```bash
./scripts/sync-cursor-rules.sh
```

Copia `*.mdc` → `.cursor/rules/` para que el IDE las cargue.

## Documentación humana

- Git: [docs/GIT-WORKFLOW.md](../../docs/GIT-WORKFLOW.md)
- Docs al cerrar: [.cursor/skills/engram-docs-on-done/SKILL.md](../../.cursor/skills/engram-docs-on-done/SKILL.md)
