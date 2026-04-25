[← Volver al README](../README.md)

# Migración desde engram (Go) a engram-dotnet (.NET)

---

## Compatibilidad

engram-dotnet es **100% compatible** con los datos y plugins del proyecto Go original:

| Componente | Compatible |
|---|---|
| Schema SQLite | ✅ Idéntico |
| API HTTP (22 endpoints) | ✅ Idéntica |
| Protocolo MCP (15 herramientas) | ✅ Idéntica |
| Formato de sync (gzip JSONL) | ✅ Idéntico |
| Plugins (Claude Code, OpenCode, Gemini, Codex) | ✅ Sin cambios |
| Archivo `engram.db` | ✅ Portable directamente |
| Obsidian Export | ✅ Porteado (`engram obsidian-export`) |

---

## Migración de datos

### Opción A — Mover el archivo SQLite directamente

El schema es idéntico, el archivo `.db` es portable:

```bash
# En la máquina con engram Go
cp ~/.engram/engram.db /tmp/engram-backup.db

# En el servidor con engram-dotnet
ENGRAM_DATA_DIR=/data/engram ./engram serve
# Copiar el archivo:
cp /tmp/engram-backup.db /data/engram/engram.db
```

### Opción B — Export/Import JSON

Para validar integridad o migrar de forma más controlada:

```bash
# En la máquina con engram Go
engram export /tmp/engram-export.json

# En el servidor con engram-dotnet
./engram import /tmp/engram-export.json
```

---

## Migración de configuración de plugins

Los plugins existentes **no requieren cambios de código**. Solo cambia la variable de entorno `ENGRAM_URL`:

### Claude Code
```bash
# Antes (local)
# ENGRAM_URL=http://localhost:7437 (default, puede no estar seteada)

# Después (servidor compartido)
export ENGRAM_URL=http://servidor.interno:7437
```

### OpenCode
```bash
# En el archivo de configuración de OpenCode o como variable de entorno
ENGRAM_URL=http://servidor.interno:7437
```

### Gemini CLI / Codex
Igual — todos los plugins leen `ENGRAM_URL`.

---

## Diferencias de comportamiento

### TUI (Terminal UI)
El proyecto Go incluye una TUI interactiva (`engram tui`) con Bubbletea. **engram-dotnet v1 no incluye TUI.** Está planificada para la Fase 2.

### Obsidian export
✅ **Porteado** — `engram obsidian-export` está disponible con paridad funcional al Go original.
Soporta export completo, incremental (`--force`), filtro por proyecto (`--project`),
scope security (`--include-personal`), y graph config (`--graph-config`).

### Auth JWT
El proyecto Go no tiene autenticación. engram-dotnet agrega autenticación JWT **opcional**:
- Sin `ENGRAM_JWT_SECRET` → comportamiento idéntico al Go (sin auth)
- Con `ENGRAM_JWT_SECRET=secreto` → todos los endpoints requieren `Authorization: Bearer <token>`

Esto es especialmente útil cuando se despliega como servidor compartido en red interna.

---

## Período de convivencia recomendado

Durante la migración, se recomienda correr ambos binarios en paralelo:

```
Fase 0: Go binary sigue sirviendo todo el tráfico
Fase 1-5: Build y tests de engram-dotnet
Fase 6: ENGRAM_URL apunta al servidor .NET en staging
        → ejecutar tests de paridad
Cutover: cambiar ENGRAM_URL en todas las máquinas de desarrollo
         Go binary se archiva (no se elimina) por 6 meses
```

Los tests de paridad (`tests/parity/run_parity.sh`) verifican que ambos binarios producen resultados idénticos para las mismas operaciones.
