---
observation_id: 16
type: "discovery"
title: "ENG-437 implementation: release v1.3.0 version unification"
created_at: "2026-07-07 21:15:00"
topic_key: "eng-437-implementation"
project: "engram-dotnet"
scope: "team"
generated_at: "2026-07-21T22:00:59.5839075Z"
---

# ENG-437 implementation: release v1.3.0 version unification

**What**: ENG-437 implementation completed — release v1.3.0 with version string unification and CHANGELOG alignment.

**Why**: Git tags followed v1.x scheme (v1.0.0→v1.2.1) but CHANGELOG said v0.x (v0.1.0→v0.3.0). Code said "0.3.0" while /health said "1.1.0". Decision: product version = git tags (v1.3.0), API version = independent constant ("1.1.0").

**Where**: 17 files modified — Program.cs, Dockerfile, docker-compose.yml, docker-compose.test.yml, dev-test.sh, post-install.sh/ps1, 5 docs, CHANGELOG.md, BACKLOG.md, GIT-WORKFLOW.md, ROADMAP.md. Commit 61f839f, tag v1.3.0.

**Learned**: 
1. When doing version bumps, grep for ALL occurrences including parsing comments and historical log entries — the spec's file inventory may not catch every instance (post-install.sh:43 and post-install.ps1:50 had parsing comments not in spec).
2. GIT-WORKFLOW release examples now use generic vX.Y.Z placeholders instead of hardcoded version numbers to avoid staleness.
3. .NET SDK not available in opencode agent environment — build/test tasks require manual execution by human.
4. CHANGELOG-git tag mapping is chronological: [0.1.0]→[1.0.0], [0.2.0]→[1.1.0], [0.3.0]→[1.2.1]. The v1.2.0 tag has no separate CHANGELOG section (absorbed into v1.2.1).
