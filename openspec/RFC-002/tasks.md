# Tasks: Multi-User Isolation Implementation

## Batch 1: Server Identity Extraction
- [ ] [RED] Create a test in `EngramServerTests.cs` that fails if `X-Engram-User` is ignored.
- [ ] [GREEN] Implement `GetUserId(HttpContext ctx)` helper in `EngramServer.cs`.
- [ ] [GREEN] Update `HandleAddObservation` to pass the User ID to the store.
- [ ] [GREEN] Update `HandleRecentObservations` to filter by User ID.

## Batch 2: Store Scope Namespacing
- [ ] [RED] Add unit tests in `Engram.Store.Tests` for scope isolation (SQLite).
- [ ] [GREEN] Implement internal scope mapping (`personal` -> `personal:{user}`) in `IStore` or a shared base.
- [ ] [GREEN] Update `SqliteStore` and `PostgresStore` to respect namespaced scopes in search and context.

## Batch 3: MCP Client Identity Forwarding
- [ ] [RED] Add test in `Engram.Mcp.Tests` to verify header injection.
- [ ] [GREEN] Update `Engram.Mcp` to capture OS user name.
- [ ] [GREEN] Configure `HttpStore` (used by MCP) to send `X-Engram-User` header.

## Batch 4: Verification & Docs
- [ ] [ ] Run full integration test suite.
- [ ] [ ] Update `docs/ARCHITECTURE.md` with Multi-User section.
- [ ] [ ] Update `README.md` with instructions for the new header.
