
--- FILE: agentic-micronaut.md ---

---
name: agentic-micronaut
description: Agentic Micronaut builder mode. Upgrade other skills into deterministic Micronaut-style runtime skills with TOML routes, manifest skill intents, action classes, and command-path compatibility.
---

# Agentic Micronaut Builder

## When to Use
Use this skill when the user wants to convert or extend an existing skill into a Micronaut-style deterministic runtime skill, especially when agentic stack language (SCXQ2, execution letters, object servers) is also in scope.

## Repo Root Discipline (Hard Constraint)
- Define `REPO_ROOT` as the process working directory (`cwd`) when this skill is invoked.
- Never operate outside `REPO_ROOT`. Subfolders are allowed; `..` escapes and unrelated absolute paths are not.
- If a user asks for paths outside `REPO_ROOT`, stop and request explicit override before continuing.

## Builder Contract (Micronaut-Builder Compatible)

For each target skill, produce or update:
1. `*.toml` skill config with `[routes]` mapping intent -> `Class.method`.
2. `*_manifest.json` with route -> `{ action, skill_intent }`.
3. `actions/*.json` action classes with deterministic op sequences.
4. Command-path compatibility through `semantic-skill-command` intent/route invocation.
5. Deterministic response shape (`ok`, `execution`, domain payload).

## Standard Micronaut Add-on Pack

When adding Micronaut behavior to another skill, prefer a dedicated class like `MicronautAddonActions` with methods:
- `start_project`
- `configure_endpoint`
- `secure_endpoint`
- `inject_bean`
- `schedule_task`
- `event_bridge`

Route naming convention:
- `micronaut_init`
- `micronaut_configure`
- `micronaut_secure`
- `micronaut_inject`
- `micronaut_schedule`
- `micronaut_event`

Endpoint naming convention:
- `/skill/micronaut/init`
- `/skill/micronaut/configure`
- `/skill/micronaut/secure`
- `/skill/micronaut/inject`
- `/skill/micronaut/schedule`
- `/skill/micronaut/event`

## Build Sequence

1. Inspect target skill for existing `toml`, manifest, and actions.
2. Preserve existing intents/routes and append Micronaut add-on intents/routes.
3. Add or update action classes with deterministic XCFE ops only.
4. Add command-path tests (intent and route forms).
5. Validate route-intent-action parity and drift checks if repo and installed copies both exist.

## Agentic Extension Layer

When the prompt includes SCXQ2 or execution-letter language, extend the builder output with:
- Object-server framing (`config.@.toml`, `_execute` sequencing).
- Symbolic packing notes for `.s` artifacts.
- Execution-letter mapping (`.pi`, `.lambda`, `.sum`, `.matrix`, `.grad`) as explanation only unless files are explicitly requested.

## Tool Calling N-grams

Use these n-grams as trigger hints when deciding which tooling/workflow to call.

Unigrams:
- `merge`
- `sync`
- `validate`
- `route`
- `intent`
- `manifest`
- `toml`
- `action`
- `skill`
- `test`

Bigrams:
- `merge skills`
- `sync skills`
- `validate routes`
- `check parity`
- `run tests`
- `patch manifest`
- `update toml`
- `wire intents`
- `map routes`
- `call tool`

Trigrams:
- `merge selected skills`
- `sync repo installed`
- `validate route parity`
- `map intent routes`
- `update manifest routes`
- `append micronaut intents`
- `add action class`
- `run command tests`
- `check contract drift`
- `execute skill command`

Deterministic mapping:
- n-grams about `merge/sync/parity/drift` => use merge + sync flow first.
- n-grams about `intent/route/manifest/toml` => edit/check route contracts before action edits.
- n-grams about `test/validate/check` => run contract and command-path tests before concluding.

## Required Quality Checks

- Every manifest `skill_intent` must exist in TOML `[routes]`.
- Every TOML route target must exist in action classes.
- Skill command execution must work for:
  - direct intent form
  - route form (`/skill/...`)
  - payload forms (JSON, query, `@file`)

## Shared Python Agent Starter (Required Addition)

When a user asks for an autonomous-agent baseline, include this Python 3 starter (or a directly equivalent version) in the response:

```python
# Python 3
import random


class Agent:
    def __init__(self, name, goals):
        self.name = name
        self.goals = goals
        self.knowledge = {}

    def sense(self, environment):
        observation = random.choice(environment)
        print(f"[{self.name}] Observed: {observation}")
        return observation

    def decide(self, observation):
        for goal in self.goals:
            if goal.lower() in observation.lower():
                print(f"[{self.name}] Deciding to act towards goal: {goal}")
                return goal
        return "explore"

    def act(self, action):
        if action == "explore":
            print(f"[{self.name}] Exploring environment...")
        else:
            print(f"[{self.name}] Working on goal: {action}")

    def learn(self, observation, action):
        self.knowledge[observation] = action
        print(f"[{self.name}] Knowledge updated: {self.knowledge}")

    def step(self, environment):
        observation = self.sense(environment)
        action = self.decide(observation)
        self.act(action)
        self.learn(observation, action)


if __name__ == "__main__":
    environment = [
        "New email about project deadline",
        "Low battery warning",
        "Weather is sunny",
        "Task: write report",
    ]

    goals = ["write report", "charge battery"]
    agent = Agent("AutonomousAgent", goals)

    for _ in range(5):
        print("\\n--- Agent Step ---")
        agent.step(environment)
```

## Response Style

- Keep outputs deterministic and concise.
- For implementation tasks, state what was changed, what was verified, and which command proves the result.


--- FILE: as-xcfe-stack-intel.md ---

---
name: as-xcfe-stack-intel
description: Deep repo-wide audit workflow for the AS-XCFE/MX2LM stack. Use when you need to analyze the full project tree (Node, Python, native, codex contracts, schemas, build scripts) and produce a deterministic 5-phase/20-step question-driven report that maps every folder/subfolder, identifies missing or inconsistent pieces with evidence, and outputs a branched next-implementation path aligned with the stack's direction (lawful, deterministic, contract-first).
---

# AS-XCFE Stack Intel

## When to Use
Invoke this skill when you need a deterministic, question-driven audit of the AS-XCFE/MX2LM repo—mapping the folder tree, identifying contract gaps, locating inconsistencies, and charting the next lawful path. Always anchor recommendations in the concrete commands referenced below and in the emitted `Stack Map`/`Gaps`/`Next Path` sections.

## Deterministic Audit Brain
- Follow the five-phase workflow exactly: topology → law/contracts → native/toolchain → runtime/orchestration → next-path planning. Each phase emits traceable evidence (commands + paths) that feed the final JSON summary.
- Structure outputs as required (`Stack Map`, `Folder Intel`, `Contracts & Specimens`, `Gaps`, `Next Path`, `Deterministic Summary`). Each bullet must cite the inspected path/command; embed the evidence in the text.
- The `Deterministic Summary` JSON must reflect the discovered gaps and next steps, so collect the required seed lines (`GAP|...`, `NEXT|...`) while auditing.

## Overview
Run a deterministic, question-driven audit of the entire repo tree and produce:
1. A concise "what exists" map of the stack by subsystem.
2. A "what is missing / inconsistent" gap list with evidence (paths + commands).
3. A next-step implementation path that branches based on the discovered gaps, aligned with the project's direction (lawful, deterministic, contract-first, native-first where appropriate).

## Audit Output Contract
Produce output in this exact structure (headings must match):
- `Stack Map`: 8-15 bullets; each bullet includes subsystem, entrypoints, and one command to validate.
- `Folder Intel`: deterministic, sorted listing of every top-level folder and key subfolders (depth=2), labeled as `execution`, `contracts`, `examples`, `artifacts`, or `projection` when possible.
- `Contracts & Specimens`: what is canonical vs specimen/example, and which validator/verifier covers each.
- `Gaps`: 5-25 bullets; each bullet includes missing/inconsistent behavior, evidence path(s), command used, and impact.
- `Next Path`: 5-12 ordered steps; must branch based on `Gaps`; each step includes a verification command.
- `Deterministic Summary`: a compact JSON block containing stable counts and a stable summary hash (no timestamps).
- `Decisions`: list only if needed; otherwise empty.

Optional sections (allowed, recommended if relevant):
- `Todo (Immediate)`: a flat checklist derived from `Next Path` with owners implied (Node/Python/native/docs) and verification commands.
- `Todo (Future)`: a flat checklist of deferred work with explicit revisit triggers.
- `Stack Comparison Notes`: short bullets comparing parallel implementations (Node vs native vs GPU/FPGA) and calling out spec drift risks.
- `Appendix: Evidence`: a compact list of the exact commands run during audit (copy/paste-able).

## Workflow Decision Tree (5 Phases, 20 Steps)
Rules:
- Each step is phrased as a question.
- Each step includes: what to inspect, the expected "good" signals, and what to do if missing.
- When something is missing, add a `Gap` entry immediately and select the next step based on the "Branch" note.
- Prefer PowerShell commands on Windows.

### Phase 1 - Repo Ground Truth Topology (Steps 1-4)
1. What is the complete folder topology (depth=2), and what is execution vs contracts vs artifacts vs projection?
   - Inspect (PowerShell): `Get-ChildItem -Directory | Sort-Object Name`; then `Get-ChildItem -Directory -Recurse -Depth 2 | Sort-Object FullName`
   - Good: clear separation exists (e.g. `codex/`, `schemas/`, `native/`, `src/`, `runtime/`, `artifacts/`, `examples/`, `docs/`)
   - Branch: if boundaries blur, add `Gap: boundary split rule missing` and prioritize enforcement checks in Phase 2.

2. What are the authoritative commands that must work (Node/Python/native) in this repo?
   - Inspect: `package.json` scripts; `scripts/`; native build scripts; `docs/`
   - Good: at least these are coherent: `npm run test:node`, `npm test`, `npm run asx:native:build`
   - Branch: if docs disagree with scripts, add `Gap: docs/commands drift` and route Next Path to doc+script sync before new features.

3. What is the actual contract surface inventory (schemas + codex + grammar) and where is it anchored?
   - Inspect: `schemas/`, `codex/schema/`, `codex/lex/`, `codex/mx2lex/`, `grammar/`, `codex/examples/`
   - Good: each contract family has schema/spec + specimens/examples + validator/verifier
   - Branch: if any family lacks a checker, add `Gap: contract exists without a validator/verifier`.

4. What is the current verification surface (Node tests, Python tests, native smoke), and what is missing coverage?
   - Inspect: `test/`, `tests/`, `scripts/*validate*`, `artifacts/` smoke binaries
   - Good: deterministic PASS/FAIL checks exist; stable machine-readable outputs exist on critical paths
   - Branch: if verification is missing for a subsystem, add `Gap: missing smoke` and route Next Path to minimal smoke first.

### Phase 2 - Law, Contracts, and Determinism (Steps 5-8)
5. Where is "no-eval / authority / determinism" enforced in code (not only docs)?
   - Inspect: any verifier/validator code paths in `src/` and native in `native/`
   - Good: enforcement exists with hard failures, stable error contracts, stable exit codes
   - Branch: if only documented, add `Gap: law only documented` and prioritize an executable gate.

6. Are canonicalization and hashing stable and used for hash-bound identities at the right layer?
   - Inspect: canonical serializers, sha256 implementations, where hashes are computed/verified
   - Good: canonical ordering is specified and used for hashing; hashes are reproducible across runs
   - Branch: if hash binding exists without canonicalization, record `Gap: hash not reproducible` and prioritize canonicalization.

7. Are examples "specimens" (minimal legal instances), and do they round-trip through validators/verifiers?
   - Inspect: `codex/examples/`, `docs/examples/` (if present)
   - Good: example runs produce the documented OK/FAIL shapes
   - Branch: if examples drift, fix specimens before adding new schema fields.

8. Is there a conformance vector system (batch runner, golden vectors) for MX2LEX and related contracts?
   - Inspect: vector schemas and runner(s); look for deterministic summary hashes
   - Good: PASS/FAIL harness exists; stable output; mismatch detection; stable summary hash rule
   - Branch: if missing, implement vector runner before any trainer work.

### Phase 3 - Native Toolchain and Parsers (Steps 9-12)
9. What native toolchain is assumed (MSVC/clang/g++) and is the build script environment-correct and deterministic?
   - Inspect: `.bat` scripts, `VsDevCmd` usage, include/lib setup
   - Good: a single build command produces all expected binaries under one artifacts folder
   - Branch: if toolchain paths are brittle/hard-coded, record `Gap: toolchain portability` and route Next Path to parameterization.

10. Do native binaries emit machine-readable JSON with stable fields and stable exit codes?
   - Inspect: native tool outputs on both success and error paths
   - Good: "hard error" is distinguishable from a FAIL verdict; stable `@kind` fields; stable ordering for arrays/objects
   - Branch: if mixed semantics, add `Gap: unstable output ABI` and normalize output contracts.

11. Does the native `.asx` surface parser cover all specimen shapes currently used in the repo?
   - Inspect: `.asx` specimens and parser feature support (nested objects, arrays, scalars)
   - Good: parser supports the specimen grammar used by the repo's lawful slice
   - Branch: if any specimen cannot be represented, expand parser before expanding contracts.

12. Are there missing native counterparts for critical-path components (SCXQ2 decode/loader, pack/unpack, schema loader)?
   - Inspect: native implementations and docs calling for decode/unpack
   - Good: minimal working slice exists with tests/vectors
   - Branch: if decode/unpack blocks runtime claims, prioritize it over new frontends.

### Phase 4 - Runtime and Execution Paths (Steps 13-16)
13. What runtime implementations exist today (CPU/GPU/FPGA/native VM), and what contract surface do they actually support?
   - Inspect: `runtime/`, `gpu/`, `fpga/`, `native/` runtime projects
   - Good: "supported" is backed by conformance tests or status docs
   - Branch: if runtime claims exceed tests, add `Gap: runtime claim>test` and route Next Path to coverage or downgrade.

14. Is there a clean boundary between data-only UI/holotape and executable nodes/actions end to end?
   - Inspect: router/loader logic; ensure config is data-only; ensure execution is explicit and gated
   - Good: only explicit bindings invoke execution; config remains data; policy/law gates are enforced
   - Branch: if inline execution exists, add `Gap: data/executable boundary violated` and enforce a validator gate.

15. Is SCXQ2 pack/unpack consistent across layers (JS compiler, native loader skeleton, GPU kernel)?
   - Inspect: SCXQ2 pack/unpack in `src/`, native loaders, GPU decode kernel(s), and any SCXQ2 specs
   - Good: one spec is treated as the source of truth; magic/versioning consistent; round-trip vectors exist
   - Branch: if inconsistent, add `Gap: SCXQ2 spec drift` and route Next Path to reconcile + add round-trip vectors.

16. Do routing/orchestration layers record deterministic provenance without contaminating deterministic compile artifacts?
   - Inspect: result contracts, artifact separation, run metadata separation
   - Good: compile outputs deterministic; run metadata separated; stable ordering preserved
   - Branch: if nondet leaks into canonical artifacts, add `Gap: nondet in canonical artifacts` and split output contracts.

### Phase 5 - Choose Next Implementation Path (Steps 17-20)
17. What is the single highest-leverage missing component (top blocker) that unlocks multiple subsystems?
   - Pick from discovered gaps (common blockers: SCXQ2 spec drift, canonical hashing gaps, missing vector coverage, brittle build)
   - Output: `Top Blocker` with evidence (2+ concrete paths/commands) and why it dominates.

18. What is the minimal lawful implementation slice that resolves the top blocker with deterministic verification?
   - Define: exact contract(s) touched, code change boundary, exit codes, and one conformance vector
   - Output: `Slice Plan` with commands and expected outputs.

19. What are the next two follow-on steps after the blocker (A then B) that keep momentum and preserve direction?
   - A should extend verification/coverage; B should extend capability/runtime integration
   - Output: `Follow-on A/B` with dependencies and verification.

20. What is explicitly deferred (to avoid premature complexity), and what triggers revisiting it?
   - Output: `Deferred` list (3-10 bullets), each with reason and revisit trigger.

## Todo Templates (Use In Output)
Use these templates when the audit discovers gaps. Keep them flat (no nested bullets) and deterministic.

### Todo (Immediate)
- [ ] <short_action> (layer: node|python|native|gpu|fpga|docs) verify: `<command>`
- [ ] <short_action> (layer: ...) verify: `<command>`

### Todo (Future)
- [ ] <deferred_action> revisit when: <trigger> (risk: <low|med|high>)
- [ ] <deferred_action> revisit when: <trigger> (risk: ...)

## Future Work Sections (What To Suggest, Not Implement)
When you include `Todo (Future)`, prefer these categories (only if the audit evidence supports them):
- Canonicalization and hashing hardening (cross-language reproducibility).
- Conformance vectors (goldens) for any contract that claims determinism.
- SCXQ2 round-trip compatibility (JS pack/unpack <-> native loader <-> GPU decode).
- Runtime contract coverage (claims backed by tests, or downgrade claims).
- Toolchain portability (parameterize MSVC paths, document fallback compilers).

## Stack Comparison Notes (Repo-Style Guidance)
This repo intentionally mixes layers. In the audit output, add a short comparison section when the same concept exists in multiple layers.

Comparison prompts to answer (pick only what is relevant):
- Node (JS) vs native (C++): are they implementing the same contract ABI, or different ones?
- JS SCXQ2 vs native SCXQ2 vs GPU SCXQ2Decode: do they share magic/versioning and test vectors?
- Validation surface: which layer is "authoritative gate" vs "reference implementation" vs "prototype"?
- Determinism: which layer is currently strongest on canonical ordering + hashing?

Rules for comparison notes:
- Always include at least one concrete path per bullet (e.g. `src/asx-compiler.js`, `native/asx-core/*`, `gpu/*.hlsl`).
- If there is drift, name it as a `Gap` and route `Next Path` to a reconciliation + vector plan.
- Do not recommend deleting a layer. Recommend narrowing claims and adding conformance first.

## Deterministic Summary (Required)
Append a JSON block at the end of the audit report:
- Must be stable for the same repo state and same audit inputs.
- Must not include timestamps, random IDs, or machine-specific paths.
- `@summary_hash` rule: compute a SHA-256 over a newline-joined list of stable "seed lines":
  - For each `Gap`: `GAP|<short_code>|<path>|<impact>`
  - For each `Next Path` step: `NEXT|<step_number>|<short_action>|<verify_command>`
  - Sort seed lines lexicographically before hashing.

Example JSON shape:
```json
{
  "@kind": "as-xcfe.stack-intel.summary.v1",
  "@counts": { "folders": 0, "contracts": 0, "gaps": 0 },
  "@top_blocker": "SCXQ2_SPEC_DRIFT",
  "@summary_hash": "sha256:..."
}
```

## Output Discipline (Deterministic)
When presenting findings:
- Always cite the path(s) inspected and the command(s) used.
- Prefer stable, machine-readable evidence (schemas, code, tests) over narrative.
- If a component exists but is incomplete, classify it as `Partial` and state the missing behaviors as verifiable claims.

## Suggested Audit Checklist (For The Auditor)
Use this as an internal checklist while producing the report. Do not paste this section into the report unless asked.

Phase 1 checklist:
- [ ] Folder tree captured (depth=2, sorted)
- [ ] `package.json` scripts captured
- [ ] Top-level contract families enumerated (`schemas/`, `codex/`, `grammar/`)
- [ ] Verification commands identified for Node, Python, native

Phase 2 checklist:
- [ ] Law enforcement points identified (no-eval, authority, determinism)
- [ ] Hashing + canonicalization evidence captured (paths + commands)
- [ ] Specimens round-tripped through verifiers/validators
- [ ] Vector harness existence confirmed (or recorded as Gap)

Phase 3 checklist:
- [ ] Native build path confirmed (or recorded as Gap)
- [ ] Native tool output ABI stable (or recorded as Gap)
- [ ] `.asx` parser coverage checked against specimens
- [ ] Critical native counterparts identified (SCXQ2 loader, etc.)

Phase 4 checklist:
- [ ] Runtime claims checked against tests/vectors
- [ ] Data-only UI/holotape boundary validated
- [ ] SCXQ2 pack/unpack consistency checked across layers
- [ ] Provenance separation checked (compile artifacts vs run metadata)

Phase 5 checklist:
- [ ] Top blocker chosen with evidence
- [ ] Minimal slice plan defined with verification
- [ ] Follow-on A/B defined with verification
- [ ] Deferred list has revisit triggers


--- FILE: chatgpt-apps.md ---

---
name: chatgpt-apps
description: Build, scaffold, refactor, and troubleshoot ChatGPT Apps SDK applications that combine an MCP server and widget UI. Use when Codex needs to design tools, register UI resources, wire the MCP Apps bridge or ChatGPT compatibility APIs, apply Apps SDK metadata or CSP or domain settings, or produce a docs-aligned project scaffold. Prefer a docs-first workflow by invoking the openai-docs skill or OpenAI developer docs MCP tools before generating code.
---

# ChatGPT Apps

## Overview

Scaffold ChatGPT Apps SDK implementations with a docs-first, example-first workflow, then generate code that follows current Apps SDK and MCP Apps bridge patterns.

Use this skill to produce:

- A primary app-archetype classification and repo-shape decision
- A tool plan (names, schemas, annotations, outputs)
- An upstream starting-point recommendation (official example, ext-apps example, or local fallback scaffold)
- An MCP server scaffold (resource registration, tool handlers, metadata)
- A widget scaffold (MCP Apps bridge first, `window.openai` compatibility/extensions second)
- A reusable Node + `@modelcontextprotocol/ext-apps` starter scaffold for low-dependency fallbacks
- A validation report against the minimum working repo contract
- Local dev and connector setup steps
- A short stakeholder summary of what the app does (when requested)

## Mandatory Docs-First Workflow

Use `$openai-docs` first whenever building or changing a ChatGPT Apps SDK app.

1. Invoke `$openai-docs` (preferred) or call the OpenAI docs MCP server directly.
2. Fetch current Apps SDK docs before writing code, especially (baseline pages):
   - `apps-sdk/build/mcp-server`
   - `apps-sdk/build/chatgpt-ui`
   - `apps-sdk/build/examples`
   - `apps-sdk/plan/tools`
   - `apps-sdk/reference`
3. Fetch `apps-sdk/quickstart` when scaffolding a new app or generating a first-pass implementation, and check the official examples repo/page before inventing a scaffold from scratch.
4. Fetch deployment/submission docs when the task includes local ChatGPT testing, hosting, or public launch:
   - `apps-sdk/deploy`
   - `apps-sdk/deploy/submission`
   - `apps-sdk/app-submission-guidelines`
5. Cite the docs URLs you used when explaining design choices or generated scaffolds.
6. Prefer current docs guidance over older repo patterns when they differ, and call out compatibility aliases explicitly.
7. If doc search times out or returns poor matches, fetch the canonical Apps SDK pages directly by URL and continue; do not let search failure block scaffolding.

If `$openai-docs` is unavailable, use:

- `mcp__openaiDeveloperDocs__search_openai_docs`
- `mcp__openaiDeveloperDocs__fetch_openai_doc`

Read `references/apps-sdk-docs-workflow.md` for suggested doc queries and a compact checklist.
Read `references/app-archetypes.md` to classify the request into a small number of supported app shapes before choosing examples or scaffolds.
Read `references/repo-contract-and-validation.md` when generating or reviewing a repo so the output stays inside a stable “working app” contract.
Read `references/search-fetch-standard.md` when the app is connector-like, data-only, sync-oriented, or meant to work well with company knowledge or deep research.
Read `references/upstream-example-workflow.md` when starting a greenfield app or when deciding whether to adapt an upstream example or use the local fallback scaffold.
Read `references/window-openai-patterns.md` when the task needs ChatGPT-specific widget behavior or when translating repo examples that use wrapper-specific `app.*` helpers.

## Prompt Guidance

Use prompts that explicitly pair this skill with `$openai-docs` so the resulting scaffold is grounded in current docs.

Preferred prompt patterns:

- `Use $chatgpt-apps with $openai-docs to scaffold a ChatGPT app for <use case> with a <TS/Python> MCP server and <React/vanilla> widget.`
- `Use $chatgpt-apps with $openai-docs to adapt the closest official Apps SDK example into a ChatGPT app for <use case>.`
- `Use $chatgpt-apps and $openai-docs to refactor this Apps SDK demo into a production-ready structure with tool annotations, CSP, and URI versioning.`
- `Use $chatgpt-apps with $openai-docs to plan tools first, then generate the MCP server and widget code.`

When responding, ask for or infer these inputs before coding:

- Use case and primary user flows
- Read-only vs mutating tools
- Demo vs production target
- Private/internal use vs public directory submission
- Backend language and UI stack
- Auth requirements
- External API domains for CSP allowlists
- Hosting target and local dev approach
- Org ownership/verification readiness (for submission tasks)

## Classify The App Before Choosing Code

Before choosing examples, repo shape, or scaffolds, classify the request into one primary archetype and state it.

- `tool-only`
- `vanilla-widget`
- `react-widget`
- `interactive-decoupled`
- `submission-ready`

Infer the archetype unless a missing detail is truly blocking. Use the archetype to choose:

- whether a UI is needed at all
- whether to preserve a split `server/` + `web/` layout
- whether to prefer official OpenAI examples, ext-apps examples, or the local fallback scaffold
- which validation checks matter most
- whether `search` and `fetch` should be the default read-only tool surface

Read `references/app-archetypes.md` for the decision rubric.

## Default Starting-Point Order

For greenfield apps, prefer these starting points in order:

1. **Official OpenAI examples** when a close example already matches the requested stack or interaction pattern.
2. **Version-matched `@modelcontextprotocol/ext-apps` examples** when the user needs a lower-level or more portable MCP Apps baseline.
3. **`scripts/scaffold_node_ext_apps.mjs`** only when no close example fits, the user wants a tiny Node + vanilla starter, or network access/example retrieval is undesirable.

Do not generate a large custom scaffold from scratch if a close upstream example already exists.
Copy the smallest matching example, remove unrelated demo code, then patch it to the current docs and the user request.

## Build Workflow

### 0. Classify The App Archetype

Pick one primary archetype before planning tools or choosing a starting point.

- Prefer a single primary archetype instead of mixing several.
- If the request is broad, infer the smallest archetype that can still satisfy it.
- Escalate to `submission-ready` only when the user asks for public launch, directory submission, or review-ready deployment.
- Call out the chosen archetype in your response so the user can correct it early if needed.

### 1. Plan Tools Before Code

Define the tool surface area from user intents.

- Use one job per tool.
- Write tool descriptions that start with "Use this when..." behavior cues.
- Make inputs explicit and machine-friendly (enums, required fields, bounds).
- Decide whether each tool is data-only, render-only, or both.
- Set annotations accurately (`readOnlyHint`, `destructiveHint`, `openWorldHint`; add `idempotentHint` when true).
- If the app is connector-like, data-only, sync-oriented, or intended for company knowledge or deep research, default to the standard `search` and `fetch` tools instead of inventing custom read-only equivalents.
- For educational/demo apps, prefer one concept per tool so the model can pick the right example cleanly.
- Group demo tools by learning objective: data into the widget, widget actions back into the conversation or tools, host/layout environment signals, and lifecycle/streaming behavior.

Read `references/search-fetch-standard.md` when `search` and `fetch` may be relevant.

### 2. Choose an App Architecture

Choose the simplest structure that fits the goal.

- Use a **minimal demo pattern** for quick prototypes, workshops, or proofs of concept.
- Use a **decoupled data/render pattern** for production UX so the widget does not re-render on every tool call.

Prefer the decoupled pattern for non-trivial apps:

- Data tools return reusable `structuredContent`.
- Render tools attach `_meta.ui.resourceUri` and optional `_meta["openai/outputTemplate"]`.
- Render tool descriptions state prerequisites (for example, "Call `search` first").

### 2a. Start From An Upstream Example When One Fits

Default to upstream examples for greenfield work when they are close to the requested app.

- Check the official OpenAI examples first for ChatGPT-facing apps, polished UI patterns, React components, file upload flows, modal flows, or apps that resemble the docs examples.
- Use `@modelcontextprotocol/ext-apps` examples when the request is closer to raw MCP Apps bridge/server wiring, or when version-matched package patterns matter more than ChatGPT-specific polish.
- Pick the smallest matching example and copy only the relevant files; do not transplant an entire showcase app unchanged.
- After copying, reconcile the example with the current docs you fetched: tool names/descriptions, annotations, `_meta.ui.*`, CSP, URI versioning, and local run instructions.
- State which example you chose and why in one sentence.

Read `references/upstream-example-workflow.md` for the selection and adaptation rubric.

### 2b. Use the Starter Script When a Low-Dependency Fallback Helps

Use `scripts/scaffold_node_ext_apps.mjs` only when the user wants a quick, greenfield Node starter and a vanilla HTML widget is acceptable, and no upstream example is a better starting point.

- Run it only after fetching current docs, then reconcile the generated files with the docs you fetched.
- If you choose the script instead of an upstream example, say why the fallback is better for that request.
- Skip it when a close official example exists, when the user already has an existing app structure, when they need a non-Node stack, when they explicitly want React first, or when they only want a plan/review instead of code.
- The script generates a minimal `@modelcontextprotocol/ext-apps` server plus a vanilla HTML widget that uses the MCP Apps bridge by default.
- The generated widget keeps follow-up messaging on the standard `ui/message` bridge and only uses `window.openai` for optional host signals/extensions.
- After running it, patch the generated output to match the current docs and the user request: adjust tool names/descriptions, annotations, resource metadata, URI versioning, and README/run instructions.

### 3. Scaffold the MCP Server

Generate a server that:

- Registers a widget resource/template with the MCP Apps UI MIME type (`text/html;profile=mcp-app`) or the SDK constant (`RESOURCE_MIME_TYPE`) when using `@modelcontextprotocol/ext-apps/server`
- Registers tools with clear names, schemas, titles, and descriptions
- Returns `structuredContent` (model + widget), `content` (model narration), and `_meta` (widget-only data) intentionally
- Keeps handlers idempotent or documents non-idempotent behavior explicitly
- Includes tool status strings (`openai/toolInvocation/*`) when helpful in ChatGPT

Keep `structuredContent` concise. Move large or sensitive widget-only payloads to `_meta`.

### 4. Scaffold the Widget UI

Use the MCP Apps bridge first for portability, then add ChatGPT-specific `window.openai` APIs when they materially improve UX.

- Listen for `ui/notifications/tool-result` (JSON-RPC over `postMessage`)
- Render from `structuredContent`
- Use `tools/call` for component-initiated tool calls
- Use `ui/update-model-context` only when UI state should change what the model sees

Use `window.openai` for compatibility and extensions (file upload, modal, display mode, etc.), not as the only integration path for new apps.

#### API Surface Guardrails

- Some examples wrap the bridge with an `app` object (for example, `@modelcontextprotocol/ext-apps/react`) and expose helper names like `app.sendMessage()`, `app.callServerTool()`, `app.openLink()`, or host getter methods.
- Treat those wrappers as implementation details or convenience layers, not the canonical public API to teach by default.
- For ChatGPT-facing guidance, prefer the current documented surface: `window.openai.callTool(...)`, `window.openai.sendFollowUpMessage(...)`, `window.openai.openExternal(...)`, `window.openai.requestDisplayMode(...)`, and direct globals like `window.openai.theme`, `window.openai.locale`, `window.openai.displayMode`, `window.openai.toolInput`, `window.openai.toolOutput`, `window.openai.toolResponseMetadata`, and `window.openai.widgetState`.
- If you reference wrapper helpers from repo examples, map them back to the documented `window.openai` or MCP Apps bridge primitives and call out that the wrapper is not the normative API surface.
- Use `references/window-openai-patterns.md` for the wrapper-to-canonical mapping and for React helper extraction patterns.

### 5. Add Resource Metadata and Security

Set resource metadata deliberately on the widget resource/template:

- `_meta.ui.csp` with exact `connectDomains` and `resourceDomains`
- `_meta.ui.domain` for app submission-ready deployments
- `_meta.ui.prefersBorder` (or OpenAI compatibility alias when needed)
- Optional `openai/widgetDescription` to reduce redundant narration

Avoid `frameDomains` unless iframe embeds are core to the product.

### 5a. Enforce A Minimum Working Repo Contract

Every generated repo should satisfy a small, stable contract before you consider it done.

- The repo shape matches the chosen archetype.
- The MCP server and tools are wired to a reachable `/mcp` endpoint.
- Tools have clear descriptions, accurate annotations, and UI metadata where needed.
- Connector-like, data-only, sync-oriented, and company-knowledge-style apps use the standard `search` and `fetch` tool shapes when relevant.
- The widget uses the MCP Apps bridge correctly when a UI exists.
- The repo includes enough scripts or commands for a user to run and check it locally.
- The response explicitly says what validation was run and what was not run.

Read `references/repo-contract-and-validation.md` for the detailed checklist and validation ladder.

### 6. Validate the Local Loop

Validate against the minimum working repo contract, not just “did files get created.”

- Run the lowest-cost checks first:
  - static contract review
  - syntax or compile checks when feasible
  - local `/mcp` health check when feasible
- Then move up to runtime checks:
  - verify tool descriptors and widget rendering in MCP Inspector
  - test the app in ChatGPT developer mode through HTTPS tunneling
  - exercise retries and repeated tool calls to confirm idempotent behavior
  - check widget updates after host events and follow-up tool calls
- If you are only delivering a scaffold and are not installing dependencies, still run low-cost checks and say exactly what you did not run.

Read `references/repo-contract-and-validation.md` for the validation ladder.

### 7. Connect and Test in ChatGPT (Developer Mode)

For local development, include explicit ChatGPT setup steps (not just code/run commands).

- Run the MCP server locally on `http://localhost:<port>/mcp`
- Expose the local server with a public HTTPS tunnel (for example `ngrok http <port>`)
- Use the tunneled HTTPS URL plus `/mcp` path when connecting from ChatGPT
- In ChatGPT, enable Developer Mode under **Settings → Apps & Connectors → Advanced settings**
- In ChatGPT app settings, create a new app for the remote MCP server and paste the public MCP URL
- Tell users to refresh the app after MCP tool/metadata changes so ChatGPT reloads the latest descriptors

Note: Some docs/screenshots still use older "connector" terminology. Prefer current product wording ("app") while acknowledging both labels when giving step-by-step instructions.

### 8. Plan Production Hosting and Deployment

When the user asks to deploy or prepare for launch, generate hosting guidance for the MCP server (and widget assets if hosted separately).

- Host behind a stable public HTTPS endpoint (not a tunnel) with dependable TLS
- Preserve low-latency streaming behavior on `/mcp`
- Configure secrets outside the repo (environment variables / secret manager)
- Add logging, request latency tracking, and error visibility for tool calls
- Add basic observability (CPU, memory, request volume) and a troubleshooting path
- Re-test the hosted endpoint in ChatGPT Developer Mode before submission

### 9. Prepare Submission and Publish (Public Apps Only)

Only include these steps when the user intends a public directory listing.

- Use `apps-sdk/deploy/submission` for the submission flow and `apps-sdk/app-submission-guidelines` for review requirements
- Keep private/internal apps in Developer Mode instead of submitting
- Confirm org verification and Owner-role prerequisites before submission work
- Ensure the MCP server uses a public production endpoint (no localhost/testing URLs) and has submission-ready CSP configured
- Prepare submission artifacts: app metadata, logo/screenshots, privacy policy URL, support contact, test prompts/responses, localization info
- If auth is required, include review-safe demo credentials and test the login path end-to-end
- Submit for review in the Platform dashboard, monitor review status, and publish only after approval

## Interactive State Guidance

Read `references/interactive-state-sync-patterns.md` when the app has long-lived widget state, repeated interactions, or component-initiated tool calls (for example, games, boards, maps, dashboards, editors).

Use it to choose patterns for:

- State snapshots plus monotonic event tokens (`stateVersion`, `resetCount`, etc.)
- Idempotent retry-safe handlers
- `structuredContent` vs `_meta` partitioning
- MCP Apps bridge-first update flows with optional `window.openai` compatibility
- Decoupled data/render tool architecture for more complex interactive apps

## Output Expectations

When using this skill to scaffold code, produce output in this order unless the user asks otherwise:

- For direct scaffold requests, do not stop at the plan: give the brief plan, then create the files immediately.

1. Primary app archetype chosen and why
2. Tool plan and architecture choice (minimal vs decoupled)
3. Upstream starting point chosen (official example, ext-apps example, or local fallback scaffold) and why
4. Doc pages/URLs used from `$openai-docs`
5. File tree to create or modify
6. Implementation (server + widget)
7. Validation performed against the minimum working repo contract
8. Local run/test instructions (including tunnel + ChatGPT Developer Mode app setup)
9. Deployment/hosting guidance (if requested or implied)
10. Submission-readiness checklist (for public launch requests)
11. Risks, gaps, and follow-up improvements

## References

- `references/app-archetypes.md` for classifying requests into a small number of supported app shapes
- `references/apps-sdk-docs-workflow.md` for doc queries, page targets, and code-generation checklist
- `references/interactive-state-sync-patterns.md` for reusable patterns for stateful or highly interactive widget apps
- `references/repo-contract-and-validation.md` for the minimum working repo contract and lightweight validation ladder
- `references/search-fetch-standard.md` for when and how to default to the standard `search` and `fetch` tools
- `references/upstream-example-workflow.md` for choosing between official examples, ext-apps examples, and the local fallback scaffold
- `references/window-openai-patterns.md` for ChatGPT-specific extensions, wrapper API translation, and React helper patterns
- `scripts/scaffold_node_ext_apps.mjs` for a minimal Node + `@modelcontextprotocol/ext-apps` fallback starter scaffold


--- FILE: cloudflare-deploy.md ---

---
name: cloudflare-deploy
description: Deploy applications and infrastructure to Cloudflare using Workers, Pages, and related platform services. Use when the user asks to deploy, host, publish, or set up a project on Cloudflare.
---

# Cloudflare Deploy

Consolidated skill for building on the Cloudflare platform. Use decision trees below to find the right product, then load detailed references.

## Prerequisites

- When sandboxing blocks the deployment network calls, rerun with `sandbox_permissions=require_escalated`.
- The deployment might take a few minutes. Use appropriate timeout values.

## Authentication (Required Before Deploy)

Verify auth before `wrangler deploy`, `wrangler pages deploy`, or `npm run deploy`:

```bash
npx wrangler whoami    # Shows account if authenticated
```

Not authenticated? → `references/wrangler/auth.md`
- Interactive/local: `wrangler login` (one-time OAuth)
- CI/CD: Set `CLOUDFLARE_API_TOKEN` env var

## Quick Decision Trees

### "I need to run code"

```
Need to run code?
├─ Serverless functions at the edge → workers/
├─ Full-stack web app with Git deploys → pages/
├─ Stateful coordination/real-time → durable-objects/
├─ Long-running multi-step jobs → workflows/
├─ Run containers → containers/
├─ Multi-tenant (customers deploy code) → workers-for-platforms/
├─ Scheduled tasks (cron) → cron-triggers/
├─ Lightweight edge logic (modify HTTP) → snippets/
├─ Process Worker execution events (logs/observability) → tail-workers/
└─ Optimize latency to backend infrastructure → smart-placement/
```

### "I need to store data"

```
Need storage?
├─ Key-value (config, sessions, cache) → kv/
├─ Relational SQL → d1/ (SQLite) or hyperdrive/ (existing Postgres/MySQL)
├─ Object/file storage (S3-compatible) → r2/
├─ Message queue (async processing) → queues/
├─ Vector embeddings (AI/semantic search) → vectorize/
├─ Strongly-consistent per-entity state → durable-objects/ (DO storage)
├─ Secrets management → secrets-store/
├─ Streaming ETL to R2 → pipelines/
└─ Persistent cache (long-term retention) → cache-reserve/
```

### "I need AI/ML"

```
Need AI?
├─ Run inference (LLMs, embeddings, images) → workers-ai/
├─ Vector database for RAG/search → vectorize/
├─ Build stateful AI agents → agents-sdk/
├─ Gateway for any AI provider (caching, routing) → ai-gateway/
└─ AI-powered search widget → ai-search/
```

### "I need networking/connectivity"

```
Need networking?
├─ Expose local service to internet → tunnel/
├─ TCP/UDP proxy (non-HTTP) → spectrum/
├─ WebRTC TURN server → turn/
├─ Private network connectivity → network-interconnect/
├─ Optimize routing → argo-smart-routing/
├─ Optimize latency to backend (not user) → smart-placement/
└─ Real-time video/audio → realtimekit/ or realtime-sfu/
```

### "I need security"

```
Need security?
├─ Web Application Firewall → waf/
├─ DDoS protection → ddos/
├─ Bot detection/management → bot-management/
├─ API protection → api-shield/
├─ CAPTCHA alternative → turnstile/
└─ Credential leak detection → waf/ (managed ruleset)
```

### "I need media/content"

```
Need media?
├─ Image optimization/transformation → images/
├─ Video streaming/encoding → stream/
├─ Browser automation/screenshots → browser-rendering/
└─ Third-party script management → zaraz/
```

### "I need infrastructure-as-code"

```
Need IaC? → pulumi/ (Pulumi), terraform/ (Terraform), or api/ (REST API)
```

## Product Index

### Compute & Runtime
| Product | Reference |
|---------|-----------|
| Workers | `references/workers/` |
| Pages | `references/pages/` |
| Pages Functions | `references/pages-functions/` |
| Durable Objects | `references/durable-objects/` |
| Workflows | `references/workflows/` |
| Containers | `references/containers/` |
| Workers for Platforms | `references/workers-for-platforms/` |
| Cron Triggers | `references/cron-triggers/` |
| Tail Workers | `references/tail-workers/` |
| Snippets | `references/snippets/` |
| Smart Placement | `references/smart-placement/` |

### Storage & Data
| Product | Reference |
|---------|-----------|
| KV | `references/kv/` |
| D1 | `references/d1/` |
| R2 | `references/r2/` |
| Queues | `references/queues/` |
| Hyperdrive | `references/hyperdrive/` |
| DO Storage | `references/do-storage/` |
| Secrets Store | `references/secrets-store/` |
| Pipelines | `references/pipelines/` |
| R2 Data Catalog | `references/r2-data-catalog/` |
| R2 SQL | `references/r2-sql/` |

### AI & Machine Learning
| Product | Reference |
|---------|-----------|
| Workers AI | `references/workers-ai/` |
| Vectorize | `references/vectorize/` |
| Agents SDK | `references/agents-sdk/` |
| AI Gateway | `references/ai-gateway/` |
| AI Search | `references/ai-search/` |

### Networking & Connectivity
| Product | Reference |
|---------|-----------|
| Tunnel | `references/tunnel/` |
| Spectrum | `references/spectrum/` |
| TURN | `references/turn/` |
| Network Interconnect | `references/network-interconnect/` |
| Argo Smart Routing | `references/argo-smart-routing/` |
| Workers VPC | `references/workers-vpc/` |

### Security
| Product | Reference |
|---------|-----------|
| WAF | `references/waf/` |
| DDoS Protection | `references/ddos/` |
| Bot Management | `references/bot-management/` |
| API Shield | `references/api-shield/` |
| Turnstile | `references/turnstile/` |

### Media & Content
| Product | Reference |
|---------|-----------|
| Images | `references/images/` |
| Stream | `references/stream/` |
| Browser Rendering | `references/browser-rendering/` |
| Zaraz | `references/zaraz/` |

### Real-Time Communication
| Product | Reference |
|---------|-----------|
| RealtimeKit | `references/realtimekit/` |
| Realtime SFU | `references/realtime-sfu/` |

### Developer Tools
| Product | Reference |
|---------|-----------|
| Wrangler | `references/wrangler/` |
| Miniflare | `references/miniflare/` |
| C3 | `references/c3/` |
| Observability | `references/observability/` |
| Analytics Engine | `references/analytics-engine/` |
| Web Analytics | `references/web-analytics/` |
| Sandbox | `references/sandbox/` |
| Workerd | `references/workerd/` |
| Workers Playground | `references/workers-playground/` |

### Infrastructure as Code
| Product | Reference |
|---------|-----------|
| Pulumi | `references/pulumi/` |
| Terraform | `references/terraform/` |
| API | `references/api/` |

### Other Services
| Product | Reference |
|---------|-----------|
| Email Routing | `references/email-routing/` |
| Email Workers | `references/email-workers/` |
| Static Assets | `references/static-assets/` |
| Bindings | `references/bindings/` |
| Cache Reserve | `references/cache-reserve/` |

## Troubleshooting

### Escalated Network Access

If deployment fails due to network issues (timeouts, DNS errors, connection resets), rerun the deploy with escalated permissions (use `sandbox_permissions=require_escalated`). The deploy requires escalated network access when sandbox networking blocks outbound requests.

Example guidance to the user:

```
The deploy needs escalated network access to deploy to Cloudflare. I can rerun the command with escalated permissions—want me to proceed?
```


--- FILE: codex-agent.md ---

---
name: codex
description: Launch the adaptable `codex` agent configuration for generating ideas, completions, and task breakdowns; use when a task needs general-purpose coding assistance or when debugging prompt/agent flows for micronaut services.
---

# Codex Agent Skill

## When to Use
Trigger this skill whenever the request is for the general-purpose Codex agent described by the configuration below (icon `❌`, model `gemini-2.5-pro`, and tools for reading, searching, and memory). This skill runs alongside or prior to the agentic Micronaut skill when you need idea generation, completions, to-do planning, or debugging of prompt flows.

## Agent Configuration Summary
- **Identity**: `codex` with display name `Codex ❌`, an adaptable assistant that blends idea generation, code completion, and task planning.
- **Model**: `gemini-2.5-pro` at low randomness (`temperature 0.1`, `maxTimeMinutes 5`, `maxTurns 15`) so the agent stays focused.
- **Tools**: `read`, `grep`, `glob`, `web_search`, `memory`, and `read_many_files` support research, filesystem inspection, and context awareness.
- **Core responsibilities**:
  1. Read URLs/text and return informative N-grams for summaries and understanding.
  2. Suggest code completions across languages by analyzing provided snippets (`code_context` input).
  3. Generate new code ideas or approaches tied to the user’s goals.
  4. Manage to-do lists, break down features, and track progress.

## Prompt & Input Guidance
- The default `query` shell is `Help me with my coding task: {task_description}`; always bind the {task_description} to the user's request and adapt additional inputs (`code_context`, `url_or_text`, `language_framework`) as available.
- Emphasize using `web_search` + N-gram synthesis before drafting answers when a URL/text is provided.
- For code completions, analyze context deeply and return idiomatic suggestions; mention language/framework names when supplied.
- For to-do breakdowns, enumerate tasks with actionable steps and align them with the `task_description`.

## Integration Notes
- This skill can be used independently for general coding assistance, or invoked before/after the `agentic-micronaut` skill to keep architecture-level design aligned with prompts that need the executable-data paradigm.
- Keep this file synchronized with any updates to the agent configuration (icons, tools, instructions) so CLI testing and debugging always refers to the single source of truth.
- For deterministic Micronaut orchestration in AS-XCFE, use the codex handoff runner:
  - `node scripts/codex-micronaut-orchestrate.js --skill-toml <path> --command <intent-or-route> [--payload ...] [--check-sync ...]`
  - Expected output contract: `{ ok, plan, execution, validation }`.

## Dataset Preparation

- Before training a tiny prompt, LoRA adapter, or hybrid memory/router stack, run `skills/dataset-training/scripts/generate_dataset_manifest.js` (set `DATA_DIR` when your corpus is outside the repo) to generate `artifacts/test/dataset-manifest.json`.
- Use the manifest plus `artifacts/supernaut/action-log-rlhf.jsonl` to align capability comparisons (see `docs/training-guidelines.md`) before invoking LoRA/QLoRA or launching `model.pool.arbitrate` in the sandbox.


--- FILE: dataset-training.md ---

---
name: dataset-training
description: Manage dataset discovery, manifest generation, and preparation for LoRA/LoRA-like training runs using the project corpus.
---

# Dataset Training Skill

Use this skill whenever you need to build, inspect, or refine a dataset for training a LoRA-style or prompt-engine model.

## When to Use

- You are gathering tokens/examples from the repository `data/` directory (chat logs, prompts, diagrams, RLHF traces).
- You want a manifest that reports file sizes, line counts, sample rows, and rough token estimates before training.
- You need to compare dataset candidates, compute train/validation splits, or generate feeds for `pipx`/LoRA runs.

## What This Skill Adds

1. **Dataset manifest generation** via `skills/dataset-training/scripts/generate_dataset_manifest.js`. It scans `data/*.jsonl`, counts lines, captures sample records, and optionally writes a manifest to `artifacts/dataset-manifest.json`.
2. **Structured naming guidance** so you can describe a set of datasets as training, evaluation, or RLHF sources before handing them to a trainer.
3. **Preparation checklist** covering metadata, splits, filtering downstream, and bucket paths for LoRA/QLoRA training.

## Workflow

1. Run `DATA_DIR=C:\\public_html\\data node skills/dataset-training/scripts/generate_dataset_manifest.js --out artifacts/dataset-manifest.json` to capture the dataset landscape. The script honors `DATA_DIR` when your corpus sits outside the repo.
2. Use the generated manifest to pick the model (fast vs deep) and assign each file to train/validation/test splits.
3. Apply filters (e.g., `--pattern rhy`) to limit the dataset to the code/abstract sections you want to reinforce.
4. Feed the manifest into your training flow (`scripts/forge-model-binary.js`, `scripts/gsnr-train.py`, etc.) so the trainer can reference cleaned, documented sources.

## Tips

- Keep `data/*.jsonl` organized; prefer descriptive filenames (e.g., `chat-dolphin.jsonl`, `prompts.xml`).
- If you need more than line counts, add derived fields to the manifest using `scripts/gsnr-training` helpers in `src/gsnr_training`.
- For very large datasets, add a `--sample` flag to `generate_dataset_manifest.js` (see the script comments) to avoid scanning every row in every pass.


--- FILE: develop-web-game.md ---

---
name: "develop-web-game"
description: "Use when Codex is building or iterating on a web game (HTML/JS) and needs a reliable development + testing loop: implement small changes, run a Playwright-based test script with short input bursts and intentional pauses, inspect screenshots/text, and review console errors with render_game_to_text."
---


# Develop Web Game

Build games in small steps and validate every change. Treat each iteration as: implement → act → pause → observe → adjust.

## Skill paths (set once)

```bash
export CODEX_HOME="${CODEX_HOME:-$HOME/.codex}"
export WEB_GAME_CLIENT="$CODEX_HOME/skills/develop-web-game/scripts/web_game_playwright_client.js"
export WEB_GAME_ACTIONS="$CODEX_HOME/skills/develop-web-game/references/action_payloads.json"
```

User-scoped skills install under `$CODEX_HOME/skills` (default: `~/.codex/skills`).

## Workflow

1. **Pick a goal.** Define a single feature or behavior to implement.
2. **Implement small.** Make the smallest change that moves the game forward.
3. **Ensure integration points.** Provide a single canvas and `window.render_game_to_text` so the test loop can read state.
4. **Add `window.advanceTime(ms)`.** Strongly prefer a deterministic step hook so the Playwright script can advance frames reliably; without it, automated tests can be flaky.
5. **Initialize progress.md.** If `progress.md` exists, read it first and confirm the original user prompt is recorded at the top (prefix with `Original prompt:`). Also note any TODOs and suggestions left by the previous agent. If missing, create it and write `Original prompt: <prompt>` at the top before appending updates.
6. **Verify Playwright availability.** Ensure `playwright` is available (local dependency or global install). If unsure, check `npx` first.
7. **Run the Playwright test script.** You must run `$WEB_GAME_CLIENT` after each meaningful change; do not invent a new client unless required.
8. **Use the payload reference.** Base actions on `$WEB_GAME_ACTIONS` to avoid guessing keys.
9. **Inspect state.** Capture screenshots and text state after each burst.
10. **Inspect screenshots.** Open the latest screenshot, verify expected visuals, fix any issues, and rerun the script. Repeat until correct.
11. **Verify controls and state (multi-step focus).** Exhaustively exercise all important interactions. For each, think through the full multi-step sequence it implies (cause → intermediate states → outcome) and verify the entire chain works end-to-end. Confirm `render_game_to_text` reflects the same state shown on screen. If anything is off, fix and rerun.
    Examples of important interactions: move, jump, shoot/attack, interact/use, select/confirm/cancel in menus, pause/resume, restart, and any special abilities or puzzle actions defined by the request. Multi-step examples: shooting an enemy should reduce its health; when health reaches 0 it should disappear and update the score; collecting a key should unlock a door and allow level progression.
12. **Check errors.** Review console errors and fix the first new issue before continuing.
13. **Reset between scenarios.** Avoid cross-test state when validating distinct features.
14. **Iterate with small deltas.** Change one variable at a time (frames, inputs, timing, positions), then repeat steps 7–13 until stable.

Example command (actions required):
```
node "$WEB_GAME_CLIENT" --url http://localhost:5173 --actions-file "$WEB_GAME_ACTIONS" --click-selector "#start-btn" --iterations 3 --pause-ms 250
```

Example actions (inline JSON):
```json
{
  "steps": [
    { "buttons": ["left_mouse_button"], "frames": 2, "mouse_x": 120, "mouse_y": 80 },
    { "buttons": [], "frames": 6 },
    { "buttons": ["right"], "frames": 8 },
    { "buttons": ["space"], "frames": 4 }
  ]
}
```

## Test Checklist

Test any new features added for the request and any areas your logic changes could affect. Identify issues, fix them, and re-run the tests to confirm they’re resolved.

Examples of things to test:
- Primary movement/interaction inputs (e.g., move, jump, shoot, confirm/select).
- Win/lose or success/fail transitions.
- Score/health/resource changes.
- Boundary conditions (collisions, walls, screen edges).
- Menu/pause/start flow if present.
- Any special actions tied to the request (powerups, combos, abilities, puzzles, timers).

## Test Artifacts to Review

- Latest screenshots from the Playwright run.
- Latest `render_game_to_text` JSON output.
- Console error logs (fix the first new error before continuing).
You must actually open and visually inspect the latest screenshots after running the Playwright script, not just generate them. Ensure everything that should be visible on screen is actually visible. Go beyond the start screen and capture gameplay screenshots that cover all newly added features. Treat the screenshots as the source of truth; if something is missing, it is missing in the build. If you suspect a headless/WebGL capture issue, rerun the Playwright script in headed mode and re-check. Fix and rerun in a tight loop until the screenshots and text state look correct. Once fixes are verified, re-test all important interactions and controls, confirm they work, and ensure your changes did not introduce regressions. If they did, fix them and rerun everything in a loop until interactions, text state, and controls all work as expected. Be exhaustive in testing controls; broken games are not acceptable.

## Core Game Guidelines

### Canvas + Layout
- Prefer a single canvas centered in the window.

### Visuals
- Keep on-screen text minimal; show controls on a start/menu screen rather than overlaying them during play.
- Avoid overly dark scenes unless the design calls for it. Make key elements easy to see.
- Draw the background on the canvas itself instead of relying on CSS backgrounds.

### Text State Output (render_game_to_text)
Expose a `window.render_game_to_text` function that returns a concise JSON string representing the current game state. The text should include enough information to play the game without visuals.

Minimal pattern:
```js
function renderGameToText() {
  const payload = {
    mode: state.mode,
    player: { x: state.player.x, y: state.player.y, r: state.player.r },
    entities: state.entities.map((e) => ({ x: e.x, y: e.y, r: e.r })),
    score: state.score,
  };
  return JSON.stringify(payload);
}
window.render_game_to_text = renderGameToText;
```

Keep the payload succinct and biased toward on-screen/interactive elements. Prefer current, visible entities over full history.
Include a clear coordinate system note (origin and axis directions), and encode all player-relevant state: player position/velocity, active obstacles/enemies, collectibles, timers/cooldowns, score, and any mode/state flags needed to make correct decisions. Avoid large histories; only include what's currently relevant and visible.

### Time Stepping Hook
Provide a deterministic time-stepping hook so the Playwright client can advance the game in controlled increments. Expose `window.advanceTime(ms)` (or a thin wrapper that forwards to your game update loop) and have the game loop use it when present.
The Playwright test script uses this hook to step frames deterministically during automated testing.

Minimal pattern:
```js
window.advanceTime = (ms) => {
  const steps = Math.max(1, Math.round(ms / (1000 / 60)));
  for (let i = 0; i < steps; i++) update(1 / 60);
  render();
};
```

### Fullscreen Toggle
- Use a single key (prefer `f`) to toggle fullscreen on/off.
- Allow `Esc` to exit fullscreen.
- When fullscreen toggles, resize the canvas/rendering so visuals and input mapping stay correct.

## Progress Tracking

Create a `progress.md` file if it doesn't exist, and append TODOs, notes, gotchas, and loose ends as you go so another agent can pick up seamlessly.
If a `progress.md` file already exists, read it first, including the original user prompt at the top (you may be continuing another agent's work). Do not overwrite the original prompt; preserve it.
Update `progress.md` after each meaningful chunk of work (feature added, bug found, test run, or decision made).
At the end of your work, leave TODOs and suggestions for the next agent in `progress.md`.

## Playwright Prerequisites

- Prefer a local `playwright` dependency if the project already has it.
- If unsure whether Playwright is available, check for `npx`:
  ```
  command -v npx >/dev/null 2>&1
  ```
- If `npx` is missing, install Node/npm and then install Playwright globally:
  ```
  npm install -g @playwright/mcp@latest
  ```
- Do not switch to `@playwright/test` unless explicitly asked; stick to the client script.

## Scripts

- `$WEB_GAME_CLIENT` (installed default: `$CODEX_HOME/skills/develop-web-game/scripts/web_game_playwright_client.js`) — Playwright-based action loop with virtual-time stepping, screenshot capture, and console error buffering. You must pass an action burst via `--actions-file`, `--actions-json`, or `--click`.

## References

- `$WEB_GAME_ACTIONS` (installed default: `$CODEX_HOME/skills/develop-web-game/references/action_payloads.json`) — example action payloads (keyboard + mouse, per-frame capture). Use these to build your burst.


--- FILE: doc.md ---

---
name: "doc"
description: "Use when the task involves reading, creating, or editing `.docx` documents, especially when formatting or layout fidelity matters; prefer `python-docx` plus the bundled `scripts/render_docx.py` for visual checks."
---


# DOCX Skill

## When to use
- Read or review DOCX content where layout matters (tables, diagrams, pagination).
- Create or edit DOCX files with professional formatting.
- Validate visual layout before delivery.

## Workflow
1. Prefer visual review (layout, tables, diagrams).
   - If `soffice` and `pdftoppm` are available, convert DOCX -> PDF -> PNGs.
   - Or use `scripts/render_docx.py` (requires `pdf2image` and Poppler).
   - If these tools are missing, install them or ask the user to review rendered pages locally.
2. Use `python-docx` for edits and structured creation (headings, styles, tables, lists).
3. After each meaningful change, re-render and inspect the pages.
4. If visual review is not possible, extract text with `python-docx` as a fallback and call out layout risk.
5. Keep intermediate outputs organized and clean up after final approval.

## Temp and output conventions
- Use `tmp/docs/` for intermediate files; delete when done.
- Write final artifacts under `output/doc/` when working in this repo.
- Keep filenames stable and descriptive.

## Dependencies (install if missing)
Prefer `uv` for dependency management.

Python packages:
```
uv pip install python-docx pdf2image
```
If `uv` is unavailable:
```
python3 -m pip install python-docx pdf2image
```
System tools (for rendering):
```
# macOS (Homebrew)
brew install libreoffice poppler

# Ubuntu/Debian
sudo apt-get install -y libreoffice poppler-utils
```

If installation isn't possible in this environment, tell the user which dependency is missing and how to install it locally.

## Environment
No required environment variables.

## Rendering commands
DOCX -> PDF:
```
soffice -env:UserInstallation=file:///tmp/lo_profile_$$ --headless --convert-to pdf --outdir $OUTDIR $INPUT_DOCX
```

PDF -> PNGs:
```
pdftoppm -png $OUTDIR/$BASENAME.pdf $OUTDIR/$BASENAME
```

Bundled helper:
```
python3 scripts/render_docx.py /path/to/file.docx --output_dir /tmp/docx_pages
```

## Quality expectations
- Deliver a client-ready document: consistent typography, spacing, margins, and clear hierarchy.
- Avoid formatting defects: clipped/overlapping text, broken tables, unreadable characters, or default-template styling.
- Charts, tables, and visuals must be legible in rendered pages with correct alignment.
- Use ASCII hyphens only. Avoid U+2011 (non-breaking hyphen) and other Unicode dashes.
- Citations and references must be human-readable; never leave tool tokens or placeholder strings.

## Final checks
- Re-render and inspect every page at 100% zoom before final delivery.
- Fix any spacing, alignment, or pagination issues and repeat the render loop.
- Confirm there are no leftovers (temp files, duplicate renders) unless the user asks to keep them.


--- FILE: figma-implement-design.md ---

---
name: "figma-implement-design"
description: "Translate Figma nodes into production-ready code with 1:1 visual fidelity using the Figma MCP workflow (design context, screenshots, assets, and project-convention translation). Trigger when the user provides Figma URLs or node IDs, or asks to implement designs or components that must match Figma specs. Requires a working Figma MCP server connection."
---


# Implement Design

## Overview

This skill provides a structured workflow for translating Figma designs into production-ready code with pixel-perfect accuracy. It ensures consistent integration with the Figma MCP server, proper use of design tokens, and 1:1 visual parity with designs.

## Prerequisites

- Figma MCP server must be connected and accessible
- User must provide a Figma URL in the format: `https://figma.com/design/:fileKey/:fileName?node-id=1-2`
  - `:fileKey` is the file key
  - `1-2` is the node ID (the specific component or frame to implement)
- **OR** when using `figma-desktop` MCP: User can select a node directly in the Figma desktop app (no URL required)
- Project should have an established design system or component library (preferred)

## Required Workflow

**Follow these steps in order. Do not skip steps.**

### Step 0: Set up Figma MCP (if not already configured)

If any MCP call fails because Figma MCP is not connected, pause and set it up:

1. Add the Figma MCP:
   - `codex mcp add figma --url https://mcp.figma.com/mcp`
2. Enable remote MCP client:
   - Set `[features].rmcp_client = true` in `config.toml` **or** run `codex --enable rmcp_client`
3. Log in with OAuth:
   - `codex mcp login figma`

After successful login, the user will have to restart codex. You should finish your answer and tell them so when they try again they can continue with Step 1.

### Step 1: Get Node ID

#### Option A: Parse from Figma URL

When the user provides a Figma URL, extract the file key and node ID to pass as arguments to MCP tools.

**URL format:** `https://figma.com/design/:fileKey/:fileName?node-id=1-2`

**Extract:**

- **File key:** `:fileKey` (the segment after `/design/`)
- **Node ID:** `1-2` (the value of the `node-id` query parameter)

**Note:** When using the local desktop MCP (`figma-desktop`), `fileKey` is not passed as a parameter to tool calls. The server automatically uses the currently open file, so only `nodeId` is needed.

**Example:**

- URL: `https://figma.com/design/kL9xQn2VwM8pYrTb4ZcHjF/DesignSystem?node-id=42-15`
- File key: `kL9xQn2VwM8pYrTb4ZcHjF`
- Node ID: `42-15`

#### Option B: Use Current Selection from Figma Desktop App (figma-desktop MCP only)

When using the `figma-desktop` MCP and the user has NOT provided a URL, the tools automatically use the currently selected node from the open Figma file in the desktop app.

**Note:** Selection-based prompting only works with the `figma-desktop` MCP server. The remote server requires a link to a frame or layer to extract context. The user must have the Figma desktop app open with a node selected.

### Step 2: Fetch Design Context

Run `get_design_context` with the extracted file key and node ID.

```
get_design_context(fileKey=":fileKey", nodeId="1-2")
```

This provides the structured data including:

- Layout properties (Auto Layout, constraints, sizing)
- Typography specifications
- Color values and design tokens
- Component structure and variants
- Spacing and padding values

**If the response is too large or truncated:**

1. Run `get_metadata(fileKey=":fileKey", nodeId="1-2")` to get the high-level node map
2. Identify the specific child nodes needed from the metadata
3. Fetch individual child nodes with `get_design_context(fileKey=":fileKey", nodeId=":childNodeId")`

### Step 3: Capture Visual Reference

Run `get_screenshot` with the same file key and node ID for a visual reference.

```
get_screenshot(fileKey=":fileKey", nodeId="1-2")
```

This screenshot serves as the source of truth for visual validation. Keep it accessible throughout implementation.

### Step 4: Download Required Assets

Download any assets (images, icons, SVGs) returned by the Figma MCP server.

**IMPORTANT:** Follow these asset rules:

- If the Figma MCP server returns a `localhost` source for an image or SVG, use that source directly
- DO NOT import or add new icon packages - all assets should come from the Figma payload
- DO NOT use or create placeholders if a `localhost` source is provided
- Assets are served through the Figma MCP server's built-in assets endpoint

### Step 5: Translate to Project Conventions

Translate the Figma output into this project's framework, styles, and conventions.

**Key principles:**

- Treat the Figma MCP output (typically React + Tailwind) as a representation of design and behavior, not as final code style
- Replace Tailwind utility classes with the project's preferred utilities or design system tokens
- Reuse existing components (buttons, inputs, typography, icon wrappers) instead of duplicating functionality
- Use the project's color system, typography scale, and spacing tokens consistently
- Respect existing routing, state management, and data-fetch patterns

### Step 6: Achieve 1:1 Visual Parity

Strive for pixel-perfect visual parity with the Figma design.

**Guidelines:**

- Prioritize Figma fidelity to match designs exactly
- Avoid hardcoded values - use design tokens from Figma where available
- When conflicts arise between design system tokens and Figma specs, prefer design system tokens but adjust spacing or sizes minimally to match visuals
- Follow WCAG requirements for accessibility
- Add component documentation as needed

### Step 7: Validate Against Figma

Before marking complete, validate the final UI against the Figma screenshot.

**Validation checklist:**

- [ ] Layout matches (spacing, alignment, sizing)
- [ ] Typography matches (font, size, weight, line height)
- [ ] Colors match exactly
- [ ] Interactive states work as designed (hover, active, disabled)
- [ ] Responsive behavior follows Figma constraints
- [ ] Assets render correctly
- [ ] Accessibility standards met

## Implementation Rules

### Component Organization

- Place UI components in the project's designated design system directory
- Follow the project's component naming conventions
- Avoid inline styles unless truly necessary for dynamic values

### Design System Integration

- ALWAYS use components from the project's design system when possible
- Map Figma design tokens to project design tokens
- When a matching component exists, extend it rather than creating a new one
- Document any new components added to the design system

### Code Quality

- Avoid hardcoded values - extract to constants or design tokens
- Keep components composable and reusable
- Add TypeScript types for component props
- Include JSDoc comments for exported components

## Examples

### Example 1: Implementing a Button Component

User says: "Implement this Figma button component: https://figma.com/design/kL9xQn2VwM8pYrTb4ZcHjF/DesignSystem?node-id=42-15"

**Actions:**

1. Parse URL to extract fileKey=`kL9xQn2VwM8pYrTb4ZcHjF` and nodeId=`42-15`
2. Run `get_design_context(fileKey="kL9xQn2VwM8pYrTb4ZcHjF", nodeId="42-15")`
3. Run `get_screenshot(fileKey="kL9xQn2VwM8pYrTb4ZcHjF", nodeId="42-15")` for visual reference
4. Download any button icons from the assets endpoint
5. Check if project has existing button component
6. If yes, extend it with new variant; if no, create new component using project conventions
7. Map Figma colors to project design tokens (e.g., `primary-500`, `primary-hover`)
8. Validate against screenshot for padding, border radius, typography

**Result:** Button component matching Figma design, integrated with project design system.

### Example 2: Building a Dashboard Layout

User says: "Build this dashboard: https://figma.com/design/pR8mNv5KqXzGwY2JtCfL4D/Dashboard?node-id=10-5"

**Actions:**

1. Parse URL to extract fileKey=`pR8mNv5KqXzGwY2JtCfL4D` and nodeId=`10-5`
2. Run `get_metadata(fileKey="pR8mNv5KqXzGwY2JtCfL4D", nodeId="10-5")` to understand the page structure
3. Identify main sections from metadata (header, sidebar, content area, cards) and their child node IDs
4. Run `get_design_context(fileKey="pR8mNv5KqXzGwY2JtCfL4D", nodeId=":childNodeId")` for each major section
5. Run `get_screenshot(fileKey="pR8mNv5KqXzGwY2JtCfL4D", nodeId="10-5")` for the full page
6. Download all assets (logos, icons, charts)
7. Build layout using project's layout primitives
8. Implement each section using existing components where possible
9. Validate responsive behavior against Figma constraints

**Result:** Complete dashboard matching Figma design with responsive layout.

## Best Practices

### Always Start with Context

Never implement based on assumptions. Always fetch `get_design_context` and `get_screenshot` first.

### Incremental Validation

Validate frequently during implementation, not just at the end. This catches issues early.

### Document Deviations

If you must deviate from the Figma design (e.g., for accessibility or technical constraints), document why in code comments.

### Reuse Over Recreation

Always check for existing components before creating new ones. Consistency across the codebase is more important than exact Figma replication.

### Design System First

When in doubt, prefer the project's design system patterns over literal Figma translation.

## Common Issues and Solutions

### Issue: Figma output is truncated

**Cause:** The design is too complex or has too many nested layers to return in a single response.
**Solution:** Use `get_metadata` to get the node structure, then fetch specific nodes individually with `get_design_context`.

### Issue: Design doesn't match after implementation

**Cause:** Visual discrepancies between the implemented code and the original Figma design.
**Solution:** Compare side-by-side with the screenshot from Step 3. Check spacing, colors, and typography values in the design context data.

### Issue: Assets not loading

**Cause:** The Figma MCP server's assets endpoint is not accessible or the URLs are being modified.
**Solution:** Verify the Figma MCP server's assets endpoint is accessible. The server serves assets at `localhost` URLs. Use these directly without modification.

### Issue: Design token values differ from Figma

**Cause:** The project's design system tokens have different values than those specified in the Figma design.
**Solution:** When project tokens differ from Figma values, prefer project tokens for consistency but adjust spacing/sizing to maintain visual fidelity.

## Understanding Design Implementation

The Figma implementation workflow establishes a reliable process for translating designs to code:

**For designers:** Confidence that implementations will match their designs with pixel-perfect accuracy.
**For developers:** A structured approach that eliminates guesswork and reduces back-and-forth revisions.
**For teams:** Consistent, high-quality implementations that maintain design system integrity.

By following this workflow, you ensure that every Figma design is implemented with the same level of care and attention to detail.

## Additional Resources

- [Figma MCP Server Documentation](https://developers.figma.com/docs/figma-mcp-server/)
- [Figma MCP Server Tools and Prompts](https://developers.figma.com/docs/figma-mcp-server/tools-and-prompts/)
- [Figma Variables and Design Tokens](https://help.figma.com/hc/en-us/articles/15339657135383-Guide-to-variables-in-Figma)


--- FILE: figma.md ---

---
name: figma
description: Use the Figma MCP server to fetch design context, screenshots, variables, and assets from Figma, and to translate Figma nodes into production code. Trigger when a task involves Figma URLs, node IDs, design-to-code implementation, or Figma MCP setup and troubleshooting.
---

# Figma MCP

Use the Figma MCP server for Figma-driven implementation. For setup and debugging details (env vars, config, verification), see `references/figma-mcp-config.md`.

## Figma MCP Integration Rules
These rules define how to translate Figma inputs into code for this project and must be followed for every Figma-driven change.

### Required flow (do not skip)
1. Run get_design_context first to fetch the structured representation for the exact node(s).
2. If the response is too large or truncated, run get_metadata to get the high-level node map and then re-fetch only the required node(s) with get_design_context.
3. Run get_screenshot for a visual reference of the node variant being implemented.
4. Only after you have both get_design_context and get_screenshot, download any assets needed and start implementation.
5. Translate the output (usually React + Tailwind) into this project's conventions, styles and framework. Reuse the project's color tokens, components, and typography wherever possible.
6. Validate against Figma for 1:1 look and behavior before marking complete.

### Implementation rules
- Treat the Figma MCP output (React + Tailwind) as a representation of design and behavior, not as final code style.
- Replace Tailwind utility classes with the project's preferred utilities/design-system tokens when applicable.
- Reuse existing components (e.g., buttons, inputs, typography, icon wrappers) instead of duplicating functionality.
- Use the project's color system, typography scale, and spacing tokens consistently.
- Respect existing routing, state management, and data-fetch patterns already adopted in the repo.
- Strive for 1:1 visual parity with the Figma design. When conflicts arise, prefer design-system tokens and adjust spacing or sizes minimally to match visuals.
- Validate the final UI against the Figma screenshot for both look and behavior.

### Asset handling
- The Figma MCP Server provides an assets endpoint which can serve image and SVG assets.
- IMPORTANT: If the Figma MCP Server returns a localhost source for an image or an SVG, use that image or SVG source directly.
- IMPORTANT: DO NOT import/add new icon packages, all the assets should be in the Figma payload.
- IMPORTANT: do NOT use or create placeholders if a localhost source is provided.

### Link-based prompting
- The server is link-based: copy the Figma frame/layer link and give that URL to the MCP client when asking for implementation help.
- The client cannot browse the URL but extracts the node ID from the link; always ensure the link points to the exact node/variant you want.

## References
- `references/figma-mcp-config.md` — setup, verification, troubleshooting, and link-based usage reminders.
- `references/figma-tools-and-prompts.md` — tool catalog and prompt patterns for selecting frameworks/components and fetching metadata.


--- FILE: micronaut-model.md ---

---
name: micronaut-model
description: "Factory for micronaut micro-models in the xjson model system. Use when defining a new model, choosing a weight backend, generating a model-specific bots.py, or registering in model_api_registry.json and micronaut.registry.xjson. Handles all three backend modes: local weights (GGUF/SCXQDDS), API stream (OpenAI/Anthropic/Ollama/custom), and GPT OSS fetch from HuggingFace. Trigger on: 'create a micronaut', 'new micro-model', 'add bot model', 'xjson model', 'scaffold bots.py', 'gpt oss weights', 'huggingface weights', 'api stream model', 'register micronaut', 'add to model registry', 'build fold model', 'new expert model', 'model with local weights', 'attach api backend', 'model bot', 'phi-3 model', 'ollama micronaut'."
metadata:
  short-description: Define and scaffold xjson micro-models with any backend
---

# Micronaut Model Factory

Scaffolds the full micronaut lifecycle: xjson definition → weight backend → bots.py → registry.

## Quick Start

```bash
# Scaffold a new micronaut with local GGUF weights
python scripts/scaffold_micronaut.py new <ID> <Name> --backend local_gguf --weights models/my.gguf --fold COMPUTE

# Scaffold with OpenAI API stream backend
python scripts/scaffold_micronaut.py new <ID> <Name> --backend api_openai --model gpt-4o --fold COMPUTE

# Scaffold with HuggingFace OSS weights (downloads on first run)
python scripts/scaffold_micronaut.py new <ID> <Name> --backend fetch_oss --hf-repo microsoft/Phi-3-mini-4k-instruct --fold COMPUTE

# Scaffold with SCXQDDS local shards
python scripts/scaffold_micronaut.py new <ID> <Name> --backend local_scxqdds --shard-dir models/shards/mymodel/ --fold COMPUTE

# Register an existing xjson into both registries
python scripts/scaffold_micronaut.py register model/agents/<ID>/<ID>.xjson

# Fetch GPT OSS weights from HuggingFace
python scripts/fetch_oss_weights.py --repo <hf-repo-id> --file <filename> --out models/oss-cache/
```

## What Gets Generated

For each new micronaut (`<ID>`):

```
model/agents/<ID>/
├── <ID>.xjson          — model definition (lanes, phases, experts, edges)
└── bots.py             — model-specific inference bot
```

Plus entries written into:
- `micronaut/micronaut/model_api_registry.json` — pool registration (dispatch_path, tools, api_namespace)
- `micronaut/micronaut/micronaut.registry.xjson` — law registration (role, responsibilities, forbidden)

## Backend Options

| Backend | When to use | Key field |
|---|---|---|
| `local_gguf` | Local .gguf/.mgguf file ready | `path: models/x.gguf` |
| `local_scxqdds` | SCXQDDS tensor shards | `shard_dir: models/shards/x/` |
| `fetch_oss` | Pull from HuggingFace on first run | `hf_repo: org/model-name` |
| `api_openai` | OpenAI API stream | `model: gpt-4o` |
| `api_anthropic` | Anthropic API stream | `model: claude-sonnet-4-6` |
| `api_ollama` | Local Ollama server | `model: phi3:mini` |
| `api_custom` | Any OpenAI-compat endpoint | `api_base: http://...` |

Full field schemas for each backend: see `references/backends.md`

## xjson Format

Full format reference with all `@`-key semantics: see `references/xjson-format.md`

## bots.py Pattern

The `assets/bots_template.py` is the base. Each generated bots.py customizes:
- Backend loader class (GGUF/SCXQDDS/API)
- Model-specific tools list (matches `model_api_registry.json tools`)
- System prompt from `@agent.main.system_prompt`
- Fold identifier (matches `_MICRONAUT_TO_FOLD` mapping)
- Streaming handler (for API backends) or token sampler (for local)

## Fold Mapping

```python
_MICRONAUT_TO_FOLD = {
    "CM-1": "⟁CONTROL_FOLD⟁",   "PM-1": "⟁DATA_FOLD⟁",
    "TM-1": "⟁TIME_FOLD⟁",      "HM-1": "⟁STATE_FOLD⟁",
    "SM-1": "⟁STORAGE_FOLD⟁",   "MM-1": "⟁COMPUTE_FOLD⟁",
    "XM-1": "⟁PATTERN_FOLD⟁",   "VM-1": "⟁UI_FOLD⟁",
    "VM-2": "⟁META_FOLD⟁",
    # Add new micronauts here
}
```

New models append their `"<ID>": "⟁<FOLD>_FOLD⟁"` entry to this dict in `micronaut/src/orchestrator_bot.py`.

## Popular OSS Weight Sources

See `references/backends.md#oss-weight-catalog` for tested HuggingFace repos and license summary.


--- FILE: micronaut.md ---

---
name: micronaut-skill
description: Deterministic Micronaut-style actions for project init, endpoint config, security checks, DI injection, scheduling, and event emission.
---

# Micronaut Skill

Use this skill when you need Micronaut-flavored workflows executed via deterministic XCFE actions:
- `startProject` — initialize project metadata from `config/application.json`.
- `configureEndpoint` — update gateway path/method.
- `secureEndpoint` — check role authorization.
- `injectBean` — simulate DI bean availability/scope.
- `scheduleTask` — set cron/task pair.
- `eventBridge` — emit events to registered listeners.
- `toolDiResolve` / `toolDiInject` — deterministic DI planning and injection handles.
- `toolStateGet` / `toolStateSet` — deterministic state snapshots and writes.
- `toolScheduleTask` / `toolEventEmit` — deterministic scheduling and event emissions.
- `toolRuntimeStep` / `toolRuntimeRun` — deterministic cognition tick controls.
- `toolClusterMigrate` / `toolClusterReplicate` — deterministic cluster migration and replication envelopes.
- Runtime tools are wired to a mini-model handler via `model.dispatch` (manifest-backed summary) for hybrid handler mode.

Routes are defined in `micronaut_manifest.json`; actions live in `actions/MicronautActions.json`. Data root is `config/` for this skill.


--- FILE: project-factory.md ---

---
name: project-factory
description: "Creation engine and factory for per-project agent and skill infrastructure. Use when working in any project directory to scaffold, generate, or evolve: CLAUDE.md, AGENTS.md, .claude/agents/*.md sub-agents, skills/project-spec/ skill packages (skill.matrix.toml + *Actions.json), model contracts (model.json + model.runtime.json), Codex SKILL.md files, and agents/openai.yaml UI metadata. Adapts entirely to the current project — probes the stack, reads actual files, and generates infrastructure specific to what this project IS and needs. Not a static spec reader. A factory. Trigger on: 'set up this project', 'scaffold', 'add agent', 'add skill', 'generate spec', 'init', 'make agent-ready', 'create CLAUDE.md', 'add model contract', 'project factory', 'build agents for this', 'what do we need', 'project infrastructure', 'create action manifest', 'build a skill for', 'agent for this project'."
metadata:
  short-description: Scaffold agent/skill infrastructure for any project
---

# Project Factory

Creation engine for agent and skill infrastructure. Probes the current project, then generates exactly what it needs — no generic templates.

## Core Operations

| Command | What it does |
|---|---|
| `probe` | Scan project, emit Project Probe Summary |
| `init` | Generate full baseline infrastructure |
| `add-agent <name>` | Scaffold one agent (Claude Code + Codex format) |
| `add-skill <name>` | Scaffold skill.matrix.toml + *Actions.json + SKILL.md |
| `add-model <name> <fold>` | Scaffold xcfe-model-1 model contract |
| `update` | Re-probe, diff, update stale specs only |
| `audit` | Report what's missing vs what exists |

## Probe Protocol

Run `scripts/probe_project.py` from the project root. It auto-detects stack, reads README + existing CLAUDE.md + git log, and emits `project.probe.json`.

```bash
python ~/.codex/skills/project-factory/scripts/probe_project.py
```

Then read `project.probe.json` and use it as the authoritative input for all generation.

## Generation Order (for `init`)

Always generate in this order — each file informs the next:

1. `CLAUDE.md` — project conventions, commands, architecture
2. `AGENTS.md` — roster of all agents and their roles
3. `.claude/agents/<name>.md` — 1–5 project-local sub-agents
4. `skills/project-spec/skill.matrix.toml` — project spec skill surface
5. `skills/project-spec/ProjectSpecActions.json` — live spec data
6. `skills/project-spec/SKILL.md` — Codex entry point for the project spec

## Output Format Details

See `references/output-formats.md` for all template schemas with field-level documentation.
See `references/action-schema.md` for ProjectSpecActions.json body format (op types, params).
See `references/agent-trigger-guide.md` for writing effective agent description/trigger fields.

## Key Rules

- **Always read before writing** — never reference a file path you haven't confirmed exists
- **Project-specific content only** — if it works for any project, it's wrong
- **No placeholders in final output** — every `<field>` must be filled with real project data
- **Check existing infra first** — run audit before init; augment, don't overwrite
- **Glossary in every project-spec** — index all key files in the `glossary` action

## Scaffolding Scripts

Generate individual artifacts with deterministic scripts:

```bash
# Scaffold a new agent pair (Claude Code + Codex format)
python scripts/scaffold.py agent <name> --purpose "<what it does>" --project-root .

# Scaffold a new skill package
python scripts/scaffold.py skill <name> --purpose "<what it does>" --project-root .

# Scaffold a model contract
python scripts/scaffold.py model <name> --fold <COMPUTE|STORAGE|META|UI>_FOLD --project-root .

# Audit existing infrastructure
python scripts/scaffold.py audit --project-root .
```

Scripts write files but do NOT fill project-specific content — that's Codex's job. Use scripts to create correct file structure + schema skeletons, then fill the content.

## Validating Output

After generating, run quick checks:
- `skill.matrix.toml` action entries match methods in `*Actions.json` (same names)
- `model.json` `fold` field matches `model.runtime.json` `fold` field
- All file paths in `glossary` action actually exist
- Agent `.md` trigger descriptions contain at least 3 distinct trigger phrases


--- FILE: sk-coordinator.md ---

---
name: sk-coordinator
description: Coordinate learning plans, multi-call orchestration, scheduling, and Micronaut DI injection via semantic planner/scheduler/DI actions.
---

# Semantic Kernel Coordinator

Purpose: high-level intent router that:
- builds learning plans (`SemanticPlannerActions.generate_learning_plan`)
- assembles multi-step orchestration plans (`SemanticPlannerActions.build_multi_step_plan`)
- schedules learning (`LearningSchedulerActions.schedule_learning`)
- injects Micronaut components (`MicronautDIActions.inject_component`)

Routes live in `sk-coordinator_manifest.json`; skill config is `sk-coordinator.toml`; actions are under `actions/`.

Use when you need structured goal planning, multi-call choreography, or DI wiring for Micronaut contexts. Keep calls deterministic and reference the intentions in `examples/intentions/intentions.yaml`.


--- FILE: sql-skill.md ---

---
name: sql-skill
description: Deterministic SQL skill for SQLite and SQL Server planning/execution flows. Use when you need SK-callable SQL actions like connect, list tables, inspect schema, and run parameterized queries across sqlite or sqlserver providers.
---

# SQL Skill

Use this skill for deterministic SQL operations with provider-scoped behavior:
- `connect` - resolve connection profile for `sqlite` or `sqlserver`.
- `listTables` - return deterministic table inventory by provider.
- `describeTable` - return stable schema metadata for a table.
- `query` - return structured query execution envelope (deterministic mock rows).
- `upsert` - return deterministic write plan/result envelope.

Routes are defined in `sql_manifest.json`; actions live in `actions/SqlActions.json`; provider profiles are in `config/sql_profiles.json`.


--- FILE: team-lead.md ---

# Team Lead Skill

**ID**: `team-lead`
**Category**: orchestration
**Entry**: `TeamLeadActions.assign_tasks`

---

## Overview

Team Lead is a meta-orchestration skill that coordinates multiple specialized agents to complete complex goals collaboratively. It maintains a live task board, assigns agents to subtasks, dispatches work in parallel or sequentially based on dependencies, and synthesizes all outputs into a final result.

## Actions

| Route | Action | Params | Description |
|---|---|---|---|
| `/skill/team-lead/assign` | `assign_tasks` | `goal`, `constraints` | Decompose goal into task board |
| `/skill/team-lead/dispatch` | `dispatch_agents` | `tasks`, `parallel` | Dispatch agents to assigned tasks |
| `/skill/team-lead/board/update` | `update_board` | `taskId`, `status`, `agent`, `result` | Update a task's status on the board |
| `/skill/team-lead/board/get` | `get_board` | — | Return current board state |
| `/skill/team-lead/synthesize` | `synthesize` | `results`, `goal` | Merge all agent outputs into final summary |
| `/skill/team-lead/task/complete` | `complete_task` | `taskId`, `artifact` | Mark a task complete with artifact |
| `/skill/team-lead/task/block` | `block_task` | `taskId`, `blockedBy`, `reason` | Mark a task blocked |

## Agent Pool

Team Lead can delegate to any agent in the AS-XCFE pool:

- `workflow-orchestrator` — planning, roadmaps, todo generation
- `supernaut` — unified skill routing surface
- `asx-verifier` — ASX envelope/contract verification
- `windows-sdk` — DirectX 12, DXGI, WinRT headers
- `vs-native-tools` / `vs2022-tools` / `vs2019-tools` — MSVC builds
- `mx2lex` — lexer grammar compilation
- `scxqdds` / `scxq2-vector` / `scx2-runtime` — native tensor pipeline
- `netfx-sdk` — .NET CLR interop
- `powershell-ise` — PowerShell scripts
- `sk-coordinator` / `micronaut` — semantic kernel, DI, skill registry

## Task Board Format

```
═══════════════════════════════════════════════
 TASK BOARD  |  Goal: <description>
═══════════════════════════════════════════════
 [✓]  #1  <completed task>     workflow-orchestrator
 [→]  #2  <active task>        windows-sdk
 [ ]  #3  <pending task>       vs-native-tools
 [!]  #4  <blocked — on #2>    asx-verifier
═══════════════════════════════════════════════
 Progress: 1/4  |  2 in flight
═══════════════════════════════════════════════
```

## Collaboration Patterns

- **Fan-out**: dispatch all independent tasks simultaneously
- **Pipeline**: feed one agent's output as the next agent's input
- **Hybrid**: mix of parallel + sequential based on the dependency graph


--- FILE: workflow-orchestrator.md ---

---
name: workflow-orchestrator
description: Break down complex projects into structured tasks and phases, generate milestones and to-do lists, analyze scope/dependencies, research tools, automate multi-step workflows, and scaffold Kuhul micro-applications. Use for project planning, workflow automation, and Kuhul scaffolding.
---

# Workflow Orchestrator

## When to Use
Use this skill whenever the user needs structured planning for a large project, automation of a multi-step workflow, or Kuhul micro-app scaffolding. Always consult `references/workflow-orchestrator.md` for the detailed agent spec, and load memory from `C:\Users\canna\.claude\agent-memory\workflow-orchestrator\` to capture persistent patterns (tools, Kuhul templates, automation heuristics).

## Phase-Based Brain
The skill reasons in five deterministic phases:

1. **Scope & Analysis**
   - Capture requirements, constraints, MVP vs future scope, and success criteria.
   - Use the `PROJECT ANALYSIS` section to document scope, owner expectations, and risk/constraint boundaries.

2. **Research & Discovery**
   - Consult `references/workflow-orchestrator.md` for tool recommendations and best practices.
   - Run `web_search` when the user requests external knowledge to back tool choices.
   - Record findings in `# RESEARCH FINDINGS` with links and evaluation notes.

3. **Strategic Planning**
   - Break the roadmap into phases (foundation, MVP, advanced, scale).
   - Include deliverables, dependencies, and milestone success criteria.
   - Describe the `# PROJECT PHASES` section with titles, outcomes, and duration estimates.

4. **Task Generation**
   - Generate phase-aligned to-do lists with effort estimates (small/medium/large/epic), acceptance criteria, blockers, and automation checklists.
   - Populate `# TASK BREAKDOWN` with actionable tasks following the structure in `references/workflow-orchestrator.md`.

5. **Automation & Kuhul Integration**
   - Identify automation opportunities and propose Kuhul micro-app scaffolds (config.toml, schema.json, processor, validators, error handlers, README).
   - Document these in `# AUTOMATION & KUHUL ARCHITECTURE`.

## Output Discipline
- Always structure responses using the headings specified in `references/workflow-orchestrator.md` (`PROJECT ANALYSIS`, `RESEARCH FINDINGS`, etc.).
- Cite the persistent memory path when referencing prior insights (e.g., “As noted in `workflow-orchestrator/memory.md`, combat mitigation steps include...”). Update memory files when you uncover a new reusable pattern or automation strategy.
- Include dependencies and critical path information in `# DEPENDENCIES & CRITICAL PATH`, using light ASCII diagrams if it clarifies parallelism.
- Provide a `# TIMELINE ESTIMATE` with phase durations and overall delivery time.

## Deep Thinking Guidance
- Frame research tasks as discrete investigations with deterministic commands (e.g., `npm info` to inspect package versions, `curl` to validate API endpoints).
- When proposing Kuhul scaffolds, briefly describe the data flow (input → processing → output), validation, and error-handling sections to help the user visualize the micro-app’s atomic brain.
- Always ask a clarifying question if requirements lack constraints, otherwise lock onto the plan and avoid speculative suggestions.

