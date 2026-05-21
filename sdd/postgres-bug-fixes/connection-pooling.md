# Bug: PostgresStore Connection Pooling

**Severity**: 🔴 Alta  
**Effort**: 2-3h  
**Reported**: 2026-05-21 (smoke test)

---

## Problem

`PostgresStore` uses a **single `NpgsqlConnection`** instance (`_db`) shared across ALL operations. When the SyncManager pushes mutations while an HTTP request is being processed, both try to use the same connection simultaneously:

```
Exception: NpgsqlOperationInProgressException
Message: The connection is already in state 'Executing'
```

This breaks search, save, and any concurrent operation.

## Fix

Replace single `NpgsqlConnection _db` with a **connection factory** that creates pooled connections:

```csharp
// Before:
private readonly NpgsqlConnection _db;
// Usage: await using var cmd = _db.CreateCommand();

// After:
private readonly string _connectionString;
private NpgsqlConnection CreateConnection()
{
    var conn = new NpgsqlConnection(_connectionString);
    conn.Open();
    return conn;
}
// Usage: await using var conn = CreateConnection();
//         await using var cmd = conn.CreateCommand();
```

Npgsql's connection pooling is **built-in** (enabled by default). Each `new NpgsqlConnection(connString)` + `Open()` returns a pooled connection automatically.

## Files to change

| File | Lines | Changes |
|------|-------|---------|
| `src/Engram.Store/PostgresStore.cs` | ~2100 | Replace `_db.CreateCommand()` → `CreateConnection().CreateCommand()` in ALL methods |
| `src/Engram.Store/PostgresStore.cs` | constructor | Store `_connectionString` instead of creating `_db` |

## Affected methods

~40 methods that currently use `_db.CreateCommand()`. All need to be updated to create a new connection per operation.

## Risk

Low — the pattern is straightforward. Main risk is missing a method that still uses `_db`. Solution: make `_db` throw an exception if accessed after migration.
