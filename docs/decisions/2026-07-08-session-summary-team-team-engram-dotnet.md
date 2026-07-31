---
observation_id: 22
type: "session_summary"
title: "Session summary: team/team/engram-dotnet"
created_at: "2026-07-08 00:56:52"
project: "engram-dotnet"
scope: "team"
generated_at: "2026-07-21T22:00:59.5818096Z"
---

# Session summary: team/team/engram-dotnet

## Goal
Release v1.3.0 — unify product version to "1.3.0" across code, Docker, scripts, docs, and CHANGELOG. Fix the four-way version divergence (git tags=v1.x, CHANGELOG=v0.x, code="0.3.0", /health="1.1.0"). Establish the two-version model (product vs API/schema) as the canonical versioning scheme.

## Instructions
- forge-memory (Phase 4 closure) should persist key architectural decisions as engram observations AND promote durable ones to ADR files
- Two-version model: product version = git tags/TIPO A files; API/schema version = TIPO B files ("1.1.0"), NEVER touch during product bumps
- Historical docs (MIGRATION.md, SYNC-SETUP.md, ADR-004) are immutable — only live docs get updated
- CHANGELOG alignment is chronological, not content-based

## Discoveries
- The four-way version conflict was caused by a premature v1.x git tagging scheme that was never reconciled with the CHANGELOG's v0.x headers
- No v0.1.0, v0.2.0, or v0.3.0 git tags ever existed — the CHANGELOG links were all broken
- v1.2.0 git tag exists but lacks a separate CHANGELOG section; its contents are covered under [1.2.1]
- The /health endpoint returns API version, not product version — this was confusing because both were "0.3.0" in code before
- Directory.Build.props centralized versioning is a separate concern (ENG-304) and was explicitly out of scope
- GIT-WORKFLOW.md release procedure examples were made generic (vX.Y.Z) so they don't need updating every release
- TIPO B files contain 6 total occurrences of "1.1.0" across 4 files — must verify unchanged on every release

## Accomplished
- ✅ **Two-version model documented**: Product version (git tags + TIPO A) vs API/schema version (TIPO B "1.1.0") — fully independent
- ✅ **17 files modified**: 14 code/docs + 3 .ai-work artifacts. No TIPO B files touched. Historical docs preserved.
- ✅ **CHANGELOG rewritten**: Headers [0.3.0]→[1.2.1], [0.2.0]→[1.1.0], [0.1.0]→[1.0.0]; new [1.3.0] for release; new empty [Unreleased]; all 5 footer links updated to valid existing tags
- ✅ **Build & tests**: 0 errors, 53 preexisting warnings, 615 passed / 14 skipped / 0 failed
- ✅ **Git tag v1.3.0**: Annotated, local only (not pushed — git-sin-push rule)
- ✅ **ADR-009 created**: Two-version model decision record
- ✅ **ADR-010 created**: Historical docs immutability policy
- ✅ **ADR README updated**: List now includes ADR-004 through ADR-010, plus gaps documented
- ✅ **5 memory observations persisted**: Two-version model, CHANGELOG mapping, AD-2 (API independence), AD-3 (historical immutability), ENG-437 implementation summary

## Relevant Files
- .ai-work/eng-437-release-v040/spec.md — Full specification with CKP-0 decisions, file inventory, CHANGELOG strategy
- .ai-work/eng-437-release-v040/plan.md — 14-task implementation plan
- .ai-work/eng-437-release-v040/verify-report.md — PASS verdict, all 19 requirements verified
- .ai-work/eng-437-release-v040/context-map.md — Phase 0 discovery with validated file inventory
- CHANGELOG.md — Headers/footer links rewritten, version drift fixed
- src/Engram.Cli/Program.cs:35 — Product version changed to "1.3.0"
- src/Engram.Server/EngramServer.cs:228 — API version "1.1.0" preserved (TIPO B, unchanged)
- src/Engram.Store/Models.cs:140 — Schema version "1.1.0" preserved (TIPO B, unchanged)
- src/Engram.Store/SqliteStore.cs:1338,1381,1439 — Export version "1.1.0" preserved (TIPO B, unchanged)
- src/Engram.Store/PostgresStore.cs:1234 — Export version "1.1.0" preserved (TIPO B, unchanged)
- docker/Dockerfile, docker-compose.yml, docker-compose.test.yml — Product version bumped to v1.3.0
- scripts/dev-test.sh, post-install.sh, post-install.ps1 — Product version bumped to 1.3.0/v1.3.0
- docs/01-QUICK-START.md, docker/README.md, docs/POSTGRES-SETUP.md — /health examples → "1.1.0" (API version)
- docs/GIT-WORKFLOW.md — Release procedure examples → generic vX.Y.Z; latest tag → v1.3.0
- docs/ROADMAP.md — Version table → 1.3.0
- docs/BACKLOG.md — ENG-437 → Done, version references corrected
- docs/architecture/adr/ADR-009-two-version-model.md — NEW: Two-version model decision record
- docs/architecture/adr/ADR-010-historical-docs-immutability.md — NEW: Historical docs policy
- docs/architecture/adr/README.md — Updated with all ADRs 001-010

## Next Steps (human)
1. 👤 Verify the release commit (61f839f) and approve
2. 👤 Push tag v1.3.0 to origin: `git push origin v1.3.0`
3. 👤 Create GitHub Release from tag v1.3.0
4. 👤 Consider ENG-304 (centralized versioning via Directory.Build.props) for future releases
