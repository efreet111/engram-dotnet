# Archive Report: Multi-User Isolation

**Archived**: 2026-05-14  
**Change**: `multi-user-isolation`  
**Status**: ✅ COMPLETE — Team multi-tenant isolation via identity-based namespacing

---

## Summary

**Intent**: Permitir que múltiples desarrolladores compartan un mismo servidor Engram sin que las memorias personales se contaminen entre sí.

**RFC**: [`docs/rfcs/RFC-002-multi-user-isolation.md`](../../docs/rfcs/RFC-002-multi-user-isolation.md)

**Commits**: 
- `80aac44` — feat: implement multi-user isolation via identity headers
- `6b39a24` — docs: add SYNC, MULTI-USER, VERIFICATION guides

**Effort**: ~4-5h

---

## Implementation Summary

### Files Modified (5)

| File | Changes | Purpose |
|------|---------|---------|
| `src/Engram.Store/SqliteStore.cs` | +AutoClassifyScope(), NormalizeProject() | Namespacing automático |
| `src/Engram.Store/HttpStore.cs` | +X-Engram-User header | Identity propagation |
| `src/Engram.Server/EngramServer.cs` | +UserHeader constant, DetectUserIdentity() | Identity detection |
| `src/Engram.Server/CloudSyncEndpoints.cs` | +X-Engram-User in pause/resume | Audit trail |
| `docs/rfcs/RFC-002-multi-user-isolation.md` | Created | Architecture spec |

### Architecture

```
Agent llama mem_save
  ↓
AutoClassifyScope() determina si es team o personal
  ↓
personal → "personal:{userId}"
team     → "team/{project}"
  ↓
Store normaliza y persiste con namespace
```

### Identity Flow

1. **Capture**: `X-Engram-User` header from MCP client (set via `ENGRAM_USER` env var)
2. **Detection**: `DetectUserIdentity()` in EngramServer middleware
3. **Namespacing**: `personal:{user}` prefix for personal scope
4. **Isolation**: Each user sees only their own personal memories

### Scope Classification

| Scope Type | Namespace | Example |
|------------|-----------|---------|
| **Team** | `team/{project}` | `team/my-api`, `team/frontend` |
| **Personal** | `personal:{user}` | `personal:victor.silgado`, `personal:juan.perez` |

---

## Key Features

### 1. Auto-Classification

```csharp
// In SqliteStore.NormalizeProject()
if (scope == "personal")
{
    // If it already has a namespace (personal:user), keep it
    if (v.StartsWith("personal:") || v.StartsWith("project:"))
        return v;
    
    // Otherwise, namespace with user identity
    return $"personal:{userId}";
}
```

### 2. Identity Header

```csharp
// In HttpStore constructor
if (!string.IsNullOrEmpty(cfg.User))
    _http.DefaultRequestHeaders.Add("X-Engram-User", cfg.User);
```

### 3. Audit Trail

```csharp
// In CloudSyncEndpoints (pause/resume)
var pausedBy = ctx.Request.Headers["X-Engram-User"].FirstOrDefault() ?? "admin";
```

---

## Test Results

```
Build:        ✅ 0 errors, 0 warnings
Integration:  ✅ Multi-user isolation tested via RFC-002 scenarios
```

**Test Coverage**:
- ✅ Personal scope namespacing
- ✅ Team scope pass-through
- ✅ Identity header propagation
- ✅ Multi-tenant isolation (shared server, isolated data)

---

## Architecture Decisions

| AD | Decision | Choice |
|----|----------|--------|
| AD-1 | Identity capture | `X-Engram-User` HTTP header |
| AD-2 | Namespace format | `personal:{user}` prefix |
| AD-3 | Scope classification | Auto-detect via `AutoClassifyScope()` |
| AD-4 | Backward compatibility | Existing `personal` scope auto-migrated |
| AD-5 | Team scope | No namespacing — shared by default |

---

## Known Issues

| Issue | Status | Notes |
|-------|--------|-------|
| Go upstream has `personal:user` shortcut | ✅ Implemented | SqliteStore handles both formats |
| Discovery during RFC-002: SQLite was wiping user identity | ✅ Fixed | Normalization now preserves namespace |

---

## Verification Status

**Verdict**: ✅ **PASS**

- Identity capture working (X-Engram-User header)
- Namespacing implemented (personal:{user})
- Auto-classification working (team vs personal)
- Multi-tenant isolation verified
- RFC-002 documented

---

## Usage

### For Team Setup

```bash
# Each developer sets their identity
export ENGRAM_USER=victor.silgado
export ENGRAM_URL=http://192.168.0.178:7437

# Personal memories are automatically isolated
mem_save "My personal note" --scope personal
# → Stored as "personal:victor.silgado/my-note"

# Team memories are shared
mem_save "API design" --scope team --project my-api
# → Stored as "team/my-api/api-design"
```

### MCP Configuration

```json
{
  "mcpServers": {
    "engram": {
      "command": "engram",
      "args": ["mcp"],
      "env": {
        "ENGRAM_USER": "victor.silgado",
        "ENGRAM_URL": "http://192.168.0.178:7437"
      }
    }
  }
}
```

---

## Related Documentation

- [`docs/rfcs/RFC-002-multi-user-isolation.md`](../../docs/rfcs/RFC-002-multi-user-isolation.md) — Full RFC with architecture details
- [`docs/MULTI-USER.md`](../../docs/MULTI-USER.md) — User guide for team setup
- [`docs/SYNC.md`](../../docs/SYNC.md) — Sync setup with multi-user support

---

## SDD Cycle Complete

✅ **Planned** → RFC-002 documented identity-based isolation  
✅ **Implemented** → Namespacing, AutoClassifyScope, X-Engram-User header  
✅ **Verified** → Multi-tenant isolation tested  
✅ **Archived** → Artifacts documented in this report

**Ready for the next change.**
