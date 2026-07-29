# File Drop Routing

When a user drops or references files, NNC-K must first classify the data domain and route to the correct processing lane. **Not every file becomes a transformer Q/K/V attention shard.**

## Routing table

| File kind | Extensions | Lane | Sidecar / Tool |
|-----------|------------|------|----------------|
| Native binaries | `.dll`, `.exe`, `.so`, `.dylib`, `.sys`, `.drv` | `binary_analysis` | PE parser, `dumpbin`, `objdump`, strings |
| Source code | `.cs`, `.cpp`, `.c`, `.h`, `.hpp`, `.py`, `.js`, `.ts`, `.ps1`, `.psm1`, `.kuhul`, `.khl` | `code_analysis` | `quantum_hybrid.exe` (Roslyn/RegEx/ELIZA/ADAM12) |
| Documents / data | `.txt`, `.md`, `.json`, `.toml`, `.xml`, `.csv`, `.yaml`, `.yml` | `semantic_ingest` | Micronaut RAG, web research, model node |
| Attention shards | `.xshard`, `.shard` | `compute_attention` | `asx_ram.exe` (D3D11) |
| Media (future) | `.png`, `.wav`, `.mp4` | `media_extract` | Dedicated sidecar |

## Example

Four Intel graphics/OpenCL DLLs dropped:

```text
C:\DRIVERS\VDO\h2vdo66us14\Gfx\950fd7b1-7601-4c2a-ae29-d33825f748c9\igfxcmjit64.dll
C:\DRIVERS\VDO\h2vdo66us14\Gfx\950fd7b1-7601-4c2a-ae29-d33825f748c9\IntelOpenCL64.dll
C:\DRIVERS\VDO\h2vdo66us14\Gfx\950fd7b1-7601-4c2a-ae29-d33825f748c9\ocl_cpu_clang_compiler64.dll
C:\DRIVERS\VDO\h2vdo66us14\Gfx\950fd7b1-7601-4c2a-ae29-d33825f748c9\ocl_cpu_IntelOpenCL64.dll
```

All four route to `binary_analysis`. The runtime inspects:

- PE magic (`MZ`)
- PE header offset at `0x3C`
- Export table names (`oclGetPlatformID`, `clCreateContext`, `igfxcmjit...`)
- Strings (`Intel(R) OpenCL`, `OpenCL 3.0`)

Result is a `NodeContribution` candidate, **not** an attention shard.

## PowerShell helpers

### `scripts/NNCK-Runtime.psm1`: `Invoke-FileDropIngest`

```powershell
Import-Module scripts\NNCK-Runtime.psm1
$files = @(
    'C:\DRIVERS\VDO\h2vdo66us14\Gfx\...\IntelOpenCL64.dll',
    'C:\DRIVERS\VDO\h2vdo66us14\Gfx\...\igfxcmjit64.dll'
)
Invoke-FileDropIngest -Paths $files
```

Returns:

```powershell
Path    Lane             Size Candidate
----    ----             ---- ---------
...dll  binary_analysis  12MB @{type=file_summary; extension=.dll; size=...; magic=4D5A...}
```

### `micronaut-ui.ps1`: `Invoke-FileDropFromKuhul`

Called from a K'UHUL program when a file list is submitted. Emits system notes for each lane.

## Why not force everything into `asx_ram.exe`?

`asx_ram.exe` expects binary `.xshard` files with a specific layout:

- magic `XSQ2`
- shape `[64, 1024]` per tile
- 16 tiles per layer
- dtype fp32/fp16/int8/int4

Arbitrary DLLs do **not** match that layout. Feeding them to `asx_ram.exe` would fail at the `xshard_valid_magic` gate. The correct lane for DLLs is binary analysis.

## Authority boundary

- Classification and routing are runtime decisions.
- Sidecars emit candidate structures/text only.
- Micronaut creation/update/promotion remains with `MicronautManager` and BOSS.

## Files

| Path | Purpose |
|------|---------|
| `skills/skill-file-drop/SKILL.md` | Skill contract |
| `scripts/NNCK-Runtime.psm1` | `Invoke-FileDropIngest` |
| `micronaut-ui.ps1` | `Invoke-FileDropFromKuhul` |
| `docs/file-drop-routing.md` | This document |
