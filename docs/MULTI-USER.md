# Multi-User Isolation

> **RFC**: [`rfcs/RFC-002-multi-user-isolation.md`](rfcs/RFC-002-multi-user-isolation.md)  
> **Requires**: `ENGRAM_USER` environment variable

---

## What Is It?

Multi-user isolation allows multiple developers to share a single Engram server without their personal memories leaking into each other's context.

Each developer has:
- **Personal memories**: Only they can see (namespaced as `personal:{user}/*`)
- **Team memories**: Visible to all enrolled developers (`team/*`)

---

## Key Concepts

### Scopes

| Scope | Visibility | Storage | Example |
|-------|-----------|---------|---------|
| `team` | Everyone on the server | `team/{project}` | `team/mi-api`, `team/frontend` |
| `personal` | Only the creator | `personal:{user}/{project}` | `personal:your-username/debug` |

### User identity

Each developer identifies via the `X-Engram-User` HTTP header or the `ENGRAM_USER` environment variable.

```
Agent calls mem_save
  → scope: team → project = team/{project}
  → scope: personal → project = personal:{user}/{project}
```

---

## Configuration

### Server (no changes needed)

The server automatically handles namespacing. No special config required.

### Client (each developer)

```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_URL": "http://localhost:7437",
        "ENGRAM_USER": "your-username"  // ← YOUR unique identity
      }
    }
  }
}
```

---

## Behavior

### Saving memories

```csharp
// In SqliteStore.NormalizeProject()
if (scope == "personal")
{
    if (v.StartsWith("personal:") || v.StartsWith("project:"))
        return v;  // Already namespaced, keep it
    return $"personal:{userId}";  // Namespace with identity
}
```

### Searching

- **Without scope filter**: Searches ALL projects the user has access to
- **With scope filter**: `scope=team` or `scope=personal` limits results

### Audit trail

```csharp
// In CloudSyncEndpoints (pause/resume)
var pausedBy = ctx.Request.Headers["X-Engram-User"].FirstOrDefault() ?? "admin";
```

---

## Use Cases

### 1. New team — configure identities

```bash
# Each developer sets their identity
export ENGRAM_USER=your-username
export ENGRAM_URL=http://localhost:7437
```

### 2. Personal memories don't leak

```bash
# Victor saves a personal note
curl -X POST http://server:7437/observations \
  -H "X-Engram-User: victor" \
  -d '{"session_id":"s1","title":"Victor's personal note","type":"manual","project":"team/mi-api","scope":"personal"}'

# Juan searches → DOESN'T see Victor's note
curl -H "X-Engram-User: juan" http://server:7437/search?q=personal
# → [] (empty)
```

### 3. Shared decisions

```bash
# Victor saves a team decision
curl -X POST http://server:7437/observations \
  -H "X-Engram-User: victor" \
  -d '{"session_id":"s1","title":"Decision: use PostgreSQL","type":"architecture","project":"team/mi-api","scope":"team"}'

# Both Victor and Juan can see it
curl -H "X-Engram-User: juan" http://server:7437/search?q=PostgreSQL
# → [Decision: use PostgreSQL]
```

---

## Verification

```bash
# Test isolation end-to-end
# 1. Victor enrolls
curl -X POST http://server:7437/sync/enroll -H "X-Engram-User: victor" -d '{"project":"team/mi-api"}'

# 2. Juan enrolls (different project)
curl -X POST http://server:7437/sync/enroll -H "X-Engram-User: juan" -d '{"project":"team/frontend"}'

# 3. Victor sees only his enrolled project
curl -H "X-Engram-User: victor" http://server:7437/sync/enroll
# → {"projects":[{"project":"team/mi-api"}],"count":1}

# 4. Juan sees only his
curl -H "X-Engram-User: juan" http://server:7437/sync/enroll
# → {"projects":[{"project":"team/frontend"}],"count":1}
```
