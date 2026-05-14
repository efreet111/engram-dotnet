# RFC-002: Ambiguous Project Recovery with Cryptographic Tokens

**Status**: Draft  
**Author**: SDD Analysis Team  
**Date**: 2026-05-12  
**Related**: Go upstream PR #307, Go upstream commit `31e7a5b`

---

## Summary

When the current working directory contains multiple git repositories, project auto-detection fails with an **ambiguous error**. This RFC proposes a recovery flow using **cryptographic tokens** (5-minute TTL) that allows users to explicitly choose which project to use.

**Effort**: 4–6h  
**Priority**: After offline-first-sync (separate feature)  
**Security Level**: High (prevents agents from inventing arbitrary projects)

---

## Problem Statement

### Scenario

Developer runs MCP server from `~/work/` which contains:
- `~/work/engram/` (git repo)
- `~/work/alan-thegentleman/` (git repo)
- `~/work/client-project/` (git repo)

Agent calls `mem_save(project: "engram", ...)` — but which "engram"? The server cannot auto-detect because the CWD is `~/work/`, not inside any repo.

### Current Behavior (without recovery)

```
mem_save → ERROR: ambiguous_project
           No recovery path. Agent fails. User confused.
```

### Desired Behavior (with recovery)

```
mem_save → ERROR 400: ambiguous_project
           + available_projects: ["engram", "alan-thegentleman", "client-project"]
           + recovery_token: "a1b2c3..." (crypto-random, 5 min TTL)

Agent asks user: "Which project?"
User chooses: "engram"

mem_save(project: "engram",
         project_choice_reason: "user_selected_after_ambiguous_project",
         recovery_token: "a1b2c3...") → SUCCESS
```

---

## Security Analysis

### Threat: Agent Invents Projects

**Without recovery token**:
- Agent could guess `project: "victim-project"` and write to wrong bucket
- Cross-tenant data pollution

**With recovery token**:
- Token is `crypto/rand` 128-bit nonce
- Bound to specific context: `available_projects` + `cwd_path` + `timestamp`
- 5-minute TTL prevents replay attacks
- Validation fails if:
  - Token expired (> 5 min old)
  - `available_projects` changed (different CWD context)
  - `cwd_path` changed (user moved directories)
  - Chosen project not in original `available_projects`

### Threat: Token Theft

**Mitigation**:
- Short TTL (5 minutes) limits exposure window
- Token is single-use (validated once, then invalidated)
- Stored in-memory only (SessionActivity), not persisted to disk

---

## Design

### 6 Error Types (New)

| Error Code | HTTP Status | Meaning |
|------------|-------------|---------|
| `ambiguous_project` | 400 | CWD contains multiple repos. Returns `available_projects` + `recovery_token`. |
| `invalid_project_choice` | 400 | Chosen project doesn't exactly match any in `available_projects`. |
| `missing_recovery_token` | 400 | `project_choice_reason=user_selected_after_ambiguous_project` but no `recovery_token` provided. |
| `invalid_recovery_token` | 400 | Token expired, context changed, or already used. |
| `invalid_project` | 400 | Explicit `project` provided in non-ambiguous context (field ignored by design). |
| `invalid_project_config` | 400 | `.engram/config.json` exists but has empty/invalid `project_name`. |

### Flow Diagram

```
mem_save call
      │
      ▼
resolveWriteProject()
      │
      ├── NOT ambiguous → Use auto-detected project (ignore explicit project field)
      │
      └── AMBIGUOUS ───→ Return error:
                           ambiguous_project
                           + available_projects: [...]
                           + recovery_token: <nonce>
                                   │
                                   ▼
                           Agent asks user
                                   │
                                   ▼
                           User chooses project X
                                   │
                                   ▼
                           mem_save with:
                           - project: "X"
                           - project_choice_reason: "user_selected_after_ambiguous_project"
                           - recovery_token: <nonce>
                                   │
                                   ▼
                           resolveWriteProjectWithChoice()
                                   │
                                   ├── Validate recovery_token
                                   │   ├── Check TTL (not expired)
                                   │   ├── Check context (same available_projects)
                                   │   ├── Check path (same cwd)
                                   │   └── Check project in list
                                   │
                                   └── SUCCESS or ERROR:
                                       - missing_recovery_token
                                       - invalid_recovery_token
                                       - invalid_project_choice
```

### SessionActivity Integration

```csharp
public class SessionActivity
{
    // Existing: activity tracking, nudges, etc.
    
    // NEW: Token management
    string IssueAmbiguousProjectRecoveryToken(
        string sessionId,
        string[] availableProjects,
        string contextPath);
    
    bool ValidateAmbiguousProjectRecoveryToken(
        string sessionId,
        string token,
        string selectedProject,
        string[] availableProjects,
        string contextPath);
}
```

**Token storage**: In-memory dictionary with 5-min TTL cleanup.

---

## API Changes

### mem_save Tool (Modified)

**New parameters**:
```json
{
  "project": {
    "description": "Optional explicit project. Accepted only when backed by known context (existing project, matching session, repo config, or ambiguous-project recovery); invalid or unbacked names fail loudly."
  },
  "project_choice_reason": {
    "description": "Must be 'user_selected_after_ambiguous_project', and only after the user explicitly chose one of available_projects from an ambiguous_project error."
  },
  "recovery_token": {
    "description": "Short-lived token returned by an ambiguous_project error. Required with project_choice_reason=user_selected_after_ambiguous_project."
  }
}
```

### Error Response Format

```json
{
  "error_code": "ambiguous_project",
  "error": "Current directory contains multiple git repositories. Please choose a project.",
  "available_projects": ["engram", "alan-thegentleman", "client-project"],
  "recovery_token": "a1b2c3d4e5f6...",
  "project_source": "ambiguous_detection",
  "project_path": "~/work"
}
```

---

## Implementation Plan

### Files to Create/Modify

| File | Action | Lines |
|------|--------|-------|
| `src/Engram.Mcp/SessionActivity.cs` | Add token methods | +40 |
| `src/Engram.Mcp/ProjectRecoveryToken.cs` | Token data structure | +20 |
| `src/Engram.Mcp/EngramTools.cs` | Modify mem_save handlers | +60 |
| `src/Engram.Mcp/ProjectErrors.cs` | 6 new error types | +30 |
| `tests/Engram.Mcp.Tests/ProjectRecoveryTests.cs` | Unit tests | +100 |

### Tasks

- [ ] 1. Add `IssueAmbiguousProjectRecoveryToken` to SessionActivity
- [ ] 2. Add `ValidateAmbiguousProjectRecoveryToken` to SessionActivity
- [ ] 3. Create `RecoveryToken` record with crypto/rand generation
- [ ] 4. Modify `resolveWriteProject` to detect ambiguous scenarios
- [ ] 5. Create `resolveWriteProjectWithChoice` for recovery path
- [ ] 6. Add 6 new error types with structured error envelopes
- [ ] 7. Modify `mem_save` handler to accept recovery params
- [ ] 8. Modify `mem_save_prompt` handler to accept recovery params
- [ ] 9. Add token TTL cleanup (background timer)
- [ ] 10. Unit tests: token generation, validation, expiration, context mismatch
- [ ] 11. Integration tests: full recovery flow end-to-end

**Effort**: 4–6h

---

## Security Checklist

- [ ] Tokens generated with `RandomNumberGenerator` (crypto-secure)
- [ ] 128-bit minimum entropy
- [ ] 5-minute TTL enforced
- [ ] Context binding: available_projects + cwd_path + timestamp
- [ ] Single-use validation (invalidate after successful use)
- [ ] In-memory storage only (never persist to disk)
- [ ] Audit logging: log all recovery attempts (success and failure)

---

## UX Considerations

1. **Clear error messages**: "Found 3 projects: engram, alan-thegentleman, client-project. Which one are you working on?"

2. **Tool hints**: MCP tool schema includes descriptions so agents know to ask the user.

3. **No silent failures**: If recovery token is invalid, fail loudly with specific error code.

4. **mem_current_project never fails**: Even in ambiguous context, this tool returns data so agent can inspect before writing.

---

## Alternatives Considered

### Alternative 1: Always Require Explicit Project

**Proposal**: Remove auto-detection entirely. Every `mem_save` must include `project`.

**Rejected**: Too burdensome for common case (single repo). 90% of users work in one repo at a time.

### Alternative 2: First Repo Wins

**Proposal**: In ambiguous CWD, pick the first repo alphabetically.

**Rejected**: Silent data pollution. User might not realize they're writing to wrong project.

### Alternative 3: Config File Override

**Proposal**: Always check `.engram/config.json` for `project_name`.

**Status**: Already implemented as primary detection method. Recovery token is fallback when config is missing or ambiguous.

---

## References

- Go upstream PR #307: "fix(mcp): tighten ambiguous project recovery"
- Go upstream commit `31e7a5b`: docs explaining ambiguous project recovery flow
- Go upstream `internal/mcp/mcp.go`: resolveWriteProjectWithChoice implementation
- Go upstream `internal/mcp/activity.go`: Issue/ValidateAmbiguousProjectRecoveryToken

---

## Decision

**Approved for implementation** as separate feature after offline-first-sync.

**Rationale**: Security-critical for team scenarios. Prevents cross-project data leaks. UX improvement over current crash-on-ambiguous behavior.

**Not blocking**: Current .NET behavior (crash on ambiguous) is acceptable for single-user scenarios. This is a team-sharing requirement.
