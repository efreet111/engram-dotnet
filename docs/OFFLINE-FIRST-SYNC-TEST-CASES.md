# Offline-First Sync — Test Cases & Validation

**Feature**: Offline-First Sync (Phases 1-4 ✅ Complete)  
**Server**: PostgreSQL at `localhost:7437`  
**Client**: Local SQLite + SyncManager background service

---

## 📋 Prerequisites

### Environment Variables

```bash
# Client (local)
export ENGRAM_DATA_DIR=~/.engram
export ENGRAM_SERVER_URL=http://localhost:7437
export ENGRAM_USER=your-username
export ENGRAM_SYNC_ENABLED=true
export ENGRAM_SYNC_TARGET=cloud
export ENGRAM_SYNC_POLL_SECONDS=30
export ENGRAM_SYNC_DEBOUNCE_MS=5000
export ENGRAM_SYNC_MAX_FAILURES=10
```

### Initial Verification

```bash
# 1. Server is running
curl http://localhost:7437/health
# Expected: {"status":"ok","backend":"PostgreSQL"}

# 2. Check sync status
engram sync status

# 3. Check enrolled projects
curl http://localhost:7437/sync/enroll
# Expected: {"projects":[],"count":0}
```

---

## 🧪 Test Cases

### TEST 1: First Project Enrollment

**Goal**: Enroll a project for sync.

```bash
# 1. Enroll
curl -X POST http://localhost:7437/sync/enroll \
  -H "X-Engram-User: your-username" \
  -d '{"project":"team/mi-api"}'

# 2. Verify
curl http://localhost:7437/sync/enroll -H "X-Engram-User: your-username"
```

**Acceptance**: ✅ Project appears in enrolled list

---

### TEST 2: Push Mutations (Online)

**Goal**: Verify local memories sync to the server.

```bash
# 1. Create local memory
engram mem_save "Test sync observation" \
  --title "Offline-First Test" \
  --type manual \
  --project team/mi-api

# 2. Wait for sync (poll interval)
sleep 35

# 3. Verify on server
curl http://localhost:7437/search?q=Offline-First+Test

# 4. Check sync status
engram sync status
```

**Acceptance**: ✅ Memory replicated to server, cursor updated

---

### TEST 3: Pull Mutations (Second Client)

**Goal**: Verify a second client receives memories from the first.

```bash
# As juan.perez:
export ENGRAM_USER=juan.perez

# 1. Enroll same project
curl -X POST http://localhost:7437/sync/enroll \
  -H "X-Engram-User: juan.perez" \
  -d '{"project":"team/mi-api"}'

# 2. Wait for pull
sleep 35

# 3. Search Victor's memory
engram search "Offline-First Test"
```

**Acceptance**: ✅ Victor's memory visible to Juan

---

### TEST 4: Offline Work + Deferred Sync

**Goal**: Verify memories created offline sync when connection is restored.

```bash
# 1. Stop server (simulate offline)
ssh root@your-server "systemctl stop engram-server"

# 2. Create memories offline
engram mem_save "Offline observation 1" --project team/mi-api
engram mem_save "Offline observation 2" --project team/mi-api

# 3. Verify pending mutations
engram sync status --json | jq '.counts.pending_push'
# Expected: 2

# 4. Restart server
ssh root@your-server "systemctl start engram-server"
sleep 35

# 5. Verify sync completed
engram sync status --json | jq '.counts.pending_push'
# Expected: 0
curl http://localhost:7437/search?q=Offline+observation
```

**Acceptance**: ✅ Offline: no errors, pending_push increments. Reconnect: auto-sync.

---

### TEST 5: Pause/Resume Sync (Admin)

**Goal**: Verify admin can pause sync for maintenance.

```bash
# 1. Pause
curl -X POST http://localhost:7437/sync/pause \
  -H "X-Engram-User: admin" \
  -d '{"project":"team/mi-api","reason":"Database maintenance"}'

# 2. Push should fail with 409
curl -X POST http://localhost:7437/sync/mutations/push \
  -H "Content-Type: application/json" \
  -H "X-Engram-User: victor" \
  -d '{"entries":[{"project":"team/mi-api","entity":"observation","entity_key":"test","op":"upsert","payload":"{}"}]}'
# Expected: HTTP 409 Conflict

# 3. Resume
curl -X DELETE "http://localhost:7437/sync/pause?project=team/mi-api" \
  -H "X-Engram-User: admin"

# 4. Verify resumed
curl http://localhost:7437/sync/status | jq '.paused_projects'
# Expected: []
```

**Acceptance**: ✅ Pause blocks push, resume allows it

---

### TEST 6: Deferred Replay (FK Misses)

**Goal**: Verify mutations that fail due to FK constraints are retried.

```bash
# 1. Check deferred queue
sqlite3 ~/.engram/engram.db "SELECT COUNT(*) FROM sync_apply_deferred;"

# 2. Create deferred entry (simulate FK miss)
# Then create the missing FK target

# 3. Wait for replay
sleep 35

# 4. Verify queue empty
sqlite3 ~/.engram/engram.db "SELECT COUNT(*) FROM sync_apply_deferred;"
# Expected: 0
```

**Acceptance**: ✅ FK misses don't block cursor, auto-replay works

---

### TEST 7: Sync Status Endpoint

**Goal**: Verify sync observability.

```bash
# 1. Full status
curl http://localhost:7437/sync/status | jq

# Expected structure:
{
  "sync_enabled": true,
  "phase": "healthy",
  "cursor": {"last_pushed_seq": 142, "last_pulled_seq": 89, ...},
  "health": {"status": "healthy", "consecutive_failures": 0, ...},
  "counts": {"pending_push": 0, "total_pushed": 142, ...},
  "enrolled_projects": ["team/mi-api"],
  "paused_projects": []
}

# 2. CLI status
engram sync status

# 3. CLI JSON
engram sync status --json | jq
```

**Acceptance**: ✅ All fields present, data consistent

---

### TEST 8: Multi-User Isolation + Sync

**Goal**: Verify users don't see each other's personal memories.

```bash
# User 1: victor
export ENGRAM_USER=your-username
engram mem_save "Victor's personal" --scope personal --project mi-api

# User 2: juan
export ENGRAM_USER=juan.perez
engram mem_save "Juan's personal" --scope personal --project mi-api

# Victor searches
export ENGRAM_USER=your-username
engram search "personal"
# Expected: Only "Victor's personal"

# Both see team memories
engram mem_save "Team memory" --scope team --project mi-api
```

**Acceptance**: ✅ `personal:{user}` isolated, `team/*` shared

---

## 📊 Validation Metrics

| Metric | Target | How to measure |
|--------|--------|---------------|
| Push latency (p95) | < 500ms | `/sync/status` endpoint |
| Pull latency (p95) | < 1000ms | SyncManager logs |
| Sync success rate | > 99% | `cloud_sync_audit_log` table |
| Deferred replay success | > 95% | `sync_apply_deferred.retry_count` |
| Failure ceiling | 10 consecutive | SyncManager phase transitions |

---

## 🐛 Troubleshooting

### Sync won't start

```bash
# 1. Check ENGRAM_SYNC_ENABLED
echo $ENGRAM_SYNC_ENABLED
# Expected: true

# 2. Check logs
journalctl -u engram -f | grep "SyncManager"

# 3. Restart
systemctl restart engram
```

### 409 Sync Paused

```bash
# Check paused projects
curl http://localhost:7437/sync/status | jq '.paused_projects'

# Resume
curl -X DELETE "http://localhost:7437/sync/pause?project=team/mi-api" \
  -H "X-Engram-User: admin"
```

### Pending mutations not decreasing

```bash
# Check pending count
engram sync status --json | jq '.counts.pending_push'

# Check error logs
journalctl -u engram -f | grep "CycleFailed"
```

### Deferred queue growing

```bash
# Check deferred rows
sqlite3 ~/.engram/engram.db "SELECT COUNT(*), AVG(retry_count) FROM sync_apply_deferred;"

# Check dead rows (retry_count >= 5)
sqlite3 ~/.engram/engram.db "SELECT * FROM sync_apply_deferred WHERE retry_count >= 5;"
```

---

## ✅ Validation Checklist

- [ ] TEST 1: Enrollment works
- [ ] TEST 2: Push online works
- [ ] TEST 3: Pull between clients works
- [ ] TEST 4: Offline + reconnection works
- [ ] TEST 5: Pause/Resume works
- [ ] TEST 6: Deferred replay works
- [ ] TEST 7: Sync status endpoint works
- [ ] TEST 8: Multi-user isolation + sync works
- [ ] Metrics within targets
- [ ] No critical log errors
