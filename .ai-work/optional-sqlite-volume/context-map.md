# Context Map: Optional SQLite Volume for PostgreSQL Backend

**Feature slug:** `optional-sqlite-volume`  
**Date:** 2026-08-05  
**Reporter:** User (during Docker installation testing)

---

## Problem Statement

When deploying engram-dotnet with PostgreSQL backend (`ENGRAM_DB_TYPE=postgres`), the Docker configuration still requires/mounts a volume for SQLite data (`/data/engram`). This is unnecessary because:

1. PostgreSQL stores data in its own database, not in the container's filesystem
2. The volume mount creates confusion about what's actually needed
3. Users deploying PostgreSQL-only setups don't want/need SQLite paths

**User request:** "Allow disabling SQLite completely when PostgreSQL is the backend" or "Make the SQLite volume optional".

---

## Current Architecture

### Backend Selection Logic (src/Engram.Cli/Program.cs:1391-1398)

```
Is ENGRAM_URL set?
  ├─ YES → HttpStore (remote proxy)
  └─ NO  → Is ENGRAM_DB_TYPE=postgres?
              ├─ YES → PostgresStore
              └─ NO  → SqliteStore (default)
```

### Data Directory Usage

| Backend | Uses `ENGRAM_DATA_DIR`? | What for |
|---------|-------------------------|----------|
| **SqliteStore** | ✅ YES | Stores `engram.db` file |
| **PostgresStore** | ❌ NO | Nothing (data in PostgreSQL) |
| **HttpStore** | ❌ NO | Nothing (proxy to remote) |

**Evidence from code:**

- `StoreConfig.cs:7-11`: `DataDir` property exists, `DbPath` derived from it
- `SqliteStore.cs:52-56`: Validates `DataDir` is absolute path, creates directory
- `PostgresStore.cs`: **No references to `DataDir`** — only uses `PgConnectionString`

### Current Docker Configuration

**docker-compose.yml (lines 35-38):**
```yaml
volumes:
  # Datos persistentes (SQLite fallback, exports, etc.)
  - ${ENGRAM_DATA_DIR_HOST:-./data}:/data/engram
```

**Problem:** Volume is **always mounted**, even when `ENGRAM_DB_TYPE=postgres`.

**docker/.env.example (line 13):**
```env
ENGRAM_DATA_DIR_HOST=./data
```

**Documentation says:** "SQLite fallback, exports, etc." but exports are in-memory (return `ExportData` object, don't write to disk).

---

## Analysis

### Is SQLite needed when using PostgreSQL?

**NO.** Evidence:

1. `PostgresStore` constructor only requires `PgConnectionString`
2. No code path in `PostgresStore` references `DataDir` or creates files
3. Export/Import operations return objects in memory, don't write to filesystem
4. No fallback mechanism requires SQLite when PostgreSQL is configured

### What uses `/data/engram` volume?

| Use case | Backend | Required? |
|----------|---------|-----------|
| SQLite database file | SqliteStore | ✅ YES |
| PostgreSQL data | PostgresStore | ❌ NO (uses PG server) |
| Exports | Both | ❌ NO (in-memory) |
| Logs | Both | ❌ NO (stdout/stderr) |
| Project identity (`.engram-id`) | Both | ⚠️ Maybe (needs verification) |

**Open question:** Does `.engram-id` (project identity file) get written to `DataDir`? If yes, PostgreSQL backend might still need the volume for this.

---

## Related Backlog Items

**ENG-444** (✅ Done): Privacy/PII cleanup — removed IPs, passwords from docs  
**ENG-479** (✅ Done): Docker runtime permissions — fixed volume ownership

**No existing item** for optional SQLite volume.

---

## Proposed Solutions

### Option A: Conditional Volume Mount (docker-compose.yml)

Use Docker Compose profiles or conditional logic:

```yaml
services:
  engram:
    # ... other config ...
    volumes:
      - ${ENGRAM_DATA_DIR_HOST:-./data}:/data/engram  # Only if ENGRAM_DB_TYPE=sqlite
```

**Problem:** Docker Compose doesn't support conditional volumes natively.

**Workaround:** Use profiles:
```yaml
services:
  engram-sqlite:
    profiles: ["sqlite"]
    volumes:
      - ${ENGRAM_DATA_DIR_HOST:-./data}:/data/engram
  
  engram-postgres:
    profiles: ["postgres"]
    # No volume needed
```

### Option B: Environment Variable Control

Add `ENGRAM_SKIP_DATA_DIR` variable:

```env
# In .env
ENGRAM_DB_TYPE=postgres
ENGRAM_SKIP_DATA_DIR=true  # Skip volume mount
```

**Problem:** docker-compose.yml can't conditionally mount volumes based on env vars.

### Option C: Documentation + Optional Volume (Recommended)

Keep volume mount but document it as optional:

1. Update `docker/README.md` to clarify volume is only needed for SQLite
2. Add comment in `docker-compose.yml` explaining when volume is needed
3. Provide separate compose files or examples for each backend

**Pros:** Simple, no code changes, backward compatible  
**Cons:** Volume still mounted (harmless but unnecessary)

### Option D: Separate Compose Files

Create `docker-compose.sqlite.yml` and `docker-compose.postgres.yml`:

```bash
# SQLite mode
docker compose -f docker-compose.sqlite.yml up -d

# PostgreSQL mode
docker compose -f docker-compose.postgres.yml up -d
```

**Pros:** Clean separation  
**Cons:** Duplication, harder to maintain

---

## Recommendations

1. **Short-term:** Option C (documentation) — clarify that volume is optional with PostgreSQL
2. **Medium-term:** Option D (separate compose files) — cleaner separation
3. **Code change needed?** Verify if `.engram-id` requires `DataDir` for PostgreSQL backend

---

## Open Questions (Resolved)

1. **OQ-1:** ✅ RESOLVED — `.engram-id` se guarda en la raíz del repo git (`repoPath`), NO en `DataDir`. PostgreSQL no lo necesita en el volumen.
2. **OQ-2:** Should we add validation to skip `DataDir` creation when using PostgreSQL? → **Recommendation: YES**, avoid unnecessary directory creation.
3. **OQ-3:** Are there any other filesystem operations that require the volume? → **Answer: NO**, exports are in-memory, logs go to stdout.

## Final Conclusion

**PostgreSQL backend does NOT need the `/data/engram` volume.** The volume is only required for SQLite.

---

## Files to Modify

| File | Change |
|------|--------|
| `docker/docker-compose.yml` | Add comments, consider conditional volume |
| `docker/.env.example` | Clarify `ENGRAM_DATA_DIR_HOST` is optional for PostgreSQL |
| `docker/README.md` | Document when volume is needed |
| `docs/DOCKER-VANILLA.md` | Update examples for PostgreSQL-only setup |
| `src/Engram.Cli/Program.cs` | Verify `.engram-id` behavior with PostgreSQL |

---

## Memory Signal

```yaml
type: config
significance: medium
summary: "Docker volume for SQLite is unnecessary when using PostgreSQL backend"
topic_key: "docker/postgresql-optional-volume"
```
