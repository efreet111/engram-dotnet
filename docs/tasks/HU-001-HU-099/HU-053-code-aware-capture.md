# HU-053 — Code-aware memory capture

**ENG:** ENG-483  
**Tipo:** Feature  
**Prioridad:** P2  
**Esfuerzo:** L (2-4 días)  
**Estado:** Idea  
**Origen:** ← feature ideas 2026-08 (ENGRAM-IDEA-001)

---

## Problema que resuelve

Hoy, la captura de memorias en engram es **manual** — tienes que recordar usar un comando CLI o llamar un MCP tool. Para desarrolladores en su workflow de código, "recordar guardar" es un punto de fricción que mata la adopción.

El momento más natural para capturar es **durante el trabajo mismo**: cuando editas un archivo, cuando haces un cambio significativo, cuando tomas una decisión arquitectónica en el código.

**Sin captura automática del contexto de código, engram es un store pasivo en vez de una parte activa del workflow.**

---

## Propuesta de solución

engram debería poder **capturar memorias automáticamente desde el contexto de código del desarrollador**:

### Opciones de captura automática

1. **`engram watch <file>`** — observar un archivo, logear cambios significativos como memorias
2. **Git hooks** (ya cubierto en ENG-481) — captura desde commits
3. **LSP integration** — cuando editas un archivo, ofrecer "guardar esto como memoria"
4. **Pre-PR hook** — extraer decisiones desde la descripción del PR

### Enfoque recomendado para ENG-483

**Fase 1 (este ENG):** File watching básico
- `engram watch <file>` — observa cambios en un archivo específico
- Cuando el archivo cambia significativamente (ej: >10 líneas modificadas), captura una memoria
- Taggear con metadata de código: file path, líneas cambiadas

**Fase 2 (future):** LSP integration
- Requiere entender el protocolo LSP completo
- Es un proyecto en sí mismo
- **Descartar por ahora**

---

## Criterios de aceptación

- [ ] `engram watch <file>`:
  - [ ] Observa cambios en el archivo especificado
  - [ ] Detecta cambios significativos (threshold configurable, default: >10 líneas)
  - [ ] Captura memoria con:
    - `type` = `code_change`
    - `title` = "Changed: {file_path}"
    - `content` = diff resumido + líneas cambiadas
    - `project` = auto-detectado desde `.engram-id` o git root
  - [ ] Output en tiempo real:
    ```
    👀 Watching src/Auth/JwtBearer.cs...
    ✓ Memory saved: #1234 "Changed: src/Auth/JwtBearer.cs" (code_change)
    ```
- [ ] Flag `--threshold <lines>` para configurar sensibilidad (default: 10)
- [ ] Flag `--ignore-pattern <glob>` para excluir archivos (ej: `*.log`, `node_modules/`)
- [ ] Soporte para múltiples archivos:
  ```bash
  engram watch src/Auth/*.cs
  ```
- [ ] Graceful shutdown con Ctrl+C (sin pérdida de datos)
- [ ] Tests:
  - [ ] File watching detecta cambios
  - [ ] Threshold funciona correctamente
  - [ ] Ignore patterns funcionan
  - [ ] Multi-file watching
- [ ] Documentación:
  - [ ] `docs/01-QUICK-START.md` — sección "Auto-capture with file watching"
  - [ ] `README.md` — mención de `engram watch`

---

## Implementación técnica

### Dónde tocar código

1. **`src/Engram.Cli/Program.cs`**:
   - Nuevo comando `watch`
   - File system watcher (usar `System.IO.FileSystemWatcher`)
   - Lógica de detección de cambios significativos (contar líneas modificadas)
   - Llamar a `store.AddObservationAsync()` con metadata de código

2. **File watching**:
   - `FileSystemWatcher` para detectar cambios
   - Throttling para evitar capturar cada keystroke (ej: debounce 5 segundos)
   - Calcular diff simple (líneas agregadas/removidas)

3. **Tests**:
   - `tests/Engram.Cli.Tests/FileWatchTests.cs` — 5-7 tests

### Edge cases a manejar

- ¿Qué pasa si el archivo no existe?
  - Error claro: "File not found: {path}"

- ¿Qué pasa con cambios muy frecuentes (hot reload)?
  - Throttling: solo capturar si pasan 5+ segundos desde última captura
  - O threshold: solo capturar si >10 líneas cambiaron

- ¿Qué pasa con archivos binarios?
  - Detectar por extensión (.dll, .exe, .png) y ignorar
  - O flag `--binary` para forzar watching

- ¿Qué pasa con directorios completos?
  - Soportar glob patterns: `engram watch src/**/*.cs`
  - Recursivo con `--recursive` flag

### Complejidad técnica

- **File watching cross-platform** es doloroso:
  - Linux: `inotify` (funciona bien)
  - macOS: `FSEvents` (funciona bien)
  - Windows: `ReadDirectoryChangesW` (funciona, pero con edge cases)
  - `FileSystemWatcher` de .NET abstrae esto, pero tiene limitaciones

- **Calcular diff** sin dependencias externas:
  - Opción 1: Llamar a `git diff` (requiere git)
  - Opción 2: Implementar diff simple (LCS algorithm)
  - Opción 3: Solo contar líneas cambiadas (sin diff detallado)

---

## Fuera de alcance

- ❌ LSP integration — es un proyecto completo en sí mismo
- ❌ Pre-PR hook (GitHub integration) — future enhancement
- ❌ Auto-tagging con símbolos de código (funciones, clases) — requiere parser de lenguaje
- ❌ UI interactiva para revisar cambios antes de capturar — trust-and-go
- ❌ Integración con IDEs específicos (VS Code, JetBrains) — future enhancement

---

## Cómo probarlo

```bash
# 1. Build
dotnet build -c Release

# 2. Watch un archivo
./src/Engram.Cli/bin/Release/net10.0/engram watch src/Engram.Cli/Program.cs
# → 👀 Watching src/Engram.Cli/Program.cs...

# 3. En otra terminal, modificar el archivo
echo "// test comment" >> src/Engram.Cli/Program.cs

# 4. Verificar que se capturó
./src/Engram.Cli/bin/Release/net10.0/engram search "Changed: Program.cs"
# → debe mostrar la memoria del cambio

# 5. Watch con threshold custom
./src/Engram.Cli/bin/Release/net10.0/engram watch src/ --threshold 20

# 6. Watch con ignore pattern
./src/Engram.Cli/bin/Release/net10.0/engram watch src/ --ignore-pattern "*.log"

# 7. Tests
dotnet test tests/Engram.Cli.Tests/ --filter "FileWatch"
```

---

## Métricas de éxito

- **Adopción**: 30%+ de desarrolladores usan `engram watch` en su workflow diario
- **Memorias capturadas**: 3+ auto-captured memories por día por desarrollador activo
- **Signal-to-noise**: <20% de memorias capturadas son ruido (cambios triviales)

---

## Dependencias

- ✅ Ninguna — se puede implementar inmediatamente
- 🔗 Relacionado: ENG-480 (quick-capture), ENG-481 (git hooks)
- ⚠️ **Riesgo:** File watching cross-platform puede ser más complejo de lo esperado

---

## Referencias

- [ENGRAM-IDEA-001](/mnt/86FC44B0FC449BF5/Proyectos/Desarrollo/engram-feature-ideas.md#engram-idea-001--code-aware-memory-capture) — idea original
- [ENG-483 en BACKLOG](../../BACKLOG.md#eng-483)
