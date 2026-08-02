// quantum_trinity_asx_ram_v2.cpp
// Config-driven D3D11 streaming attention sidecar for GPT-OSS / NNC-K .xshard
// Reads model_config.json for hidden_size, head_dim, num_heads, num_kv_heads.
// Generates HLSL kernels with the correct D_MODEL / SEQ constants.
//
// Authority boundary: compute-only. Never mutates micronaut registry.

#include "json.hpp"
using json = nlohmann::json;

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cstdint>
#include <cmath>
#include <chrono>
#include <vector>
#include <string>
#include <fstream>
#include <sstream>
#include <future>
#include <iostream>

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#include <wrl/client.h>
#include <d3d11.h>
#include <d3dcompiler.h>
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "d3dcompiler.lib")
using Microsoft::WRL::ComPtr;
#endif

bool g_quiet = false;

// ── xshard header ───────────────────────────────────────────────────────────

#define XSHARD_CLASS_ATTENTION 0
#define XSHARD_CLASS_EXPERT    1
#define XSHARD_CLASS_EMBEDDING 2
#define XSHARD_CLASS_GENERIC   3

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

static const char* xshard_class_name(uint32_t cls) {
    switch (cls) {
        case XSHARD_CLASS_ATTENTION: return "attention";
        case XSHARD_CLASS_EXPERT:    return "expert";
        case XSHARD_CLASS_EMBEDDING: return "embedding";
        case XSHARD_CLASS_GENERIC:   return "generic";
        default:                     return "unknown";
    }
}

static bool xshard_valid_magic(const XShardHeader& h) {
    return h.magic[0] == 'X' && h.magic[1] == 'S' && h.magic[2] == 'Q' && h.magic[3] == '2';
}

static uint64_t xshard_tile_bytes(const XShardHeader& h) {
    uint32_t elem_bytes = (h.dtype == 0) ? 4 : (h.dtype == 1) ? 2 : 1;
    uint64_t raw = (uint64_t)h.tile_size * elem_bytes;
    return ((raw + 4095) / 4096) * 4096;
}

static uint64_t xshard_tile_offset(const XShardHeader& h, uint32_t tile) {
    return 64 + (uint64_t)tile * xshard_tile_bytes(h);
}

// ── shard file ────────────────────────────────────────────────────────────────

struct ShardFile {
    FILE* fp = nullptr;
    XShardHeader hdr{};
    bool open(const char* path) {
        fp = fopen(path, "rb");
        if (!fp) { fprintf(stderr, "[asx_ram_v2] cannot open %s\n", path); return false; }
        if (fread(&hdr, 1, 64, fp) != 64) { fclose(fp); fp = nullptr; return false; }
        if (!xshard_valid_magic(hdr)) {
            fprintf(stderr, "[asx_ram_v2] bad magic in %s\n", path);
            fclose(fp); fp = nullptr; return false;
        }
        return true;
    }
    bool read_tile(uint32_t t, float* buf) {
        uint64_t off = xshard_tile_offset(hdr, t);
        if (fseek(fp, (long)off, SEEK_SET) != 0) return false;
        uint32_t elem_bytes = (hdr.dtype == 0) ? 4 : (hdr.dtype == 1) ? 2 : 1;
        uint64_t want = (uint64_t)hdr.tile_size * elem_bytes;
        if (elem_bytes == 4) {
            return fread(buf, 1, want, fp) == want;
        }
        std::vector<uint8_t> tmp(want);
        if (fread(tmp.data(), 1, want, fp) != want) return false;
        if (elem_bytes == 2) {
            for (uint32_t i = 0; i < hdr.tile_size; i++) buf[i] = ((uint16_t*)tmp.data())[i] / 65535.0f;
        } else {
            for (uint32_t i = 0; i < hdr.tile_size; i++) buf[i] = tmp[i] / 255.0f;
        }
        return true;
    }
    ~ShardFile() { if (fp) fclose(fp); }
};

// ── D3D11 helpers ─────────────────────────────────────────────────────────────

#ifdef _WIN32
#define CHK(hr, label) if (FAILED(hr)) { fprintf(stderr, "[asx_ram_v2] %s hr=0x%08X\n", label, (unsigned)hr); return false; }

struct GPUResources {
    ComPtr<ID3D11Device> dev;
    ComPtr<ID3D11DeviceContext> ctx;
    ComPtr<ID3D11ComputeShader> softmax_cs;
    ComPtr<ID3D11ComputeShader> vmul_cs;
    ComPtr<ID3D11Buffer> q_buf, k_buf, v_buf, p_buf, out_buf;
    ComPtr<ID3D11ShaderResourceView> q_srv, k_srv, v_srv, p_srv;
    ComPtr<ID3D11UnorderedAccessView> p_uav, out_uav;

    bool init(uint32_t rows, uint32_t cols) {
        UINT flags = D3D11_CREATE_DEVICE_DISABLE_GPU_TIMEOUT;
        D3D_FEATURE_LEVEL level;
        HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags,
                                       nullptr, 0, D3D11_SDK_VERSION, dev.GetAddressOf(),
                                       &level, ctx.GetAddressOf());
        if (FAILED(hr)) {
            hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, flags,
                                  nullptr, 0, D3D11_SDK_VERSION, dev.GetAddressOf(),
                                  &level, ctx.GetAddressOf());
        }
        CHK(hr, "D3D11CreateDevice");

        uint32_t tile_elems = rows * cols;
        q_buf = make_buf(tile_elems * 4, D3D11_USAGE_DEFAULT, D3D11_BIND_SHADER_RESOURCE, 0);
        k_buf = make_buf(tile_elems * 4, D3D11_USAGE_DEFAULT, D3D11_BIND_SHADER_RESOURCE, 0);
        v_buf = make_buf(tile_elems * 4, D3D11_USAGE_DEFAULT, D3D11_BIND_SHADER_RESOURCE, 0);
        p_buf  = make_buf(rows * rows * 4, D3D11_USAGE_DEFAULT,
                          D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_UNORDERED_ACCESS, 0);
        out_buf = make_buf(tile_elems * 4, D3D11_USAGE_DEFAULT,
                           D3D11_BIND_UNORDERED_ACCESS, 0);

        q_srv = make_srv(q_buf.Get(), tile_elems);
        k_srv = make_srv(k_buf.Get(), tile_elems);
        v_srv = make_srv(v_buf.Get(), tile_elems);
        p_srv = make_srv(p_buf.Get(), rows * rows);
        p_uav = make_uav(p_buf.Get(), rows * rows);
        out_uav = make_uav(out_buf.Get(), tile_elems);

        std::string softmax_src = build_softmax_hlsl(rows, cols);
        std::string vmul_src = build_vmul_hlsl(rows, cols);
        softmax_cs = compile_cs(softmax_src.c_str(), "softmax");
        vmul_cs = compile_cs(vmul_src.c_str(), "vmul");
        return true;
    }

    ComPtr<ID3D11Buffer> make_buf(UINT bytes, D3D11_USAGE usage, UINT bind, UINT cpu) {
        D3D11_BUFFER_DESC d{}; d.ByteWidth = bytes; d.Usage = usage; d.BindFlags = bind; d.CPUAccessFlags = cpu;
        ComPtr<ID3D11Buffer> b;
        CHK(dev->CreateBuffer(&d, nullptr, b.GetAddressOf()), "CreateBuffer");
        return b;
    }
    ComPtr<ID3D11ShaderResourceView> make_srv(ID3D11Buffer* b, UINT n) {
        D3D11_SHADER_RESOURCE_VIEW_DESC d{}; d.Format = DXGI_FORMAT_R32_FLOAT;
        d.ViewDimension = D3D11_SRV_DIMENSION_BUFFER; d.Buffer.NumElements = n;
        ComPtr<ID3D11ShaderResourceView> v;
        CHK(dev->CreateShaderResourceView(b, &d, v.GetAddressOf()), "SRV");
        return v;
    }
    ComPtr<ID3D11UnorderedAccessView> make_uav(ID3D11Buffer* b, UINT n) {
        D3D11_UNORDERED_ACCESS_VIEW_DESC d{}; d.Format = DXGI_FORMAT_R32_FLOAT;
        d.ViewDimension = D3D11_UAV_DIMENSION_BUFFER; d.Buffer.NumElements = n;
        ComPtr<ID3D11UnorderedAccessView> v;
        CHK(dev->CreateUnorderedAccessView(b, &d, v.GetAddressOf()), "UAV");
        return v;
    }
    ComPtr<ID3D11ComputeShader> compile_cs(const char* src, const char* name) {
        ComPtr<ID3DBlob> blob, errs;
        HRESULT hr = D3DCompile(src, strlen(src), name, nullptr, nullptr, "main", "cs_5_0",
                               D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, blob.GetAddressOf(), errs.GetAddressOf());
        if (FAILED(hr)) {
            if (errs) fprintf(stderr, "%s\n", (char*)errs->GetBufferPointer());
            exit(1);
        }
        ComPtr<ID3D11ComputeShader> cs;
        CHK(dev->CreateComputeShader(blob->GetBufferPointer(), blob->GetBufferSize(), nullptr, cs.GetAddressOf()), "CreateCS");
        return cs;
    }

    std::string build_softmax_hlsl(uint32_t rows, uint32_t cols) {
        float scale = 1.0f / sqrtf((float)cols);
        std::ostringstream s;
        s << "#define D_MODEL " << cols << "u\n";
        s << "#define SEQ " << rows << "u\n";
        s << "#define SCALE " << scale << "f\n";
        s << "Buffer<float> Q : register(t0);\n";
        s << "Buffer<float> K : register(t1);\n";
        s << "RWBuffer<float> Out : register(u0);\n";
        s << "groupshared float smax[SEQ];\n";
        s << "groupshared float ssum[SEQ];\n";
        s << "[numthreads(SEQ, 1, 1)]\n";
        s << "void main(uint3 gid : SV_GroupID, uint3 tid : SV_GroupThreadID) {\n";
        s << "  uint row = gid.x; uint col = tid.x;\n";
        s << "  double s = 0.0;\n";
        s << "  for (uint i = 0u; i < D_MODEL; i++)\n";
        s << "    s += (double)Q[row * D_MODEL + i] * (double)K[col * D_MODEL + i] * (double)SCALE;\n";
        s << "  smax[col] = (float)s;\n";
        s << "  GroupMemoryBarrierWithGroupSync();\n";
        s << "  for (uint stride = SEQ/2u; stride > 0u; stride >>= 1u) {\n";
        s << "    if (col < stride) smax[col] = max(smax[col], smax[col+stride]);\n";
        s << "    GroupMemoryBarrierWithGroupSync();\n";
        s << "  }\n";
        s << "  float m = smax[0];\n";
        s << "  GroupMemoryBarrierWithGroupSync();\n";
        s << "  float e = exp(s - m);\n";
        s << "  ssum[col] = e;\n";
        s << "  GroupMemoryBarrierWithGroupSync();\n";
        s << "  for (uint stride = SEQ/2u; stride > 0u; stride >>= 1u) {\n";
        s << "    if (col < stride) ssum[col] += ssum[col+stride];\n";
        s << "    GroupMemoryBarrierWithGroupSync();\n";
        s << "  }\n";
        s << "  Out[row * SEQ + col] = e / ssum[0];\n";
        s << "}\n";
        return s.str();
    }

    std::string build_vmul_hlsl(uint32_t rows, uint32_t cols) {
        std::ostringstream s;
        s << "#define SEQ " << rows << "u\n";
        s << "#define D_MODEL " << cols << "u\n";
        s << "Buffer<float> P : register(t0);\n";
        s << "Buffer<float> V : register(t1);\n";
        s << "RWBuffer<float> Out : register(u0);\n";
        s << "[numthreads(8, 8, 1)]\n";
        s << "void main(uint3 id : SV_DispatchThreadID) {\n";
        s << "  uint row = id.x; uint col = id.y;\n";
        s << "  if (row >= SEQ || col >= D_MODEL) return;\n";
        s << "  double s = 0.0;\n";
        s << "  for (uint k = 0u; k < SEQ; k++)\n";
        s << "    s += (double)P[row * SEQ + k] * (double)V[k * D_MODEL + col];\n";
        s << "  Out[row * D_MODEL + col] = (float)s;\n";
        s << "}\n";
        return s.str();
    }
};
#else
struct GPUResources { bool init(uint32_t,uint32_t){return false;} };
#endif

// ── config ────────────────────────────────────────────────────────────────────

struct ModelConfig {
    uint32_t n_layers = 24;
    uint32_t hidden_size = 2880;
    uint32_t num_heads = 64;
    uint32_t num_kv_heads = 8;
    uint32_t head_dim = 64;
    uint32_t seq_len = 64;  // rows per tile

    bool load(const char* path) {
        std::ifstream f(path);
        if (!f.is_open()) return false;
        json j; try { f >> j; } catch(...) { return false; }
        n_layers = j.value("n_layers", n_layers);
        hidden_size = j.value("hidden_size", hidden_size);
        num_heads = j.value("num_attention_heads", num_heads);
        num_kv_heads = j.value("num_key_value_heads", num_kv_heads);
        head_dim = j.value("head_dim", head_dim);
        seq_len = j.value("seq_len", head_dim);
        return true;
    }
};

// ── CPU reference ─────────────────────────────────────────────────────────────

static void cpu_softmax(const float* q, const float* k, float* p, uint32_t rows, uint32_t cols) {
    float scale = 1.0f / sqrtf((float)cols);
    // Compute scaled scores in double precision to match GPU and avoid order-dependent fp32 error.
    for (uint32_t r = 0; r < rows; r++) {
        for (uint32_t c = 0; c < rows; c++) {
            double s = 0.0;
            for (uint32_t i = 0; i < cols; i++)
                s += (double)q[r * cols + i] * (double)k[c * cols + i] * (double)scale;
            p[r * rows + c] = (float)s;
        }
    }
    // Stable softmax (max-subtracted) matching HLSL groupshared reduction.
    for (uint32_t r = 0; r < rows; r++) {
        float buf[64];
        for (uint32_t c = 0; c < rows; c++) buf[c] = p[r * rows + c];
        for (uint32_t stride = rows / 2; stride > 0; stride >>= 1) {
            for (uint32_t c = 0; c < stride; c++)
                buf[c] = (buf[c] > buf[c + stride]) ? buf[c] : buf[c + stride];
        }
        float m = buf[0];
        float sum = 0;
        for (uint32_t c = 0; c < rows; c++) {
            p[r * rows + c] = expf(p[r * rows + c] - m);
            sum += p[r * rows + c];
        }
        for (uint32_t c = 0; c < rows; c++) p[r * rows + c] /= sum;
    }
}

static void cpu_vmul(const float* p, const float* v, float* out, uint32_t rows, uint32_t cols) {
    for (uint32_t r = 0; r < rows; r++)
        for (uint32_t c = 0; c < cols; c++) {
            double s = 0.0;
            for (uint32_t k = 0; k < rows; k++)
                s += (double)p[r * rows + k] * (double)v[k * cols + c];
            out[r * cols + c] = (float)s;
        }
}

// ── main ─────────────────────────────────────────────────────────────────────

int main(int argc, char** argv) {
    if (argc < 4) {
        fprintf(stderr,
            "Usage: asx_ram_v2.exe <q_shard> <k_shard> <v_shard> [config.json] [passes] [--prefetch]\n"
            "  config.json defaults to model_config.json next to the shards\n");
        return 1;
    }
    const char* q_path = argv[1];
    const char* k_path = argv[2];
    const char* v_path = argv[3];

    std::string config_path = "model_config.json";
    int arg_idx = 4;
    if (arg_idx < argc && strstr(argv[arg_idx], ".json")) {
        config_path = argv[arg_idx++];
    }
    int passes = 1;
    bool prefetch = false;
    for (int i = arg_idx; i < argc; i++) {
        std::string a = argv[i];
        if (a == "--prefetch") prefetch = true;
        else passes = atoi(argv[i]);
    }

    ModelConfig cfg;
    if (!cfg.load(config_path.c_str())) {
        fprintf(stderr, "[asx_ram_v2] warning: could not load %s, using GPT-OSS 20B defaults\n", config_path.c_str());
    }

    ShardFile q_shard, k_shard, v_shard;
    if (!q_shard.open(q_path)) return 1;
    if (!k_shard.open(k_path)) return 1;
    if (!v_shard.open(v_path)) return 1;

    if (q_shard.hdr.rows != cfg.head_dim || q_shard.hdr.cols != cfg.hidden_size) {
        fprintf(stderr, "[asx_ram_v2] shard shape %ux%u does not match config head_dim=%u hidden=%u\n",
                q_shard.hdr.rows, q_shard.hdr.cols, cfg.head_dim, cfg.hidden_size);
    }

    const uint64_t HOT_SWAP_MAX_BYTES = 2ULL * 1024 * 1024 * 1024;
    auto classify = [&](ShardFile& s, const char* name) -> bool {
        uint64_t file_bytes = (uint64_t)s.hdr.tile_count * xshard_tile_bytes(s.hdr);
        if (s.hdr.shard_class == XSHARD_CLASS_EXPERT && file_bytes > HOT_SWAP_MAX_BYTES) {
            fprintf(stderr, "[asx_ram_v2] %s is expert shard (%.1f GB) > 2GB hot-swap limit; use cold lane\n",
                    name, file_bytes / (1024.0 * 1024.0 * 1024.0));
            return false;
        }
        if (s.hdr.shard_class == XSHARD_CLASS_EMBEDDING) {
            if (!g_quiet) printf("[asx_ram_v2] %s is embedding table (%.1f MB) — load-once, not hot-swapped\n",
                                 name, file_bytes / (1024.0 * 1024.0));
        }
        if (!g_quiet) printf("[asx_ram_v2] %s class=%s file_bytes=%llu\n",
                             name, xshard_class_name(s.hdr.shard_class), (unsigned long long)file_bytes);
        return true;
    };
    if (!classify(q_shard, "q")) return 1;
    if (!classify(k_shard, "k")) return 1;
    if (!classify(v_shard, "v")) return 1;

    uint32_t tile_count = q_shard.hdr.tile_count;
    uint32_t tile_elems = q_shard.hdr.tile_size;
    uint32_t rows = q_shard.hdr.rows;
    uint32_t cols = q_shard.hdr.cols;

    if (!g_quiet) {
        printf("[asx_ram_v2] q: %s\n[asx_ram_v2] k: %s\n[asx_ram_v2] v: %s\n", q_path, k_path, v_path);
        printf("[asx_ram_v2] config: hidden=%u heads=%u kv=%u head_dim=%u\n",
               cfg.hidden_size, cfg.num_heads, cfg.num_kv_heads, cfg.head_dim);
        printf("[asx_ram_v2] heads per layer: %u  tile: %ux%u (%u floats)\n\n",
               tile_count, rows, cols, tile_elems);
    }

#ifdef _WIN32
    GPUResources gpu;
    if (!gpu.init(rows, cols)) {
        fprintf(stderr, "[asx_ram_v2] GPU init failed\n");
        return 1;
    }
#endif

    std::vector<float> q(tile_elems), k(tile_elems), v(tile_elems);
    std::vector<float> p(rows * rows), out(tile_elems);
    std::vector<float> ref_p(rows * rows), ref_out(tile_elems);

    using Clock = std::chrono::high_resolution_clock;
    auto t0 = Clock::now();
    int mismatches = 0;
    float max_err = 0;

    for (int pass = 0; pass < passes; pass++) {
        for (uint32_t head = 0; head < tile_count; head++) {
            uint32_t kv_head = (cfg.num_kv_heads > 0 && cfg.num_heads > 0)
                               ? ((head / (cfg.num_heads / cfg.num_kv_heads)) % cfg.num_kv_heads)
                               : head;
            auto t_read = Clock::now();
            if (!q_shard.read_tile(head, q.data())) { fprintf(stderr, "read Q head %u failed\n", head); return 1; }
            if (!k_shard.read_tile(kv_head, k.data())) { fprintf(stderr, "read K head %u (kv_head %u) failed\n", head, kv_head); return 1; }
            if (!v_shard.read_tile(kv_head, v.data())) { fprintf(stderr, "read V head %u (kv_head %u) failed\n", head, kv_head); return 1; }
            float disk_ms = std::chrono::duration<float, std::milli>(Clock::now() - t_read).count();

            // CPU reference for validation
            cpu_softmax(q.data(), k.data(), ref_p.data(), rows, cols);
            cpu_vmul(ref_p.data(), v.data(), ref_out.data(), rows, cols);

#ifdef _WIN32
            // Upload Q/K/V
            gpu.ctx->UpdateSubresource(gpu.q_buf.Get(), 0, nullptr, q.data(), 0, 0);
            gpu.ctx->UpdateSubresource(gpu.k_buf.Get(), 0, nullptr, k.data(), 0, 0);
            gpu.ctx->UpdateSubresource(gpu.v_buf.Get(), 0, nullptr, v.data(), 0, 0);

            // Softmax dispatch: rows groups, 64 threads each
            gpu.ctx->CSSetShader(gpu.softmax_cs.Get(), nullptr, 0);
            ID3D11ShaderResourceView* srvs[] = { gpu.q_srv.Get(), gpu.k_srv.Get() };
            gpu.ctx->CSSetShaderResources(0, 2, srvs);
            gpu.ctx->CSSetUnorderedAccessViews(0, 1, gpu.p_uav.GetAddressOf(), nullptr);
            gpu.ctx->Dispatch(rows, 1, 1);

            // V-mul dispatch
            gpu.ctx->CSSetShader(gpu.vmul_cs.Get(), nullptr, 0);
            ID3D11ShaderResourceView* vsrvs[] = { gpu.p_srv.Get(), gpu.v_srv.Get() };
            gpu.ctx->CSSetShaderResources(0, 2, vsrvs);
            gpu.ctx->CSSetUnorderedAccessViews(0, 1, gpu.out_uav.GetAddressOf(), nullptr);
            gpu.ctx->Dispatch((rows + 7) / 8, (cols + 7) / 8, 1);

            // Readback
            ComPtr<ID3D11Buffer> readback = gpu.make_buf(tile_elems * 4, D3D11_USAGE_STAGING, 0, D3D11_CPU_ACCESS_READ);
            gpu.ctx->CopyResource(readback.Get(), gpu.out_buf.Get());
            D3D11_MAPPED_SUBRESOURCE map{};
            CHK(gpu.ctx->Map(readback.Get(), 0, D3D11_MAP_READ, 0, &map), "Map");
            memcpy(out.data(), map.pData, tile_elems * 4);
            gpu.ctx->Unmap(readback.Get(), 0);
#endif

            // Validate
            float head_max_err = 0;
            for (uint32_t i = 0; i < tile_elems; i++) {
                float e = fabsf(out[i] - ref_out[i]);
                if (e > head_max_err) head_max_err = e;
            }
            if (head_max_err > max_err) max_err = head_max_err;
            if (head_max_err > 1e-2f) mismatches++;

            if (!g_quiet) {
                printf("[asx_ram_v2]  pass %d  head %3u  disk_ms %6.2f  max_err %.2e\n",
                       pass, head, disk_ms, head_max_err);
            }
        }
    }

    auto total_ms = std::chrono::duration<float, std::milli>(Clock::now() - t0).count();
    if (!g_quiet) {
        printf("\n[asx_ram_v2] total heads  : %d\n", tile_count * passes);
        printf("[asx_ram_v2] total time   : %.1f ms\n", total_ms);
        printf("[asx_ram_v2] heads/sec    : %.1f\n", (tile_count * passes) / (total_ms / 1000.0f));
        printf("[asx_ram_v2] max_err      : %.2e  (tol=1e-02)\n", max_err);
        printf("[asx_ram_v2] mismatches   : %d\n", mismatches);
        printf("[asx_ram_v2] %s\n", mismatches == 0 ? "PASS" : "FAIL");
    }

    json response;
    response["status"] = mismatches == 0 ? "success" : "validation_failed";
    response["heads"] = tile_count * passes;
    response["max_error"] = max_err;
    response["mismatches"] = mismatches;
    response["authority_boundary"] = "compute_only";
    std::cout << response.dump(2) << std::endl;
    return mismatches > 0 ? 2 : 0;
}
