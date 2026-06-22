[← Volver a docs](../ROADMAP.md)

# ADR-001 — SQL directo sin ORM

| Campo      | Valor |
|------------|-------|
| **ADR**    | 001 |
| **Estado** | Accepted |
| **Fecha**  | 2026-04-20 |
| **Contexto** | Decisión inicial de persistencia; reafirmada al planificar PostgreSQL backend |

---

## Contexto

`engram-dotnet` es un port del proyecto Go original `engram`. El proyecto Go usa SQL directo contra SQLite. Al portar a C#/.NET 10 teníamos que decidir si usar un ORM (Entity Framework Core, Dapper) o mantener SQL directo.

Al planificar `PostgresStore` (RFC-001), esta decisión se reafirma explícitamente.

---

## Decisión

**SQL directo** usando `Microsoft.Data.Sqlite` para SQLite y `Npgsql` para PostgreSQL. Sin ORM.

---

## Razones

### 1 — Paridad de schema con el proyecto Go original

El schema de base de datos de `engram` debe ser **idéntico** al del proyecto Go para permitir migración directa de datos (los archivos `.db` de SQLite son intercambiables). Un ORM mapea entidades a schema propio y hace difícil mantener esta paridad con precisión quirúrgica.

### 2 — Control total sobre FTS5 / tsvector

Las queries de full-text search (FTS5 en SQLite, `tsvector` en PostgreSQL) son específicas de cada base de datos. EF Core no tiene abstracción de FTS5 estable para SQLite, y `tsvector` requiere anotaciones no estándar. Con SQL directo, cada query es explícita y auditarle.

### 3 — Binario self-contained sin overhead

EF Core añade ~5-10MB al binario publicado. Para un binario que idealmente debería ser < 50MB, esto es significativo.

### 4 — Lógica de deduplicación en SQL

La deduplicación usa transacciones, SELECTs dentro de transacciones, y UPDATE condicionales que son más claros escritos en SQL que con métodos de EF Core. El código es auditarle directamente.

### 5 — Consistencia con el proyecto Go original

El código Go usa SQL directo. Los contribuyentes que vengan del proyecto Go encontrarán el código familiar.

---

## Consecuencias

**Positivas**:
- Queries explícitas y auditables
- Schema exactamente como se diseñó
- Sin overhead de ORM en binario ni en runtime
- Los contribuyentes del proyecto Go original pueden leer el código sin friction

**Negativas**:
- Más código boilerplate para mapeo de resultados (`DataReader` → model)
- Queries duplicadas entre `SqliteStore` y `PostgresStore` (no DRY)
- Sin generación automática de migrations

**Mitigación de las negativas**:
- Los helpers de `SqliteStore` (`ReadObservation`, `ReadPrompt`, etc.) se replican en `PostgresStore` — aceptable dado que son estables
- Las migrations son código `IF NOT EXISTS` — idempotentes y simples
- El patrón es consistente y fácil de seguir para nuevos contribuyentes

---

## Alternativas consideradas

### EF Core
- Pro: migrations automáticas, menos boilerplate
- Contra: abstracción sobre FTS5/tsvector, overhead en binario, friction con schema paridad

### Dapper
- Pro: micro-ORM, reduce boilerplate de DataReader, sin overhead de EF
- Contra: agrega dependencia sin eliminar la necesidad de SQL explícito, y el código de SqliteStore ya tiene helpers que cumplen el mismo rol

### Repositorio genérico con Dapper
- Similar a Dapper puro — descartado por las mismas razones

---

*Esta decisión se revisa si la cantidad de código duplicado entre backends se vuelve problemática de mantener.*
