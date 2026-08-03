# XVM Execution Backend Matrix

## Canonical scheduling contract

```text
Field
  -> Cards
  -> one execution Fiber/work-item per Card
  -> Card iterates its owned Tokens
```

Tokens are not independently paged or scheduled.

SafeTensors weights are likewise paged by Card ownership. The metadata-only
`SafeTensorsIndex` converts data-buffer-relative offsets to absolute file byte
ranges and emits Card residency plans. It reads only the 8-byte length prefix
and JSON header until a Card is admitted for execution.

Model execution has two distinct levels:

```text
forwardField()
  = one bounded pass through the Field's Card Fibers
  = logits and/or training loss

cluster.generate()
  = repeated forwardField() calls
  = decoding policy + KV state + attention mask + EOS/budget
```

The Cluster owns generation. A Card Fiber owns only its bounded portion of one
forward pass.

## Local backend roles

| Backend | Intended role | Local proof/status |
|---|---|---|
| XVM `CPUCluster32` (CPU) | **Semantic / manifold layer** — glyphs, folds, phases, geodesics, curvature, pressure | 32-fiber phase-barrier cluster; Field/Card binding and phase/manifold opcode tests pass. The CPU compute engine |
| DirectML via `ggml-xcfe` (GPU) | **Dense-tensor layer** — GEMM / attention | Verified: `xcfe_matmul_test` ≈ 6e-7 vs CPU; `xcfe_probe` reports XCFE backend registered. The primary GEMM path |
| SXME (GPU, D3D12) | Dense-tensor — SCX-MoE forward pass | `native/d3d12_compute/sxme_compute.cpp` |
| Intel OpenCL CPU | Reach lane (non-DirectML hosts only) | Present but native dispatch **un-wired** — a current `task-run` reports 0 platforms; the executor path returns `invalid_task_request`. Not required here |
| Intel OpenCL GPU | GPU compute alternative/interoperability | HD Graphics 4600 platform present; kernel integration pending |
| Direct3D 11 | Canonical native tensor/shader execution | GPT-2 training smoke and matched shader library pass |
| Direct3D 12 | Orchestration and resource coordination only | Do not treat as the default tensor compute backend |
| Direct3D 8–10 | Compatibility/projection donor surfaces | Inspect and bridge only when a concrete legacy consumer requires them |

## Registered Intel OpenCL platform

The system Khronos ICD loader registers `IntelOpenCL64.dll` and exposes:

- `Intel(R) HD Graphics 4600`, OpenCL 1.2 / OpenCL C 1.2;
- `Intel(R) Core(TM) i7-4790S CPU @ 3.20GHz`,
  OpenCL 1.2 build 10094 / OpenCL C 1.2.

The CPU device reports FP64, native/local-thread execution, D3D11 sharing,
atomics, SPIR, and image extensions.

That reflects the installed driver's *advertised* capability. A current runtime
probe does not enumerate it, however: `kuhul_engine --providers` finds the driver
DLLs (with `KUHUL_DRIVER_ROOT` set) but `task-run` reports **0 OpenCL platforms**
and the executor returns `invalid_task_request`. Treat native OpenCL as **un-wired**
— a documented reach lane for non-DirectML hosts, not an active backend. The CPU
semantic layer runs on the XVM cluster; GPU tensor math runs on DirectML/SXME.

The driver installation payload under `C:\DRIVERS\VDO\...` contains the Intel
clang/LLVM CPU compiler, task executor, CPU backend, TBB, SVML, and ICD
libraries. These files are driver-owned dependencies. Do not copy them into
the repository or redistribute them. Applications should load `OpenCL.dll`
through the registered system ICD.

## Reproducible probe

Run:

```powershell
python scripts\probe_opencl_xvm.py
```

The probe:

1. loads the system Khronos ICD;
2. selects an OpenCL CPU device;
3. compiles OpenCL C through the registered Intel compiler;
4. launches one work-item per Card;
5. executes a closed XVM subset (`LOAD_CONST`, `ADD`, `MUL`,
   `RIEMANN_CURVATURE`, `PHASE_TRANSITION`, `RETURN`);
6. verifies returned Card indices, owned Token counts, and final Fiber facts.

`xvm-opencl-conformance.test.js` compares those final facts with
`CPUCluster32`. This is not yet a complete XVM opcode lowerer, full per-op trace
comparison, or SCXQ7-authorized execution bridge.
