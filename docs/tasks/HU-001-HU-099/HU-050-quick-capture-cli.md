# HU-050 — Quick-capture CLI (`engram "<memo>"`)

**ENG:** ENG-480  
**Tipo:** Feature  
**Prioridad:** P1  
**Esfuerzo:** S (3-4 horas)  
**Estado:** Idea  
**Origen:** ← feature ideas 2026-08 (ENGRAM-IDEA-003)

---

## Problema que resuelve

Hoy, capturar una memoria con engram requiere múltiples pasos:

```bash
engram save --title "Decisión importante" --content "Elegimos PostgreSQL por JSONB" --type decision --project mi-proyecto
```

Esto toma 30+ segundos y rompe el flujo de trabajo. El resultado: los desarrolladores no capturan memorias espontáneas ("acabo de entender por qué falló ese test", "convención: usamos MediatR para CQRS"), y engram se convierte en un store pasivo en vez de activo.

**Fricción mata adopción.** Si capturar toma más de 5 segundos, no lo hacen.

---

## Propuesta de solución

Un modo "quick-capture" de un solo argumento:

```bash
engram "remember: usamos MediatR para CQRS en este proyecto"
engram "decisión: elegimos PostgreSQL sobre MongoDB por soporte JSONB"
engram -t blocker "auth flow se rompe cuando token_version > user.current_token_version"
```

### Smart defaults

1. **Auto-detectar project** desde:
   - `.engram-id` en el directorio actual o padre
   - Git root (buscar `.git/`)
   - Si no encuentra, usar `default`

2. **Default type** a `note` o `insight` (los más comunes para captura rápida)

3. **Auto-generar title** desde las primeras palabras del contenido (primeras 5-7 palabras, truncado a 50 chars)

4. **Session ID**: `quick-capture-{timestamp}` (no requiere interacción manual)

---

## Criterios de aceptación

- [ ] `engram "<texto>"` crea una memoria con:
  - [ ] `content` = texto completo
  - [ ] `title` = primeras 5-7 palabras del contenido (max 50 chars)
  - [ ] `type` = `note` (default) o el especificado con `-t`
  - [ ] `project` = auto-detectado desde `.engram-id` o git root
  - [ ] `session_id` = `quick-capture-{timestamp}`
- [ ] Soporte para multi-line con `$'...'` o heredoc:
  ```bash
  engram $'línea 1\nlínea 2\nlínea 3'
  ```
- [ ] Output confirma la creación:
  ```
  ✓ Memory saved: #1234 "Decisión: PostgreSQL sobre MongoDB" (note)
  ```
- [ ] Tests unitarios cubren:
  - [ ] Captura básica
  - [ ] Auto-detect de project (con `.engram-id`)
  - [ ] Auto-detect de project (con git root)
  - [ ] Custom type con `-t`
  - [ ] Multi-line input
- [ ] Documentación actualizada:
  - [ ] `docs/01-QUICK-START.md` — ejemplo de quick-capture
  - [ ] `README.md` — mención en sección de uso básico

---

## Implementación técnica

### Dónde tocar código

1. **`src/Engram.Cli/Program.cs`**:
   - Agregar handler para argumento posicional (string)
   - Lógica de auto-detect de project (reutilizar `ProjectIdentity.TryGetProjectId()`)
   - Auto-generar title desde content
   - Llamar a `store.AddObservationAsync()` con smart defaults

2. **Tests**:
   - `tests/Engram.Cli.Tests/QuickCaptureTests.cs` — 5-7 tests

### Edge cases a manejar

- ¿Qué pasa si el texto está vacío? → Error claro: "Memory content cannot be empty"
- ¿Qué pasa si no hay `.engram-id` ni git repo? → Usar `project = "default"` con warning
- ¿Qué pasa con caracteres especiales (comillas, newlines)? → Soportar `$'...'` y heredoc
- ¿Qué pasa si el usuario quiere editor? → Futuro: flag `-e` para abrir `$EDITOR`

---

## Fuera de alcance

- ❌ Editor interactivo (`engram -e`) — future enhancement
- ❌ Historial de quick-captures (`engram recent`) — future enhancement
- ❌ Confirmación interactiva — trust-and-go (el usuario puede hacer `mem_delete` si se equivoca)

---

## Cómo probarlo

```bash
# 1. Build
dotnet build -c Release

# 2. Quick capture básico
./src/Engram.Cli/bin/Release/net10.0/engram "decisión: usamos MediatR para CQRS"
# → ✓ Memory saved: #1234 "decisión: usamos MediatR para CQRS" (note)

# 3. Con type custom
./src/Engram.Cli/bin/Release/net10.0/engram -t insight "pattern: usamos Result<T> para errores"
# → ✓ Memory saved: #1235 "pattern: usamos Result<T> para errores" (insight)

# 4. Verificar que se guardó
./src/Engram.Cli/bin/Release/net10.0/engram search "MediatR"
# → debe mostrar la memoria #1234

# 5. Tests
dotnet test tests/Engram.Cli.Tests/ --filter "QuickCapture"
```

---

## Métricas de éxito

- **Time-to-first-memory**: <3 segundos (desde que el usuario termina de escribir hasta que ve "✓ Memory saved")
- **Adopción**: 60%+ de memorias capturadas via quick-capture (vs. `engram save` completo) en 3 meses
- **Zero learning curve**: no hay nuevos comandos que aprender, solo `engram "<texto>"`

---

## Dependencias

- ✅ Ninguna — se puede implementar inmediatamente
- 🔗 Relacionado: ENG-481 (git hooks), ENG-482 (stats mejorado)

---

## Referencias

- [ENGRAM-IDEA-003](/mnt/86FC44B0FC449BF5/Proyectos/Desarrollo/engram-feature-ideas.md#engram-idea-003--quick-capture-from-terminal-engram-memo) — idea original
- [ENG-480 en BACKLOG](../../BACKLOG.md#eng-480)
