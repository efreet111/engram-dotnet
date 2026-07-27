---
observation_id: 18
type: "decision"
title: "CHANGELOG-git tag mapping alignment strategy"
created_at: "2026-07-08 00:55:38"
topic_key: "changelog/tag-mapping"
project: "engram-dotnet"
scope: "team"
generated_at: "2026-07-21T22:00:59.5831927Z"
---

# CHANGELOG-git tag mapping alignment strategy

**What**: Mapped CHANGELOG historical version headers to actual git tags by chronological order: [0.1.0]→[1.0.0], [0.2.0]→[1.1.0], [0.3.0]→[1.2.1]. The new release is [1.3.0].

**Why**: CHANGELOG used v0.x headers that matched NO existing git tags. Footer links pointed to tags that don't exist (v0.1.0, v0.2.0, v0.3.0). Contributors saw broken links and version drift. The mapping is purely chronological, not content-based.

**Where**: CHANGELOG.md (headers, footer links), .ai-work/eng-437-release-v040/spec.md §6

**Learned**: 
- v1.2.0 has no separate CHANGELOG entry — v1.2.0→v1.2.1 was a small patch, both covered by [1.2.1] section
- Footer links before: all broken (v0.x.0 tags don't exist); after: all point to real tags
- Dates in CHANGELOG must be preserved (they're the original release dates, not changed)
- The mapping table in spec.md §6 is the canonical reference for future reconciliation
- Content (bullet points) must NEVER change — only headers and links
