# Quantum (quantum_trinity) — build

Source is tracked; **binaries and `build/` are not** (they live in a zip or are rebuilt).
The `bin/` tree is gitignored (`.gitignore: **/bin/`); these `.cpp` are force-added.

## Rebuild quantum_hybrid.exe (the CHEESE code-edge emitter)

From a VS2022 x64 dev shell (or via vcvars64), in `bin/Quantum`:

```bat
cl /nologo /std:c++17 /EHsc /O2 /I include src\quantum_trinity_hybrid.cpp ^
   /Fe:build\quantum_hybrid.exe /Fo:build\hybrid_
```

`json.hpp` (nlohmann, vendored, header-only) is in `include/`. Only `winhttp` is needed
by the web-research/personality/hybrid targets; `asx_ram_v2`/`asx_gemm` also need
`d3d11 d3dcompiler`. `CMakeLists.txt` lists all targets.

## extract_relations (used by micronaut-ui.ps1)

Emits structural code edges for the CHEESE/@flux feed. Pipe a JSON request to stdin:

```bat
echo {"operation":"extract_relations","code":"namespace A.B {\nclass Dog : Animal {\n}\n}"} | build\quantum_hybrid.exe
```

Returns `{ "edges": [ {"source","relation","target"}, ... ] }` with relations
`contains` (namespace), `inherits` (class base), `declares`/`returns` (method),
`typed` (var/property). Note: the parser is **line-oriented** — one construct per line.
