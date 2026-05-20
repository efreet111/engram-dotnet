# Engram Agent Protocol — Sync Collaboration

> **Version**: 1.0  
> **Purpose**: Protocol for AI agents to correctly use multi-user sync.  
> **Read this BEFORE saving or searching memories in a team environment.**

---

## 1. Why Does This Exist?

**Problem**: When 5 developers work with Engram, each has their own SQLite database. Victor's memories are invisible to Juan, Ana's decisions are invisible to Pedro. The team has no "shared brain."

**Solution**: Offline-First Sync. Each developer has local SQLite (fast, offline), and a shared PostgreSQL server syncs team memories automatically via the SyncManager.

---

## 2. Golden Rules for Agents

### Rule #1: Always specify `scope`

```markdown
// ✅ TEAM scope: everyone sees it
mem_save(title="Decision: use ORM", ..., project="team/mi-api", scope="team")

// ✅ PERSONAL scope: only you see it  
mem_save(title="Temp debug note", ..., project="team/mi-api", scope="personal")
```

| Scope | Visibility | Sync |
|-------|-----------|------|
| `team` | Whole team | ✅ Synced to server |
| `personal` | Only current user | ❌ Stays in local SQLite |

### Rule #2: Use `team/` prefix for shared projects

```markdown
✅ project: "team/mi-api"      → Synced, visible to all
✅ project: "team/flowforge"   → Synced, visible to all
❌ project: "mi-api"           → NOT synced (no team/ prefix)
✅ project: "personal:debug"   → NOT synced (personal: prefix)
```

### Rule #3: Before closing a session, verify sync

```markdown
// Check sync health via HTTP endpoint
curl http://server:7437/sync/status

// Or via CLI
engram sync status
```

### Rule #4: If you can't find a memory, search the team project

```markdown
// Search only in your project
mem_search(query="architecture")

// Search in team project (if you have access)
mem_search(query="ORM decision", project="team/mi-api")
```

---

## 3. Communication Protocol

### 3.1 Data Model

```
Observation = {
  id: long,              // Auto-increment
  session_id: string,    // Session where it was created
  title: string,         // Searchable title
  content: string,       // **What**...**Why**...**Where**...**Learned**
  type: string,          // decision | architecture | bugfix | pattern | learning | discovery | config
  project: string,       // team/{project} or personal:{user}/{project}
  scope: string,         // team | personal
  topic_key: string|null,// For upserts (same topic_key = update)
  created_at: string,    // ISO timestamp
}
```

### 3.2 Storage Layering

```
┌──────────────────────────────────────────────┐
│           LEVEL 1: OPERATIONAL                │
│  What: sessions, debugging, tool_use, command  │
│  Where: local SQLite + PostgreSQL server       │
│  TTL: 30-90 days by type                       │
│  Who writes: agent automatically               │
├──────────────────────────────────────────────┤
│           LEVEL 2: STRUCTURED                  │
│  What: decisions, architecture, patterns       │
│  Where: .md files in repo + metadata           │
│  TTL: permanent (removed with PR)              │
│  Who writes: Memory Agent (Phase 4) + human    │
└──────────────────────────────────────────────┘
```

### 3.3 Sync Flow (SyncManager)

```
1. Agent calls mem_save()
2. Observation saved in LOCAL SQLite (immediate, offline-safe)
3. SyncManager detects new mutation (poll every 30s or on debounce)
4. SyncManager POSTs /sync/mutations/push to server
5. Server stores in PostgreSQL (cloud_mutations table)
6. Other developers' SyncManager GETs /sync/mutations/pull
7. Mutations applied to their local SQLite
```

### 3.4 Conflict Resolution

- **Last-write-wins**: If two devs save the same observation, the most recent wins
- **FK deferral**: If a mutation fails due to FK (e.g., session doesn't exist), it goes to `sync_apply_deferred` and retries up to 5 times
- **Manual**: If there's a conflict, run `engram doctor` to diagnose

---

## 4. MCP Tools

| Tool | When to use | Example |
|------|-------------|---------|
| `mem_save` | **Always** when you discover something | Save decision, pattern, bugfix |
| `mem_search` | **Before** starting a task | Find context from previous sessions |
| `mem_context` | **At session start** | Recover previous sessions for the project |
| `mem_session_summary` | **At session end** | Document what was done |
| `mem_get_observation` | **To read full content** | Get complete text of a search result |
| `mem_update` | **To correct a memory** | Fix outdated information |
| `mem_doctor` | **When something fails** | Diagnose DB, HTTP, MCP health |

### Agent flow example:

```markdown
// 1. Session start: recover context
mem_context(project="team/mi-api")

// 2. Search for similar decisions
mem_search(query="JWT authentication", project="team/mi-api")

// 3. Do the work...
// 4. Save the decision
mem_save(
  title="Decision: use JWT with refresh tokens",
  content="**What**: ... **Why**: ... **Where**: ... **Learned**: ...",
  type="decision",
  project="team/mi-api",
  scope="team",
  topic_key="architecture/auth-model"
)

// 5. Check sync via CLI
engram sync status

// 6. The SyncManager automatically pushes to the server
```

---

## 5. Human Operations (SysAdmin)

| Operation | Command |
|-----------|---------|
| Check sync status | `engram sync status` or `curl /sync/status` |
| View enrolled projects | `curl -H "X-Engram-User: user" /sync/enroll` |
| Enroll a project | `POST /sync/enroll` |
| Pause sync | `POST /sync/pause` |
| Diagnose system | `engram doctor --server http://server:7437` |
| View logs | `docker logs -f engram` or `journalctl -u engram-server` |

---

## 6. Troubleshooting for Agents

| Symptom | What to do |
|---------|------------|
| `mem_search` returns no team memories | Check search includes correct `project` |
| `curl /sync/enroll` returns 409 | Project already enrolled — continue normally |
| `curl /sync/status` shows `sync_enabled: false` | SyncManager is off — memories are LOCAL only |
| HTTP 500 on POST | Check server logs — errors are now fully logged |

---

## 7. Quick Reference

```bash
# Check if server is alive
curl http://server:7437/health

# View enrolled projects
curl -H "X-Engram-User: victor" http://server:7437/sync/enroll

# Run full diagnostics
engram doctor --server http://server:7437

# CLI sync status
engram sync status
```
