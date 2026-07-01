# ADR-008: Self-loop detection for SyncManager

**Status:** Accepted
**Date:** 2026-07-01
**Deciders:** victor
**Related:** ENG-452, ADR-007, ENG-451

## Context

When `engram serve` runs with a local SQLite store and no `ENGRAM_SERVER_URL` configured, the embedded `SyncManager` defaults to `http://localhost:{port}` — pointing back at the same process that hosts it.

This causes:

1. **HTTP 501 responses** on `/sync/mutations/pull` and `/sync/mutations/push` — the local SQLite store does not implement `ICloudMutationStore` (the relay interface), so the endpoints return 501.
2. **Log noise** — every 30ms the SyncManager tries to pull and logs `Mutation transport failed with status 501`.
3. **Wasted CPU** — backoff logic only delays; the cycle repeats indefinitely.
4. **Confusing UX** — user sees sync errors with no clear explanation of what to fix.

This was discovered during ENG-451 verification: even after fixing the recovery of orphaned pulled mutations, the SyncManager remained stuck because it was talking to itself.

The FlowForge installer (v0.1.0-alpha.6) installs in `mode=sync` but does **not** persist `ENGRAM_SERVER_URL`, so the default self-loop path is hit on every fresh install.

## Decision

`engram serve` will detect self-loop and **disable the SyncManager with a clear warning** when the resolved `ENGRAM_SERVER_URL` points to the same host:port as the server itself.

### Detection rules

The SyncManager is disabled if any of these match:

1. `ENGRAM_SERVER_URL` is unset, so the default `http://localhost:{port}` is used — and that port matches the server's own port. The server binds to `0.0.0.0` (wildcard), so a request to `localhost:{port}` reaches the same process.
2. `ENGRAM_SERVER_URL` is set explicitly to `http://localhost:{port}` (or `http://127.0.0.1:{port}`, `http://[::1]:{port}`) where `{port}` matches the server port.
3. `ENGRAM_SERVER_URL` is set to a URL where host resolves to a loopback name AND port matches.

DNS resolution is intentionally avoided — the check is purely lexical against literal loopback names (`localhost`, `127.0.0.1`, `::1`). This keeps startup fast and offline-safe.

### Behavior when self-loop detected

```
warning: SyncManager disabled — ENGRAM_SERVER_URL points to this server itself
  configured: http://localhost:7437
  this server: http://0.0.0.0:7437
  Set ENGRAM_SERVER_URL to a remote sync server, or ENGRAM_SYNC_ENABLED=false to silence this warning.
```

The `engram serve` process continues to serve HTTP traffic and accepts local writes; only the SyncManager (background push/pull cycle) is skipped.

### Behavior when ENGRAM_SERVER_URL points to remote

SyncManager registers normally with the configured URL. No change from current behavior.

### Why not validate at install time

The FlowForge installer cannot know the user's intended sync target — that is user knowledge. Validation at runtime is the only safe place. The user gets a clear actionable error pointing at the env var to set.

## Consequences

### Positive

- No more 501 spam in logs
- No wasted CPU on doomed sync cycles
- Clear actionable error message for misconfiguration
- Local-only usage no longer requires `ENGRAM_SYNC_ENABLED=false` workaround
- Server with `ICloudMutationStore` (PostgreSQL relay) still works — it has a real sync target

### Negative

- None for local-only users (they get local-only behavior, which is what they want)
- Users with valid remote `ENGRAM_SERVER_URL` see no change
- Users who intentionally set `ENGRAM_SERVER_URL=http://localhost:7437` thinking they need it (e.g. for testing) will see the warning — this is correct, they should remove it

### Follow-up

- **FlowForge installer** (separate repo): when installing in `mode=sync`, must ask the user for the remote `ENGRAM_SERVER_URL` and persist it. The current `mode=sync` install that does not persist is a **separate bug** filed as ENG-453 in FlowForge.

## Implementation

In `src/Engram.Server/EngramServer.cs`, the SyncManager registration block (lines 57-92) now resolves the sync URL once, checks for self-loop, and either logs a warning + skips registration or registers normally.

The helper `IsSyncSelfLoop(string resolvedSyncUrl, int serverPort)` is `public static` to be unit-testable without spinning up a full server. It returns `true` when:
- The URL is `http` or `https`
- The port matches the server's port (default port for scheme used if unspecified)
- The host is a literal loopback name (`localhost`, `127.0.0.1`, `::1`)

## Test plan

Unit tests in `tests/Engram.Server.Tests/SyncSelfLoopDetectionTests.cs` (12 cases):

| Input | Server port | Expected |
|-------|-------------|----------|
| `http://localhost:7437` | 7437 | `true` |
| `http://127.0.0.1:7437` | 7437 | `true` |
| `http://LOCALHOST:7437` | 7437 | `true` (case-insensitive) |
| `http://[::1]:7437` | 7437 | `true` |
| `http://192.168.1.5:7437` | 7437 | `false` |
| `http://10.0.0.1:7437` | 7437 | `false` |
| `http://sync.example.com:7437` | 7437 | `false` |
| `http://localhost:8000` | 7437 | `false` (port mismatch) |
| `http://127.0.0.1:9999` | 7437 | `false` (port mismatch) |
| `not-a-url` | 7437 | `false` (parse failure) |
| `""` (empty) | 7437 | `false` (parse failure) |
| `ftp://localhost:7437` | 7437 | `false` (unsupported scheme) |

Integration verification (manual): running `engram serve` locally on a fresh install now shows the warning and produces no 501 spam. To verify in your local environment:

```bash
pkill -f "engram serve"
engram serve 2>&1 | head -20
# Expect: "[engram] warning: SyncManager disabled — ENGRAM_SERVER_URL points to this server itself"
```
