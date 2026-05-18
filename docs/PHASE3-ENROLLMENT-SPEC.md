# Phase 3: Enrollment + Conflict Handling — Specification

**Status**: Ready for Implementation  
**Priority**: High (blocks production use)  
**Estimated Effort**: 6–8h  
**Related**: RFC-003, Phase 1 & 2 complete

---

## Overview

Phase 3 implements project enrollment management and admin pause controls for offline-first sync. This phase is critical for production use as it provides:

1. **Security**: Projects must be explicitly enrolled before sync works (fail-closed)
2. **Control**: Admin can pause sync for maintenance or troubleshooting
3. **Conflict Resolution**: FK misses are deferred and retried automatically

---

## Requirements

### REQ-1: Enrollment Endpoint

**Endpoint**: `POST /sync/enroll`

**Purpose**: Add a project to the user's enrollment list.

**Request**:
```http
POST /sync/enroll
Authorization: Bearer <token>
Content-Type: application/json

{
  "project": "team/mi-proyecto"
}
```

**Response 200**:
```json
{
  "project": "team/mi-proyecto",
  "enrolled_at": "2026-05-17T19:00:00Z",
  "enrolled_by": "victor.silgado"
}
```

**Response 409**:
```json
{
  "error": "project already enrolled",
  "project": "team/mi-proyecto"
}
```

**Implementation**:
- Check `sync_enrolled_projects` table
- Insert if not exists (idempotent)
- Log enrollment event

---

### REQ-2: Unenroll Endpoint

**Endpoint**: `DELETE /sync/enroll`

**Purpose**: Remove a project from enrollment list.

**Request**:
```http
DELETE /sync/enroll?project=team/mi-proyecto
Authorization: Bearer <token>
```

**Response 200**:
```json
{
  "project": "team/mi-proyecto",
  "unenrolled_at": "2026-05-17T19:00:00Z",
  "status": "unenrolled"
}
```

**Response 404**:
```json
{
  "error": "project not enrolled",
  "project": "team/mi-proyecto"
}
```

---

### REQ-3: List Enrolled Projects Endpoint

**Endpoint**: `GET /sync/enroll`

**Purpose**: List all enrolled projects for current user.

**Request**:
```http
GET /sync/enroll
Authorization: Bearer <token>
```

**Response 200**:
```json
{
  "projects": [
    { "project": "team/mi-proyecto", "enrolled_at": "2026-05-17T19:00:00Z" },
    { "project": "team/otro-proyecto", "enrolled_at": "2026-05-17T18:00:00Z" }
  ],
  "count": 2
}
```

---

### REQ-4: Pause Endpoint

**Endpoint**: `POST /sync/pause`

**Purpose**: Admin pause sync for a project (blocks all push operations).

**Request**:
```http
POST /sync/pause
Authorization: Bearer <token>
Content-Type: application/json

{
  "project": "team/mi-proyecto",
  "reason": "Database maintenance"
}
```

**Response 200**:
```json
{
  "project": "team/mi-proyecto",
  "paused": true,
  "paused_at": "2026-05-17T19:00:00Z",
  "paused_by": "admin",
  "reason": "Database maintenance"
}
```

**Implementation**:
- Update `cloud_project_controls` table
- Set `sync_enabled = false`
- Store reason in `pause_reason`
- Log to `cloud_sync_audit_log`

---

### REQ-5: Resume Endpoint

**Endpoint**: `DELETE /sync/pause`

**Purpose**: Resume sync for a paused project.

**Request**:
```http
DELETE /sync/pause?project=team/mi-proyecto
Authorization: Bearer <token>
```

**Response 200**:
```json
{
  "project": "team/mi-proyecto",
  "paused": false,
  "resumed_at": "2026-05-17T20:00:00Z",
  "resumed_by": "admin"
}
```

---

### REQ-6: Enrollment Filter in Pull

**Update**: `GET /sync/mutations/pull`

**Current Behavior**: Hardcoded filter "if project is provided"

**New Behavior**: Check `sync_enrolled_projects` table

**Implementation**:
```csharp
// In CloudSyncEndpoints.Pull (line ~151)
var enrolledProjects = await store.GetEnrolledProjectsAsync(user, ct);

if (!string.IsNullOrEmpty(project))
{
    // STRICT MODE: Only return mutations for enrolled projects
    if (!enrolledProjects.Contains(project))
    {
        return Results.Ok(new { 
            mutations = Array.Empty<Mutation>(),
            has_more = false,
            latest_seq = 0,
            project = project,
            note = "project not enrolled"
        });
    }
}
```

**Security**: Fail-closed — if project not enrolled, return empty mutations.

---

### REQ-7: ReplayDeferredAsync Implementation

**Method**: `SqliteStore.ReplayDeferredAsync()`

**Current**: Stub returning `(0, 0)`

**Implementation**:
```csharp
public async Task<ReplayDeferredResult> ReplayDeferredAsync(CancellationToken ct = default)
{
    using var conn = CreateConnection();
    await conn.OpenAsync(ct);
    
    // Get deferred rows with retry_count < 5
    var cmd = conn.CreateCommand();
    cmd.CommandText = @"
        SELECT id, session_id, title, content, type, project, scope, retry_count
        FROM sync_apply_deferred
        WHERE retry_count < 5
        ORDER BY created_at
        LIMIT 100";
    
    using var reader = await cmd.ExecuteReaderAsync(ct);
    var deferredRows = new List<DeferredRow>();
    
    while (await reader.ReadAsync(ct))
    {
        deferredRows.Add(new DeferredRow
        {
            Id = reader.GetInt64(0),
            SessionId = reader.GetString(1),
            Title = reader.GetString(2),
            Content = reader.GetString(3),
            Type = reader.GetString(4),
            Project = reader.GetString(5),
            Scope = reader.GetString(6),
            RetryCount = reader.GetInt32(7)
        });
    }
    
    int applied = 0;
    int dead = 0;
    
    foreach (var row in deferredRows)
    {
        try
        {
            // Try to apply the deferred mutation
            await SaveAsync(row.SessionId, row.Title, row.Content, row.Type, row.Project, row.Scope, ct);
            
            // Delete from deferred table on success
            var deleteCmd = conn.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM sync_apply_deferred WHERE id = @id";
            deleteCmd.Parameters.AddWithValue("@id", row.Id);
            await deleteCmd.ExecuteNonQueryAsync(ct);
            
            applied++;
        }
        catch (Exception)
        {
            // Increment retry_count
            var updateCmd = conn.CreateCommand();
            updateCmd.CommandText = @"
                UPDATE sync_apply_deferred 
                SET retry_count = retry_count + 1 
                WHERE id = @id";
            updateCmd.Parameters.AddWithValue("@id", row.Id);
            await updateCmd.ExecuteNonQueryAsync(ct);
            
            if (row.RetryCount >= 4)
            {
                // Mark as dead (will be logged separately)
                dead++;
            }
        }
    }
    
    return new ReplayDeferredResult(applied, dead);
}
```

---

## Data Model

### Tables (Already Exist)

| Table | Purpose | Status |
|-------|---------|--------|
| `sync_enrolled_projects` | User enrollment list | ✅ Exists, needs endpoints |
| `cloud_project_controls` | Per-project pause flag | ✅ Exists, needs endpoints |
| `sync_apply_deferred` | FK miss deferral queue | ✅ Exists, needs implementation |

---

## Implementation Tasks

### Task 3.1: Enrollment Endpoints (2h)

- [ ] `POST /sync/enroll` — Add project to enrollment
- [ ] `DELETE /sync/enroll` — Remove project from enrollment
- [ ] `GET /sync/enroll` — List enrolled projects
- [ ] `GetEnrolledProjectsAsync()` method in IStore
- [ ] Tests for enrollment endpoints

### Task 3.2: Pause Endpoints (1.5h)

- [ ] `POST /sync/pause` — Pause sync for project
- [ ] `DELETE /sync/pause` — Resume sync
- [ ] Update `cloud_project_controls` on pause/resume
- [ ] Tests for pause endpoints

### Task 3.3: Enrollment Filter in Pull (1h)

- [ ] Update `CloudSyncEndpoints.Pull` to check enrolled projects
- [ ] Fail-closed: return empty if not enrolled
- [ ] Tests for enrollment filter

### Task 3.4: ReplayDeferredAsync (1.5h)

- [ ] Implement `SqliteStore.ReplayDeferredAsync()`
- [ ] Retry logic with retry_count < 5
- [ ] Dead row logging
- [ ] Tests for deferred replay

---

## Testing Strategy

### Unit Tests

- Enrollment endpoint success/conflict
- Unenroll endpoint success/not-found
- Pause endpoint success/already-paused
- Resume endpoint success/not-paused
- Enrollment filter blocks non-enrolled projects
- ReplayDeferredAsync applies and deletes on success
- ReplayDeferredAsync increments retry_count on failure

### Integration Tests

- Full enrollment flow: enroll → pull → unenroll
- Pause flow: pause → push fails (409) → resume → push succeeds
- Deferred replay: FK miss → retry → success → delete

---

## API Changes Summary

| Endpoint | Method | Status |
|----------|--------|--------|
| `/sync/enroll` | POST, DELETE, GET | ❌ New |
| `/sync/pause` | POST, DELETE | ❌ New |
| `/sync/mutations/pull` | GET | ⚠️ Update (enrollment filter) |
| `/sync/mutations/push` | POST | ✅ No change (already has pause check) |

---

## Security Considerations

1. **Authentication**: All endpoints require JWT token
2. **Authorization**: Only enrolled users can enroll/unenroll their own projects
3. **Admin-only pause**: Only users with admin role can pause/resume sync
4. **Audit logging**: All enrollment and pause events logged to `cloud_sync_audit_log`

---

## Rollout Plan

1. **Implement endpoints** (Task 3.1, 3.2)
2. **Update pull filter** (Task 3.3)
3. **Implement deferred replay** (Task 3.4)
4. **Write tests** (all tasks)
5. **Manual testing** with TrueNAS server
6. **Update documentation** (OFFLINE-FIRST-SYNC.md)
7. **Deploy** with `ENGRAM_SYNC_ENABLED=true`

---

## Success Criteria

- ✅ All 5 endpoints implemented and tested
- ✅ Enrollment filter working (fail-closed)
- ✅ ReplayDeferredAsync applying deferred mutations
- ✅ 20+ tests passing
- ✅ Documentation updated
- ✅ No breaking changes to Phase 1 & 2

---

## Next Steps After Phase 3

1. **Phase 4**: Observability (`/sync/status`, CLI, metrics)
2. **Production rollout**: Enable sync for all team members
3. **Monitoring**: Track sync health, errors, performance

---

**Ready to start**: All requirements clear, tables exist, just need endpoints + implementation.
