# RFCs — Requests for Comments

This directory contains RFCs (Requests for Comments) documenting architectural decisions, feature designs, and technical specifications for engram-dotnet.

## Active RFCs

| RFC | Title | Status | Effort | Priority |
|-----|-------|--------|--------|----------|
| [RFC-001](RFC-001-offline-first-sync-architecture.md) | Offline-First Sync Architecture — Chunk + Mutation Hybrid Protocol | Draft | 32–44h | High |
| [RFC-002](RFC-002-ambiguous-project-recovery.md) | Ambiguous Project Recovery with Cryptographic Tokens | Draft | 4–6h | Medium |
| [RFC-003](RFC-003-prompt-auto-capture.md) | Prompt Auto-Capture on Memory Save | Draft | 1–2h | Low |

## RFC Lifecycle

```
Draft → Review → Approved → Implemented → Obsolete
```

- **Draft**: Initial proposal, open for feedback
- **Review**: Under active discussion
- **Approved**: Ready for implementation
- **Implemented**: Code merged, RFC archived
- **Obsolete**: Superseded by newer RFC

## How to Create an RFC

1. Copy `RFC-XXX-template.md` (create template if doesn't exist)
2. Fill in all sections
3. Submit PR with `type:docs` label
4. Link to related GitHub issue

## Related Documentation

- [Offline-First Sync Feature Index](../OFFLINE-FIRST-SYNC.md)
- [Architecture Documentation](../ARCHITECTURE.md)
- [Roadmap](../ROADMAP.md)

---

**Last Updated**: 2026-05-12
