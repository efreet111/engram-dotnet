# HU-059 — code-aware-dev-agent

**As**: AI Dev Agent  
**I want**: query memories by code context (file, module, symbol) before and after edits  
**To**: produce code consistent with architectural decisions and team conventions

---

## Acceptance Criteria

- [ ] `mem_recall_for_file(file_path, limit?, type?)` returns memories associated with a file
- [ ] `mem_recall_for_module(module, limit?, type?)` returns memories for a namespace/module
- [ ] `mem_recall_for_symbol(symbol, limit?)` returns memories for a class or function
- [ ] Pre-edit hook surfaces relevant memories before file modification
- [ ] Post-edit hook offers to capture the change as a memory
- [ ] Symbol-level recall integrated when modifying classes/functions
- [ ] Graceful degradation when no memories exist for target

---

## Tasks (Implementation)

- [ ] ENG-416: Schema evolution — add file_path, symbol, namespace columns to observations table
- [ ] ENG-416: Create indexes on file_path, symbol, namespace for query performance
- [ ] ENG-484: Implement `mem_recall_for_file` MCP tool
- [ ] ENG-484: Implement `mem_recall_for_module` MCP tool
- [ ] ENG-484: Implement `mem_recall_for_symbol` MCP tool
- [ ] ENG-484: Add `mem_recall_for_module` to Dev agent initialization
- [ ] ENG-484: Wire pre-edit hook to `mem_recall_for_file`
- [ ] ENG-483: Implement `engram watch` for automatic metadata capture (soft blocker)
- [ ] Add unit tests for each query tool
- [ ] Add integration tests for Dev agent memory flow

---

## Dependencies

- 🔴 ENG-416: Schema evolution (prerequisite for all)
- 🔴 ENG-484: Code-context query tools
- 🟡 ENG-483: engram watch (soft — without it, memories lack automatic metadata)

---

## Notes

- ENG-416 is the universal prerequisite — no code-aware metadata without schema evolution
- HU-061 (Arch Agent) shares the same ENG dependencies — implement ENG-416/484 once, benefit twice
- FlowForge HU-033 maps to this HU
