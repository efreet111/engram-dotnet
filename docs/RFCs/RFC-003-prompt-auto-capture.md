# RFC-003: Prompt Auto-Capture on Memory Save

**Status**: Draft  
**Author**: SDD Analysis Team  
**Date**: 2026-05-12  
**Related**: Go upstream PR #309, Go upstream commit `a9b83d6`

---

## Summary

When an agent calls `mem_save`, automatically capture the user's prompt (the question/statement that triggered the save) as context. This ensures "why was this saved?" is never lost.

**Effort**: 1–2h  
**Priority**: Quick win — can implement anytime  
**Breaking Change**: No (opt-out via `capture_prompt: false`)

---

## Problem Statement

### Scenario

User asks: "Refactor this function to use async/await"
Agent refactors code → calls `mem_save` with the refactored code as observation

**Without prompt capture**:
- Observation: "Refactored to use async/await"
- Missing context: WHY was this refactored? What problem did the user have?

**With prompt capture**:
- Observation: "Refactored to use async/await"
- Linked prompt: "Refactor this function to use async/await"
- Full context: User's original request is preserved

### Why This Matters

1. **Memory retrieval**: When searching later, knowing the prompt helps understand the context
2. **Team sharing**: Other developers see not just what changed, but why
3. **Debugging**: If the refactoring breaks something, knowing the original intent helps

---

## Design

### Two-Tool Flow

```
User asks question
      │
      ▼
Agent calls mem_save_prompt(content: "User's question")
      │
      ▼
SessionActivity.RecordPrompt(sessionId, project, content)
      │
      ▼
Agent does work (refactor, code, etc.)
      │
      ▼
Agent calls mem_save(capture_prompt: true)  // default
      │
      ▼
SessionActivity.CurrentPrompt(sessionId, project)
      │ (if found and matches project)
      ▼
Auto-add prompt via AddPrompt
      │
      ▼
Save observation + linked prompt
```

### SessionActivity Extension

```csharp
public class SessionActivity
{
    // Existing: activity tracking, nudges
    
    // NEW: Prompt context
    private readonly Dictionary<string, PromptContext> _sessionPrompts;
    
    public void RecordPrompt(string sessionId, string project, string content)
    {
        _sessionPrompts[sessionId] = new PromptContext(project, content);
    }
    
    public (string content, bool found) CurrentPrompt(string sessionId, string project)
    {
        if (!_sessionPrompts.TryGetValue(sessionId, out var ctx))
            return ("", false);
        
        if (ctx.Project != project || string.IsNullOrEmpty(ctx.Content))
            return ("", false);
        
        return (ctx.Content, true);
    }
}

private record PromptContext(string Project, string Content);
```

### mem_save Parameter

```csharp
[McpServerTool(Name = "mem_save", ...)]
public async Task<string> MemSave(
    // ... existing params ...
    
    [McpParameter(Description = "Automatically capture the current user prompt when available (default: true). Set false for SDD artifacts or automated saves.")]
    bool capturePrompt = true)
{
    // ... resolve project, save observation ...
    
    if (capturePrompt && _activity != null)
    {
        var (prompt, found) = _activity.CurrentPrompt(sessionId, project);
        if (found)
        {
            await AddPromptIfMissing(new AddPromptParams
            {
                SessionId = sessionId,
                Project = project,
                Content = prompt
            });
        }
    }
    
    // ... return result ...
}
```

### Deduplication

**Problem**: User might call `mem_save` multiple times for the same prompt.

**Solution**: `AddPromptIfMissing` checks if identical (sessionId, project, content) already exists.

```csharp
private async Task AddPromptIfMissing(AddPromptParams p)
{
    // Check if prompt already exists for this session/project/content
    var existing = await _store.ListPromptsAsync(p.SessionId, p.Project);
    if (existing.Any(pr => pr.Content == p.Content))
        return; // Already exists, skip
    
    await _store.AddPromptAsync(p);
}
```

---

## API Changes

### mem_save Tool (Modified)

**New optional parameter**:
```json
{
  "capture_prompt": {
    "type": "boolean",
    "description": "Automatically capture the current user prompt when available (default: true). Set false for SDD artifacts or automated saves.",
    "default": true
  }
}
```

### mem_save_prompt Tool (Existing)

No changes required. Tool already exists in .NET (line 303 in EngramTools.cs).

---

## Implementation Plan

### Files to Modify

| File | Action | Lines |
|------|--------|-------|
| `src/Engram.Mcp/SessionActivity.cs` | Add RecordPrompt + CurrentPrompt | +25 |
| `src/Engram.Mcp/EngramTools.cs` | Modify MemSave to auto-capture | +15 |
| `src/Engram.Mcp/PromptContext.cs` | Data structure | +10 |
| `tests/Engram.Mcp.Tests/PromptCaptureTests.cs` | Unit tests | +50 |

### Tasks

- [ ] 1. Add `PromptContext` record (project, content)
- [ ] 2. Add `_sessionPrompts` dictionary to SessionActivity
- [ ] 3. Implement `RecordPrompt(sessionId, project, content)`
- [ ] 4. Implement `CurrentPrompt(sessionId, project) → (content, found)`
- [ ] 5. Modify `MemSave` to accept `capturePrompt` parameter
- [ ] 6. Add auto-capture logic after saving observation
- [ ] 7. Implement `AddPromptIfMissing` for deduplication
- [ ] 8. Unit tests: capture on save, dedupe, skip on mismatch
- [ ] 9. Integration tests: full flow mem_save_prompt → mem_save

**Effort**: 1–2h

---

## When to Disable

Set `capture_prompt: false` when:

1. **SDD artifacts**: `mem_save(type: "architecture", ...)` — the prompt isn't relevant
2. **Automated saves**: Scripts, CI/CD, scheduled tasks
3. **Passive captures**: `capture_passive` background saves
4. **Bulk operations**: Importing data, migrations

**Default is `true`**: Most agent interactions benefit from context preservation.

---

## Security Considerations

- **No sensitive data**: Prompts are user-facing questions, not credentials
- **Session-scoped**: Prompts tied to sessionId, not shared across sessions
- **Project-scoped**: Prompt only captured if it matches the save's project
- **In-memory only**: No persistent storage of prompt context (ephemeral)

---

## UX Benefits

1. **Richer search results**: When searching memories, seeing the prompt helps context
2. **Better team collaboration**: Other devs understand the "why" behind changes
3. **Debugging aid**: If a change causes issues, the original intent is preserved
4. **No manual work**: Automatic — agents don't need to remember to capture context

---

## Alternatives Considered

### Alternative 1: Include Prompt in Observation Content

**Proposal**: Agents manually include the prompt in the observation text.

**Rejected**: Inconsistent. Some agents will forget. Format will vary.

### Alternative 2: Separate mem_link_prompt Tool

**Proposal**: Explicit tool to link a prompt to an observation after save.

**Rejected**: Extra step. Agents won't use it consistently. Auto-capture is seamless.

### Alternative 3: Always Capture (No Opt-Out)

**Proposal**: Always capture prompt, no `capture_prompt` parameter.

**Rejected**: Some saves (SDD artifacts, automated) don't benefit from prompt context. Clutters database.

---

## References

- Go upstream PR #309: "feat(mcp): auto-capture prompt context on save"
- Go upstream commit `a9b83d6`: Implementation of RecordPrompt/CurrentPrompt
- Go upstream `internal/mcp/mcp.go`: handleSave with capture_prompt logic
- Go upstream `internal/mcp/activity.go`: Prompt context methods

---

## Decision

**Approved for immediate implementation** as quick win.

**Rationale**: Small effort (1-2h), high UX value, no breaking changes. .NET already has `mem_save_prompt` — just needs SessionActivity integration.

**Not blocking**: Can implement before, during, or after offline-first-sync. Independent feature.
