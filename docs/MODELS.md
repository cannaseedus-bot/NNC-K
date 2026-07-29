# K'UHUL WebX-3D Model Registry

These are the WebX-3D tool-calling models. They are **not generic GPT-2 models** — they are fine-tuned on the full K'UHUL Supernaut tool surface and are dependent on the WebX-3D KXML dispatch system for correct tool-call routing.

---

## Production Models

### kuhul-tool-medium-q8.gguf
**The primary tool-calling model.**

| Property | Value |
|---|---|
| File | `models/kuhul-tool-medium-q8.gguf` |
| Size | 706 MB (Q8_0) |
| Params | 345M (GPT-2 medium, 24L × 1024d × 16h) |
| Final loss | **0.1298** |
| Compression | 4.1× vs F32 |
| Trainer | PyTorch CPU, 500 steps, cosine LR 5e-5→5e-6 |
| Status | **Production** |

**Loss curve:** 1.93 → 0.975 → 0.551 → 0.287 → 0.184 → 0.130

At loss 0.13 the model generates complete, valid toolcall JSON reliably. Recommended for all Supernaut/KXML tool dispatch workflows.

---

### kuhul-tool-small-q8.gguf
**Lightweight baseline — awaiting relearn.**

| Property | Value |
|---|---|
| File | `models/kuhul-tool-small-q8.gguf` |
| Size | 386 MB (Q8_0) |
| Params | 117M (GPT-2 small, 12L × 768d × 12h) |
| Final loss | ~4–5 avg / 0.025 best per-sample |
| Compression | 7.5× vs F32 |
| Trainer | D3D11 iGPU (Intel HD 4600), 2000 steps |
| Status | Pre-production |

Knows JSON skeleton and high-frequency toolcall tokens (`tool`, `":"`, `type`, `\n`). Noisy due to D3D11 Adam NaN guard. Being replaced by `kuhul-tool-lite`.

---

### kuhul-tool-lite-q8.gguf *(training)*
**Lite production model — CPU cluster profile.**

| Property | Value |
|---|---|
| File | `models/kuhul-tool-lite-q8.gguf` |
| Expected size | ~120 MB (Q8_0) |
| Params | 124M (GPT-2 small, 12L × 768d × 12h) |
| Target loss | < 0.4 |
| Trainer | PyTorch CPU, 500 batches × 4 epochs = 2000 steps |
| Profile | `XVM_TRAINING_OPTIMUM` — 500×4 CPU cluster |
| Status | Training |

Relearn of `kuhul-tool-small` from the D3D11 checkpoint. Uses the PyTorch CPU trainer that achieved 0.13 on medium. Expected to reach 0.3–0.4 loss given it starts from a model that already knows the toolcall structure.

---

## Trained Tokens

These tokens are reliably predicted at near-zero loss after toolcall training:

| Token ID | Token | Loss (medium) | Note |
|---|---|---|---|
| 25981 | `tool` | ~0.025 | Tool name prefix |
| 2404 | `":"` | ~0.05 | JSON colon-quote |
| 4906 | `type` | ~0.1 | Argument type key |
| 198 | `\n` | ~0.03 | Line separator |
| 2430 | `","` | ~0.1 | JSON field separator |
| 3556 | (common arg) | ~0.08 | Recurring arg token |

---

## Tool Surface

All three models are trained on the **Supernaut toolcall-v2 dataset** — 24 tools, 75 example pairs, 6000 synthetic records:

**Supernaut actions:** `supernaut_call`, `supernaut_analyze`, `supernaut_plan`, `supernaut_execute`, `supernaut_generate`, `supernaut_route`, `supernaut_orchestrate`, `supernaut_discover`, `supernaut_health`

**Fleet dispatch:** `micronaut_dispatch` (SHELL-1, FLEET-1, PM-1, OC-1, OV-1)

**Event/field bus:** `event_emit`, `field_mutate`, `gpu_dispatch`

**Core tools:** `git_status`, `git_commit`, `git_diff`, `git_log`, `git_branch`, `file_read`, `file_write`, `file_search`, `shell_run`, `gpt2_infer`, `kuhul_fold_query`

---

## Tool Call Format

```
### Instruction:
You have access to the following tools:
[{"name":"supernaut_call","description":"...","parameters":{...}}]

<question>

### Response:
<tool_call>{"name":"supernaut_call","arguments":{"action":"health"}}</tool_call>
<tool_response>{"result":"ok, status:ready, version:3.5.0"}</tool_response>
ok, status:ready, version:3.5.0
```

---

## System Integration

These models are designed to work with the KXML bidirectional graph dispatch system:

```js
import { KXMLGraph, PhaseGatedDispatcher } from 'kuhul-es/kxml';
import { ShardRegistry }                   from 'kuhul-es/kxml';

// The models resolve tool calls through the phase-gated dispatcher
// Tool actions map to KXML nodes via kxml-shard-registry.js
```

**Required:** `npm i kuhul-es` (v3.5.0+)

**Tool action routing:** `src/kxml/kxml-ops.js` → `OPS['@supernaut_call']` → `PhaseGatedDispatcher.dispatchNode()`

**Model registry:** `models/model-registry.json`

---

## Source Locations (before rename)

| New name | Original path | Training |
|---|---|---|
| `kuhul-tool-medium-q8.gguf` | `E:\models\GPT2\med-GPT\gpt2_medium_ft_toolcall_q8.gguf` | PyTorch CPU, 500 steps |
| `kuhul-tool-small-q8.gguf` | `E:\models\GPT2\mini-GPT\gpt2_small_ft_toolcall_q8.gguf` | D3D11 iGPU, 2000 steps |
| `kuhul-tool-lite-q8.gguf` | `E:\models\GPT2\mini-GPT\gpt2_small_lite_tool_q8.gguf` | PyTorch CPU relearn |
| `kuhul-tool-medium-ft.safetensors` | `E:\models\GPT2\med-GPT\gpt2_medium_ft_toolcall.safetensors` | Full precision weights |

---

## Real Micronauts — MM-1 Variants (In Progress)

Three domain-specialist micronauts are being built on the MM-1 (ModelMicronaut) slot, all bound to `⟁COMPUTE_FOLD⟁`. All three can chat.

| ID | Name | Base | Path | Fine-tune |
|----|------|------|------|-----------|
| MM-MATH | MathMicronaut | GPT-2 medium 117M | `E:\models\GPT2\math-GPT` | Math reasoning + KXML step-by-step |
| MM-CODER | CoderMicronaut | Qwen2.5 0.5B Instruct Q8 + C++ AST engine | `bin/micronaut/coder/Release/micronaut_coder_mcp.exe` | Persistent project coding + phase-gated AST operations |
| MM-TOOLCALL | ToolcallMicronaut | GPT-2 medium 117M | `E:\models\GPT2\med-GPT` | Toolcall dispatch + geodesic ARC attention |

Registry: `micronaut/micronaut.registry.xjson` (authoritative). Fold system: `micronaut/folds.toml` (15 folds, mutation forbidden).

### MM-CODER AST runtime

MM-CODER uses the local controller model at
`C:\Users\canna\.lmstudio\models\qwen2.5-0.5b-instruct-q8_0.gguf` to propose
bounded coding operations. The model does not edit files directly. It calls
`bin/micronaut/coder/Release/micronaut_coder_mcp.exe`, which exposes `read`,
`write`, `patch`, `search`, `validate`, `ingest`, `ast`, `spit`, `phase`,
`publish`, and `grammar`.

The MCP AST path loads language parsers from `bin/CodeWASM/` through
`scripts/tree_sitter_wasm_bridge.mjs`. Supported WASM grammars include Bash,
C, C++, C#, CSS, Go, HTML, Java, JavaScript, JSON, K-UHUL/KHL, PHP, Python, Ruby, Rust,
Scala, TypeScript, and TSX. The resulting `kuhul.coder.ast.v1` graph passes
through POP, WO, SEK, CHEN, and XUL before it is returned to the model.

K-UHUL uses one semantic runtime loop. `Pop`, `Wo`, `Sek`, `Ch'en`, and `Xul`
are authority gates that decide which operation is allowed at each tick.
`Sek => Sek <= Sek` permits reflexive compute refinement; a changed tensor
shape must carry an explicit transformation law before output can be verified
and committed.

The Node binding supplies its matching core Tree-sitter runtime; the
repository's `bin/CodeWASM/tree-sitter.wasm` is a separate WASI build. The
language grammar WASMs remain the authoritative parser assets.

Build the MCP target from a Visual Studio developer shell:

```powershell
& 'C:\Program Files\CMake\bin\cmake.exe' `
  -S bin\micronaut\coder `
  -B bin\micronaut\coder\build-nnck-vs `
  -G Ninja -DCMAKE_BUILD_TYPE=Release
& 'C:\Program Files\CMake\bin\cmake.exe' `
  --build bin\micronaut\coder\build-nnck-vs `
  --target micronaut_coder_mcp
```

### MM-CODER Internet Learning Pipeline

MM-CODER is augmented with an autonomous data harvesting pipeline that continuously feeds new training data from public APIs:

```
data-harvester.mjs (port 25120)
  └─ fetches: GitHub API, StackOverflow, arXiv CS.LG, HuggingFace, HN, NASA
  └─ rate-limited: 1 req/domain/sec
  └─ writes: data/harvested/batch_<ts>.jsonl
        ↓
internet_harvester.py
  └─ extracts (prompt, completion) pairs fold-tagged ⟁COMPUTE_FOLD⟁
  └─ writes: E:\models\GPT2\med-GPT\training\harvest_<ts>.jsonl
        ↓
learning-engine.mjs (port 25121)
  └─ spawns trainer per batch
  └─ hot-swaps improved model
  └─ notifies coordinator of model update
```

Config: `micronaut/internet-learning.xjson`. K'UHUL PS module: `micronaut/kuhul/autonomous_learning.ps1`.

---

## Training Infrastructure

| Component | Path |
|---|---|
| D3D11 trainer (fixed LayerNorm bwd) | `C:\Users\canna\.gpu_trainer\bin\gpt2_trainer.exe` |
| PyTorch CPU trainer | `C:\Users\canna\.gpu_trainer\finetune_toolcall_pt.py` |
| CPU cluster relearn | `C:\Users\canna\.gpu_trainer\retrain_small_lite.py` |
| Toolcall data generator | `E:\models\GPT2\med-GPT\toolcall_data.py` (v2, 24 tools) |
| Token build pipeline | `E:\models\GPT2\small-instruct\build_train_jsonl.py` |
| Shape fix (D3D11 bug) | `E:\models\GPT2\mini-GPT\fix_safetensors_shapes.py` |
| GGUF conversion | `E:\models\GPT2\med-GPT\to_gguf.py` |
