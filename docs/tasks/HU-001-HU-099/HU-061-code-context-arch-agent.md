# HU-061 — code-context-arch-agent

**As**: AI Arch Agent  
**I want**: query past architectural decisions for a module before designing  
**To**: build on existing decisions instead of re-deriving them every session

---

## Acceptance Criteria

- [ ] `mem_recall_for_file` integrated in Arch agent context-loading
- [ ] `mem_decisions_for_module` integrated in Arch agent context-loading
- [ ] Prior decisions are shown as constraints before new design work
- [ ] Warning displayed when proposed design contradicts stored decision
- [ ] Graceful handling when no prior decisions exist (proceed normally)
- [ ] Graceful handling when Engram service is unavailable (stateless derivation)
- [ ] Tests verify Arch agent uses memory context on second execution

---

## Tasks (Implementation)

- [ ] ENG-416: Schema evolution (reuse from HU-059)
- [ ] ENG-484: Implement `mem_decisions_for_module` MCP tool
- [ ] Integrate `mem_decisions_for_module` into Arch agent initialization
- [ ] Implement module detection from user request
- [ ] Add contradiction warning UI when new design conflicts with stored decision
- [ ] Handle "no prior decisions" and "service unavailable" edge cases
- [ ] Add unit tests for contradiction detection
- [ ] Add integration test for Arch agent memory context flow

---

## Dependencies

- 🔴 ENG-416: Schema evolution
- 🔴 ENG-484: Code-context query tools (including mem_decisions_for_module)

---

## Notes

- HU-059 (Dev Agent) shares the same ENG dependencies — implement ENG-416/484 once
- This HU enables "stateful architecture" — Arch agent that remembers past decisions
- FlowForge HU-035 maps to this HU
