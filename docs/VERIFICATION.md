# Verification Tools — Guía de Uso

> **SDD**: [`sdd/verification-tools/`](../sdd/archive/2026-05-14-verification-tools/)

---

## ¿Qué es?

Las herramientas de verificación permiten validar que el código implementado cumple con los requisitos de un `spec.md`. Usan **LLM-as-Judge** (Anthropic API) para evaluar cada requisito y generar reportes estructurados.

---

## Componentes

### `mem_verify_artifact` — Verificación de compliance

**Propósito**: Verificar que un cambio de código satisface los requisitos de un spec.

**Input**:
- `spec_path` — Ruta al archivo `spec.md`
- `code_diff` — Diff de cambios (unified diff o file listing)
- `change_name` — Identificador del cambio (ej: "verification-tools")

**Output**: `VerificationReport` con:
- `items[]` — Veredicto por requisito (Pass/Fail/Untested)
- `coverage_pct` — Porcentaje de requisitos cubiertos
- `pass_pct` — Porcentaje de requisitos que pasan
- `cycle` — Número de ciclo actual
- `escalate` — `true` si se alcanzó el máximo de ciclos

### `mem_traceability` — Matriz de trazabilidad

**Propósito**: Generar matriz RF/RNF → código.

**Input**:
- `spec_path` — Ruta al archivo `spec.md`

**Output**: `TraceabilityMatrix` con:
- `entries[]` — Requisito + status + evidencia (file paths)
- `covered` — Count de requisitos cubiertos
- `missing` — Count de requisitos sin cobertura
- `coverage_pct` — Porcentaje de cobertura

### `CycleTracker` — Trackeo de ciclos

**Propósito**: Trackear ciclos de rework por cambio.

**Persistencia**: Observación en Engram con `topic_key = "cycle-count/{change_name}"`.

**Comportamiento**:
- Ciclo 1: Primera verificación
- Ciclo 2: Re-trabajo (si hubo fails)
- Ciclo 3: Escalado a humano (si `ENGRAM_VERIFICATION_MAX_CYCLES=3`)

---

## Configuración

### Variables de entorno

```bash
# Modelo para LLM-as-Judge
ENGRAM_VERIFICATION_MODEL=claude-sonnet-4-20250514

# Máximos ciclos antes de escalar
ENGRAM_VERIFICATION_MAX_CYCLES=3

# API key de Anthropic (requerida)
ANTHROPIC_API_KEY=sk-...
```

---

## Flujo de trabajo

### 1. Crear spec.md canónico

El spec debe seguir el formato canónico:

```markdown
# Título del Feature

## Objetivo
Descripción clara del feature.

## Functional Requirements

- RF-001: El sistema debe permitir login con email/password
- RF-002: El sistema debe validar email antes de crear cuenta
- RF-003: El sistema debe rate-limitar intentos de login

## Non-Functional Requirements

- RNF-001: El login debe responder en < 200ms (p95)
- RNF-002: Las contraseñas deben hashearse con bcrypt
- RNF-003: No loggear contraseñas en plaintext
```

### 2. Implementar cambios

```bash
# Trabajar normal en el código
git diff > changes.diff
```

### 3. Verificar compliance

```bash
# Primera verificación (Ciclo 1)
mem_verify_artifact \
  spec_path="sdd/verification-tools/specs/artifact-verification/spec.md" \
  code_diff="git diff main" \
  change_name="verification-tools"
```

**Output**:
```json
{
  "items": [
    {
      "requirement": {
        "id": "RF-001",
        "type": "RF",
        "description": "El sistema debe permitir login..."
      },
      "verdict": "Pass",
      "reasoning": "El código implementa endpoint POST /login que acepta email/password...",
      "confidence": 0.95,
      "evidence": "src/Engram.Server/EngramServer.cs:65"
    },
    {
      "requirement": {
        "id": "RNF-003",
        "type": "RNF",
        "description": "No loggear contraseñas en plaintext"
      },
      "verdict": "Fail",
      "reasoning": "Se encontró console.log(password) en la línea 42...",
      "confidence": 0.98,
      "evidence": "src/Engram.Server/EngramServer.cs:42"
    }
  ],
  "coverage_pct": 100.0,
  "pass_pct": 83.3,
  "total": 6,
  "passed": 5,
  "failed": 1,
  "cycle": 1,
  "escalate": false,
  "summary": "5/6 requisitos pasan. 1 fail: RNF-003 (password en logs)."
}
```

### 4. Generar rework ticket (si hubo fails)

El reporte incluye automáticamente un rework ticket:

```markdown
# Rework Ticket — Cycle 1/3

## Failed Items
- [ ] RNF-003: Se encontró `console.log(password)` en la línea 42

## Instructions
1. Remover console.log del handler de login
2. Reemplazar con logger estructurado que omita campos sensibles
3. Re-verificar con mem_verify_artifact
```

### 5. Iterar (si es necesario)

```bash
# Después de fixear
git add -A && git commit -m "fix: remove password logging"

# Re-verificar (Ciclo 2)
mem_verify_artifact \
  spec_path="..." \
  code_diff="git diff HEAD~1" \
  change_name="verification-tools"
```

### 6. Escalar a humano (si cycle >= max)

Si después de 3 ciclos hay fails:

```json
{
  "cycle": 3,
  "escalate": true,
  "summary": "3 ciclos sin resolver RNF-003. Escalar a humano."
}
```

---

## Casos de uso

### 1. Verificar PR antes de merge

```bash
# En CI/CD o pre-commit
mem_verify_artifact \
  spec_path="sdd/my-feature/spec.md" \
  code_diff="git diff origin/main" \
  change_name="my-feature"

# Fail el build si pass_pct < 100
if [ $pass_pct -lt 100 ]; then
  echo "Verification failed: ${pass_pct}% pass rate"
  exit 1
fi
```

### 2. Generar matriz de trazabilidad

```bash
# Para auditoría o documentación
mem_traceability \
  spec_path="sdd/auth-module/spec.md"

# Output: matriz RF/RNF → file paths
```

### 3. Trackear ciclos de rework

```bash
# Ver ciclo actual
mem_search query="cycle-count verification-tools"

# Output: observación con revision_count = ciclo actual
```

---

## Formato de reporte

### `VerificationReport`

```json
{
  "items": [
    {
      "requirement": {
        "id": "RF-001",
        "type": "RF",
        "description": "...",
        "section": "Functional Requirements"
      },
      "verdict": "Pass|Fail|Untested|Error|Escalate",
      "reasoning": "...",
      "confidence": 0.95,
      "evidence": "src/Engram.Server/EngramServer.cs:65"
    }
  ],
  "coverage_pct": 100.0,
  "pass_pct": 83.3,
  "total": 6,
  "passed": 5,
  "failed": 1,
  "cycle": 1,
  "escalate": false,
  "summary": "5/6 requisitos pasan..."
}
```

### `TraceabilityMatrix`

```json
{
  "entries": [
    {
      "requirement": {
        "id": "RF-001",
        "type": "RF",
        "description": "..."
      },
      "status": "covered|partial|missing|untraced",
      "evidence": [
        "src/Engram.Server/EngramServer.cs:65",
        "tests/Engram.Server.Tests/AuthTests.cs:20"
      ]
    }
  ],
  "total": 6,
  "covered": 5,
  "missing": 1,
  "coverage_pct": 83.3
}
```

---

## Mejores prácticas

### 1. Specs canónicos

- Usar formato `- RF-NNN: descripción`
- Separar claramente RF y RNF
- Incluir sección `## Objetivo`

### 2. Code diffs pequeños

- Verificar por cambio pequeño (no todo el PR junto)
- Ideal: < 200 líneas por verificación

### 3. Confidence threshold

- `confidence >= 0.9`: Confiar en el veredicto
- `confidence < 0.7`: Revisar manualmente
- `confidence < 0.5`: Ignorar (falso positivo probable)

### 4. Cycle management

- Ciclo 1: Verificación inicial
- Ciclo 2: Fixear fails obvios
- Ciclo 3: Escalar a humano si persisten fails

---

## Troubleshooting

### Problema: `ANTHROPIC_API_KEY is not set`

**Causa**: Variable de entorno no configurada.

**Solución**:
```bash
export ANTHROPIC_API_KEY=sk-...
```

### Problema: Spec no parseable

**Causa**: Formato no canónico.

**Solución**:
```markdown
# Formato correcto
## Functional Requirements
- RF-001: descripción

# Formato incorrecto
### Requisitos
1. El sistema debe...
```

### Problema: Falso positivo en veredicto

**Causa**: LLM no entendió el contexto.

**Solución**:
- Agregar más contexto al diff (incluir files relacionados)
- Revisar manualmente con `confidence < 0.7`
- Ajustar prompt del verifier (advanced)

---

## Ver también

- [SDD](../sdd/archive/2026-05-14-verification-tools/) — Documentación técnica completa
- [ARCHITECTURE.md](ARCHITECTURE.md#engramverification--compliance-verification) — Arquitectura del módulo
- [Promotion Tools](PROMOTION.md) — Promover observaciones a .md
