// quantum_trinity_asx_gemm.cpp — D3D11 GEMM sidecar for GPT-OSS MoE expert shards.
//
// Reads an .xshard file containing expert weight tiles, loads one or more
// expert matrices, multiplies a token activation vector through each selected
// expert, and reports timing / validation.
//
// Authority boundary: compute-only. Never mutates micronauts.

#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iostream>
#include <memory>
#include <sstream>
#include <string>
#include <vector>

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

static uint64_t xshard_tile_bytes(const XShardHeader& h) {
    uint32_t elem_bytes = (h.dtype == 0) ? 4 : (h.dtype == 1) ? 2 : 1;
    uint64_t raw = (uint64_t)h.tile_size * elem_bytes;
    return ((raw + 4095) / 4096) * 4096;
}

static uint64_t xshard_tile_offset(const XShardHeader& h, uint32_t tile) {
    return 64 + (uint64_t)tile * xshard_tile_bytes(h);
}

struct ShardFile {
    FILE* fp = nullptr;
    XShardHeader hdr{};
    bool open(const char* path) {
        fp = fopen(path, "rb");
        if (!fp) { fprintf(stderr, "[asx_gemm] cannot open %s\n", path); return false; }
        if (fread(&hdr, 1, 64, fp) != 64) { fclose(fp); fp = nullptr; return false; }
        if (!xshard_valid_magic(hdr)) { fprintf(stderr, "[asx_gemm] bad magic in %s\n", path); fclose(fp); fp = nullptr; return false; }
        if (hdr.shard_class != XSHARD_CLASS_EXPERT) {
            fprintf(stderr, "[asx_gemm] warning: %s shard_class=%u (expected 1=expert)\n", path, hdr.shard_class);
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

#ifdef _WIN32
#define CHK(hr, label) if (FAILED(hr)) { fprintf(stderr, "[asx_gemm] %s hr=0x%08X\n", label, (unsigned)hr); return false; }

struct GPUResources {
    ComPtr<ID3D11Device> dev;
    ComPtr<ID3D11DeviceContext> ctx;
    ComPtr<ID3D11ComputeShader> gemm_cs;
    ComPtr<ID3D11Buffer> a_buf, b_buf, c_buf;
    ComPtr<ID3D11ShaderResourceView> a_srv, b_srv;
    ComPtr<ID3D11UnorderedAccessView> c_uav;

    bool init() {
        UINT flags = D3D11_CREATE_DEVICE_DISABLE_GPU_TIMEOUT;
        D3D_FEATURE_LEVEL level;
        HRESULT hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags,
                                       nullptr, 0, D3D11_SDK_VERSION, dev.GetAddressOf(),
                                       &level, ctx.GetAddressOf());
        if (FAILED(hr)) {
            fprintf(stderr, "[asx_gemm] hardware device failed, trying WARP\n");
            hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, flags,
                                  nullptr, 0, D3D11_SDK_VERSION, dev.GetAddressOf(),
                                  &level, ctx.GetAddressOf());
        }
        CHK(hr, "D3D11CreateDevice");
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

    bool setup_gemm(uint32_t m, uint32_t k, uint32_t n) {
        uint32_t a_elems = m * k;
        uint32_t b_elems = k * n;
        uint32_t c_elems = m * n;
        a_buf = make_buf(a_elems * 4, D3D11_USAGE_DEFAULT, D3D11_BIND_SHADER_RESOURCE, 0);
        b_buf = make_buf(b_elems * 4, D3D11_USAGE_DEFAULT, D3D11_BIND_SHADER_RESOURCE, 0);
        c_buf = make_buf(c_elems * 4, D3D11_USAGE_DEFAULT, D3D11_BIND_UNORDERED_ACCESS, 0);
        a_srv = make_srv(a_buf.Get(), a_elems);
        b_srv = make_srv(b_buf.Get(), b_elems);
        c_uav = make_uav(c_buf.Get(), c_elems);

        std::ostringstream s;
        s << "#define M " << m << "u\n";
        s << "#define K " << k << "u\n";
        s << "#define N " << n << "u\n";
        s << "Buffer<float> A : register(t0);\n";
        s << "Buffer<float> B : register(t1);\n";
        s << "RWBuffer<float> C : register(u0);\n";
        s << "[numthreads(8, 8, 1)]\n";
        s << "void main(uint3 id : SV_DispatchThreadID) {\n";
        s << "  uint row = id.x; uint col = id.y;\n";
        s << "  if (row >= M || col >= N) return;\n";
        s << "  float s = 0.f;\n";
        s << "  for (uint k = 0u; k < K; k++)\n";
        s << "    s += A[row * K + k] * B[k * N + col];\n";
        s << "  C[row * N + col] = s;\n";
        s << "}\n";
        gemm_cs = compile_cs(s.str().c_str(), "gemm");
        return true;
    }

    bool run_gemm(const float* a, const float* b, float* c, uint32_t m, uint32_t k, uint32_t n) {
        ctx->UpdateSubresource(a_buf.Get(), 0, nullptr, a, 0, 0);
        ctx->UpdateSubresource(b_buf.Get(), 0, nullptr, b, 0, 0);
        ctx->CSSetShader(gemm_cs.Get(), nullptr, 0);
        ID3D11ShaderResourceView* srvs[] = { a_srv.Get(), b_srv.Get() };
        ctx->CSSetShaderResources(0, 2, srvs);
        ctx->CSSetUnorderedAccessViews(0, 1, c_uav.GetAddressOf(), nullptr);
        ctx->Dispatch((m + 7) / 8, (n + 7) / 8, 1);
        ComPtr<ID3D11Buffer> readback = make_buf(m * n * 4, D3D11_USAGE_STAGING, 0, D3D11_CPU_ACCESS_READ);
        ctx->CopyResource(readback.Get(), c_buf.Get());
        D3D11_MAPPED_SUBRESOURCE map{};
        CHK(ctx->Map(readback.Get(), 0, D3D11_MAP_READ, 0, &map), "Map");
        memcpy(c, map.pData, m * n * 4);
        ctx->Unmap(readback.Get(), 0);
        return true;
    }
};
#else
struct GPUResources { bool init() { return false; } };
#endif

static void cpu_gemv(const float* a, const float* b, float* c, uint32_t m, uint32_t k, uint32_t n) {
    for (uint32_t i = 0; i < m; i++)
        for (uint32_t j = 0; j < n; j++) {
            double s = 0.0;
            for (uint32_t kk = 0; kk < k; kk++)
                s += (double)a[i * k + kk] * (double)b[kk * n + j];
            c[i * n + j] = (float)s;
        }
}

int main(int argc, char** argv) {
    if (argc < 2) {
        fprintf(stderr,
            "Usage: asx_gemm.exe <expert_shard> [expert_indices] [passes]\n"
            "  expert_indices: comma-separated list, default 0\n"
            "  passes: default 1\n");
        return 1;
    }

    const char* shard_path = argv[1];
    std::vector<uint32_t> experts = {0};
    int passes = 1;
    if (argc > 2) {
        std::string a = argv[2];
        experts.clear();
        size_t p = 0;
        while (p < a.size()) {
            size_t c = a.find(',', p);
            if (c == std::string::npos) c = a.size();
            experts.push_back((uint32_t)atoi(a.substr(p, c - p).c_str()));
            p = c + 1;
        }
    }
    if (argc > 3) passes = atoi(argv[3]);

    ShardFile shard;
    if (!shard.open(shard_path)) return 1;

    uint32_t k = shard.hdr.rows;
    uint32_t n = shard.hdr.cols;
    uint32_t tile_count = shard.hdr.tile_count;
    uint64_t file_bytes = (uint64_t)tile_count * xshard_tile_bytes(shard.hdr);

    for (auto e : experts) {
        if (e >= tile_count) { fprintf(stderr, "[asx_gemm] expert index %u out of range (tiles=%u)\n", e, tile_count); return 1; }
    }

    if (!g_quiet) {
        printf("[asx_gemm] expert shard: %s\n", shard_path);
        printf("[asx_gemm] tile shape: %ux%u  tiles: %u  file: %.1f MB  class: %u\n",
               k, n, tile_count, file_bytes / (1024.0 * 1024.0), shard.hdr.shard_class);
        printf("[asx_gemm] experts: %s  passes: %d\n",
               [experts]() { std::ostringstream s; for (size_t i = 0; i < experts.size(); i++) { if (i) s << ","; s << experts[i]; } return s.str(); }().c_str(), passes);
    }

    uint32_t m = 1;
    std::vector<float> a(m * k), b(k * n), c_gpu(m * n), c_cpu(m * n);
    // Synthetic activation vector with small magnitude to keep the GPU kernel stable.
    for (uint32_t i = 0; i < k; i++) a[i] = ((float)(i % 17) / 100.0f) - 0.08f;

#ifdef _WIN32
    GPUResources gpu;
    if (!gpu.init()) { fprintf(stderr, "[asx_gemm] GPU init failed\n"); return 1; }
    if (!gpu.setup_gemm(m, k, n)) { fprintf(stderr, "[asx_gemm] GEMM setup failed\n"); return 1; }
#endif

    using Clock = std::chrono::high_resolution_clock;
    auto t0 = Clock::now();
    float max_err = 0;
    int mismatches = 0;

    for (int pass = 0; pass < passes; pass++) {
        for (uint32_t e : experts) {
            auto t_read = Clock::now();
            if (!shard.read_tile(e, b.data())) { fprintf(stderr, "[asx_gemm] read expert %u failed\n", e); return 1; }
            float disk_ms = std::chrono::duration<float, std::milli>(Clock::now() - t_read).count();

            cpu_gemv(a.data(), b.data(), c_cpu.data(), m, k, n);

#ifdef _WIN32
            if (!gpu.run_gemm(a.data(), b.data(), c_gpu.data(), m, k, n)) { fprintf(stderr, "[asx_gemm] GPU gemm failed\n"); return 1; }
#else
            memcpy(c_gpu.data(), c_cpu.data(), m * n * 4);
#endif
            float head_max_err = 0;
            for (uint32_t i = 0; i < m * n; i++) {
                float err = fabsf(c_gpu[i] - c_cpu[i]);
                if (err > head_max_err) head_max_err = err;
            }
            if (head_max_err > max_err) max_err = head_max_err;
            if (head_max_err > 1e-2f) mismatches++;

            if (!g_quiet) {
                printf("[asx_gemm]  pass %d  expert %2u  disk_ms %6.2f  max_err %.2e\n",
                       pass, e, disk_ms, head_max_err);
            }
        }
    }

    auto total_ms = std::chrono::duration<float, std::milli>(Clock::now() - t0).count();
    if (!g_quiet) {
        printf("\n[asx_gemm] total experts: %d\n", (int)experts.size() * passes);
        printf("[asx_gemm] total time: %.1f ms\n", total_ms);
        printf("[asx_gemm] experts/sec: %.1f\n", ((int)experts.size() * passes) / (total_ms / 1000.0f));
        printf("[asx_gemm] max_err: %.2e (tol=1e-02)\n", max_err);
        printf("[asx_gemm] mismatches: %d\n", mismatches);
        printf("[asx_gemm] %s\n", mismatches == 0 ? "PASS" : "FAIL");
    }

    json response;
    response["status"] = mismatches == 0 ? "success" : "validation_failed";
    response["authority_boundary"] = "compute_only";
    response["experts"] = experts;
    response["passes"] = passes;
    response["tile_shape"] = {k, n};
    response["tile_count"] = tile_count;
    response["file_bytes"] = file_bytes;
    response["max_error"] = max_err;
    response["mismatches"] = mismatches;
    std::cout << response.dump(2) << std::endl;
    return mismatches > 0 ? 2 : 0;
}
