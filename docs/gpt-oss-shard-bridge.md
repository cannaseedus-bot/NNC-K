# GPT-OSS → NNC-K Shard Bridge

This document describes how to bring OpenAI GPT-OSS weights into NNC-K's native
`.xshard` attention-tile format so the `asx_ram_v2.exe` D3D11 sidecar can stream
real model weights (and, in the future, real attention activations).

## Why sharding matters

For GPT-OSS 20B/120B, the parameter count exceeds single-GPU memory. Training
uses FSDP sharded `.pt` checkpoints; inference uses Tensor Parallelism in
vLLM/SGLang; distribution uses HuggingFace `.safetensors` shards. NNC-K's
`.xshard` format adds a **local streaming inference** layer on top of those
existing formats.

```text
GPT-OSS training
      │
      ▼
FSDP sharded .pt checkpoints
      │
      ▼
export_to_safetensors.py
      │
      ▼
HuggingFace .safetensors shards (5 GB each)
      │
      ├──────► Transformers / vLLM / SGLang
      │
      └──────► scripts/safetensors_to_xshard.py
                    │
                    ▼
            NNC-K .xshard tiles per layer (Q/K/V/O)
                    │
                    ▼
              asx_ram_v2.exe D3D11 streaming attention
```

## GPT-OSS 20B MXFP4 GGUF on disk

You already have:

```text
C:\Users\canna\.lmstudio\models\lmstudio-community\gpt-oss-20b-GGUF\gpt-oss-20b-MXFP4.gguf
```

This file is a **quantized checkpoint**. `llama-cpp-python` 0.3.23 cannot
instantiate it, but `gguf-py` can read the tensor list and metadata directly.

## Key discovery: attention weights are Q8_0, MoE experts are MXFP4

Using `scripts/gguf_inspector.py` and `gguf-py`:

| Tensor group | Quantization | Dequantizable now? |
|--------------|--------------|--------------------|
| `token_embd.weight`, `output.weight` | Q8_0 | ✅ |
| `blk.N.attn_q/k/v/output.weight` | Q8_0 | ✅ |
| `blk.N.attn_*_bias`, norms | F32 | ✅ |
| `blk.N.ffn_gate_exps/down_exps/up_exps.weight` | **MXFP4** | ❌ (needs MXFP4 dequant) |

This means we can already extract and shard the attention weights from your
local GGUF without waiting for MXFP4 support.

## Inspect the GGUF

```powershell
py.exe scripts\gguf_inspector.py `
  C:\Users\canna\.lmstudio\models\lmstudio-community\gpt-oss-20b-GGUF\gpt-oss-20b-MXFP4.gguf
```

For GPT-OSS 20B this reports:

```text
Architecture: gpt-oss
Layers:       24
Hidden size:  2880
Query heads:  64
KV heads:     8
Head dim:     64
Tensors:      459
```

Tensor naming follows the GGUF convention:

```text
blk.0.attn_q.weight
blk.0.attn_k.weight
blk.0.attn_v.weight
blk.0.attn_output.weight
blk.0.ffn_gate_exps.weight
blk.0.ffn_up_exps.weight
blk.0.ffn_down_exps.weight
```

## Create a conversion plan

```powershell
py.exe scripts\gguf_to_xshard_plan.py `
  C:\Users\canna\.lmstudio\models\lmstudio-community\gpt-oss-20b-GGUF\gpt-oss-20b-MXFP4.gguf `
  -o .learning\xshard_samples\gguf_plan.json
```

This writes a plan JSON with the per-layer Q/K/V/O tensor mapping. It does not
dequantize.

## Quick start: put Safetensors + shards on `E:\models\GPT-OSS`

If you want your model data outside the project tree, use this layout:

```text
E:\models\GPT-OSS\
  config.json
  model-00001-of-00001.safetensors   ← real GGUF-derived attention weights
  shards\
    model_config.json
    layer_00_q.xshard
    layer_00_k.xshard
    layer_00_v.xshard
    layer_00_o.xshard
```

### Step 1 — create Safetensors from your local GGUF

Your local GGUF is at:

```text
C:\Users\canna\.lmstudio\models\lmstudio-community\gpt-oss-20b-GGUF\gpt-oss-20b-MXFP4.gguf
```

Extract layer 0 attention weights as Safetensors:

```powershell
py.exe scripts\gguf_layer_to_safetensors.py `
  "C:\Users\canna\.lmstudio\models\lmstudio-community\gpt-oss-20b-GGUF\gpt-oss-20b-MXFP4.gguf" `
  --out-dir "E:\models\GPT-OSS" --layers 0
```

This writes `E:\models\GPT-OSS\config.json` and
`E:\models\GPT-OSS\model-00001-of-00001.safetensors` (~101 MB for layer 0).

### Step 2 — run one-layer CPU forward pass and emit activation `.xshard`

```powershell
py.exe scripts\gptoss_layer_forward.py "E:\models\GPT-OSS" `
  --out-dir "E:\models\GPT-OSS\activations" --layer 0 --seq-len 64
```

(`--scale-embed` defaults to `0.0001` on this GPU to keep the D3D11 softmax
kernel numerically stable; omit or increase it on hardware with better fp32
range or once the kernel is upgraded to log-space softmax.)

```text
E:\models\GPT-OSS\activations\layer_00_q.xshard
E:\models\GPT-OSS\activations\layer_00_k.xshard
E:\models\GPT-OSS\activations\layer_00_v.xshard
E:\models\GPT-OSS\activations\model_config.json
```

### Step 3 — stream through `asx_ram_v2.exe`

```powershell
.\bin\asx_ram_v2.exe `
  "E:\models\GPT-OSS\activations\layer_00_q.xshard" `
  "E:\models\GPT-OSS\activations\layer_00_k.xshard" `
  "E:\models\GPT-OSS\activations\layer_00_v.xshard" `
  "E:\models\GPT-OSS\activations\model_config.json" `
  1 --prefetch
```

Result on Intel HD Graphics 4600:

```text
[asx_ram_v2] total heads  : 64
[asx_ram_v2] total time   : 129.1 ms
[asx_ram_v2] heads/sec    : 495.9
[asx_ram_v2] max_err      : 3.43e-03  (tol=1e-02)
[asx_ram_v2] mismatches   : 0
[asx_ram_v2] PASS
```

This is the first real GPT-OSS model layer running through the NNC-K D3D11
attention sidecar from the local MXFP4 GGUF.

## Convert Safetensors → `.xshard` (weight shards for I/O bring-up)

If you want weight-style shards rather than activations:

```powershell
py.exe scripts\safetensors_to_xshard.py `
  "E:\models\GPT-OSS" `
  "E:\models\GPT-OSS\shards" `
  --layers 0
```

These weight shards load and dispatch through `asx_ram_v2.exe` but will show
`max_err ~1e-01` because they are not semantic attention activations.

## Convert the local MXFP4 GGUF attention weights directly to `.xshard`

You can also skip the Safetensors intermediate and go directly:

```powershell
py.exe scripts\gguf_to_xshard.py `
  "C:\Users\canna\.lmstudio\models\lmstudio-community\gpt-oss-20b-GGUF\gpt-oss-20b-MXFP4.gguf" `
  --out "E:\models\GPT-OSS\shards" --layers 0
```

This writes the same four `.xshard` files plus `model_config.json` and
`conversion_report.json`.

## Convert pre-converted Safetensors to `.xshard`

If you have (or download) a Safetensors version of GPT-OSS, run:

```powershell
py.exe scripts\safetensors_to_xshard.py `
  C:\path\to\gpt-oss-20b-safetensors `
  .learning\xshard_safetensors\gpt-oss-20b
```

Input directory must contain `config.json` and `model-*.safetensors`. Output:

```text
.learning\xshard_safetensors\gpt-oss-20b\layer_00_q.xshard
.learning\xshard_safetensors\gpt-oss-20b\layer_00_k.xshard
.learning\xshard_safetensors\gpt-oss-20b\layer_00_v.xshard
.learning\xshard_safetensors\gpt-oss-20b\layer_00_o.xshard
...
```

Each file is one layer, one tensor type, tiled by head.

## Native `.xshard` format

`.xshard` is a binary attention-tile format with a 64-byte header (`XSQ2`
magic, version, layer_id, tensor_type, rows, cols, tile_size, tile_count,
dtype, **shard_class**, 24-byte padding) followed by contiguous 2-D float tiles.
The sidecar streams tiles through the D3D11 compute shader; `asx_ram_v2.exe`
reads `model_config.json` for head counts and GQA mapping but uses the shard's
actual `rows × cols` for compute.

The same container holds:

- **Activation-style tiles** (`[seq_len, head_dim]` per head) — semantically
  valid for the softmax+vmul attention kernel, class=`attention`.
- **Weight-style tiles** (`[head_dim, hidden_size]` per head, or other layouts)
  — useful for I/O/compute bring-up and future weight-streaming, class=`attention`.
- **MoE expert shards** — class=`expert`; must be split into ≤2 GB tiles for
  hot-swapping on this GPU.
- **Token embedding table** — class=`embedding`; load once, do not swap.

## 2 GB hot-swap window

On the current Intel HD Graphics 4600 test hardware, the practical window for
hot-swapping shards is **2 GB**. The policy is:

```text
attention tiles  ≤ 2 GB  → hot lane
expert shards    ≤ 2 GB  → cold lane (swappable)
experts          > 2 GB  → rejected for hot lane; must be split
embedding tables         → load once, not swapped
```

`asx_ram_v2.exe` prints the lane for each input shard. `scripts/xshard_classify.py`
can be used standalone.

## MXFP4 MoE expert dequantization → `.xshard`

Your local GGUF stores MoE FFN expert weights (`ffn_gate_exps`, `ffn_up_exps`,
`ffn_down_exps`) in MXFP4 format. `gguf-py` already supports dequantizing these,
so we can convert them directly to native `.xshard` files with
`shard_class=expert`.

Convert layer 0 experts:

```powershell
py.exe scripts\gguf_experts_to_xshard.py `
  "C:\Users\canna\.lmstudio\models\lmstudio-community\gpt-oss-20b-GGUF\gpt-oss-20b-MXFP4.gguf" `
  --out "E:\models\GPT-OSS\experts" --layers 0
```

Output (one file per tensor type per layer, all 32 experts as tiles):

```text
E:\models\GPT-OSS\experts\layer_00\gate.xshard   ~1.01 GB
E:\models\GPT-OSS\experts\layer_00\up.xshard     ~1.01 GB
E:\models\GPT-OSS\experts\layer_00\down.xshard   ~1.01 GB
```

Each file is under the 2 GB hot-swap window. Verify:

```powershell
py.exe scripts\xshard_classify.py "E:\models\GPT-OSS\experts\layer_00\gate.xshard"
py.exe scripts\verify_expert_xshard.py `
  "C:\Users\canna\.lmstudio\models\lmstudio-community\gpt-oss-20b-GGUF\gpt-oss-20b-MXFP4.gguf" `
  "E:\models\GPT-OSS\experts\layer_00\gate.xshard" --layer 0 --comp gate
```

## Run expert GEMM through `asx_gemm.exe`

```powershell
.\bin\asx_gemm.exe "E:\models\GPT-OSS\experts\layer_00\gate.xshard" 0,1,2,3 1
```

Result on Intel HD Graphics 4600:

```text
[asx_gemm] total experts: 4
[asx_gemm] total time: 3683.0 ms
[asx_gemm] experts/sec: 1.1
[asx_gemm] max_err: 9.09e-07 (tol=1e-02)
[asx_gemm] mismatches: 0
[asx_gemm] PASS
```

## Run the D3D11 sidecar on synthetic activation tiles

```powershell
py.exe scripts\generate_xshard.py .learning\xshard_samples\st_test
.\bin\asx_ram_v2.exe `
  .learning\xshard_samples\st_test\layer_00_q.xshard `
  .learning\xshard_samples\st_test\layer_00_k.xshard `
  .learning\xshard_samples\st_test\layer_00_v.xshard `
  .learning\xshard_samples\st_test\model_config.json `
  1 --prefetch
```

Expected result:

```text
[asx_ram_v2] total heads  : 64
[asx_ram_v2] total time   : ~120 ms
[asx_ram_v2] heads/sec    : ~540
[asx_ram_v2] max_err      : ~7e-03  (tol=1e-02)
[asx_ram_v2] mismatches   : 0
[asx_ram_v2] PASS
```

## Gaps and next steps

1. **MXFP4 dequantization** — MoE FFN expert weights are still MXFP4. They must
   be dequantized and tiled into ≤2 GB shards to fit the hot-swap window.
2. **Real activations at full magnitude** — the current GPU softmax kernel
   overflows on full-magnitude Q/K values. A future log-space or fp64 kernel
   would remove the `--scale-embed` workaround.
3. **Tensor Parallelism** — extend beyond single D3D11 device to multi-GPU TP.
4. **MoE experts** — GPT-OSS uses 32 experts with top-4 routing. The MoE FFN
   weights (`ffn_gate_exps`, `ffn_up_exps`, `ffn_down_exps`) are not yet tiled
   into `.xshard`.

## Files

| Path | Purpose |
|------|---------|
| `scripts/gguf_inspector.py` | Read GGUF metadata and tensor list without loading model |
| `scripts/gguf_to_xshard_plan.py` | Build per-layer Q/K/V/O mapping plan from GGUF |
| `scripts/gguf_layer_to_safetensors.py` | Extract GGUF attention layers to Safetensors |
| `scripts/gguf_to_xshard.py` | Convert GGUF attention Q/K/V/O weights directly to `.xshard` |
| `scripts/safetensors_to_xshard.py` | Convert HF Safetensors weights to `.xshard` tiles |
| `scripts/generate_xshard.py` | Generate synthetic attention `.xshard` samples |
| `scripts/gptoss_layer_forward.py` | One-layer CPU forward pass → activation `.xshard` |
| `scripts/gptoss_multi_layer_forward.py` | Multi-layer CPU forward pass → activation `.xshard` |
| `scripts/gptoss_harmony_forward.py` | Harmony prompt → token embeddings → activation `.xshard` |
| `scripts/xshard_classify.py` | Inspect `.xshard` headers and assign lanes |
| `docs/gpt-oss-shard-bridge.md` | This document |
| `docs/asx-ram-attention.md` | D3D11 sidecar documentation |

## Weights vs activations

A real inference pipeline needs:

```text
GGUF / Safetensors weights
    ↓
load into model (transformers / llama.cpp / custom forward pass)
    ↓
run forward pass for a prompt
    ↓
extract Q/K/V activations per layer per head
    ↓
write as .xshard tiles
    ↓
stream through asx_ram_v2.exe
```

- `generate_xshard.py` creates synthetic **activations**.
- `gguf_to_xshard.py` extracts real **weights** from the local MXFP4 GGUF.
- `safetensors_to_xshard.py` tiles HuggingFace **weights**.

## Authority boundary

- Converter scripts only emit candidate `.xshard` files.
- They do not create, update, merge, or promote micronauts.
- `asx_ram.exe` / `asx_ram_v2.exe` are compute-only.
- The PowerShell runtime / `MicronautManager` owns persistence and routing decisions.
