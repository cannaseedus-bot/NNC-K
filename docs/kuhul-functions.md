# K'UHUL Function Bindings → NNC-K Sidecars

The K'UHUL language treats effectful operations as **external function nodes**. The runtime calls them; they do not call back into K'UHUL. This maps directly to NNC-K's sidecar architecture.

## Core rule

```text
Compute = pi-field is pure, no side effects
Action  = functions have side effects, they touch the world
Bridge  = functions are just external nodes with bindings
Rule    = KUHUL calls functions; functions don't call KUHUL
```

## Function registry

| Function | Effect | NNC-K binding | Authority |
|----------|--------|---------------|-----------|
| `read_file` | io | C# `File.ReadAllText` | filesystem |
| `write_file` | io | C# `File.WriteAllText` | filesystem |
| `exec` | process | `Process.Start` external binary | OS |
| `shell` | shell | `cmd.exe /c` | OS |
| `tool` | tool | `quantum_hybrid.exe`, `quantum_grammar.exe`, `quantum_trinity.exe`, `quantum_microagents.exe` | candidate-only |
| `agent` | agent | `quantum_microagents.exe` | candidate-only |
| `micronaut` | micronaut | `micronaut_factory.exe create <name>` | factory scaffolding only |
| `skill` | skill | `asx_ram_v2.exe`, `Invoke-FileDropIngest`, `Refine-Micronaut` | skill registry |
| `action` | action | generic action envelope | runtime |
| `verb` | verb | `scripts/xcfe_router.py` intent routing | XCFE |
| `bot` | bot | model-node chat response | model node |
| `http` | network | `HttpWebRequest` / `scripts/research_bot.py` | network |

## Authority boundary

- Functions may only emit **candidate text/structures**.
- They **never** create, update, merge, or promote micronauts.
- `MicronautManager` and BOSS own persistence and promotion.

## C# registry

`src/NeuralGrammar.Core/KuhulFunctionRegistry.cs` provides:

```csharp
var reg = new KuhulFunctionRegistry(projectRoot);
bool ok = reg.Has("tool");
var result = reg.Call("tool", JArray.Parse("[\"hybrid\", {\"operation\":\"process\",\"input\":\"hello\"}]"));
```

It dispatches:
- `tool("hybrid", ...)` → `quantum_hybrid.exe`
- `tool("grammar", ...)` → `quantum_grammar.exe`
- `tool("research", ...)` → `quantum_trinity.exe`
- `tool("microagents", ...)` → `quantum_microagents.exe`
- `agent(...)` → `quantum_microagents.exe`
- `micronaut(name, args)` → `micronaut_factory.exe create <name>`
- `skill("asx_ram", {q_shard,k_shard,v_shard,config})` → `asx_ram_v2.exe`
- `http(method, url, body)` → `HttpWebRequest`
- `read_file`, `write_file`, `exec`, `shell` → OS primitives

## Example K'UHUL usage

```kuhul
[Tensor content = read_file("data.txt")]
[Tensor result = tool("hybrid", {operation: "process", input: content})]
[Tensor reply = agent("therapist", "I feel anxious")]
[Tensor deployed = action("deploy", {target: "production"})]
```

## Files

| Path | Purpose |
|------|---------|
| `schemas/programs/KUHUL_FUNCTIONS.kuhul` | K'UHUL function binding reference |
| `src/NeuralGrammar.Core/KuhulFunctionRegistry.cs` | C# registry + dispatch |
| `bin/v3.5.0-WebX/native/kxml/kuhul_functions.h` | Legacy C++ registry |
| `docs/kuhul-functions.md` | This document |
