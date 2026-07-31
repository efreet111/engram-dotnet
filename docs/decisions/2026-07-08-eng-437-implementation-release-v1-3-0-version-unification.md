---
observation_id: 21
type: "discovery"
title: "ENG-437 implementation: release v1.3.0 version unification"
created_at: "2026-07-08 00:55:48"
topic_key: "release/eng-437-v1.3.0"
project: "engram-dotnet"
scope: "team"
generated_at: "2026-07-21T22:00:59.5822492Z"
---

# ENG-437 implementation: release v1.3.0 version unification

**What**: ENG-437 completed — release v1.3.0 with version string unification and CHANGELOG alignment. 17 files modified (14 code/docs + 3 .ai-work artifacts). Product version unified to "1.3.0" across TIPO A files (Program.cs, Docker, scripts). CHANGELOG headers and footer links rewritten to match git tags. Docs live updated. Git tag v1.3.0 created locally (not pushed).

**Why**: Four-way version chaos: git tags=v1.x, CHANGELOG=v0.x, code="0.3.0", /health="1.1.0". Users couldn't determine the running version. CHANGELOG links were broken. The release needed to happen before new work started on main.

**Where**: Commit 61f839f. Files: CHANGELOG.md, src/Engram.Cli/Program.cs, docker/Dockerfile, docker/docker-compose.yml, docker/docker-compose.test.yml, docker/README.md, docs/01-QUICK-START.md, docs/POSTGRES-SETUP.md, docs/GIT-WORKFLOW.md, docs/ROADMAP.md, docs/BACKLOG.md, scripts/dev-test.sh, scripts/post-install.sh, scripts/post-install.ps1

**Learned**: 
- 615 tests passed, 14 skipped, 0 failed — no regressions
- 0 build errors, 53 preexisting warnings (none introduced)
- Verify: ALL 19 requirements (F01-F12, N01-N07) PASS per verify-report
- Git tag v1.3.0 is annotated and local-only (git-sin-push rule)
- Next steps for human: verify, approve, push tag to origin, create GitHub Release
