# ASX RAM Attention Sidecar

This document maps the high-level K'UHUL attention-fold router (`attention.fold.khl`) to the low-level native compute sidecar `bin/asx_ram.exe`.

## What `asx_ram.exe` does

`bin/asx_ram.exe` (built from `bin/v3.5.0-WebX/native/gpu_trainer/asx_ram_test.cpp`) is a **D3D11 streaming attention benchmark**. It:

1. Loads three binary `.xshard` files: Q, K, and V.
2. Streams attention-head tiles through a fixed GPU window using three slots:
   - **C** — disk → CPU buffer
   - **B** — CPU buffer ready
   - **A** — GPU compute dispatch
3. Runs embedded HLSL compute shaders for:
   - scaled dot-product attention (`softmax(Q·Kᵀ / √d)`)
   - value multiplication (`P·V`)
4. Benchmarks against a CPU reference and reports timing/errors.

Usage:

```text
asx_ram_test.exe <q_shard> <k_shard> <v_shard> [passes] [--prefetch]
```

## Shard format

| Field | Offset | Size | Notes |
|-------|--------|------|-------|
| magic | 0 | 4 | `XSQ2` |
| version | 4 | 4 | e.g. `1` |
| layer_id | 8 | 4 | transformer layer |
| tensor_type | 12 | 4 | 0=Q, 1=K, 2=V, 3=O |
| rows | 16 | 4 | e.g. `64` |
| cols | 20 | 4 | e.g. `1024` |
| tile_size | 24 | 4 | elements per tile |
| tile_count | 28 | 4 | heads per layer (typically 16) |
| dtype | 32 | 4 | 0=fp32, 1=fp16, 2=int8, 3=int4 |
| padding | 36 | 28 | reserved |

Each tile is `tile_size * element_bytes` raw, aligned to `xshard_tile_bytes(header)`. Header is 64 bytes.

## K'UHUL → Compute mapping

```text
attention.fold.khl              asx_ram.exe
─────────────────────────────────────────────────
fold = reasoning, memory,         q_shard = reasoning query tensor
security, creativity,             k_shard = fold key tensor
execution                           v_shard = fold value tensor

attention::attend(Q)              → compute attention scores across lanes
attention::execute_lane(fold, Q) → spawn asx_ram.exe for that lane
attention::guard(security)         → clamp/filter scores before V extraction
```

## PowerShell helpers

### `micronaut-ui.ps1`: `Invoke-AsxRamFromKuhul`

```powershell
Invoke-AsxRamFromKuhul -QShard 'layer_00_q.xshard' -KShard 'layer_00_k.xshard' -VShard 'layer_00_v.xshard' -Prefetch
```

### `scripts/NNCK-Runtime.psm1`: `Invoke-AsxRam`

```powershell
Import-Module scripts\NNCK-Runtime.psm1
$result = Invoke-AsxRam -QShard 'layer_00_q.xshard' -KShard 'layer_00_k.xshard' -VShard 'layer_00_v.xshard' -Prefetch
$result.StdOut
```

Both use temp-file redirection to avoid Windows pipe-buffer deadlocks.

## Portable compute backends

The same shard layout and shader math can target:

| Backend | Status | Notes |
|---------|--------|-------|
| D3D11 / D3D11_1 | ✅ existing | `asx_ram.exe` uses typed `Buffer<float>` SRV/UAV. |
| WebGL 2.0 | ⚠️ possible | No compute shaders; emulate with fragment shaders + texture ping-pong. Slow but functional. |
| WebGPU | ✅ recommended | Native compute; port shaders to WGSL. Best web target. |
| Vulkan / Metal | ⚠️ future | Use SPIR-V/MSL cross-compilation of the HLSL kernels. |

## Authority boundary

- `asx_ram.exe` is a **compute-only** sidecar.
- It reads shards, runs attention math, and emits timing/stats to stdout.
- It does **not** create, update, merge, or promote micronauts.
- The K'UHUL runtime (`micronaut-ui.ps1` / `MicronautManager`) owns persistence.

## Generating sample `.xshard` files

No sample shards exist in the repo. Use the included generator:

```powershell
py.exe scripts\generate_xshard.py .learning\xshard_samples
```

This creates four files (~4 MB each):

```text
layer_00_q.xshard
layer_00_k.xshard
layer_00_v.xshard
layer_00_o.xshard
```

Then run the sidecar:

```powershell
.\bin\asx_ram.exe .learning\xshard_samples\layer_00_q.xshard `
    .learning\xshard_samples\layer_00_k.xshard `
    .learning\xshard_samples\layer_00_v.xshard `
    1 --prefetch
```

Example output:

```text
[asx_ram] heads per layer: 16  tile_size: 65536 floats (256KB)
[asx_ram] adapter: Intel(R) HD Graphics 4600
...
[asx_ram] total heads  : 16
[asx_ram] disk hidden  : 4.2 ms  (98% of reads overlapped with GPU)
[asx_ram] gpu  time    : 660.2 ms
[asx_ram] throughput   : 24.2 heads/sec
[asx_ram] max_err      : 5.59e-09  (tol=1e-03)
[asx_ram] mismatches   : 0
[asx_ram] PASS — all 16 head(s) correct, prefetch slot C async, device stable
```

## Files

| Path | Purpose |
|------|---------|
| `bin/asx_ram.exe` | Legacy fixed-shape D3D11 streaming attention executable |
| `bin/asx_ram_v2.exe` | Config-driven GPT-OSS-compatible D3D11 streaming executable |
| `bin/Quantum/src/quantum_trinity_asx_ram_v2.cpp` | Source with config-driven HLSL shaders |
| `bin/v3.5.0-WebX/native/gpu_trainer/asx_ram_test.cpp` | Source of legacy `asx_ram.exe` |
| `schemas/programs/asx-ram-attention.kuhul` | K'UHUL program declaring fold→sidecar binding |
| `scripts/NNCK-Runtime.psm1` | `Invoke-AsxRam` module helper |
| `scripts/generate_xshard.py` | Generate synthetic activation `.xshard` files |
| `scripts/safetensors_to_xshard.py` | Convert HF Safetensors weights to weight shards (future streaming) |
| `micronaut-ui.ps1` | `Invoke-AsxRamFromKuhul` runtime helper |

## Config-driven v2 (`asx_ram_v2.exe`)

The legacy `asx_ram.exe` hardcodes `64×1024` tiles and 16 heads. GPT-OSS 20B
needs `64×64` activation tiles, 64 query heads, and 8 KV heads. `asx_ram_v2.exe`
reads `model_config.json` and generates HLSL with the correct constants.

Example `model_config.json`:

```json
{
  "n_layers": 2,
  "hidden_size": 2880,
  "num_attention_heads": 64,
  "num_key_value_heads": 8,
  "head_dim": 64,
  "seq_len": 64
}
```

Generate test activation shards and run:

```powershell
py.exe scripts\generate_xshard.py .learning\xshard_samples\st_test
.\bin\asx_ram_v2.exe `
  .learning\xshard_samples\st_test\layer_00_q.xshard `
  .learning\xshard_samples\st_test\layer_00_k.xshard `
  .learning\xshard_samples\st_test\layer_00_v.xshard `
  .learning\xshard_samples\st_test\model_config.json `
  1 --prefetch
```

Expected output:

```text
[asx_ram_v2] config: hidden=2880 heads=64 kv=8 head_dim=64
[asx_ram_v2] heads per layer: 64  tile: 64x64 (4096 floats)
...
[asx_ram_v2] total heads  : 64
[asx_ram_v2] total time   : 118.7 ms
[asx_ram_v2] heads/sec    : 539.0
[asx_ram_v2] max_err      : 7.46e-03  (tol=1e-02)
[asx_ram_v2] mismatches   : 0
[asx_ram_v2] PASS
```

## Activation vs weight `.xshard`

`.xshard` is a generic attention-tile container. It can hold:

- **Activation tiles** (`[seq_len, head_dim]` per head) — semantically valid for
  the softmax+vmul attention kernel.
- **Weight tiles** (`[head_dim, hidden_size]` per head, or other layouts) —
  used for I/O/compute bring-up and future weight-streaming inference.

`generate_xshard.py` creates synthetic **activation** shards; `asx_ram_v2.exe`
validates them with `max_err < 1e-02` and reports `PASS`.

`safetensors_to_xshard.py` and `gguf_to_xshard.py` currently produce **weight**
shards. `gguf_to_xshard.py` can convert the local GPT-OSS 20B MXFP4 GGUF
attention weights into `.xshard` files that `asx_ram_v2.exe` loads and dispatches.
Because those files contain *weights* rather than *activations*, the numerical
validation reports `max_err ~1e-01` and `FAIL` — this is expected and confirms
the full GGUF → `.xshard` → GPU pipeline works. See `docs/gpt-oss-shard-bridge.md`
for the exact command and the planned activation-extraction step.

## Real GPT-OSS activations from the local MXFP4 GGUF

`scripts/gptoss_layer_forward.py` runs a minimal CPU forward pass on layer-0
attention weights extracted from your GGUF and writes **activation** `.xshard`
files to `E:\models\GPT-OSS\activations`:

```powershell
py.exe scripts\gptoss_layer_forward.py "E:\models\GPT-OSS" `
  --out-dir "E:\models\GPT-OSS\activations" --layer 0 --seq-len 64 --scale-embed 0.0001
```

`--scale-embed` scales the synthetic prompt embedding before RMS normalization
so the resulting Q/K/V activations stay small enough for the current D3D11
softmax kernel to remain numerically stable. On more capable hardware or with
a future log-space softmax implementation, full-magnitude activations can be
streamed without this scaling step.

Run the sidecar:

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

## Shard class and 2 GB hot-swap policy

`.xshard` files now carry a `shard_class` byte in the 64-byte header:

| Class | Value | Lane policy |
|-------|-------|-------------|
| `attention` | 0 | Hot lane if payload ≤ 2 GB |
| `expert`    | 1 | Cold lane if ≤ 2 GB per tile; rejected if larger |
| `embedding` | 2 | Cold load-once lane (do not swap) |
| `generic`   | 3 | Route by size |

`asx_ram_v2.exe` enforces this at load time:

```text
[asx_ram_v2] q class=attention file_bytes=1048576 lane=hot
[asx_ram_v2] k class=attention file_bytes=131072 lane=hot
[asx_ram_v2] v class=attention file_bytes=131072 lane=hot
```

Expert shards that exceed 2 GB are rejected for the hot lane. Token embedding
tables (e.g., `token_embd.weight`) are marked as `embedding` and loaded once,
not swapped per turn.

Use `scripts/xshard_classify.py` to inspect shard headers:

```powershell
py.exe scripts\xshard_classify.py "E:\models\GPT-OSS\activations\layer_00_q.xshard"
```

## MoE expert GEMM sidecar (`asx_gemm.exe`)

`bin/asx_gemm.exe` performs matrix multiplication on MXFP4-derived MoE expert
weight shards. It reads a `.xshard` file with `shard_class=expert`, loads the
selected expert tile(s), multiplies a token activation vector through each,
and validates against a CPU reference.

Example:

```powershell
.\bin\asx_gemm.exe "E:\models\GPT-OSS\experts\layer_00\gate.xshard" 0,1,2,3 1
```

Expected output on Intel HD Graphics 4600:

```text
[asx_gemm] total experts: 4
[asx_gemm] total time: 3683.0 ms
[asx_gemm] experts/sec: 1.1
[asx_gemm] max_err: 9.09e-07 (tol=1e-02)
[asx_gemm] mismatches: 0
[asx_gemm] PASS
```

## Authority boundary

- `asx_ram.exe` / `asx_ram_v2.exe` / `asx_gemm.exe` are compute-only sidecars.
- They emit timing/metrics; they never create, update, merge, or promote micronauts.
- K'UHUL runtime / `MicronautManager` owns persistence.
