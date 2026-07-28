# NNC-K System Guide

## What NNC-K Is

NNC-K is a contract-driven neural runtime and development stack. It combines
Neural Net Code (NNC), K'UHUL symbolic execution, Micronaut actors, model
adapters, persistent traces, and native GPU compute. It is not one neural
network. It is the layer that describes, routes, executes, observes, and
governs multiple models and capabilities.

The core rule is:

```text
schemas and manifests declare
K'UHUL defines legal execution
Micronauts perform bounded capabilities
models perform inference
native sidecars perform compute
the runtime records and governs state
```

## Architecture

```text
WPF UI / HTTP / MCP / WebX
          |
          v
Manifests + schemas + registries
          |
          v
K'UHUL phases and NeuralGrammar.Core
          |
          v
Micronaut manager, workers, skills, and model routing
          |
     +----+----------------+
     |                     |
Python/model adapters   Native GPU sidecars
     |                     |
GGUF/Safetensors       .xshard / D3D11 compute
          \               /
           execution traces
           and .learning/
```

Primary surfaces:

- `micronaut-ui.ps1` — WPF chat, inspection, and runtime control.
- `src/NeuralGrammar.Core/` — C# grammar, validation, routing, memory, and
  execution services.
- `kuhul/` and `compiler/` — K'UHUL language, compiler, IR, runtime, tests,
  tools, and examples.
- `scripts/` and `tools/` — model servers, converters, trainers, validators,
  and orchestration helpers.
- `native/`, `shaders/`, and `bin/Quantum/src/` — DirectX compute, shader,
  geometry, trainer, and sidecar implementations.
- `schemas/`, `contracts/`, `registry/`, and root manifests — declared
  contracts and routing surfaces.

## K'UHUL and Micronauts

K'UHUL supplies deterministic phase semantics. The common lifecycle is
`Pop -> Wo -> Yax -> Sek -> Ch'en -> Xul`: establish context, transform or
route state, resolve it, execute a bounded action, persist an admitted result,
and close the cycle.

A Micronaut is an addressable runtime actor with a constrained capability,
state, evidence, and lifecycle. Micronauts may call models or tools, but model
output is only a candidate. Runtime validators and managers control mutations
and promotion.

## `.xshard`: Custom Hot-Swappable GPU Models

`.xshard` is NNC-K's 64-byte-header binary tile container. The `XSQ2` header
records version, layer, tensor type, shape, tile size/count, dtype, and shard
class. Users can convert or generate model-specific tiles from GGUF,
Safetensors, activations, or MoE experts, then swap compatible shards without
loading an entire large model into GPU memory.

```text
GGUF / Safetensors / generated activations
                 |
                 v
converter + model_config.json
                 |
                 v
attention, expert, or embedding .xshard tiles
                 |
          +------+------+
          |             |
   asx_ram_v2.exe   asx_gemm.exe
   Q/K/V attention  selected experts
          |             |
          +------v------+
           validated metrics
```

Lane policy on the current implementation:

| Shard class | Policy |
| --- | --- |
| attention | Hot lane when payload is at most 2 GB |
| expert | Swappable cold lane; split tiles larger than 2 GB |
| embedding | Load once; do not swap per request |
| generic | Route according to size and declared use |

Model hot swapping is constrained by compatible header metadata,
`model_config.json`, tensor meaning, and shader expectations. Weight shards
are not interchangeable with Q/K/V activation shards merely because both use
the same container.

## D3D11 and D3D11_1 Constraints

The current attention sidecar creates a D3D11 device and compiles `cs_5_0`
HLSL. This supports older integrated GPUs, including the tested Intel HD 4600
path, but requires conservative buffer and synchronization behavior:

- typed SRV/UAV buffers must use compatible formats and shapes;
- SRV/UAV bindings must be explicitly cleared before resource reuse;
- staging readback and CPU/GPU synchronization can become bottlenecks;
- full-magnitude attention values can exceed the present softmax stability
  envelope, so current GPT-OSS activation extraction uses scaling;
- the practical hot-swap window is 2 GB per admitted shard payload;
- D3D11_1 interfaces may improve platform integration, but the compute path
  must retain a D3D11/feature-level-compatible fallback.

`asx_ram_v2.exe` validates GPU output against a CPU reference and uses exit
code `2` for numerical validation failure. It is a compute sidecar, not a
model registry or training authority.

## Clang, OpenCL, and CPU Orchestration

The development host includes an Intel LLVM/OpenCL CPU driver bundle with:

```text
ocl_cpu_clang_compiler64.dll
ocl_cpu_OclCpuBackend64.dll
ocl_cpu_task_executor64.dll
ocl_cpu_IntelOpenCL64.dll
```

These components provide a Clang-based OpenCL C compiler, CPU device backend,
and task executor. They are system driver assets, not repository binaries, and
must not be copied into releases without reviewing the accompanying licenses
and Intel redistribution terms.

NNC-K's `GPUComputePipeline` can generate OpenCL C 1.2 source and prefers
D3D11_1, then OpenCL, then CPU. The present code does **not** yet load the
OpenCL ICD, create a context/queue, compile kernels through the driver, or
register OpenCL as an admitted native-dispatch backend. Therefore OpenCL CPU
orchestration is an available host capability and intended backend, but the
current training path remains a truthful managed-CPU fallback.

The next lawful OpenCL slice is:

```text
discover platform/device
  -> register an available OpenCL provider
  -> create context and command queue
  -> compile generated OpenCL C 1.2
  -> bind typed buffers and dispatch
  -> compare with the managed CPU reference
  -> mark native dispatch admitted only after validation
```

## GEMM Compute

GEMM means General Matrix-Matrix Multiplication. Its standard BLAS form is:

```text
C = alpha * A * B + beta * C
```

GEMM powers linear layers, attention projections, convolution lowering, and
MoE expert networks. Performance depends on tiling, packing, cache reuse,
vectorization, asynchronous transfer, and dispatch scheduling.

NNC-K's `asx_gemm.exe` is currently a specialized GEMM-family sidecar. It
loads selected expert tiles from a `shard_class=expert` `.xshard`, multiplies
token activations through those expert matrices on D3D11, and validates the
result against a CPU reference. It is not yet a complete BLAS implementation:
the public sidecar does not expose arbitrary `alpha`, `beta`, transpose,
batched, sparse, or mixed-layout options.

The intended compute hierarchy is:

```text
K'UHUL/Micronaut orchestration
  -> classify tensor and select backend
  -> tile/pack A, B, and optional C
  -> D3D11 or admitted OpenCL dispatch
  -> CPU reference/conformance check
  -> metrics and trace
```

The Intel LLVM/OpenCL CPU backend is relevant here: once native OpenCL dispatch
is wired, it can execute GEMM kernels on the CPU through the OpenCL scheduler.
That complements the D3D11 GPU sidecar; it is not a language-model choice.

## Gemma Model Family

Gemma (the model family) is distinct from GEMM (the matrix operation). NNC-K
can discover GGUF models under LM Studio's model directory and route inference
through a compatible local server. This development host uses:

```text
%USERPROFILE%\.lmstudio\models\lmstudio-community\gemma-3-4b-it-GGUF
%USERPROFILE%\.lmstudio\models\lmstudio-community\gemma-4-E2B-it-GGUF
%USERPROFILE%\.lmstudio\models\lmstudio-community\gemma-3-1B-it-QAT-GGUF
```

Models are not included in the repository. Download a chosen quantization from
Hugging Face:

```powershell
python -m pip install -U huggingface_hub

hf download lmstudio-community/gemma-3-4b-it-GGUF `
  gemma-3-4b-it-Q4_K_M.gguf `
  --local-dir "$env:USERPROFILE\.lmstudio\models\lmstudio-community\gemma-3-4b-it-GGUF"

hf download lmstudio-community/gemma-4-E2B-it-GGUF `
  gemma-4-E2B-it-Q4_K_M.gguf `
  --local-dir "$env:USERPROFILE\.lmstudio\models\lmstudio-community\gemma-4-E2B-it-GGUF"

hf download lmstudio-community/gemma-3-1B-it-qat-GGUF `
  gemma-3-1B-it-QAT-Q4_0.gguf `
  --local-dir "$env:USERPROFILE\.lmstudio\models\lmstudio-community\gemma-3-1B-it-QAT-GGUF"
```

The 4B model's separate `mmproj-model-f16.gguf` is needed for its vision
projection path. Choose quantization according to available RAM, accuracy, and
runtime support, and review each model repository's license before use:

- `https://huggingface.co/lmstudio-community/gemma-3-4b-it-GGUF`
- `https://huggingface.co/lmstudio-community/gemma-4-E2B-it-GGUF`
- `https://huggingface.co/lmstudio-community/gemma-3-1B-it-qat-GGUF`

## Build and Validation

```powershell
npm install
npm run build
npm test
python -m pip install -r requirements.txt
py.exe scripts\generate_xshard.py .learning\xshard_samples\st_test
```

After extracting `bin/windows-binaries.zip`, validate synthetic GPU tiles:

```powershell
.\bin\asx_ram_v2.exe `
  .learning\xshard_samples\st_test\layer_00_q.xshard `
  .learning\xshard_samples\st_test\layer_00_k.xshard `
  .learning\xshard_samples\st_test\layer_00_v.xshard `
  .learning\xshard_samples\st_test\model_config.json 1 --prefetch
```

See `docs/asx-ram-attention.md` and `docs/gpt-oss-shard-bridge.md` for the
conversion and expert-GEMM workflows.

## Current Boundaries

- Root `run.ps1` and `quick.ps1` reference legacy module files not present in
  this checkout; use the npm, Python, UI, and component-specific commands
  documented above.
- Native build caches may contain machine-specific paths; configure fresh
  out-of-source CMake builds on another workstation.
- Several manifests describe broader HTTP/MCP surfaces than the currently
  packaged UI path. Treat a manifest as a declared contract and verify its
  entrypoint before claiming the service is available.
- GPU evidence currently covers selected attention and expert operations, not
  end-to-end arbitrary-model inference.
- OpenCL kernel generation exists, but native OpenCL dispatch and conformance
  evidence are not implemented yet.

## Stack Audit Summary

The repository contains 30 non-generated top-level subsystems, 47 inspected
contract/grammar files, and 84 test/validation-oriented files. The highest
leverage next step is a single portable build-and-conformance command that
validates schema resolution, K'UHUL tests, `.xshard` round trips, and native
sidecar output from a clean checkout. The OpenCL follow-on is a native dispatch
adapter validated against the same CPU reference kernels.
