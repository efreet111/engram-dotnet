---
observation_id: 44
type: "decision"
title: "config.json remote_url no se usa para sync"
created_at: "2026-07-16 02:48:57"
topic_key: "config-json-remote-url-not-used"
project: "team/engram-dotnet"
scope: "personal"
generated_at: "2026-07-21T22:00:59.5713409Z"
---

# config.json remote_url no se usa para sync

**What**: config.json tiene campo sync.remote_url pero SyncManager NO lo lee

**Why**: Engañoso para usuarios. Ven el campo en config.json y piensan que está configurado, pero en realidad no lo está.

**Where**: 
- ~/.engram/config.json (campo sync.remote_url)
- src/Engram.Server/EngramServer.cs:847 (lee solo ENGRAM_SERVER_URL)
- src/Engram.Sync/SyncManagerConfig.cs (FromEnvironment solo lee env vars)

**Current behavior**:
```csharp
// EngramServer.cs:847
var remoteUrl = Environment.GetEnvironmentVariable("ENGRAM_SERVER_URL");
var resolvedSyncUrl = !string.IsNullOrEmpty(remoteUrl)
    ? remoteUrl.TrimEnd('/')
    : $"http://localhost:{cfg.Port}";
```

**config.json actual**:
```json
{
  "sync": {
    "mode": "local",
    "remote_url": "",  ← NO SE USA
    "user": "victor@local.dev"
  }
}
```

**Fix propuesto** (parte de ENG-459):
```csharp
var remoteUrl = Environment.GetEnvironmentVariable("ENGRAM_SERVER_URL")
    ?? config.Sync.RemoteUrl;  ← Fallback a config.json
```

**Learned**: Documentar que config.json no se usa para sync URL, o implementar fallback
