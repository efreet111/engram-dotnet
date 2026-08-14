# ADR Template

> Architecture Decision Record — Michael Nygard format

## Template Fields

| Campo | Descripción |
|-------|-------------|
| **ADR** | Número de decisión (001, 002...) |
| **Estado** | `Draft` / `In Review` / `Accepted` / `Deprecated` |
| **Fecha** | YYYY-MM-DD de la decisión |
| **Contexto** | Breve descripción del contexto o decisión original |

---

## Required Sections

### 1. Contexto

Explica el contexto y el problema que motiva esta decisión. Incluye:
- El problema que se está resolviendo
- Factores que influyen en la decisión
- Restricciones del sistema

### 2. Decisión

Describe la decisión tomada. Usa lenguaje prescriptivo:
- "Se decide..."
- "Se utilizará..."
- "Se implementará..."

### 3. Razones (opcional pero recomendado)

Explica las razones que justifican la decisión. Puede incluir:
- Beneficios esperados
- Trade-offs considerados
- Comparación con alternativas descartadas

### 4. Consecuencias

Describe los efectos de la decisión. Incluye:

**Positivas:**
- Beneficios que resultan de esta decisión

**Negativas:**
- Costos, limitaciones, side effects

**Mitigaciones:**
- Cómo se manejan las consecuencias negativas

### 5. Alternativas Consideradas

Lista otras opciones evaluadas y por qué fueron descartadas.

---

## Status Lifecycle

```
Draft → In Review → Accepted
                  ↘ Rejected
                  ↘ Deprecated
```

Un ADR pasa a `Deprecated` cuando es reemplazado por otro ADR o la decisión ya no aplica.

---

## Example

```markdown
# ADR-XXX — Título de la decisión

| Campo | Valor |
|-------|-------|
| **ADR** | XXX |
| **Estado** | Accepted |
| **Fecha** | 2026-01-15 |
| **Contexto** | Problema o decisión que motivó este ADR |

---

## Contexto

[Explicación del problema y contexto]

---

## Decisión

[La decisión tomada]

---

## Razones

[Beneficios y trade-offs]

---

## Consecuencias

**Positivas:**
- [Beneficio 1]

**Negativas:**
- [Costo 1]

---

## Alternativas Consideradas

### Opción 1
- Pro: ...
- Contra: ...

### Opción 2
- Pro: ...
- Contra: ...
```

---

## Naming Convention

```
ADR-NNN-title-in-kebab-case.md
```

Ejemplos:
- `ADR-001-no-orm.md`
- `ADR-002-auth-jwt.md`
- `ADR-003-caching-strategy.md`

---

## ADR Index

Todos los ADRs deben ser referenciados en `docs/architecture/adr/INDEX.md`.
