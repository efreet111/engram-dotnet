# Spec — ENG-437: Release v1.3.0 + fix version string chaos + CHANGELOG alignment

> **Phase 1 (forge-arch) | Date: 2026-07-06**
>
> CKP-0 decisions resolved. No blockers.

---

## 1. Overview

### What

Release v1.3.0 — unify product version to `1.3.0` across code, Docker, scripts, docs, and CHANGELOG. Fix the version divergence where:

- **Git tags** follow the `v1.x` scheme (`v1.0.0` → `v1.1.0` → `v1.2.0` → `v1.2.1`)
- **CHANGELOG** follows the `v0.x` scheme (`[0.1.0]` → `[0.2.0]` → `[0.3.0]`)
- **Code** says `"0.3.0"` (Program.cs) while docs say `"1.1.0"` (API version) and `"0.3.0"` (demos)

The release tags `v0.1.0`, `v0.2.0`, `v0.3.0` do NOT exist in git. The CHANGELOG links to `compare/v0.3.0...HEAD` (broken).

### Why

- Users cloning `main` see 22+ commits in `[Unreleased]` with no tag — unclear what version they're running.
- `/health` returns `"1.1.0"` while `engram version` prints `0.3.0` — contradictory.
- The CHANGELOG version numbers don't match any existing git tag or GitHub Release — broken traceability.
- OSS contributors see v0.x in CHANGELOG but the project tags say v1.x — confusing.

### For whom

- **End users** running the CLI/Docker — `engram version` will now report a coherent version.
- **Contributors** — the CHANGELOG now maps 1:1 to git tags.
- **CI/CD** — Docker build args align with actual tag names.

---

## 2. Requirements

### 2.1 Functional Requirements

| ID | Description | Priority |
|----|-------------|----------|
| **REQ-437-F01** | `engram version` (and `--version`) MUST print `1.3.0` after changes | P0 |
| **REQ-437-F02** | All TIPO A files (product version) MUST be updated from `0.3.0`/`v0.3.0` to `1.3.0`/`v1.3.0` | P0 |
| **REQ-437-F03** | TIPO B files (API/schema version `"1.1.0"`) MUST NOT be changed | P0 |
| **REQ-437-F04** | CHANGELOG header `## [Unreleased]` MUST be renamed to `## [1.3.0] — 2026-07-06` | P0 |
| **REQ-437-F05** | A new empty `## [Unreleased]` section MUST be created above `## [1.3.0]` | P0 |
| **REQ-437-F06** | CHANGELOG historical version headers MUST be rewritten to match git tags: `[0.3.0]` → `[1.2.1]`, `[0.2.0]` → `[1.1.0]`, `[0.1.0]` → `[1.0.0]` | P0 |
| **REQ-437-F07** | CHANGELOG date on renamed headers MUST be preserved as-is (the original release date) | P0 |
| **REQ-437-F08** | CHANGELOG footer links MUST be updated to point to actual git tags/releases: `[unreleased]` → `compare/v1.3.0...HEAD`, new `[1.3.0]` link added, `[0.3.0]` → `[1.2.1]`, `[0.2.0]` → `[1.1.0]`, `[0.1.0]` → `[1.0.0]` | P0 |
| **REQ-437-F09** | Docs live (C1-C3, C7-C8) MUST be updated per the file inventory in §5.1 | P0 |
| **REQ-437-F10** | Docs históricas (C4, C5, C9) MUST NOT be modified | P0 |
| **REQ-437-F11** | A git tag `v1.3.0` MUST be created (annotated, with message `Release v1.3.0`) **but NOT pushed** (git-sin-push rule) | P0 |
| **REQ-437-F12** | CHANGELOG content (bullet points, descriptions) MUST remain unchanged — only headers, dates, and links change | P1 |

### 2.2 Non-Functional Requirements

| ID | Description | Priority |
|----|-------------|----------|
| **REQ-437-N01** | `dotnet build -c Release` MUST pass without errors or warnings | P0 |
| **REQ-437-N02** | All existing tests MUST pass (`dotnet test -c Release --filter "FullyQualifiedName!~Engram.Postgres.Tests&Category!=RequiresDocker"`) | P0 |
| **REQ-437-N03** | No breaking changes to `/health`, `/stats`, or any API endpoint | P0 |
| **REQ-437-N04** | All TIPO B files retain the exact string `"1.1.0"` — verify with grep post-change | P0 |
| **REQ-437-N05** | Git working tree MUST be clean before tagging (no uncommitted changes outside the scope) | P0 |
| **REQ-437-N06** | No push to `main` or `origin` without explicit human approval | P0 |
| **REQ-437-N07** | Commit message MUST follow Conventional Commits: `chore: release v1.3.0 — unify version strings and CHANGELOG alignment` | P0 |

---

## 3. Scope

### In scope

| Item | Rationale |
|------|-----------|
| TIPO A version bump (7 locations) | Product version — the single source of truth |
| CHANGELOG header rewrite | Align with git tags |
| CHANGELOG footer link update | Fix broken `compare/` and `releases/tag/` URLs |
| Docs live update (C1-C3, C7-C8) | Public-facing docs must reflect current reality |
| Git tag creation | Formalize the release (never pushed) |
| Build + test verification | Regression gate |
| `docs/BACKLOG.md` status update | Mark ENG-437 as Done |

### Out of scope

| Item | Reason |
|------|--------|
| Push git tag to origin | Requires explicit human approval (git-sin-push) |
| GitHub Release notes creation | Post-tag step, human-driven |
| Directory.Build.props `<Version>` | Out of scope per human decision Q4 |
| API/schema version changes (TIPO B) | `"1.1.0"` is API version, not product version (Q2) |
| Historical docs (C4, C5, C9) | Immutable records (Q3) |
| `.ai-work/`, `sdd/`, session reports | Historical artifacts — never modified |
| `global.json` SDK version | Unrelated to product version |
| `src/Engram.Obsidian/Exporter.cs` | Internal state version, not user-facing |
| `release.yml` workflow | May need ENGRAM_VERSION update, but that's CI-level and separate |
| Docker image rebuild/test | The spec covers string changes only |

---

## 4. Architecture Decisions (CKP-0 resolved)

| # | Decision | Rationale |
|---|----------|-----------|
| **AD-1** | Tag = `v1.3.0` | Git tags follow `v1.0.0` → `v1.2.1` scheme. CHANGELOG `v0.x` is the anomaly. |
| **AD-2** | `/health` and ExportData `"1.1.0"` = API version, NOT product version | These are schema version markers for API compatibility — independent of the marketing/product version. `"1.1.0"` stays. |
| **AD-3** | Docs live → update; docs históricas (MIGRATION, SYNC-SETUP, ADR-004) → leave | Historical docs are immutable records of "what was true at the time." Live docs must reflect current state. |
| **AD-4** | Directory.Build.props → not touched | Outside this chore's scope. Centralized versioning is a separate concern (ENG-304). |

---

## 5. File Inventory

### 5.1 Files TO CHANGE

#### TIPO A — Product version (`0.3.0`/`v0.3.0` → `1.3.0`/`v1.3.0`)

| # | File | Line(s) | Current | New | Note |
|---|------|---------|---------|-----|------|
| A1 | `src/Engram.Cli/Program.cs` | 35 | `const string Version = "0.3.0";` | `const string Version = "1.3.0";` | Source of truth for `engram version` |
| A2 | `docker/Dockerfile` | 6 | `ARG ENGRAM_VERSION=v0.3.0` | `ARG ENGRAM_VERSION=v1.3.0` | Also changes default download URL |
| A3 | `docker/docker-compose.yml` | 24 | `ENGRAM_VERSION: v0.3.0` | `ENGRAM_VERSION: v1.3.0` | Build arg for prod compose |
| A4a | `docker/docker-compose.test.yml` | 37 | `ENGRAM_VERSION: v0.3.0` | `ENGRAM_VERSION: v1.3.0` | Server service |
| A4b | `docker/docker-compose.test.yml` | 66 | `ENGRAM_VERSION: v0.3.0` | `ENGRAM_VERSION: v1.3.0` | Client-A service |
| A4c | `docker/docker-compose.test.yml` | 100 | `ENGRAM_VERSION: v0.3.0` | `ENGRAM_VERSION: v1.3.0` | Client-B service |
| A5a | `scripts/dev-test.sh` | 15 (comment) | `ENGRAM_VERSION - build arg version tag (default v0.3.0; must be semver)` | `ENGRAM_VERSION - build arg version tag (default v1.3.0; must be semver)` | Comment only |
| A5b | `scripts/dev-test.sh` | 33 | `ENGRAM_VERSION="${ENGRAM_VERSION:-v0.3.0}"` | `ENGRAM_VERSION="${ENGRAM_VERSION:-v1.3.0}"` | Default value |
| A6 | `scripts/post-install.sh` | 9 (comment) | `--engram-version 0.3.0` | `--engram-version 1.3.0` | Example in usage comment |
| A7 | `scripts/post-install.ps1` | 20 (comment) | `-EngramVersion "0.3.0"` | `-EngramVersion "1.3.0"` | Example in usage comment |

#### TIPO C — Docs live

| # | File | Line | Current | New | Rationale |
|---|------|------|---------|-----|-----------|
| C1 | `docker/README.md` | 91 | `"version":"0.3.0"` | `"version":"1.1.0"` | Example of `/health` response → API version, not product version |
| C2 | `docs/01-QUICK-START.md` | 34 | `"version":"0.3.0"` | `"version":"1.1.0"` | Same — `/health` example |
| C3 | `docs/POSTGRES-SETUP.md` | 143 | `"version":"0.3.0"` | `"version":"1.1.0"` | Same — `/health` example |
| C7 | `docs/GIT-WORKFLOW.md` | 187 | `tag: v0.3.0` | `tag: v1.2.1` (or `v1.3.0` after release) | Reference to "último tag" in paragraph text |
| C8 | `docs/ROADMAP.md` | 31 | `Version 0.3.0` | `Version 1.3.0` | Table entry for "Doc/code sync + MCP tools" row |

**Note on C7 (GIT-WORKFLOW):** The paragraph on line 187 says: _"Entre releases, main puede ir adelantada respecto al último tag (como ahora: tag v0.3.0, código con más fixes)."_ This should become `v1.3.0` after the tag is created, since v1.3.0 will be the new "último tag". The release procedure on lines 165-183 also references `v0.4.0` in examples — these should be updated to `v1.3.0`.

#### CHANGELOG (TIPO D)

| # | Section | Change |
|---|---------|--------|
| D1 | Line 6: `## [Unreleased]` | Rename to `## [1.3.0] — 2026-07-06` |
| D2 | Above D1 | Insert `## [Unreleased]\n\n(empty — next release)\n\n` |
| D3 | Line 83: `## [0.3.0] — 2026-05-11` | Rename to `## [1.2.1] — 2026-05-11` |
| D4 | Line 127: `## [0.2.0] — 2026-04-30` | Rename to `## [1.1.0] — 2026-04-30` |
| D5 | Line 145: `## [0.1.0] — 2026-04-20` | Rename to `## [1.0.0] — 2026-04-20` |
| D6 | Line 162: `[unreleased]: .../compare/v0.3.0...HEAD` | Update to `[unreleased]: .../compare/v1.3.0...HEAD` |
| D7 | Insert after D6 | Add `[1.3.0]: https://github.com/efreet111/engram-dotnet/releases/tag/v1.3.0` |
| D8 | Line 163: `[0.3.0]: .../releases/tag/v0.3.0` | Update to `[1.2.1]: https://github.com/efreet111/engram-dotnet/releases/tag/v1.2.1` |
| D9 | Line 164: `[0.2.0]: .../releases/tag/v0.2.0` | Update to `[1.1.0]: https://github.com/efreet111/engram-dotnet/releases/tag/v1.1.0` |
| D10 | Line 165: `[0.1.0]: .../releases/tag/v0.1.0` | Update to `[1.0.0]: https://github.com/efreet111/engram-dotnet/releases/tag/v1.0.0` |

#### Documentation: release procedure (GIT-WORKFLOW lines 165-183)

| # | Line | Current | New |
|---|------|---------|-----|
| G1 | 170 | `mover [Unreleased] a [v0.4.0] — fecha` | `mover [Unreleased] a [vX.Y.Z] — fecha` (generic) |
| G2 | 179 | `chore: prepare release v0.4.0` | `chore: release vX.Y.Z — unify version` |
| G3 | 180 | `git tag -a v0.4.0 -m "Release v0.4.0"` | `git tag -a vX.Y.Z -m "Release vX.Y.Z"` |

**Note:** GIT-WORKFLOW examples use `v0.4.0` as a placeholder. Since the actual next version is `v1.3.0`, these examples should be updated — but since GIT-WORKFLOW is a process doc, it's better to make the examples generic or bump to `v1.4.0` (next future version). Decision: make them generic placeholders (`vX.Y.Z`) so they don't need updating every release.

#### BACKLOG status

| # | File | Line(s) | Current | New |
|---|------|---------|---------|-----|
| B1 | `docs/BACKLOG.md` | 107 | `ENG-437 \| P0 \| Chore \| Release v0.4.0... \| Ready` | `ENG-437 \| P0 \| Chore \| Release v1.3.0... \| Done` |
| B2 | `docs/BACKLOG.md` | 502 | `ENG-437 — Release v0.4.0 + fix version string chaos` | `ENG-437 — Release v1.3.0 + fix version string chaos` |
| B3 | `docs/BACKLOG.md` | 510-516 | Criteria list mentioning `v0.4.0` | Update to `v1.3.0` and mark as complete |

### 5.2 Files to NEVER touch

| # | File | Reason |
|---|------|--------|
| TIPO B1 | `src/Engram.Server/EngramServer.cs:228` | API version `"1.1.0"` in `/health` — schema version, not product |
| TIPO B2 | `src/Engram.Store/Models.cs:140` | ExportData.Version default `"1.1.0"` — schema version |
| TIPO B3 | `src/Engram.Store/SqliteStore.cs:1338, 1381, 1439` | Export methods use `"1.1.0"` as data format version |
| TIPO B4 | `src/Engram.Store/PostgresStore.cs:1234` | ExportSinceAsync uses `"1.1.0"` |
| C4 | `docs/MIGRATION.md` | Historical migration guide from v0.3.0 — immutable |
| C5 | `docs/SYNC-SETUP.md` | Historical minimum requirement — immutable |
| C9 | `docs/architecture/adr/ADR-004-post-install-registration.md` | ADR is immutable decision record |
| — | `docs/SESSION-REPORT-*.md` | Historical session records — never modified |
| — | `docs/architecture/adr/ADR-*` (all) | ADRs are immutable |
| — | `.ai-work/` (all files) | FlowForge artifacts — historical |
| — | `sdd/` (all files) | Legacy spec proposals — historical |
| — | `global.json` | SDK version, unrelated to product version |
| — | `src/Engram.Obsidian/Exporter.cs` | Internal state version, not user-facing |
| — | `docs/API-REFERENCE.md` line 21 | Already says `"1.1.0"` — correct |
| — | `docs/INSTALLER-TROUBLESHOOTING.md` line 147 | References `0.4.0` but as an error recovery example — leave as-is (not version-sensitive) |

### 5.3 Verification checklist (post-change grep)

After all changes, the following grep commands MUST return expected counts:

```bash
# TIPO A: All product version strings now say 1.3.0
grep -rn '"0\.3\.0"' src/Engram.Cli/Program.cs | wc -l        # → 0
grep -rn 'v0\.3\.0' docker/ scripts/ | wc -l                  # → 0 (docker-compose.test.yml counts 3, Dockerfile 1, compose 1, dev-test.sh 2 = 7 matches → 0)

# TIPO B: All API version strings still say "1.1.0"
grep -rn '"1\.1\.0"' src/Engram.Server/EngramServer.cs | wc -l   # → 1
grep -rn '"1\.1\.0"' src/Engram.Store/ | wc -l                    # → ≥4

# CHANGELOG: No v0.x headers remain
grep -n '\[0\.' CHANGELOG.md | wc -l                          # → 0
# CHANGELOG: v1.x headers exist
grep -n '\[1\.0\.0\]' CHANGELOG.md | wc -l                     # → ≥1
grep -n '\[1\.1\.0\]' CHANGELOG.md | wc -l                     # → ≥1
grep -n '\[1\.2\.1\]' CHANGELOG.md | wc -l                     # → ≥1
grep -n '\[1\.3\.0\]' CHANGELOG.md | wc -l                     # → ≥1
# CHANGELOG: [Unreleased] still exists (new empty one)
grep -n '\[Unreleased\]' CHANGELOG.md | wc -l                  # → 1

# Historical docs NOT touched
grep -rn 'v0\.3\.0' docs/MIGRATION.md | wc -l                  # → ≥1 (still present)
grep -rn 'v0\.3\.0' docs/SYNC-SETUP.md | wc -l                 # → ≥1 (still present)
```

---

## 6. CHANGELOG Rewrite Strategy

### Problem

The CHANGELOG uses version numbers `0.1.0`, `0.2.0`, `0.3.0` that correspond to no existing git tag. Git tags follow `v1.0.0`, `v1.1.0`, `v1.2.0`, `v1.2.1`. The CHANGELOG links also reference `v0.x.0` tags that don't exist.

### Mapping decision

The git tags and CHANGELOG sections are aligned by **release order** (chronological), not by exact content match:

| Git tag | CHANGELOG old header | CHANGELOG new header | Date | Content topic |
|---------|---------------------|---------------------|------|---------------|
| `v1.0.0` | `[0.1.0]` | `[1.0.0]` | 2026-04-20 | Obsidian Export |
| `v1.1.0` | `[0.2.0]` | `[1.1.0]` | 2026-04-30 | PostgreSQL Backend + Upstream Phase 1 |
| `v1.2.1` | `[0.3.0]` | `[1.2.1]` | 2026-05-11 | Session Activity Tracker + Phase 2 API Parity |
| *(new)* | `[Unreleased]` | `[1.3.0]` | 2026-07-06 | Sync recovery, project identity, installer, MCP tools, logging |

**Note on `v1.2.0`:** There is a git tag `v1.2.0` ("cleanup") with no separate CHANGELOG section. The `v1.2.0` → `v1.2.1` delta was likely small (native libs fix). The `[0.3.0]` CHANGELOG section covers work delivered across both `v1.2.0` and `v1.2.1`. We map `[0.3.0]` → `[1.2.1]` because `v1.2.1` is the latest tag in that series and includes everything from `v1.2.0`.

### What changes vs what stays

| Aspect | Change? | Detail |
|--------|---------|--------|
| Version number in header | **YES** | `[0.x.0]` → `[1.x.y]` |
| Release date | **NO** | Preserved from original CHANGELOG |
| Section content (bullet points) | **NO** | Exact same text |
| Footer links (compare/releases) | **YES** | Point to actual existing tags |
| `Prior releases` note (line 159) | **NO** | Remains as-is |

### Footer links transformation

```
# Before (BROKEN — tags don't exist):
[unreleased]: https://github.com/efreet111/engram-dotnet/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/efreet111/engram-dotnet/releases/tag/v0.3.0
[0.2.0]: https://github.com/efreet111/engram-dotnet/releases/tag/v0.2.0
[0.1.0]: https://github.com/efreet111/engram-dotnet/releases/tag/v0.1.0

# After (VALID — tags exist):
[unreleased]: https://github.com/efreet111/engram-dotnet/compare/v1.3.0...HEAD
[1.3.0]: https://github.com/efreet111/engram-dotnet/releases/tag/v1.3.0
[1.2.1]: https://github.com/efreet111/engram-dotnet/releases/tag/v1.2.1
[1.1.0]: https://github.com/efreet111/engram-dotnet/releases/tag/v1.1.0
[1.0.0]: https://github.com/efreet111/engram-dotnet/releases/tag/v1.0.0
```

---

## 7. Acceptance Criteria

### Pre-implementation verification
- [ ] **CKP-0**: All 4 human decisions documented and accepted (AD-1 through AD-4)
- [ ] **CKP-1**: This spec approved by human

### Implementation verification
- [ ] `REQ-437-F01`: `engram version` prints `engram 1.3.0`
- [ ] `REQ-437-F02`: All 9 TIPO A locations updated (A1–A7), grep confirms zero `0.3.0`/`v0.3.0`
- [ ] `REQ-437-F03`: Zero TIPO B files modified (verify with git diff)
- [ ] `REQ-437-F04`: CHANGELOG `[Unreleased]` → `[1.3.0] — 2026-07-06`
- [ ] `REQ-437-F05`: New empty `[Unreleased]` section created
- [ ] `REQ-437-F06`: CHANGELOG headers `[0.3.0]`, `[0.2.0]`, `[0.1.0]` → `[1.2.1]`, `[1.1.0]`, `[1.0.0]`
- [ ] `REQ-437-F07`: Release dates preserved (2026-05-11, 2026-04-30, 2026-04-20)
- [ ] `REQ-437-F08`: All 5 footer links updated to valid tag URLs
- [ ] `REQ-437-F09`: Docs C1, C2, C3 updated to show `"1.1.0"` (API version); C7, C8 updated to `v1.3.0`/`Version 1.3.0`
- [ ] `REQ-437-F10`: C4, C5, C9 untouched (verify with git diff)
- [ ] `REQ-437-F12`: CHANGELOG bullet content unchanged (diff should show only header/link changes)

### Build & test
- [ ] `REQ-437-N01`: `dotnet build -c Release` passes (0 errors, 0 warnings)
- [ ] `REQ-437-N02`: `dotnet test -c Release --filter "FullyQualifiedName!~Engram.Postgres.Tests&Category!=RequiresDocker"` passes
- [ ] `REQ-437-N04`: TIPO B grep confirms `"1.1.0"` unchanged in all 4 locations

### Git
- [ ] `REQ-437-N05`: `git status` shows only expected files modified
- [ ] `REQ-437-N07`: Single commit with correct Conventional Commits message
- [ ] `REQ-437-F11`: `git tag -a v1.3.0 -m "Release v1.3.0"` created locally
- [ ] `REQ-437-N06`: **No push performed** — tags and commits remain local
- [ ] BACKLOG.md updated: ENG-437 status → Done, version references corrected

---

## 8. Risks

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| **CHANGELOG link mismatch**: Renamed headers point to tags that exist but have different release notes content | Medium | Low | The links are for navigation, not content validation. Tags exist; GitHub will show the tag's commit. Release notes (GitHub Releases page) are created separately post-tag. |
| **`v1.2.0` gap**: No CHANGELOG section with `[1.2.0]` header despite git tag existing | Low | Low | `v1.2.0` → `v1.2.1` was a small patch. Both are covered by the `[1.2.1]` CHANGELOG section. Documented in §6. |
| **Docker image rebuild needed**: Changing `ENGRAM_VERSION` in Dockerfile changes the download URL on rebuild | Medium | Medium | The new URL `.../releases/download/v1.3.0/engram-linux-x64` won't exist until the release CI runs. Docker builds using the ARG will fail until the release is published. **Mitigation**: This is expected — the tag is created first, then pushed, then CI publishes the binary. In the meantime, `docker build --build-arg ENGRAM_VERSION=v1.2.1` still works. |
| **Dev-test.sh breakage**: `ENGRAM_VERSION=v1.3.0` will cause Docker build to look for a tag that doesn't exist yet | Medium | Low | `dev-test.sh` builds from local source, not from release download. The ARG is only used if someone overrides it. The script defaults can be kept at `v1.3.0` since local Docker builds ignore the ARG when building from context. |
| **GIT-WORKFLOW example staleness**: The release procedure doc still references `v0.4.0` | Low | Low | Addressed by updating to generic `vX.Y.Z` placeholders. |

---

## 9. Out of Scope (detailed)

These items are explicitly excluded and will NOT be done as part of ENG-437:

1. **Pushing the `v1.3.0` tag** to origin — requires explicit human approval.
2. **Creating GitHub Release notes** — post-tag step, done manually.
3. **Updating `release.yml`** CI workflow — may need `ENGRAM_VERSION` changes but is a separate CI concern.
4. **Adding `<Version>` to Directory.Build.props** — excluded per AD-4.
5. **Changing API/schema version** (`"1.1.0"`) — excluded per AD-2.
6. **Modifying historical artifacts** — `.ai-work/`, `sdd/`, `SESSION-REPORT-*.md`.
7. **Updating `global.json` SDK version** — unrelated to product versioning.
8. **Modifying `src/Engram.Obsidian/Exporter.cs`** — internal state version, not user-facing.
9. **Running Docker smoke tests (T3)** — the spec covers string changes; integration tests remain unchanged.
10. **Updating `docs/INSTALLER-TROUBLESHOOTING.md`** — example values are illustrative, not version-sensitive.
11. **`docs/API-REFERENCE.md`** — already shows correct `"1.1.0"` for `/health`.
12. **Refactoring CHANGELOG content organization** — the multiple `Added`/`Fixed` sections within `[Unreleased]` are messy but outside scope; this task only changes headers/links.

---

## 10. Dependencies

| Dependency | Status | Impact |
|------------|--------|--------|
| CKP-0 decisions (AD-1–AD-4) | **Resolved** | All 4 decisions documented in §4 |
| Git tags `v1.0.0`–`v1.2.1` | **Exist** | Verified: `git tag --sort=-version:refname` confirms all 4 |
| GitHub repo access | **Available** | Needed only to verify existing release URLs (no push) |
| Build toolchain (.NET 10) | **Available** | Required for REQ-437-N01 and REQ-437-N02 |
| `docs/BACKLOG.md` editability | **Available** | Status update for ENG-437 |
| No conflicting branches | **TBD** | Verify `git status` is clean before starting |

---

## Appendix A: Current state snapshot

```
Git tags:          v1.0.0, v1.1.0, v1.2.0, v1.2.1
Latest tag:        v1.2.1
HEAD:              e196366 (docs(backlog): add ENG-456)
Program.cs:         "0.3.0"
/health:            "1.1.0"
CHANGELOG [Unreleased]: 22+ commits since v1.2.1
CHANGELOG last titled:  [0.3.0] — 2026-05-11
CHANGELOG links:        v0.1.0, v0.2.0, v0.3.0 (all broken)
```

## Appendix B: Post-implementation desired state

```
Git tags:          v1.0.0, v1.1.0, v1.2.0, v1.2.1, v1.3.0 (new, local only)
Latest tag:        v1.3.0
HEAD:              (release commit)
Program.cs:         "1.3.0"
/health:            "1.1.0" (unchanged)
CHANGELOG [Unreleased]: empty, waiting for next release
CHANGELOG last titled:  [1.3.0] — 2026-07-06
CHANGELOG history:      [1.2.1], [1.1.0], [1.0.0] (all match git tags)
CHANGELOG links:        all valid, pointing to existing tags
```

---

## Memory Signal

> **forge-memory**: This spec establishes the following decisions that should be persisted:
>
> 1. **Version scheme**: Product version follows git tags (`v1.x`), API/schema version is independent (`"1.1.0"` for `/health` and export).
> 2. **CHANGELOG mapping**: `[0.1.0]` → `[1.0.0]`, `[0.2.0]` → `[1.1.0]`, `[0.3.0]` → `[1.2.1]` — chronological alignment, not content-based.
> 3. **AD-2**: The string `"1.1.0"` in `EngramServer.cs`, `Models.cs`, `SqliteStore.cs`, `PostgresStore.cs` is the API/schema version and must never be changed by product version bumps.
> 4. **AD-3**: Historical docs (MIGRATION.md, SYNC-SETUP.md, ADR-004) are immutable records.
> 5. **ENG-437 slug**: `.ai-work/eng-437-release-v040/` — note the slug uses the backlog's original `v040` naming.
>
> PM-001: Two-version model — product version (git tags) vs API/schema version (code constant)
> PM-002: CHANGELOG-git tag mapping table (see §6)
