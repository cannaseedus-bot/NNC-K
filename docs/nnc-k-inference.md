# NNC-K sharded inference server

`scripts/nnc_k_server.py` provides an OpenAI-compatible HTTP inference server
that runs GPT-OSS end-to-end using NNC-K's native `.xshard` tiles. It is the
counterpart to `scripts/model_server.py`, which wraps classic `llama.cpp`.

## Architecture

```text
prompt tokens
    ↓
openai-harmony tokenizer (or stub)
    ↓
token embedding lookup  → load-once, class=embedding
    ↓
for each token:
    for each layer:
        RMS norm
        attention Q/K/V/O projection  ← .xshard attention tiles, hot lane
        RoPE + GQA softmax + V-mul     (CPU glue, sidecar optional)
        residual
        RMS norm
        MoE router (top-k)
        gate/up/down expert GEMM       ← .xshard expert tiles, cold lane
        residual
    LM head projection
    sampler
    next token
```

## Files

| Path | Purpose |
|------|---------|
| `scripts/nnc_k_server.py` | OpenAI-compatible HTTP server |
| `scripts/bench_llama_vs_xshard.py` | Race NNC-K vs `llama.cpp` |
| `scripts/model_server.py` | Classic `llama.cpp` baseline server |

## Starting the server

```powershell
py.exe scripts\nnc_k_server.py --model-dir "E:\models\GPT-OSS" --port 1235
```

The server expects:

- A GGUF file somewhere under the model dir (for token embeddings + layer norms)
- `shards/layer_NN_{q,k,v,o}.xshard` attention weight tiles (hot lane)
- `experts/layer_NN/{gate,up,down}.xshard` MoE expert weight tiles (cold lane)

If a shard is missing, the server falls back to small random weights so the
architecture can still be exercised end-to-end. Output quality depends on which
layers have real `.xshard` weights.

## API

```powershell
$body = @{
    model = "gpt-oss-20b"
    messages = @(@{ role = "user"; content = "What is the capital of France?" })
    max_tokens = 32
    temperature = 0.7
} | ConvertTo-Json -Compress

Invoke-RestMethod -Uri "http://127.0.0.1:1235/v1/chat/completions" -Method Post -Body $body -ContentType "application/json"
```

Response includes a `timings` object:

```json
{
  "timings": {
    "prompt_tokens": 5,
    "first_token_ms": 2450.0,
    "tokens_generated": 32,
    "total_s": 18.5,
    "tps": 1.73,
    "pager_stats": {
      "hot_loads": 24,
      "cold_loads": 72,
      "evictions": 0,
      "hot_bytes": 1012500000,
      "cold_bytes": 3037500000
    }
  }
}
```

## Benchmark against llama.cpp

```powershell
py.exe scripts\bench_llama_vs_xshard.py `
  --gguf "C:\Users\canna\.lmstudio\models\lmstudio-community\gpt-oss-20b-GGUF\gpt-oss-20b-MXFP4.gguf" `
  --model-dir "E:\models\GPT-OSS" `
  --max-tokens 32 --n-prompts 3
```

This script:

1. Starts `model_server.py` on port 1234 with the GGUF.
2. Waits for `/health` to report `ok`.
3. Starts `nnc_k_server.py` on port 1235 with the model dir.
4. Sends the same prompts to both servers.
5. Records load time, first-token latency, tokens/sec, and total duration.
6. Writes a Markdown report to `.learning/bench_reports/`.

## PowerShell module helpers

```powershell
Import-Module scripts\NNCK-Runtime.psm1

# Start / stop from the WPF UI
/nncserver start E:\models\GPT-OSS
/nncserver chat What is the capital of France?
/nncserver stop
/bench C:\path\to\gpt-oss-20b-MXFP4.gguf E:\models\GPT-OSS 32 3

# Direct module call
$response = Invoke-NncKRequest -Prompt "Explain quantum computing" -MaxTokens 64
```

## 2 GB hot-swap policy

The `WeightPager` inside the server enforces the same policy as the rest of
NNC-K:

- `attention` tiles ≤ 2 GB → hot lane, LRU-evictable
- `expert` tiles ≤ 2 GB → cold lane, LRU-evictable
- `embedding` table → load once, never evicted
- Tiles > 2 GB are rejected

## Status

This is an **Option A / full autoregressive server** scaffold. It produces
tokens and can be benchmarked against `llama.cpp`. Coherent output requires
real `.xshard` weights for all layers. Current derived assets cover layer 0
attention and experts; deeper layers fall back to random weights for
architectural bring-up.

## Authority boundary

- `nnc_k_server.py` emits candidate text and compute metrics only.
- It does not create, update, merge, or promote micronauts.
- `MicronautManager` and BOSS own persistence and promotion.
