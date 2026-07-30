# NNC-K BOSS Guide 🎯

**Neural Network Compiler - Kernel Server**

Complete reference for the GPT-OSS 20B .xshard conversion and inference pipeline.

---

## Runtime Authority Chain

NNC-K follows a three-stage execution architecture:

```
User
↓
XCFE (admission & validation)
↓
K'UHUL (Pop→Wo→Yax→Sek→Ch'en→Xul)
↓
Forward (Model inference)
↓
Collapse (token selection)
↓
CHEESE (evaluation & proof)
↓
@flux (execution lineage)
↓
BossPromotion (promotion decision)
↓
Persistence (governed storage)
```

**Models propose candidate outputs. Runtime components evaluate, record, and govern execution.**

This mental map applies to every component in the project. See [CANONICAL_EXECUTION_LIFECYCLE.md](CANONICAL_EXECUTION_LIFECYCLE.md) for the complete lifecycle.

---

## 📁 Project Structure

```
<repo-root>/
├── scripts/
│   ├── nnc_k_server.py              # Main inference server (OpenAI-compatible)
│   ├── convert_all_layers_to_xshard.py  # Full layer conversion script
│   ├── gguf_to_xshard.py            # Attention weight conversion (Q/K/V/O)
│   ├── gguf_experts_to_xshard.py    # MoE expert conversion (gate/up/down)
│   ├── test_convert_few_layers.py   # Test conversion (layers 0-2)
│   ├── convert_all_layers.bat       # Windows batch runner
│   ├── convert_all_layers.ps1       # PowerShell runner
│   └── CONVERSION_NOTES.md          # Conversion documentation
│
├── bin/
│   ├── asx_ram_v2.exe               # Attention sidecar (Q/K/V tile processing)
│   └── asx_gemm.exe                 # GEMM sidecar (MoE expert matrices)
│
└── BOSS-Guide.md                    # This file
```

```
E:/models/GPT-OSS/hf/                # Converted model output
├── model_config.json                # Model architecture config
├── conversion_report.json           # Conversion statistics
├── CONVERSION_COMPLETE.md           # Conversion summary
├── layer_00/                        # Layer 0 weights
│   ├── q.xshard                     # Query projection (attention, hot lane)
│   ├── k.xshard                     # Key projection (attention, hot lane)
│   ├── v.xshard                     # Value projection (attention, hot lane)
│   ├── o.xshard                     # Output projection (attention, hot lane)
│   ├── gate.xshard                  # MoE gate (experts, cold lane)
│   ├── up.xshard                    # MoE up projection (experts, cold lane)
│   └── down.xshard                  # MoE down projection (experts, cold lane)
├── layer_01/                        # Layer 1 weights
├── ...
└── layer_23/                        # Layer 23 weights
```

---

## 🔑 Key Files Reference

### Core Scripts

| File | Purpose | Size | Status |
|------|---------|------|--------|
| `scripts/nnc_k_server.py` | OpenAI-compatible inference server | ~800 lines | ✅ Production |
| `scripts/convert_all_layers_to_xshard.py` | Unified layer conversion | ~250 lines | ✅ Production |
| `scripts/gguf_to_xshard.py` | Attention weight conversion | ~200 lines | ✅ Production |
| `scripts/gguf_experts_to_xshard.py` | MoE expert conversion | ~180 lines | ✅ Production |

### Helper Scripts

| File | Purpose | Usage |
|------|---------|-------|
| `scripts/test_convert_few_layers.py` | Test conversion pipeline | `py.exe scripts/test_convert_few_layers.py --gguf <path> --out <output>` |
| `scripts/convert_all_layers.bat` | Windows batch conversion | `scripts\convert_all_layers.bat` |
| `scripts/convert_all_layers.ps1` | PowerShell conversion | `.\scripts\convert_all_layers.ps1 -Fp16` |

### Sidecars (Native Executables)

| Executable | Purpose | Lane | Status |
|------------|---------|------|--------|
| `bin/asx_ram_v2.exe` | Attention tile processing | Hot | ✅ Active |
| `bin/asx_gemm.exe` | MoE expert GEMM operations | Cold | ✅ HF-style MoE Forward |
| `bin/asx_gemm_hf_forward.exe` | Complete MoE forward (router + experts) | Cold | 🆕 Ready |

> **Note on asx_gemm.exe**: Now supports complete Hugging Face-style MoE forward pass with router network, top-k expert selection, and weighted combination. See `HF_FORWARD_INTEGRATION.md` for details.

### Configuration Files

| File | Location | Purpose |
|------|----------|---------|
| `model_config.json` | `E:/models/GPT-OSS/hf/` | Model architecture (hidden size, layers, heads, experts) |
| `conversion_report.json` | `E:/models/GPT-OSS/hf/` | Conversion statistics and per-layer status |
| `CONVERSION_COMPLETE.md` | `E:/models/GPT-OSS/hf/` | Human-readable conversion summary |

---

## 🏗️ Architecture Overview

### Unified Tree Structure

All 24 layers follow the same structure:

```
layer_NN/
  ├── q.xshard       [64 tiles, 64×2880] fp16  (attention)
  ├── k.xshard       [8 tiles, 64×2880]  fp16  (attention)
  ├── v.xshard       [8 tiles, 64×2880]  fp16  (attention)
  ├── o.xshard       [64 tiles, 64×2880] fp16  (attention)
  ├── gate.xshard    [32 tiles, 2880×2880] fp32 (MoE experts)
  ├── up.xshard      [32 tiles, 2880×2880] fp32 (MoE experts)
  └── down.xshard    [32 tiles, 2880×2880] fp32 (MoE experts)
```

### Weight Pager Lanes

| Lane | Components | Evictable | Max Bytes |
|------|------------|-----------|-----------|
| **Hot** | Q, K, V, O | ✅ Yes | 2 GB |
| **Cold** | gate, up, down | ✅ Yes | Variable |
| **Load-once** | embeddings | ❌ No | Full size |

### Model Configuration (GPT-OSS 20B)

```json
{
  "hidden_size": 2880,
  "num_layers": 24,
  "num_heads": 64,
  "num_kv_heads": 8,
  "head_dim": 64,
  "num_experts": 32,
  "top_k_experts": 8,
  "vocab_size": 200064
}
```

---

## 🚀 Quick Start

### 1. Start the Server

```bash
cd <repo-root>
py.exe scripts/nnc_k_server.py --model-dir "E:\models\GPT-OSS\hf" --port 1235
```

### 2. Test Endpoints

```bash
# Health check
curl http://localhost:1235/health

# List models
curl http://localhost:1235/v1/models

# Chat completion
curl http://localhost:1235/v1/chat/completions ^
  -H "Content-Type: application/json" ^
  -d "{\"messages\": [{\"role\": \"user\", \"content\": \"Hello\"}], \"max_tokens\": 50}"
```

### 3. Convert New Models

```bash
# Test with 3 layers first
py.exe scripts/test_convert_few_layers.py ^
  "C:\path\to\model.gguf" ^
  --out "E:\models\test"

# Full conversion
py.exe scripts/convert_all_layers_to_xshard.py ^
  "C:\path\to\model.gguf" ^
  --out "E:\models\output" ^
  --fp16 ^
  --resume
```

---

## 📊 Conversion Statistics

| Metric | Value |
|--------|-------|
| Source GGUF | ~12 GB |
| Output size | 73 GB |
| Total files | 168 .xshard files |
| Layers | 24 |
| Files per layer | 7 (4 attention + 3 expert) |
| Attention format | fp16 |
| Expert format | fp32 (dequantized from MXFP4) |

---

## 🔧 Command Reference

### Server Commands

```bash
# Basic startup
py.exe scripts/nnc_k_server.py --model-dir <path> --port 1235

# With custom settings
py.exe scripts/nnc_k_server.py ^
  --model-dir "E:\models\GPT-OSS\hf" ^
  --port 1235 ^
  --layers 24 ^
  --n-ctx 2048 ^
  --top-k-experts 8 ^
  --use-gpu 1 ^
  --verify-with-sidecars
```

### Conversion Commands

```bash
# Full conversion with fp16 attention weights
py.exe scripts/convert_all_layers_to_xshard.py ^
  <gguf_path> ^
  --out <output_dir> ^
  --fp16

# Resume interrupted conversion
py.exe scripts/convert_all_layers_to_xshard.py ^
  <gguf_path> ^
  --out <output_dir> ^
  --resume

# Convert specific layers only
py.exe scripts/convert_all_layers_to_xshard.py ^
  <gguf_path> ^
  --out <output_dir> ^
  --layers 0,1,2,3

# Attention weights only
py.exe scripts/convert_all_layers_to_xshard.py ^
  <gguf_path> ^
  --out <output_dir> ^
  --attention-only

# Expert weights only
py.exe scripts/convert_all_layers_to_xshard.py ^
  <gguf_path> ^
  --out <output_dir> ^
  --experts-only
```

---

## 🐛 Troubleshooting

### Memory Errors During Conversion

**Symptom**: `numpy._core._exceptions._ArrayMemoryError`

**Solution**:
1. Close other applications to free RAM
2. Use `--resume` flag to continue from last successful layer
3. Convert in smaller batches: `--layers 0,1,2,3,4,5`

### Server Not Responding

**Check**:
```bash
# Verify model files exist
dir "E:\models\GPT-OSS\hf\layer_00"

# Check server logs
# (Server outputs to console on startup)

# Test health endpoint
curl http://localhost:1235/health
```

### Missing Expert Files

**Symptom**: Layer directories only have 4 files (q, k, v, o)

**Solution**:
```bash
# Run expert-only conversion
py.exe scripts/convert_all_layers_to_xshard.py ^
  <gguf_path> ^
  --out <output_dir> ^
  --experts-only ^
  --resume
```

---

## 📝 API Endpoints

### GET /health

```json
{
  "status": "ok",
  "model": "gpt-oss-20b-MXFP4.gguf",
  "error": null
}
```

### GET /v1/models

```json
{
  "object": "list",
  "data": [
    {
      "id": "gpt-oss-20b-MXFP4.gguf",
      "object": "model"
    }
  ]
}
```

### POST /v1/chat/completions

**Request**:
```json
{
  "messages": [
    {"role": "system", "content": "You are a helpful assistant."},
    {"role": "user", "content": "Hello!"}
  ],
  "max_tokens": 100,
  "temperature": 0.7
}
```

**Response**:
```json
{
  "id": "chatcmpl-<uuid>",
  "object": "chat.completion",
  "created": <timestamp>,
  "model": "gpt-oss-20b-MXFP4.gguf",
  "choices": [{
    "index": 0,
    "message": {"role": "assistant", "content": "..."},
    "finish_reason": "stop"
  }],
  "usage": {
    "prompt_tokens": 10,
    "completion_tokens": 50,
    "total_tokens": 60
  },
  "timings": {
    "first_token_ms": 150.5,
    "per_token_ms": [45.2, 43.1, ...],
    "total_s": 2.5,
    "tps": 20.0
  }
}
```

### POST /shutdown

Gracefully shuts down the server.

---

## 🔐 Authority Boundaries

### Conversion Scripts
- ✅ Emit candidate `.xshard` files only
- ❌ Do **not** create, update, merge, or promote micronauts
- ❌ Do **not** modify existing model files

### Server
- ✅ Reads from unified tree structure
- ✅ Supports hot/cold weight paging
- ⚠️ MoE compute path is placeholder (expert routing implemented, GEMM pending)

### Sidecars
- ✅ `asx_ram_v2.exe`: Full attention tile processing
- ⚠️ `asx_gemm.exe`: Specialized GEMM for selected matrices (not complete BLAS)

---

## 📚 Related Documentation

| Document | Location | Purpose |
|----------|----------|---------|
| `ARCHITECTURE_FORWARD_PASS.md` | `.NNC-K/` | **Core**: HF forward pass ↔ K'UHUL semantic fold mapping |
| `HF_FORWARD_INTEGRATION.md` | `.NNC-K/` | MoE forward pass implementation plan |
| `MOE_FORWARD_SUMMARY.md` | `.NNC-K/` | Quick reference for MoE implementation |
| `CONVERSION_NOTES.md` | `scripts/` | Conversion workflow and tips |
| `CONVERSION_COMPLETE.md` | `E:/models/GPT-OSS/hf/` | Specific conversion summary |
| `README.md` | `.NNC-K/` | Project overview |
| `CHANGELOG.md` | `.NNC-K/` | Version history |

---

## 🎯 Next Steps / TODO

### Immediate
- [ ] Clean up staging directories (`attention_staging`, `experts_staging`)
- [ ] Test full generation loop with longer prompts
- [ ] Profile memory usage during inference

### Short-term
- [x] Complete MoE GEMM integration in `asx_gemm.exe` (HF forward pass)
- [ ] Implement proper expert routing (currently hash-based placeholder)
- [ ] Add KV cache persistence across requests

### Long-term
- [ ] Multi-GPU support for expert parallelism
- [ ] Quantization-aware inference (INT8/INT4)
- [ ] Streaming responses for long generations

---

## 📞 Contact / Support

**Project**: NNC-K (Neural Network Compiler - Kernel)
**Location**: `<repo-root>/`
**Model Output**: `E:/models/GPT-OSS/hf/`

For issues or questions, refer to:
1. This BOSS Guide
2. `scripts/CONVERSION_NOTES.md`
3. Server logs (console output)
4. Conversion report: `E:/models/GPT-OSS/hf/conversion_report.json`

---

## Component Status

| Component | Status | Notes |
|-----------|--------|-------|
| **Runtime Core** | ✅ Stable | NNC-K server, weight pager, KV cache |
| **Attention Sidecars** | ✅ Stable | `asx_ram_v2.exe`, Q/K/V/O processing |
| **`.xshard` Pipeline** | ✅ Stable | Conversion, hot/cold lanes, 168 files |
| **MoE Forward (HF-style)** | 🚧 Active | `asx_gemm_hf_forward.exe` in development |
| **Expert Routing** | 🔄 Partial | Hash-based placeholder → full router |
| **CHEESE Integration** | 📋 Planned | Post-forward evaluation |
| **`@flux` Lineage** | 📋 Planned | Execution trace recording |
| **BossPromotion** | 📋 Planned | Promotion decisions |

---

**Last Updated**: 2026-07-28
**Version**: 1.0
