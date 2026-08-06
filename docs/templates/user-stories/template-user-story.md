# User Story Template

> HU — Historia de Usuario siguiendo FlowDoc

## Template Fields

| Campo | Descripción |
|-------|-------------|
| **As a** | Tipo de usuario (dev, IT admin, etc.) |
| **I want** | Acción que quiere realizar |
| **To** | Beneficio o razón |

---

## Required Sections

### 1. Title

```
# HU-NNN — Short Feature Name
```

Nombre corto en kebab-case.

### 2. As a user...

```
**As**: [user type]
**I want**: [action]
**To**: [benefit/reason]
```

### 3. Acceptance Criteria

Lista de criterios observables y testables:

```
- [ ] [Expected behavior 1]
- [ ] [Expected behavior 2]
```

Cada criterio debe poder verificarse sin ambigüedad.

### 4. Tasks (Implementation)

Lista de tareas concretas y ordenadas:

```
- [ ] [Technical task 1]
- [ ] [Technical task 2]
```

### 5. Notes (opcional)

Contexto adicional, dependencias, preguntas abiertas.

---

## Naming Convention

```
HU-NNN-name-in-kebab-case.md
```

Ejemplos:
- `HU-001-backend-config-switch.md`
- `HU-010-deploy-profile-system.md`
- `HU-011-docs-and-script-qa.md`

---

## Example

```markdown
# HU-XXX — Feature Name

**As**: Developer
**I want**: perform an action
**To**: achieve a benefit

---

## Acceptance Criteria

- [ ] Criterion 1
- [ ] Criterion 2

---

## Tasks (Implementation)

- [ ] Task 1
- [ ] Task 2

---

## Notes

- Dependency: requires ADR-XXX
- Open question: TBD
```

---

## Post-Development Updates

Después de implementar, actualizar el documento:

1. Marcar `Acceptance Criteria` cumplidos con `[x]`
2. Marcar `Tasks` completados con `[x]`
3. Agregar sección `## Implementation Notes` si hay desviaciones
4. Agregar `## Deviations from Plan` si cambió algo
