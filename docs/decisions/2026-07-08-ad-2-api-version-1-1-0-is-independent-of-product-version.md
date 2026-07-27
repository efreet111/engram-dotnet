---
observation_id: 19
type: "architecture"
title: "AD-2: API version \"1.1.0\" is independent of product version"
created_at: "2026-07-08 00:55:41"
topic_key: "architecture/api-version-independence"
project: "engram-dotnet"
scope: "team"
generated_at: "2026-07-21T22:00:59.5828985Z"
---

# AD-2: API version "1.1.0" is independent of product version

**What**: The string "1.1.0" in EngramServer.cs (line 228), Models.cs (line 140), SqliteStore.cs (lines 1338, 1381, 1439), and PostgresStore.cs (line 1234) represents the API/schema version, NOT the product version. It must never be changed by product version bumps.

**Why**: These are schema/format version markers. /health returns it for API compatibility negotiation. ExportData.Version uses it as the data format version. Changing it during product bumps would break API consumers who check for "1.1.0" and signal a schema change that hasn't happened.

**Where**: src/Engram.Server/EngramServer.cs:228, src/Engram.Store/Models.cs:140, src/Engram.Store/SqliteStore.cs:1338,1381,1439, src/Engram.Store/PostgresStore.cs:1234

**Learned**: 
- TIPO B = API/schema version = 6 occurrences of "1.1.0" that are NEVER touched during product releases
- These 4 files should be added to a release checklist under "FILES TO VERIFY UNCHANGED"
- The distinction was confusing because both versions were "0.3.0" in code before — now product is "1.3.0" and API stays "1.1.0"
