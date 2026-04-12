[← Volver al README](../README.md)

# Guía de configuración para el desarrollador — engram-dotnet

> Esta guía es para vos, el desarrollador. IT ya configuró el servidor centralizado y te va a pasar la URL. Acá explicamos cómo conectar tu editor (Cursor o VS Code) al servidor y empezar a usar la memoria persistente con tu agente de IA.

---

## Índice

- [¿Qué es engram y para qué me sirve?](#qué-es-engram-y-para-qué-me-sirve)
- [Prerrequisitos](#prerrequisitos)
- [Paso 1 — Variables de entorno](#paso-1--variables-de-entorno)
- [Paso 2 — Configurar Cursor](#paso-2--configurar-cursor)
- [Paso 3 — Configurar VS Code](#paso-3--configurar-vs-code)
- [Verificar que funciona](#verificar-que-funciona)
- [Cómo usarlo en la práctica](#cómo-usarlo-en-la-práctica)
- [Preguntas frecuentes](#preguntas-frecuentes)

---

## ¿Qué es engram y para qué me sirve?

Engram es una memoria persistente para tu agente de IA. Cuando el agente trabaja con vos (corrige bugs, toma decisiones de arquitectura, refactoriza código), puede guardar lo importante en engram. La próxima sesión —o la próxima semana— puede recuperar ese contexto automáticamente.

Sin engram, cada sesión empieza de cero. Con engram, el agente "recuerda" lo que hicieron juntos.

**Modo equipo**: en lugar de que cada uno tenga su instancia local, usamos un servidor compartido. Tus memorias son tuyas (`ENGRAM_USER` las namespcea automáticamente) — no hay colisión con las de tus compañeros.

---

## Prerrequisitos

- URL del servidor engram (te la pasa IT, ej: `http://10.0.0.5:7437`)
- Tu identificador de desarrollador (ej: `victor.silgado`)
- Los archivos de configuración del repo (en `config/cursor/` o `config/vscode/`)

---

## Paso 1 — Variables de entorno

Agregar estas dos variables en tu perfil de shell (`~/.bashrc`, `~/.zshrc`, o equivalente):

```bash
# URL del servidor compartido de engram (te la da IT)
export ENGRAM_URL=http://10.0.0.5:7437

# Tu identidad — namespcea tus memorias en el servidor
export ENGRAM_USER=nombre.apellido
```

Recargar el shell:

```bash
source ~/.bashrc    # o ~/.zshrc según corresponda
```

Verificar:

```bash
echo $ENGRAM_URL
echo $ENGRAM_USER
```

> **¿Por qué ENGRAM_USER?** Cuando el agente guarda una memoria del proyecto `mi-proyecto`, internamente se guarda como `victor.silgado/mi-proyecto` — sin que el agente lo sepa ni lo tenga que recordar. Así tu memoria y la de María no se mezclan aunque ambas sean del mismo proyecto.

---

## Paso 2 — Configurar Cursor

### Archivos a instalar

| Fuente (en el repo) | Destino |
|---|---|
| `config/cursor/mcp.json` | `~/.cursor/mcp.json` |
| `config/cursor/rules/engram.mdc` | `~/.cursor/rules/engram.mdc` |
| `config/cursor/rules/sdd-orchestrator.md` | `~/.cursor/rules/sdd-orchestrator.md` |
| `config/cursor/agents/*.md` (9 archivos) | `~/.cursor/agents/` |

### Instalación

```bash
# Asegurarse que los directorios existen
mkdir -p ~/.cursor/rules ~/.cursor/agents

# MCP config
cp config/cursor/mcp.json ~/.cursor/mcp.json

# Reglas
cp config/cursor/rules/engram.mdc ~/.cursor/rules/
cp config/cursor/rules/sdd-orchestrator.md ~/.cursor/rules/

# Agentes SDD
cp config/cursor/agents/*.md ~/.cursor/agents/
```

### Verificar la configuración MCP

1. Reiniciar Cursor
2. Abrir el panel MCP (Settings → MCP)
3. Debería aparecer `engram` como servidor activo
4. Si hay error de conexión, verificar que `ENGRAM_URL` esté en el entorno y que el servidor responda

### Cómo el agente usa engram en Cursor

Una vez configurado, el agente (Claude en Cursor) tiene acceso a las herramientas de memoria. Las instrucciones en `engram.mdc` le dicen cuándo y cómo usarlas.

No tenés que hacer nada especial — el agente guarda automáticamente lo importante. Si querés que recuerde algo específico, podés pedírselo:

> "Guardá en memoria que decidimos usar JWT para auth porque es stateless"

---

## Paso 3 — Configurar VS Code

### Archivos a instalar

| Fuente (en el repo) | Destino |
|---|---|
| `config/vscode/mcp.json` | `~/.config/Code/User/mcp.json` |
| `config/vscode/settings.json` | Merge manual en `~/.config/Code/User/settings.json` |
| `config/vscode/prompts/engram.instructions.md` | `~/.config/Code/User/prompts/engram.instructions.md` |

> **Nota sobre settings.json**: no reemplaces todo el archivo — solo mergeá las keys de `config/vscode/settings.json` en tu settings global existente. VS Code guarda tus preferencias personales ahí y no querés perderlas.

### Instalación

```bash
# Crear directorio de prompts si no existe
mkdir -p ~/.config/Code/User/prompts

# MCP config (global — afecta todos los proyectos)
cp config/vscode/mcp.json ~/.config/Code/User/mcp.json

# Instrucciones para el agente (modo Agent)
cp config/vscode/prompts/engram.instructions.md ~/.config/Code/User/prompts/engram.instructions.md
```

Luego agregar estas keys en `~/.config/Code/User/settings.json`:

```json
{
  "chat.agent.enabled": true,
  "chat.mcp.enabled": true,
  "chat.mcp.access": "all",
  "chat.promptFiles": true,
  "mcp.autoStart": true,
  "github.copilot.chat.memory.enabled": false,
  "github.copilot.chat.tools.memory.enabled": false
}
```

> **¿Por qué dos settings de memory?** `github.copilot.chat.memory.enabled` desactiva la UI de memoria de Copilot. `github.copilot.chat.tools.memory.enabled` desactiva la tool `functions.memory` que el agente usa internamente. Necesitás ambas en `false` para que el agente use exclusivamente `engram-team`.

### Cómo usar el agente en VS Code

El protocolo engram **solo funciona en modo Agent**, no en modo Ask:

1. Abrir Copilot Chat (`Ctrl+Alt+I`)
2. Seleccionar modo **Agent** en el dropdown (no "Ask", no "Edit")
3. El servidor MCP `engram-team` arranca automáticamente cuando el agente lo necesita

### Verificar en VS Code

1. Abrir la paleta de comandos (`Ctrl+Shift+P`)
2. Buscar "MCP: List Servers"
3. Debería aparecer `engram-team` como servidor disponible
4. Preguntar al agente en modo Agent: `¿qué MCP tools tenés disponibles?`
5. Debería listar `mem_save`, `mem_context`, `mem_search`, etc. — **no** `functions.memory`

---

## Verificar que funciona

Podés verificar la conexión directamente desde la terminal:

```bash
# Health check del servidor
curl $ENGRAM_URL/health
# → {"status":"ok","service":"engram","version":"1.0.0"}

# Ver tus proyectos en el servidor (debería estar vacío al principio)
curl $ENGRAM_URL/context
```

O desde el agente — pedirle que guarde algo:

> "Guardá en memoria que esta es mi primera sesión con engram"

Y luego:

> "¿Qué recordás de sesiones anteriores?"

---

## Cómo usarlo en la práctica

### El agente guarda automáticamente

El agente está instruido para guardar de forma proactiva:
- Decisiones de arquitectura ("decidimos usar X porque Y")
- Bugs resueltos ("el problema era Z, lo solucionamos con W")
- Configuraciones ("el servidor corre en puerto 3000 por variable de entorno PORT")
- Preferencias ("el usuario prefiere TypeScript estricto sin `any`")

### Vos podés pedir memoria explícita

Cualquier momento:

> "Recordá que el cliente pide que la fecha se muestre en formato DD/MM/YYYY"

> "¿Qué sabés del proyecto auth-service?"

> "¿Qué hicimos la semana pasada con el módulo de pagos?"

### Al final de cada sesión

El agente guarda un resumen de la sesión automáticamente cuando terminás. Si querés asegurarte:

> "Hacé un resumen de lo que trabajamos hoy y guardálo en memoria"

### Namespacing automático

No tenés que preocuparte por el namespacing. Cuando el agente guarda una memoria del proyecto `mi-api`, internamente se guarda como `victor.silgado/mi-api`. Las búsquedas también filtran por tu usuario automáticamente — no ves las memorias de tus compañeros.

---

## Preguntas frecuentes

**¿Mis memorias son privadas?**

Sí — las memorias se guardan con tu `ENGRAM_USER` como prefijo. El servidor las mantiene separadas. En principio nadie más puede leer tus memorias (a menos que IT configure acceso especial o que se consulte directamente la BD SQLite).

**¿Puedo usar engram sin conexión al servidor?**

No en modo equipo. Si no hay conexión al servidor, el agente mostrará un error al intentar guardar o buscar memorias. Si necesitás modo offline, hablá con IT para evaluar una instancia local.

**¿Qué pasa si cambio de máquina?**

Las memorias están en el servidor, no en tu máquina. Alcanza con configurar `ENGRAM_URL` y `ENGRAM_USER` en la nueva máquina e instalar los archivos de config — tus memorias siguen estando.

**¿Cómo busco memorias viejas?**

Podés pedirle al agente:

> "Buscá en memoria todo lo relacionado con autenticación"

O directamente en la terminal:

```bash
# El binario engram puede consultar el servidor también
ENGRAM_URL=http://10.0.0.5:7437 ENGRAM_USER=victor.silgado \
  engram search "autenticación" --project mi-proyecto
```

**¿Qué es SDD y para qué son los agentes en `~/.cursor/agents/`?**

SDD (Spec-Driven Development) es un flujo de trabajo para cambios grandes y estructurados. Los agentes permiten que Cursor coordine múltiples sub-agentes para explorar, especificar, diseñar, implementar y verificar cambios de forma sistemática. No es obligatorio usarlo — son herramientas opcionales.
