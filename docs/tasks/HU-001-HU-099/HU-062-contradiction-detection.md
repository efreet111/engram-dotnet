# HU-062 — contradiction-detection

**As**: AI Agent / Developer  
**I want**: detect conflicting memories and resolve them  
**To**: keep the memory store trustworthy as a source of truth

---

## Acceptance Criteria

- [ ] `mem_check_contradictions(limit?, confidence_threshold?, types?)` implemented
- [ ] Detects direct contradictions (same topic, conflicting content)
- [ ] Detects temporal supersedence (newer memory contradicts older on same topic)
- [ ] Detects embedding similarity conflicts (similar embeddings, different content)
- [ ] Returns confidence score and suggested resolution for each conflict
- [ ] Resolution options: keep both, mark superseded, merge, ignore
- [ ] Auto-mark supersedence when confidence exceeds threshold
- [ ] Periodic execution option (cron) or on-demand
- [ ] Unit tests for contradiction detection logic
- [ ] Integration tests with mocked memory store

---

## Tasks (Implementation)

- [ ] ENG-412: Add memory_type column (decision, insight, transient, convention)
- [ ] ENG-412: Add lifecycle column (persistent, ephemeral, expiring)
- [ ] ENG-412: Add expires_at column for transient memories
- [ ] ENG-414: Implement `mem_check_contradictions` MCP tool
- [ ] ENG-414: Implement heuristics: direct conflict, temporal supersedence, embedding similarity
- [ ] ENG-414: Add resolution workflow via `mem_relations`
- [ ] ENG-418: Extend `mem_search` with hybrid mode (vector + FTS5 + metadata)
- [ ] Add confidence threshold configuration for auto-marking
- [ ] Add cron job or on-demand trigger for contradiction check
- [ ] Add unit tests for each detection heuristic
- [ ] Add integration tests with mocked store

---

## Dependencies

- 🔴 ENG-412: Memory taxonomy & lifecycle (HARD — type-aware conflict detection requires it)
- 🔴 ENG-414: Contradiction logic (HARD — core detection logic)
- 🟡 ENG-416: Schema evolution (soft — helps with temporal contradictions)
- 🟢 ENG-418: Hybrid search (soft — embedding similarity improves detection)

---

## Notes

- Priority P3 — only matters when memory store has 500+ memories
- Most "engram-heavy" HU — if ENG-412/414/418 are prioritized, FlowForge integration is minimal
- ENG-412 is prerequisite for ENG-414 — must implement taxonomy before contradiction logic
- FlowForge HU-036 maps to this HU
