# HU-060 — code-aware-capture-parte-b

**As**: AI Agent (Memory phase)  
**I want**: capture decisions with code-aware metadata (file_path, symbol, namespace)  
**To**: make decisions traceable to exact code locations for future retrieval

---

## Acceptance Criteria

- [ ] Decisions extracted from plan.md include `file_path` metadata when code reference exists
- [ ] Decisions extracted from plan.md include `symbol` metadata when class/function is identified
- [ ] Decisions extracted from plan.md include `namespace` metadata when module is identified
- [ ] Partial metadata capture: if only file_path is known, symbol and namespace are null
- [ ] Fallback to text-only capture when ENG-483 tools are unavailable (Parte A behavior)
- [ ] Tests verify metadata capture for all three fields
- [ ] Integration test verifies end-to-end flow from plan.md to engram

---

## Tasks (Implementation)

- [ ] ENG-416: Schema evolution (reuse from HU-059)
- [ ] ENG-483: Implement `engram_watch` MCP tool with actions: capture, analyze, track
- [ ] ENG-483: Parse file to extract classes, functions, imports
- [ ] ENG-483: Auto-generate namespace from file path structure
- [ ] Wire code-aware capture to CKP-2 (Plan) post-hook
- [ ] Implement fallback logic when ENG-483 is unavailable
- [ ] Add unit tests for metadata extraction
- [ ] Add integration test for plan.md → engram flow

---

## Dependencies

- 🔴 ENG-416: Schema evolution
- 🔴 ENG-483: Code-aware memory capture (engram watch)
- ✅ HU-030: Parte A already implemented (prerequisite)

---

## Notes

- HU-030 (Parte A) is already done — this extends it with code-aware metadata
- Parte B does not replace Parte A — it adds on top with metadata fields
- FlowForge HU-034 maps to this HU
