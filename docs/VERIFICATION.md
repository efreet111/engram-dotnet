# Verification Tools — Usage Guide

> **SDD**: [`sdd/archive/2026-05-14-verification-tools/`](../sdd/archive/2026-05-14-verification-tools/)

---

## What is it?

Verification tools allow validating that implemented code meets the requirements of a `spec.md`. They use **LLM-as-Judge** (Anthropic API) to evaluate each requirement and generate structured reports.

---

## Components

### `mem_verify_artifact` — Compliance verification

**Purpose**: Verify that a code change satisfies the requirements of a spec.

**Input**:
- `spec_path` — Path to `spec.md`
- `code_diff` — Diff of changes (unified diff or file listing)
- `change_name` — Change identifier (e.g. "verification-tools")

**Output**: `VerificationReport` with:
- `items[]` — Verdict per requirement (Pass/Fail/Untested)
- `coverage_pct` — Percentage of requirements covered
- `pass_pct` — Percentage of passing requirements
- `cycle` — Current cycle number
- `escalate` — `true` if max cycles reached

### `mem_traceability` — Traceability matrix

**Purpose**: Generate RF/RNF → code traceability matrix.

**Input**:
- `spec_path` — Path to `spec.md`

**Output**: `TraceabilityMatrix` with:
- `entries[]` — Requirement + status + evidence (file paths)
- `covered` — Count of covered requirements
- `missing` — Count of missing requirements
- `coverage_pct` — Coverage percentage

### `CycleTracker` — Cycle tracking

**Purpose**: Track rework cycles per change.

**Persistence**: Engram observation with `topic_key = "cycle-count/{change_name}"`.

**Behavior**:
- Cycle 1: First verification
- Cycle 2: Rework (if failures)
- Cycle 3: Escalate to human (if `ENGRAM_VERIFICATION_MAX_CYCLES=3`)

---

## Configuration

### Environment Variables

```bash
# Model for LLM-as-Judge
ENGRAM_VERIFICATION_MODEL=claude-sonnet-4-20250514

# Max cycles before escalation
ENGRAM_VERIFICATION_MAX_CYCLES=3

# Anthropic API key (required ONLY for mem_verify_artifact)
ANTHROPIC_API_KEY=sk-...
```

> **Note**: `ANTHROPIC_API_KEY` is only required for `mem_verify_artifact`. All other MCP tools
> (`mem_save`, `mem_search`, `mem_get`, etc.) work normally without it. If the key is not set,
> the MCP server starts normally and `mem_verify_artifact` returns a structured `api_key_missing`
> error with instructions on how to configure it.

### Missing API Key Behavior

When `ANTHROPIC_API_KEY` is not configured, calling `mem_verify_artifact` returns:

```json
{
  "error": true,
  "error_code": "api_key_missing",
  "message": "ANTHROPIC_API_KEY is not configured — verification is unavailable.",
  "hint": "Set the ANTHROPIC_API_KEY environment variable to enable LLM-based verification"
}
```

---

## Workflow

### 1. Create canonical spec.md

```markdown
# Feature Title

## Objective
Clear description.

## Functional Requirements
- RF-001: System must allow login with email/password
- RF-002: System must validate email before creating account

## Non-Functional Requirements
- RNF-001: Login must respond in < 200ms (p95)
- RNF-002: Passwords must be hashed with bcrypt
```

### 2. Implement changes

```bash
git diff > changes.diff
```

### 3. Verify compliance

```bash
mem_verify_artifact \
  spec_path="sdd/my-feature/spec.md" \
  code_diff="git diff main" \
  change_name="my-feature"
```

**Output**:
```json
{
  "items": [
    {
      "requirement": {"id": "RF-001", "type": "RF", "description": "..."},
      "verdict": "Pass",
      "reasoning": "...",
      "confidence": 0.95,
      "evidence": "src/Engram.Server/EngramServer.cs:65"
    }
  ],
  "coverage_pct": 100.0,
  "pass_pct": 83.3,
  "cycle": 1,
  "escalate": false
}
```

### 4. Iterate (if needed)

```bash
# After fixing
git commit -m "fix: resolve issue"

# Re-verify (Cycle 2)
mem_verify_artifact \
  spec_path="..." \
  code_diff="git diff HEAD~1" \
  change_name="my-feature"
```

### 5. Escalate to human

If after 3 cycles there are failures:
```json
{"cycle": 3, "escalate": true, "summary": "3 cycles without resolution. Escalate to human."}
```

---

## Use Cases

### 1. Verify PR before merge

```bash
mem_verify_artifact \
  spec_path="sdd/my-feature/spec.md" \
  code_diff="git diff origin/main" \
  change_name="my-feature"

# Fail the build if pass_pct < 100
if [ $pass_pct -lt 100 ]; then
  echo "Verification failed: ${pass_pct}% pass rate"
  exit 1
fi
```

### 2. Generate traceability matrix

```bash
mem_traceability \
  spec_path="sdd/auth-module/spec.md"
```

### 3. Track rework cycles

```bash
mem_search query="cycle-count verification-tools"
# → observation with revision_count = current cycle
```

---

## Best Practices

1. **Canonical specs**: Use `- RF-NNN: description` format
2. **Small diffs**: < 200 lines per verification
3. **Confidence threshold**: `>= 0.9` trust, `< 0.7` manual review
4. **Cycle management**: Cycle 1 = initial, Cycle 2 = fix, Cycle 3 = escalate

---

## Troubleshooting

| Problem | Cause | Solution |
|---------|-------|----------|
| `api_key_missing` on `mem_verify_artifact` | `ANTHROPIC_API_KEY` not set | `export ANTHROPIC_API_KEY=sk-...` |
| MCP server crashes on startup (old behavior) | Fixed in ENG-456 | Update to latest version; server now starts without API key |
| Spec not parseable | Non-canonical format | Use `- RF-NNN: description` format |
| False positive in verdict | LLM didn't understand context | Add more context to diff, check `confidence < 0.7` |

---

## See Also

- [SDD](../sdd/archive/2026-05-14-verification-tools/) — Full technical documentation
- [ARCHITECTURE.md](ARCHITECTURE.md#engramverification--compliance-verification) — Module architecture
