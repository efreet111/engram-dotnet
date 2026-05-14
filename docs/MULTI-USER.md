# Multi-User Isolation — Guía de Uso

> **RFC**: [RFC-002](rfcs/RFC-002-multi-user-isolation.md)

---

## ¿Qué es?

El aislamiento multi-usuario permite que múltiples desarrolladores compartan un servidor Engram centralizado manteniendo sus memorias **personales** aisladas, mientras las memorias **team** son compartidas.

---

## Conceptos clave

### Scopes

| Scope | Visibilidad | Ejemplo |
|-------|-------------|---------|
| `team` | Todos los devs del equipo | Decisiones de arquitectura, bugfixes críticos |
| `personal:{user}` | Solo el dev dueño | Tool usage, comandos, file changes |

### Identidad del usuario

El servidor identifica a cada dev mediante el header `X-Engram-User`:

1. **Explícita**: Variable de entorno `ENGRAM_USER=victor.silgado`
2. **Implícita**: Usuario del sistema operativo (`Environment.UserName`)

---

## Configuración

### Servidor centralizado

No requiere configuración especial — el aislamiento es automático.

```bash
# Server (TrueNAS, Linux, etc.)
./engram serve --port 7437
```

### Cliente MCP (cada dev)

```bash
# ~/.engram/config.json o variables de entorno
ENGRAM_URL=http://192.168.0.178:7437
ENGRAM_USER=victor.silgado  # ← Identidad explícita
```

### Opencode / Cursor config

```json
{
  "mcp": {
    "servers": {
      "engram": {
        "command": "/home/user/.local/bin/engram",
        "args": ["mcp", "--project", "my-api"],
        "env": {
          "ENGRAM_URL": "http://192.168.0.178:7437",
          "ENGRAM_USER": "victor.silgado"
        }
      }
    }
  }
}
```

---

## Comportamiento

### Guardado de memorias

#### Scope `team` (compartido)

```json
// mem_save con type=architecture
{
  "project": "my-api",
  "scope": "team",
  "type": "architecture",
  "title": "JWT auth implementation",
  "content": "..."
}
```

**Resultado**: `project = "team/my-api"` — visible para todos.

#### Scope `personal` (aislado)

```json
// mem_save con type=tool_use (auto-classified)
{
  "project": "my-api",
  "scope": "personal",
  "type": "tool_use",
  "title": "Debugging session",
  "content": "..."
}
```

**Resultado**: `project = "victor.silgado/my-api"` — solo visible para `victor.silgado`.

### Búsquedas

#### Sin scope filter (wide-read)

```bash
mem_search query="auth"
```

**Comportamiento**:
- Dev A ve: `team/*` + `personal:A/*`
- Dev B ve: `team/*` + `personal:B/*`

#### Con scope filter

```bash
mem_search query="auth" scope="team"
```

**Comportamiento**: Solo memorias `team/*` para todos.

---

## Casos de uso

### 1. Equipo nuevo — configurar identidades

```bash
# Cada dev configura su identidad
# Dev 1
export ENGRAM_USER=ana.gomez

# Dev 2
export ENGRAM_USER=juan.perez

# Dev 3
export ENGRAM_USER=victor.silgado
```

### 2. Memorias personales no se filtran

```bash
# Ana guarda debugging session
mem_save --type tool_use --scope personal \
  "Debug: null pointer en UserService"

# Resultado: project = "ana.gomez/my-api"
# Juan y Victor NO ven esta memoria
```

### 3. Decisiones compartidas

```bash
# Ana guarda decisión de arquitectura
mem_save --type architecture --scope team \
  "JWT vs Session authentication"

# Resultado: project = "team/my-api"
# Todo el equipo ve esta memoria
```

### 4. Migración de proyecto legacy

Si ya tenías memorias sin namespacing:

```bash
# Listar proyectos
engram projects list

# Consolidar variantes
engram projects consolidate --all

# Podar proyectos vacíos
engram projects prune --dry-run
```

---

## Verificación

### Chequear identidad actual

```bash
# Desde el cliente MCP
mem_current_project

# Output esperado
{
  "project": "my-api",
  "project_source": "git_remote",
  "project_path": "/home/victor/proyectos/my-api",
  "cwd": "/home/victor/proyectos/my-api",
  "user": "victor.silgado"  # ← Identidad activa
}
```

### Chequear aislamiento

```bash
# Dev A guarda memoria personal
mem_save --scope personal --type tool_use \
  "Test isolation"

# Dev B busca la misma memoria
mem_search query="Test isolation"

# Resultado: 0 resultados (aislamiento funciona)

# Dev B busca solo team
mem_search query="Test isolation" scope="team"

# Resultado: 0 resultados (es personal de Dev A)
```

---

## Troubleshooting

### Problema: Memorias personales son visibles para otros

**Causa**: `ENGRAM_USER` no está configurado — todos usan `global`.

**Solución**:
```bash
# Configurar identidad explícita
export ENGRAM_USER=victor.silgado

# Reiniciar servidor MCP
```

### Problema: Header `X-Engram-User` no llega al servidor

**Causa**: `HttpStore` no está enviando el header.

**Solución**:
```csharp
// Verificar Engram.Store/HttpStore.cs
// Debe incluir:
_request.DefaultRequestHeaders.Add("X-Engram-User", _config.User);
```

### Problema: Conflicto de nombres de proyecto

**Causa**: Dos devs usan el mismo nombre de proyecto sin namespacing.

**Solución**:
```bash
# Listar proyectos duplicados
engram projects list

# Consolidar con namespacing
engram projects consolidate --all
```

---

## Compatibilidad Legacy

### Modo single-user (sin `ENGRAM_USER`)

Si `ENGRAM_USER` no está configurado, el servidor usa identidad `global`:

- Todas las memorias `personal` → `global/*`
- Todas las memorias `team` → `team/*`

**Recomendación**: Configurar `ENGRAM_USER` para todos los devs nuevos.

### Migración desde legacy

```bash
# 1. Backup
engram export backup-legacy.json

# 2. Configurar identidades
export ENGRAM_USER=victor.silgado

# 3. Listar proyectos legacy
engram projects list

# 4. Consolidar con namespacing
engram projects consolidate --all
```

---

## Ver también

- [RFC-002](rfcs/RFC-002-multi-user-isolation.md) — RFC técnico
- [TEAM-SETUP.md](TEAM-SETUP.md) — Configuración de equipo
- [SYNC.md](SYNC.md) — Git-based sync para distribuir memorias
