---
observation_id: 13
type: "decision"
title: "Two-version model: product (git tags) vs API/schema (code constant)"
created_at: "2026-07-07 02:45:59"
topic_key: "architecture/versioning-scheme"
project: "engram-dotnet"
scope: "team"
generated_at: "2026-07-21T22:00:59.5852567Z"
---

# Two-version model: product (git tags) vs API/schema (code constant)

**What**: Two-version model established for engram-dotnet: product version (git tags, follows v1.x scheme) vs API/schema version (hardcoded "1.1.0" in /health endpoint and ExportData).

**Why**: Git tags were v1.0.0→v1.2.1 but CHANGELOG said v0.1.0→v0.3.0. Code said "0.3.0" while /health said "1.1.0". Decision: product version = git tags (v1.3.0 next), API version = independent constant that only changes on API contract changes.

**Where**: Product version in Program.cs, Dockerfile, docker-compose, scripts. API version in EngramServer.cs:228 (/health), Models.cs:140 (ExportData), SqliteStore.cs (1338,1381,1439), PostgresStore.cs:1234.

**Learned**: CHANGELOG headers must align with git tags chronologically: [0.1.0]→[1.0.0], [0.2.0]→[1.1.0], [0.3.0]→[1.2.1]. The v1.2.0 tag has no separate CHANGELOG section (absorbed into v1.2.1). Historical docs (MIGRATION.md, SYNC-SETUP.md, ADR-004) are immutable records — never update them on version bumps.
