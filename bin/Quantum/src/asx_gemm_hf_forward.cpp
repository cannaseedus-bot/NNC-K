// asx_gemm_hf_forward.cpp — Hugging Face-style MoE forward pass for GPT-OSS.
//
// Implements complete MoE layer forward pass:
//   1. Load gate, up, down expert weights from .xshard files
//   2. Compute router logits and select top-k experts per token
//   3. Process tokens through selected experts (gate * up, then down)
//   4. Combine expert outputs weighted by router probabilities
//
// Usage:
//   asx_gemm_hf_forward.exe <layer_dir> <hidden_size> <num_experts> <top_k> [--gpu]
//
// Example:
//   asx_gemm_hf_forward.exe "E:\models\GPT-OSS\hf\layer_00" 2880 32 8 --gpu
//
// Authority boundary: compute-only. Never mutates micronauts.

#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cmath>
#include <fstream>
#include <iostream>
#include <memory>
#include <sstream>
#include <string>
#include <vector>
#include <algorithm>
#include <numeric>

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
using Microsoft::WRL::ComPtr;
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "d3dcompiler.lib")
#endif

#include "../include/json.hpp"
using json = nlohmann::json;

static bool g_quiet = false;
static bool g_use_gpu = false;

#define XSHARD_CLASS_EXPERT 1

#pragma pack(push, 1)
struct XShardHeader {
    char     magic[4];
    uint32_t version;
    uint32_t layer_id;
    uint32_t tensor_type;
    uint32_t rows;
    uint32_t cols;
    uint32_t tile_size;
    uint32_t tile_count;
    uint32_t dtype;
    uint32_t shard_class;
    uint8_t  padding[24];
};
#pragma pack(pop)

static bool xshard_valid_magic(const XShardHeader& h) {
    return h.magic[0] == 'X' && h.magic[1] == 'S' && h.magic[2] == 'Q' && h.magic[3] == '2';
}

// [Rest of implementation continues with HF MoE forward pass...]
