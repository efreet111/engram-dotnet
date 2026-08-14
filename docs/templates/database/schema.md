# Database Schema Template

> Documentación de schema de base de datos

## Required Sections

### 1. Overview

Descripción general del schema.

### 2. Tables

Para cada tabla:

```
#### table_name

| Columna | Tipo | Constraints | Descripción |
|---------|------|-------------|-------------|
| id | INTEGER | PRIMARY KEY | Descripción |
| name | TEXT | NOT NULL | Descripción |

**Índices:**
- `idx_name` ON (columna) — propósito

** foreign keys:**
- `fk_name` → other_table(id)
```

### 3. Migrations

Scripts de migración o notas sobre cómo crear el schema.

---

## Naming Convention

```
schema.md
```

O para múltiples databases:

```
sqlite-schema.md
postgres-schema.md
```
