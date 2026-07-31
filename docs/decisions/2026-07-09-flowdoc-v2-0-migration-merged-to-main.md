---
observation_id: 28
type: "pattern"
title: "FlowDoc v2.0 migration merged to main"
created_at: "2026-07-09 03:34:30"
topic_key: "flowdoc-v2-migration-merged-2026-07-08"
project: "team/flowforge"
scope: "team"
generated_at: "2026-07-21T22:00:59.5779196Z"
---

# FlowDoc v2.0 migration merged to main

**What**: Successfully merged `absorb/flowdocs-v2-adoption` into main on 2026-07-08. Branch is now live on origin/main at commit `0f3b872`.

**Why**: Pre-launch cleanup before public release (planned this month). User explicitly approved the merge + push because they want to analyze all the features afterward ("better now than later").

**Where**: 
- Merge commit: `0f3b872`
- Feature commit: `600f6ee feat(flowdoc-v2): absorb FlowDocsv2Adoption + close migration gaps`
- Original branch: `d6a2288` by Crhistian Mendoza
- 16 files changed, +527/-140

**Learned**:

The migration absorbed branch `FlowDocsv2Adoption` (Crhistian, 2026-07-08) which had incomplete parallel updates. The original branch updated 7 files but missed 5 consumer surfaces (template, C# installer, shell scripts, Spanish QUICKSTART, agents) plus had UX issues with the example HU (status:done, hardcoded slug). Full absorb strategy chosen because user is the only adopter and partial migration would have caused internal drift.

**Gap closure pattern** is reusable: when a contributor submits an incomplete migration branch, prefer to finish the missing parallel surfaces in the same PR rather than rejecting or splitting. Document everything in a new ADR (ADR-007 was created for this).

**Workflow notes**:
- User approval pattern: explicit merge approval + explicit push approval are separate actions per `.agents/rules/git-sin-push.md`
- Used `--no-ff` merge for traceability (merge commit preserves "this branch was merged here" history)
- Cannot validate C# compilation locally (no .NET SDK on Linux dev box) — relies on CI `test-installer.yml` which auto-triggers on push to main

**Next steps user mentioned**: "we need to work in these changes... after the merge we need to analice all the features". User wants to do a full feature analysis post-merge. This should be a separate task in the next session.

**Future engram-dotnet work** (saved separately as `engram-dotnet/flowdoc-v2-inspired-enhancements`):
- hu_id field on observations
- tech_debt observation type
- test_ref / code_ref fields
- observation lifecycle (active/superseded/archived)
