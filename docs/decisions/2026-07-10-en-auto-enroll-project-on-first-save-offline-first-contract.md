---
observation_id: 31
type: "decision"
title: "EN: Auto-enroll project on first save (offline-first contract)"
created_at: "2026-07-10 00:04:00"
topic_key: "engram-dotnet/auto-enroll-on-first-save"
project: "team/engram-dotnet"
scope: "team"
generated_at: "2026-07-21T22:00:59.5763358Z"
---

# EN: Auto-enroll project on first save (offline-first contract)

**What**: EN for the missing "auto-enroll" behavior in the offline-first sync design. Currently, every new project must be manually enrolled via `engram sync enroll --project X` or `POST /sync/enroll/X` before its local observations are pushed to the team server. The user discovered this on 2026-07-09 when `mem_doctor` showed `enrolled_projects: []` for `team/flowforge` despite `sync_enabled: true` and a healthy server.

**Why**: This is a real design gap that contradicts the "offline-first" principle documented in `FlowForge/docs/10-memory-mapping-fallback.md` §3. The current contract is:
- ✅ Local works without server (saves go to SQLite)
- ❌ Sync is NOT automatic — even with server healthy, the project is not enrolled
- Result: every new adopter hits this; they think sync is working, but their team server is empty

**Evidence**:
- `FlowForge/.ai-work/agent-proactive-memory/summary.md` (P1 next step, 2026-06-XX):
  > "When server healthy: sync enroll `team/flowforge` + ingest local buffer" — **never executed**
- `FlowForge/docs/19-project-memory-association-backlog.md` line 16 mentions "Use the shared PostgreSQL server but not enroll / not sync" as a valid user choice — this is the *only* justification for the manual step
- `FlowForge/.engram/local_memory/obs-2026-05-27-session-close.md` says "Offline-first en engram: guardar en SQLite local primero; sync al servidor solo cuando esté sano" — but never addresses the enroll gate

**Where**:
- **Primary** (engram-dotnet side): the fix must be in the runtime or installer
- **Secondary** (FlowForge side): agents need awareness of enrollment status
- Affected agents: `forge-memory` (auto-enroll on first save), `forge-verify` (warn if sync enabled but not enrolled), `forge-discovery` (log enrollment status in Context Map)

**Learned**:

### The gap in detail

The current lifecycle is:
```
mem_save → SQLite local
         → sync_mutations queue
         → push to server when healthy   ← BLOCKED here for non-enrolled projects
         (the queue is filled but never drained)
```

The intended offline-first contract should be:
```
mem_save (first time for project) → check enrollment
                                  → if not enrolled and server healthy:
                                      enroll the project
                                  → SQLite local
                                  → sync_mutations queue
                                  → push to server when healthy
```

### Three options to fix

**Option A: Auto-enroll in engram-dotnet runtime (recommended)**
- Where: `Engram.Store.SyncManager` (or equivalent)
- When: on first `mem_save` for a project, if `sync_enabled=true` and server is healthy and project is not enrolled
- Action: call internal `Enroll(project)` automatically
- Pros: works for any MCP caller (no agent logic needed), truly offline-first, single source of truth
- Cons: silent auto-action (user might not know) — mitigate via log message "Auto-enrolled team/flowforge on first save"

**Option B: Auto-enroll at install time (FlowForge installer)**
- Where: `src/FlowForge.Installer/Commands/InstallCommand.cs` (or InitCommand.cs)
- When: after `flowforge install --mode=sync` writes `~/.engram/config.json`
- Action: call `POST /sync/enroll/{project}` with the project name from `paths.project` or `engram.project`
- Pros: one-time, visible in installer logs, no runtime change
- Cons: doesn't help existing installs; if user changes `engram.project` later, no re-enroll

**Option C: Auto-enroll on discovery (FlowForge agent)**
- Where: `skills/forge-discovery/SKILL.md` step 3 (Memory Search)
- When: first `mem_search` for a new project, if sync enabled and not enrolled
- Action: call enroll + log in Context Map `## Sync Status` block
- Pros: visible in the flow (user sees the enroll happen)
- Cons: only works if user runs `/flow-start`; doesn't help CLI users

**Recommendation: Option A + B as a pair, skip C.**
- A is the canonical fix (true offline-first, works everywhere)
- B handles the case where the server is up at install time but the user hasn't saved anything yet (saves 1 round-trip)
- C is redundant if A works, and adds agent complexity

### What the agents need to change (FlowForge side)

If A and B are implemented in engram-dotnet + installer, the agents only need to be **aware** of enrollment, not implement it. Three updates:

1. **`skills/forge-memory/SKILL.md`**:
   - Add a step before/after `mem_session_summary` that checks `enrolled_projects` (via `/sync/enroll` GET)
   - If the current project is not enrolled AND `sync_enabled=true`, log a WARNING in the session summary
   - The session-summary output should include a `## Sync Status` block:
     ```
     ## Sync Status
     - Server: 192.168.0.178:7437 (healthy)
     - Project: team/flowforge (enrolled: yes/no)
     - Pending push: N
     - Last sync: 2026-07-09T23:00:00Z
     ```

2. **`skills/forge-verify/SKILL.md`**:
   - Add a check: if `.flowforge.json` has `engram.enabled=true` AND `engram.sync_enabled=true` AND the project is not enrolled → emit a REWORK ticket with the message: "Sync is enabled but project is not enrolled. Run `engram sync enroll --project <name>` or upgrade to engram-dotnet v1.4.0+ for auto-enroll."

3. **`skills/forge-discovery/SKILL.md`**:
   - In step 3 (Memory Search), add a `## Sync Status` block to the Context Map output (similar to the existing `## FlowDoc context` block)
   - This makes enrollment status visible at the start of every feature

### Acceptance criteria

- [ ] New engram-dotnet release has `Enroll` called automatically on first `mem_save` for a project when sync is enabled and server is healthy
- [ ] Installer (`flowforge install --mode=sync`) calls `POST /sync/enroll/{project}` after writing config
- [ ] `mem_doctor` (or new tool) shows `auto_enrolled: true` for projects that were auto-enrolled
- [ ] `forge-memory` reports enrollment status in session summary
- [ ] `forge-verify` issues REWORK for non-enrolled + sync-enabled projects (with a clear remediation message)
- [ ] Test: fresh install + `flowforge install --mode=sync` + first `mem_save` → observations appear on team server within 60 seconds without any manual enrollment

### Test plan (user will execute)

The user said they will:
1. Implement the fix in engram-dotnet
2. Test the auto-enroll on a fresh install
3. Use this as the test case for the new feature

**Suggested test sequence**:
1. Stop the current engram container
2. Wipe `team/flowforge` enrollment from server (or test on a different project name)
3. Rebuild engram-dotnet with the fix
4. Start container, verify `enrolled_projects: []` initially
5. From the dev box, run `mem_save` for a new project name (e.g., `team/auto-enroll-test`)
6. Wait 60 seconds, check `enrolled_projects` → should now include `team/auto-enroll-test`
7. Check team server has the new observation
8. Done

### Related work

- `FlowForge/docs/19-project-memory-association-backlog.md` — describes the user-facing project-vs-team-vs-personal choice. The auto-enroll should only fire for `team/{project}` (not personal), and only when the user previously chose team scope.
- `FlowForge/docs/decisions/ADR-009-flowforge-sync-connect.md` (Proposed) — proposes `flowforge sync connect` command. Auto-enroll is a different mechanism (runtime vs explicit command) and the two can coexist.
- The engram-dotnet memory observation `engram-dotnet/flowdoc-v2-inspired-enhancements` (saved 2026-07-08) lists 4 schema enhancements. Auto-enroll is a behavioral change, not a schema change — separate concern.

### Out of scope (not part of this EN)

- Personal-scope projects: should never auto-enroll (different namespace, different sync semantics)
- Multi-user conflicts: if two users both auto-enroll the same project, last-write-wins (already in the design)
- Renaming projects: out of scope — covered by the 19-project-memory-association-backlog ADR

**Trigger event**: 2026-07-09 23:38 UTC, user discovered `enrolled_projects: []` for `team/flowforge` despite healthy server. Direct quote: "why its manual? offline first right?"

**Reporter**: Victor (user) via orchestrator session

**Implementation owner**: TBD — user will implement, but not in this FlowForge session (engram-dotnet is a separate repo)
