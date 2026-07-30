# MoE Forward Pass - Implementation Summary

## ✅ What's Ready

### Current Runtime Authority

#### Implemented
- **Forward compute**
  - Attention (Q/K/V/O) via `asx_ram_v2.exe`
  - MoE routing (hash-based placeholder)
  - Sidecars (D3D11 GEMM)
- **Pre-forward**
  - XCFE admission
  - Trinity semantic field
- **Post-forward**
  - Collapse (token sampling)
  - Persistence (`.learning/`)

#### Planned
- CollapseProof validation
- CHEESE wiring
- @flux recording
- BossPromotion integration
- Complete MoE forward pass (`asx_gemm_hf_forward.exe`)

## 🎯 Next Implementation Steps

### Step 1: Create `asx_gemm_hf_forward.cpp`

Based on the design in `HF_FORWARD_INTEGRATION.md`, implement:

```cpp
// Core components needed:
1. Load expert weights (gate, up, down) from .xshard files
2. Router network (linear projection + softmax)
3. Top-k expert selection
4. Expert processing (gate × up → activation → down)
5. Weighted combination by router probabilities
6. GPU acceleration via D3D11 compute shaders
```

### Step 2: Build the Sidecar

```bash
cd <repo-root>/bin/Quantum/build
cmake ..
cmake --build . --config Release --target asx_gemm_hf_forward
```

### Step 3: Test Standalone

```bash
# Single token test
asx_gemm_hf_forward.exe "E:\models\GPT-OSS\hf\layer_00" 2880 32 8

# Batch test with GPU
asx_gemm_hf_forward.exe "E:\models\GPT-OSS\hf\layer_00" 2880 32 8 --gpu --seq-len 512
```

### Step 4: Integrate with Server

Update `nnc_k_server.py`:
- Add `--use-moe-sidecar` flag
- Call sidecar in `layer_forward()` for MoE compute
- Handle token I/O between Python and sidecar

## 📊 Expected Performance

### GPT-OSS 20B (2880 hidden, 32 experts, top-8)

| Mode | Seq Len | Device | Tokens/sec | Latency |
|------|---------|--------|------------|---------|
| CPU  | 1       | CPU    | ~50        | 20 ms   |
| CPU  | 512     | CPU    | ~250       | 2000 ms |
| GPU  | 1       | GPU    | ~500       | 2 ms    |
| GPU  | 512     | GPU    | ~5000      | 100 ms  |

## 🔧 Key Files

| File | Purpose | Status |
|------|---------|--------|
| `HF_FORWARD_INTEGRATION.md` | Complete design spec | ✅ Done |
| `BOSS-Guide.md` | Updated reference | ✅ Done |
| `asx_gemm_hf_forward.cpp` | Implementation | 📝 To create |
| `nnc_k_server.py` | Server integration | 📝 To update |

## 🚀 Quick Start (Once Implemented)

```bash
# 1. Build the sidecar
cd <repo-root>/bin/Quantum
build.bat Release

# 2. Test standalone
asx_gemm_hf_forward.exe "E:\models\GPT-OSS\hf\layer_00" 2880 32 8 --gpu

# 3. Start server with MoE
py.exe scripts/nnc_k_server.py ^
  --model-dir "E:\models\GPT-OSS\hf" ^
  --port 1235 ^
  --use-moe-sidecar ^
  --use-gpu 1

# 4. Test chat
curl http://localhost:1235/v1/chat/completions ^
  -H "Content-Type: application/json" ^
  -d "{\"messages\": [{\"role\": \"user\", \"content\": \"Hello\"}], \"max_tokens\": 50}"
```

## 📈 Impact

Once the HF forward pass is fully implemented:

- ✅ **Complete MoE pipeline**: Router → Expert Selection → Compute → Combine
- ✅ **GPU acceleration**: 10-20x speedup for batch processing
- ✅ **Production ready**: Full GPT-OSS 20B inference with MoE
- ✅ **Scalable**: Foundation for expert parallelism and advanced routing

---

**Status**: Design complete, ready for implementation
**Priority**: High
**ETA**: 1-2 days for CPU, 3-5 days for GPU
