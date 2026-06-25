# ADR-004: Post-install registration con el FlowForge installer

**Estado:** Aceptado  
**Fecha:** 2026-06-23  
**Autores:** equipo engram  
**Contexto de trabajo:** ENG-301 — Stack Installer (Fase 0)

---

## Contexto

El FlowForge installer (`flowforge`) gestiona múltiples componentes del stack. Para que pueda:

- Mostrar qué versión de `engram-dotnet` está instalada (`flowforge` sin args)
- Detectar si hay actualizaciones disponibles (`flowforge update --check`)
- Verificar compatibilidad entre versiones

...necesita saber la versión y path del binario de `engram-dotnet` que está actualmente instalado.

### Problema

El installer descarga y coloca el binario de `engram-dotnet`. Pero hay casos donde `engram-dotnet` ya estaba instalado manualmente (sin el installer), o fue actualizado directamente por el usuario. En esos casos, `~/.engram/config.json` puede no reflejar la realidad.

### Opciones consideradas

| Opción | Descripción | Problema |
|--------|-------------|---------|
| El installer siempre escribe `config.json` | Solo funciona cuando el installer hizo la instalación | No cubre instalaciones manuales o actualizaciones directas |
| `engram-dotnet` informa su versión al correr | Agregar flag `--register` al CLI de engram | Acoplamiento: engram necesita saber del installer |
| **Post-install scripts (elegido)** | Scripts en el repo de `engram-dotnet` que leen la versión del binario y escriben `config.json` | Idempotentes, fáciles de invocar desde cualquier contexto |

---

## Decisión

**`engram-dotnet` incluye scripts post-instalación** que registran el componente en `~/.engram/config.json`.

- `scripts/post-install.sh` — para Linux/macOS
- `scripts/post-install.ps1` — para Windows (PowerShell 5.1+)

Ambos scripts:

1. Detectan el binario de `engram-dotnet` (argumento o PATH)
2. Extraen la versión ejecutando `engram --version`
3. Escriben (o actualizan) la entrada `components.engram_dotnet` en `~/.engram/config.json`
4. Son **idempotentes** — correrlos múltiples veces no corrompe la config

El formato escrito en `config.json`:

```json
{
  "components": {
    "engram_dotnet": {
      "installed": true,
      "version": "0.3.0",
      "binary": "/usr/local/bin/engram",
      "registered_at": "2026-06-23T21:00:00Z"
    }
  }
}
```

### Cuándo se invoca

| Escenario | Quién invoca el script |
|-----------|----------------------|
| Instalación via `flowforge install` | El `EngramModule.cs` del installer |
| Actualización via `flowforge update` | El `EngramModule.UpdateAsync()` del installer |
| Instalación manual del binario | El usuario, manualmente, o como post-install step del release |
| CI / automatizaciones | Cualquier pipeline que instale el binario |

---

## Consecuencias

**Positivas:**
- Desacoplamiento: `engram-dotnet` no necesita saber de FlowForge ni del installer.
- Los scripts son simples y auditables (bash / PowerShell puro, sin dependencias).
- El installer puede verificar el estado real aunque el binario haya sido instalado/actualizado por fuera.

**Restricciones asumidas:**
- El script requiere `python3` disponible en el host para manipular JSON (Linux/macOS). En Windows usa las cmdlets nativas de PowerShell.
- El script debe ejecutarse con permisos de escritura en `~/.engram/`.
- Si `engram-dotnet` cambia el formato de `--version`, el script debe actualizarse.

---

## Referencias

- `scripts/post-install.sh`
- `scripts/post-install.ps1`
- `FlowForge/src/FlowForge.Installer/Modules/EngramModule.cs`
- `FlowForge/docs/architecture/adr/ADR-002-runtime-manifest-for-compatibility.md`
