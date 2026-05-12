# Proposal: Multi-User Isolation for Personal Scopes

## 1. Goal
Implement data isolation for observations and prompts with `scope: personal`, ensuring that each user has their own private space while sharing the `scope: team` data within the same project.

## 2. Approach
We will introduce a new concept of **User Identity** at the API layer.

### 2.1 Identity Capture
- The server will look for the `X-Engram-User` HTTP header in every request.
- If missing, it will default to a reserved user ID `global`.

### 2.2 Scope Transformation
When a client requests `scope: personal`, the server will internally map this to `personal:{userId}`.
- User `gantz` + `scope: personal` -> `personal:gantz`
- User `antigravity` + `scope: personal` -> `personal:antigravity`
- User `*` + `scope: team` -> `team` (remains shared)

### 2.3 Affected Components
- **Engram.Server**: Add a helper to extract the identity from `HttpContext`.
- **Engram.Store**: Update `IStore` and its implementations to accept an optional `userId` or handle namespaced scopes.

## 3. Detailed Changes

### Engram.Server
- Add `GetUserId(HttpContext ctx)` helper.
- Update all handlers that deal with observations, prompts, and context to use this ID.

### Engram.Store
- Update `AddObservationAsync`, `RecentObservationsAsync`, `SearchAsync`, and `FormatContextAsync` to internalize the namespacing logic.
- We will NOT change the DB schema. The `scope` column is already a string, which is perfect for storing `personal:user_id`.

## 4. Risks & Mitigations
- **Risk**: Clients not sending the header will lose access to their previous "global" personal notes.
- **Mitigation**: Default the user identity to `global` if the header is missing, maintaining legacy compatibility.

## 5. Next Steps
1. Create Specifications (`spec.md`) with Gherkin scenarios for isolation.
2. Create Task Checklist (`tasks.md`) for implementation batches.
