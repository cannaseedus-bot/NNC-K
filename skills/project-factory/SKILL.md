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
