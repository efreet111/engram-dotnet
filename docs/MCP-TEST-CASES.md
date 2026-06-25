# MCP Tools — Test Cases

> **Purpose**: Verify all 28 MCP tools work correctly.  
> **Server**: `http://localhost:7437` (PostgreSQL)  
> **Requires**: `engram mcp` running locally OR direct curl to REST API.

---

## 📋 Prerequisites

```bash
# 1. Server must be running
curl http://localhost:7437/health
# → {"status":"ok","backend":"postgres"}

# 2. Enroll a test project
curl -X POST http://localhost:7437/sync/enroll \
  -H "X-Engram-User: your-username" \
  -d '{"project":"team/mcp-test"}'

# 3. Create a test session
curl -X POST http://localhost:7437/sessions \
  -H "Content-Type: application/json" \
  -d '{"id":"mcp-test-session","project":"team/mcp-test","directory":"/tmp"}'
```

---

## 🧪 CASE 1: `mem_save` — Save a memory

**Endpoint**: POST /observations

```bash
curl -X POST http://localhost:7437/observations \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: your-username" \
  -d '{
    "session_id":"mcp-test-session",
    "title":"MCP Test - Technical Decision",
    "content":"**What**: Test decision\n**Why**: Verify mem_save\n**Where**: tests/\n**Learned**: Works",
    "type":"decision",
    "project":"team/mcp-test",
    "topic_key":"testing/mcp-save"
  }'
```

**Expected**: 200 + `{"id":<number>,"status":"created"}`

**Criteria**: ✅ Memory created and visible via search

---

## 🧪 CASE 2: `mem_search` — Search memories

**Endpoint**: GET /search

```bash
# Simple search
curl "http://localhost:7437/search?q=MCP+Test&limit=5" \
  -H "X-Engram-User: your-username" | jq '.[] | {id: .observation.id, title: .observation.title, rank: .rank}'

# Search by project
curl "http://localhost:7437/search?q=MCP+Test&project=team/mcp-test" | jq '. | length'

# Search by type
curl "http://localhost:7437/search?q=decision&type=decision" | jq '. | length'
```

**Expected**: Results with `observation.id`, `observation.title`, `rank` > 0

---

## 🧪 CASE 3: `mem_get_observation` — View observation

**Endpoint**: GET /observations/{id}

```bash
# Use the ID from CASE 1
curl http://localhost:7437/observations/ID_FROM_CASE1 | jq
```

**Expected**: Full JSON with `title`, `content`, `type`, `project`, etc.

---

## 🧪 CASE 4: `mem_update` — Update a memory

**Endpoint**: PATCH /observations/{id}

```bash
curl -X PATCH http://localhost:7437/observations/ID_FROM_CASE1 \
  -H "Content-Type: application/json" \
  -d '{"title":"MCP Test - UPDATED","content":"**What**: Updated version"}'
```

**Expected**: 200 + `{"status":"updated"}`

**Verify**:
```bash
curl http://localhost:7437/observations/ID_FROM_CASE1 | jq '.title'
# → "MCP Test - UPDATED"
```

---

## 🧪 CASE 5: `mem_delete` — Delete a memory

**Endpoint**: DELETE /observations/{id}

```bash
# Create temp memory
curl -X POST http://localhost:7437/observations \
  -H "Content-Type: application/json" \
  -d '{"session_id":"mcp-test-session","title":"Temp","content":"To delete","type":"manual","project":"team/mcp-test"}'

# Soft-delete
curl -X DELETE http://localhost:7437/observations/ID_TO_DELETE
```

**Expected**: 200 + `{"status":"deleted"}`

**Verify**:
```bash
curl http://localhost:7437/observations/ID_TO_DELETE | jq '.deleted_at'
# → Date (not null)
```

---

## 🧪 CASE 6: `mem_context` — Session context

**Endpoint**: GET /context

```bash
curl "http://localhost:7437/context?session_id=mcp-test-session&limit=5" | jq
```

**Expected**: Observations from the session ordered by date

---

## 🧪 CASE 7: Session lifecycle

**Endpoint**: POST /sessions + POST /sessions/{id}/end

```bash
# Start session
curl -X POST http://localhost:7437/sessions \
  -H "Content-Type: application/json" \
  -d '{"id":"mcp-flow-test","project":"team/mcp-test","directory":"/tmp"}'

# End session
curl -X POST http://localhost:7437/sessions/mcp-flow-test/end \
  -H "Content-Type: application/json" \
  -d '{"summary":"MCP test session completed successfully"}'

# Verify ended session
curl http://localhost:7437/sessions/mcp-flow-test | jq '{id, summary, started_at, ended_at}'
```

**Expected**: Session with `ended_at` and `summary` set

---

## 🧪 CASE 8: `mem_stats` — Statistics

**Endpoint**: GET /stats

```bash
curl http://localhost:7437/stats | jq
```

**Expected**: JSON with `total_observations`, `total_sessions`, `total_prompts`, etc.

---

## 🧪 CASE 9: `mem_timeline` — Timeline

**Endpoint**: GET /timeline

```bash
curl "http://localhost:7437/timeline?observation_id=ID_FROM_CASE1&window=5" | jq
```

**Expected**: Observations surrounding the specified one

---

## 🧪 CASE 10: `mem_save_prompt` — Save a prompt

**Endpoint**: POST /prompts

```bash
curl -X POST http://localhost:7437/prompts \
  -H "Content-Type: application/json" \
  -d '{
    "session_id":"mcp-test-session",
    "content":"Which design pattern should I use for module X?",
    "project":"team/mcp-test"
  }'
```

**Expected**: 200 + prompt created

---

## 🧪 CASE 11: `mem_doctor` — Diagnostics

**Endpoint**: CLI `engram doctor`

```bash
engram doctor --server http://localhost:7437
```

**Expected**: 
```
Engram Diagnostic Report
========================
✓ database      (healthy) 12ms
✓ http_server   (healthy) 45ms  
✓ mcp_server    (healthy) 2ms
```

---

## 🧪 CASE 12: `mem_suggest_topic_key` — Upsert via topic_key

**Endpoint**: POST /observations (with topic_key, already tested in CASE 1)

**Verify upsert**:
```bash
# Save with same topic_key
curl -X POST http://localhost:7437/observations \
  -H "Content-Type: application/json" \
  -d '{
    "session_id":"mcp-test-session",
    "title":"Upsert test - V2",
    "content":"Updated topic version",
    "type":"decision",
    "project":"team/mcp-test",
    "topic_key":"testing/mcp-save"
  }'

# Verify it updated (same topic_key = upsert)
curl "http://localhost:7437/search?q=testing/mcp-save&project=team/mcp-test" | jq '.[0].observation.title'
# → "Upsert test - V2"
```

---

## 🧪 CASE 13: `mem_capture_passive` — Passive capture

**Endpoint**: POST /observations/passive

```bash
curl -X POST http://localhost:7437/observations/passive \
  -H "Content-Type: application/json" \
  -d '{
    "session_id":"mcp-test-session",
    "title":"Automatic discovery",
    "content":"**What**: Found during debugging\n**Learned**: The error was caused by X",
    "type":"discovery",
    "project":"team/mcp-test"
  }'
```

---

## 🧪 CASE 14: Retention tools

### `mem_retention_stats`

```bash
curl http://localhost:7437/retention/stats | jq
```

### `mem_retention_prune`

```bash
curl -X POST http://localhost:7437/retention/prune \
  -H "Content-Type: application/json" \
  -d '{"ttl_days":90}'
```

---

## 🧪 CASE 15: `mem_merge_projects` — Merge projects

**Endpoint**: POST /projects/migrate

```bash
curl -X POST http://localhost:7437/projects/migrate \
  -H "Content-Type: application/json" \
  -d '{"source":["team/mcp-test-old"],"target":"team/mcp-test"}'
```

---

## 🧪 CASE 16: `mem_promote_to_md` — Promote to markdown

**Endpoint**: POST /md/promote/{id}

```bash
curl -X POST http://localhost:7437/md/promote/ID_FROM_CASE1 \
  -H "Content-Type: application/json" \
  -d '{"md_dir":"docs/decisions"}'
```

---

## 🧪 CASE 17: `mem_sync_md_to_repo` — Sync markdown to repo

**Endpoint**: POST /md/sync

```bash
curl -X POST http://localhost:7437/md/sync \
  -H "Content-Type: application/json" \
  -d '{}'
```

---

## ✅ TEST CHECKLIST

- [ ] CASE 1: `mem_save` — memory created
- [ ] CASE 2: `mem_search` — search works
- [ ] CASE 3: `mem_get_observation` — detail visible
- [ ] CASE 4: `mem_update` — update works
- [ ] CASE 5: `mem_delete` — soft-delete works
- [ ] CASE 6: `mem_context` — session context
- [ ] CASE 7: Session lifecycle
- [ ] CASE 8: `mem_stats` — statistics
- [ ] CASE 9: `mem_timeline` — timeline
- [ ] CASE 10: `mem_save_prompt` — prompt saved
- [ ] CASE 11: `mem_doctor` — diagnostics OK
- [ ] CASE 12: Upsert via topic_key
- [ ] CASE 13: `mem_capture_passive` — passive capture
- [ ] CASE 14: Retention tools
- [ ] CASE 15: `mem_merge_projects`
- [ ] CASE 16: `mem_promote_to_md`
- [ ] CASE 17: `mem_sync_md_to_repo`

---

## 🔍 PostgreSQL Verification

In DBeaver, verify data directly:

```sql
-- View created memories
SELECT id, title, type, project, created_at 
FROM observations 
WHERE project = 'team/mcp-test'
ORDER BY id DESC;

-- View sessions
SELECT id, project, started_at, ended_at, summary 
FROM sessions 
WHERE project = 'team/mcp-test'
ORDER BY started_at DESC;

-- View prompts
SELECT id, session_id, content, project, created_at 
FROM user_prompts 
WHERE project = 'team/mcp-test';
```
