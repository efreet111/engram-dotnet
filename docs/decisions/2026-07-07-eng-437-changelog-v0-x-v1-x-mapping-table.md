---
observation_id: 12
type: "decision"
title: "ENG-437 CHANGELOG v0.x → v1.x mapping table"
created_at: "2026-07-07 02:45:24"
topic_key: "eng-437/changelog-mapping"
project: "engram-dotnet"
scope: "team"
generated_at: "2026-07-21T22:00:59.5856509Z"
---

# ENG-437 CHANGELOG v0.x → v1.x mapping table

**What**: Defined the CHANGELOG header rewrite mapping for ENG-437 (Release v1.3.0). Old v0.x headers that don't correspond to any git tag are renamed to match existing git tags chronologically.

**Why**: Git tags follow v1.0.0→v1.2.1 but CHANGELOG uses v0.1.0→v0.3.0. The v0.x tags don't exist, so CHANGELOG links are broken.

**Where**: CHANGELOG.md — headers and footer links only. Content (bullet points) unchanged.

**Mapping**:
- `[0.1.0] — 2026-04-20` → `[1.0.0] — 2026-04-20` (Obsidian Export)
- `[0.2.0] — 2026-04-30` → `[1.1.0] — 2026-04-30` (PostgreSQL Backend)
- `[0.3.0] — 2026-05-11` → `[1.2.1] — 2026-05-11` (Session Activity Tracker)
- `[Unreleased]` → `[1.3.0] — 2026-07-06` (22+ commits since v1.2.1)

**Learned**: v1.2.0 tag exists but has no separate CHANGELOG section — it was a small patch before v1.2.1. Both are covered by the [1.2.1] section.
