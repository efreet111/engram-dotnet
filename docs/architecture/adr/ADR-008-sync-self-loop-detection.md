# ADR-008: Self-loop detection for SyncManager

**Status:** Proposed
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

1. `ENGRAM_SERVER_URL` is unset AND `cfg.Host == "0.0.0.0"` (or any) AND `port` is the same as the configured port — i.e. the default `http://localhost:{port}` would equal the server's own address.
2. `ENGRAM_SERVER_URL` is set explicitly to `http://localhost:{port}` (or `http://127.0.0.1:{port}`) where `{port}` matches the server port.
3. `ENGRAM_SERVER_URL` is set to a URL where host resolves to the same loopback address AND port matches.

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

In `EngramServer.cs`, change the SyncManager registration block to:

```csharp
if (syncConfig.Enabled && store is ILocalSyncStore localSyncStore)
{
    var remoteUrl = Environment.GetEnvironmentVariable("ENGRAM_SERVER_URL");
    var resolvedUrl = !string.IsNullOrEmpty(remoteUrl)
        ? remoteUrl.TrimEnd('/')
        : $"http://localhost:{cfg.Port}";

    if (IsSelfLoop(resolvedUrl, cfg))
    {
        // Log warning and skip SyncManager registration
    }
    else
    {
        // Register SyncManager as before
    }
}
```

`IsSelfLoop` parses the URL, compares host:port against the server's bind address and port. Treats `0.0.0.0`, `localhost`, and `127.0.0.1` as loopback.

## Test plan

- Unit test: `IsSelfLoop("http://localhost:7437", port=7437)` → `true`
- Unit test: `IsSelfLoop("http://192.168.1.5:7437", port=7437)` → `false`
- Unit test: `IsSelfLoop("http://localhost:8000", port=7437)` → `false`
- Integration test: `engram serve` with no `ENGRAM_SERVER_URL` → warning logged, no SyncManager, no 501 spam
- Integration test: `engram serve` with valid remote URL → SyncManager registers, no warning
