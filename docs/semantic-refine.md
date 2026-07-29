# Semantic Refinement — Provisional Micronaut Skill Table

When NNC-K cannot resolve a user turn against any resident semantic node, the runtime raises a **semantic page fault**. The research branch mints a provisional micronaut at confidence `0.50` and re-routes the turn.

The next important behavior is **refinement**: if the user later contradicts or corroborates the provisional answer, the runtime should update the *existing* micronaut row rather than spawning an unrelated one.

## Skill Table Model

Each provisional micronaut is a row in a semantic skill table:

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Stable topic id derived from first response |
| `subject` | string | Human-readable title |
| `response` | string | Current best candidate text |
| `confidence` | float | Provisional confidence carried across turns |
| `evidence` | array | Corroborations and successful collapses |
| `contradictions` | array | User corrections and detected errors |
| `provenance` | array | Confidence deltas and action history |

Confidence is bounded between `0.10` and `0.85`. Promotion to `>= 0.85` is a **BOSS** decision; demotion below `0.10` flags the row for review.

## PowerShell Helpers

### `Find-RelevantMicronaut`

Scores existing `.learning/micronauts/*.json` files against recent conversation text plus the current user message. Returns the best matching micronaut file or `$null`.

### `Test-CorrectionSignal`

Classifies a user turn:

| Signal | Triggers |
|--------|----------|
| `contradiction` | `actually`, `wrong`, `incorrect`, `not true`, `correction`, `fix`, `you said`, `is`, `mostly`, `primarily` |
| `corroboration` | `yes`, `that is right`, `correct`, `good answer`, `exactly` |

### `Refine-Micronaut`

Updates the matched row:

- For `contradiction`: confidence `-= 0.05`, append to `contradictions`.
- For `corroboration`: confidence `+= 0.05`, append to `evidence`.
- Always records a `provenance` entry with `action`, `signal`, `confidence_delta`, and `timestamp`.

## Integration in `micronaut-ui.ps1`

1. On every assistant response, `Test-CorrectionSignal` is run against the user message.
2. If a signal is detected and `Find-RelevantMicronaut` returns a strong match (`score >= 3`), `Refine-Micronaut` is called.
3. Otherwise, `Save-Micronaut` mints/updates the row as before.
4. On `Research-And-MintMicronaut`, web results first try to refine an existing provisional row before creating a new one.

## Authority Boundary

- **Model / research workers** propose candidate text only.
- **PowerShell runtime** (`Refine-Micronaut`, `Save-Micronaut`) writes provisional rows.
- **BOSS** verifies and promotes contracts when confidence reaches the promotion threshold.
- **MicronautManager** commits normalized contracts.
- **CHEESE** judges collapsed edges; it does not directly refine rows.

## Example Live Trace

```text
query: What type of natural organic compounds does Saturn have?
  ↓
Yax classify: unresolved (conf=0)
  ↓
semantic page fault
  ↓
research: web results retrieved
  ↓
factory: type_natural_organic scaffolded
  ↓
Save-Micronaut: titan-saturnx27s created [Sek] conf=0.5

user: Actually Saturn's atmosphere is mostly hydrogen and helium.
  ↓
Test-CorrectionSignal: contradiction
  ↓
Find-RelevantMicronaut: titan-saturnx27s (score=4)
  ↓
Refine-Micronaut: titan-saturnx27s contradiction conf=0.45
```

## Files

| Path | Purpose |
|------|---------|
| `micronaut-ui.ps1` | `Save-Micronaut`, `Find-RelevantMicronaut`, `Test-CorrectionSignal`, `Refine-Micronaut` |
| `skills/skill-semantic-refine/SKILL.md` | Skill contract |
| `skills/skill.matrix.toml` | Skill registry entry `agent.semantic_refine` |
