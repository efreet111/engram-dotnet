# HU-060 — code-aware-capture-parte-b

**As**: AI Agent (Memory phase)  
**I want**: capture decisions with code-aware metadata (file_path, symbol, namespace)  
**To**: make decisions traceable to exact code locations for future retrieval

---

## Acceptance Criteria

- [x] Decisions extracted from plan.md include `file_path` metadata when code reference exists
- [x] Decisions extracted from plan.md include `symbol` metadata when class/function is identified
- [x] Decisions extracted from plan.md include `namespace` metadata when module is identified
- [x] Partial metadata capture: if only file_path is known, symbol and namespace are null
- [x] Fallback to text-only capture when ENG-483 tools are unavailable (Parte A behavior)
- [x] Tests verify metadata capture for all three fields
- [x] Integration test verifies end-to-end flow from plan.md to engram

**Status:** ✅ Done (2026-09-29) — implementación en `.ai-work/hu-060-code-aware-capture-parte-b/`

---

## Tasks (Implementation)

- [x] ENG-416: Schema evolution (reuse from HU-059) — ✅ Done (HU-063)
- [x] ENG-483: Implement `engram_watch` MCP tool with actions: capture, analyze, track — ✅ Done (HU-053), extendido con HU-060
- [x] ENG-483: Parse file to extract classes, functions, imports — ✅ Done (regex-based via `CodeMetadataExtractor.cs`)
- [x] ENG-483: Auto-generate namespace from file path structure — ✅ Done (regex-based via `CodeMetadataExtractor.cs`)
- [x] Wire code-aware capture to CKP-2 (Plan) post-hook — ✅ Done (`FLOWFORGE-INTEGRATION.md` actualizado)
- [x] Implement fallback logic when ENG-483 is unavailable — ✅ Done (graceful null + debug log)
- [x] Add unit tests for metadata extraction — ✅ Done (53 tests en `CodeMetadataExtractorTests.cs`)
- [x] Add integration test for plan.md → engram flow — ✅ Done (PM-1..PM-4 ejecutados y passing)

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
