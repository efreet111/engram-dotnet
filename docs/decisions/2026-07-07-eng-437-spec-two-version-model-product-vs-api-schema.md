---
observation_id: 11
type: "decision"
title: "ENG-437 spec: Two-version model (product vs API/schema)"
created_at: "2026-07-07 02:45:16"
topic_key: "architecture/version-model"
project: "engram-dotnet"
scope: "team"
generated_at: "2026-07-21T22:00:59.5861248Z"
---

# ENG-437 spec: Two-version model (product vs API/schema)

**What**: Established that engram-dotnet has TWO independent version numbers: product version (git tags, v1.x) and API/schema version (hardcoded "1.1.0" in /health and export endpoints).

**Why**: These serve different purposes. Product version is what users see (engram version, Docker tags, CHANGELOG). API version is a schema compatibility marker for data formats — changing it would break clients that parse export/health responses expecting "1.1.0".

**Where**: 
- Product version: src/Engram.Cli/Program.cs:35, docker/Dockerfile:6, docker/docker-compose.yml:24, docker/docker-compose.test.yml:37,66,100, scripts/dev-test.sh:15,33, scripts/post-install.sh:9, scripts/post-install.ps1:20
- API version (NEVER change): src/Engram.Server/EngramServer.cs:228, src/Engram.Store/Models.cs:140, src/Engram.Store/SqliteStore.cs:1338,1381,1439, src/Engram.Store/PostgresStore.cs:1234

**Learned**: The version divergence (git tags v1.x vs CHANGELOG v0.x) was caused by independently choosing version schemes for different purposes without reconciling them. The fix aligns everything to v1.x which matches the git tags.
