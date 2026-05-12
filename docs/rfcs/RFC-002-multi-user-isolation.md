# RFC-002: Multi-User Isolation for Personal Scopes

## Status
- **Date**: 2026-05-11
- **Status**: Proposed
- **Author**: Victor (gantz) & Antigravity
- **Target Version**: v1.4.0

## 1. Background
Currently, **engram-dotnet** acts as a centralized memory server for teams. While project-level data (`scope: team`) is correctly shared among all participants, personal preferences and workflows (`scope: personal`) are globally visible and overwritable by any connected client.

In the current implementation:
- The server does not distinguish between different users.
- `RecentObservationsAsync(project, scope: "personal")` returns all personal observations for that project, regardless of who created them.
- This leads to "memory pollution" where one developer's personal preferences affect another developer's AI context.

## 2. Proposed Change
Introduce an **Identity Header** (`X-Engram-User`) that allows the server to partition the `personal` scope while keeping the `team` scope shared.

### 2.1 The `X-Engram-User` Header
Clients SHOULD provide a unique identifier in the `X-Engram-User` HTTP header. 
- Example: `X-Engram-User: gantz`

### 2.2 Server-Side Logic (Internal Namespacing)
The server will internally transform the `scope` value before querying or persisting to the database.

| Requested Scope | Header Value | Internal DB Scope | Behaviour |
|-----------------|--------------|-------------------|-----------|
| `team`          | *any*        | `team`            | Shared across all users. |
| `personal`      | `gantz`      | `personal:gantz`  | Isolated to user "gantz". |
| `personal`      | *missing*    | `personal:global` | Fallback to a shared personal scope (legacy compatibility). |

### 2.3 Database Impact
- No schema changes required.
- The `observations.scope` column (TEXT) will store the namespaced string (e.g., `personal:gantz`).
- Indexes on `scope` and `project` will continue to work efficiently.

## 3. Implementation Plan

### Phase 1: Middleware/Handler Update
Update `EngramServer.cs` to extract the header and pass it to the Store methods.

### Phase 2: Store Logic
Update `IStore`, `SqliteStore`, and `PostgresStore` to handle namespaced scopes.
- `AddObservationAsync`
- `RecentObservationsAsync`
- `SearchAsync`
- `FormatContextAsync`

### Phase 3: Client Compatibility
- Update the `Engram.Cli` to allow setting a user ID.
- Update the `Engram.Mcp` proxy to forward the local OS user as the default `X-Engram-User`.

## 4. Verification Plan
1. Create Observation A as `user-1` with `scope: personal`.
2. Create Observation B as `user-2` with `scope: personal`.
3. Verify that `user-1` searching for personal context ONLY sees Observation A.
4. Verify that both users searching for `scope: team` see the same shared data.

## 5. Documentation Updates
- Update `docs/ARCHITECTURE.md` to reflect the multi-user isolation strategy.
- Update `README.md` with instructions on how to set the User ID.
