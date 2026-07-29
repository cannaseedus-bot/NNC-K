# Quantum Trinity Web Research Worker

The `bin/Quantum/quantum_trinity.exe` C++ worker is NNC-K's external **web-research** sidecar. It performs real HTTP research using the DuckDuckGo Instant Answer API and the Wikimedia (Wikipedia) search/extract APIs, then returns structured JSON that the PowerShell runtime can turn into provisional micronauts.

For multi-source research (Semantic Scholar, arXiv, Wikipedia, Google News RSS), the PowerShell UI prefers the Python bot at `scripts/research_bot.py` and falls back to this C++ worker when Python is unavailable.

## Files

| Path | Purpose |
|------|---------|
| `bin/Quantum/src/quantum_trinity_web_research.cpp` | Full runtime source |
| `bin/Quantum/include/json.hpp` | nlohmann/json single-header dependency |
| `bin/Quantum/CMakeLists.txt` | CMake project |
| `bin/Quantum/quantum_trinity.exe` | Built executable |
| `scripts/research_bot.py` | Multi-source Python research bot |

## Build

Run the top-level build:

```powershell
pwsh -File build.ps1
```

Or build only the Quantum worker:

```powershell
cd bin\Quantum
cmake -S . -B build
cmake --build build --config Release
```

Requirements:
- Visual Studio 2022 Build Tools (MSVC v143)
- CMake 3.20+
- Windows SDK

## Python Bot (Preferred for Multi-Source Research)

```powershell
py.exe scripts\research_bot.py "machine learning in healthcare" --json
```

Sources queried:
- Semantic Scholar Graph API (free, 100/5min without key)
- arXiv OAI-PMH API (keyless, generous limits)
- Wikipedia / MediaWiki summary API
- Google News RSS (keyless)

The UI also exposes a slash command:

```text
/research machine learning in healthcare
```

Implementation note: the PowerShell UI spawns `py.exe` with `-RedirectStandardOutput` to a temp file to avoid Windows pipe-buffer deadlocks on large JSON payloads.

## Requirements

```powershell
py.exe -m pip install -r requirements.txt
```

Or let `build.ps1` install dependencies automatically.

## Usage

### Demo / interactive mode

```powershell
.\bin\Quantum\quantum_trinity.exe
```

### Query as argument

```powershell
.\bin\Quantum\quantum_trinity.exe "organic compounds on Saturn"
```

### JSON RPC over stdin

```powershell
'{"operation":"research","query":"Saturn rings composition","depth":2}' | .\bin\Quantum\quantum_trinity.exe
```

### Quiet JSON output

```powershell
.\bin\Quantum\quantum_trinity.exe --quiet "quantum machine learning"
```

## Operations

| Operation | Parameters | Description |
|-----------|------------|-------------|
| `research` | `query`, `depth` | Web research; returns `results` array |
| `analyze_ngrams` | `text`, `n` | Indexes text into the n-gram analyzer |
| `translate_notation` | `notation`, `target` | Translates quantum glyph notation |
| `store_memory` | `key`, `value` | Stores a value in quantum memory |
| `retrieve_memory` | `key` | Retrieves a value from quantum memory |
| `get_metrics` | — | Returns runtime metrics |

## Web Research Pipeline

1. **DuckDuckGo Instant Answer API** — fetches `AbstractText` and `RelatedTopics`.
2. **Wikimedia search/extract API** — searches Wikipedia, returns the top article intro plus related snippets.
3. **DuckDuckGo HTML (fallback)** — only if the above fail; often blocked by bot challenges.

## Integration with NNC-K

The PowerShell UI's `Research-And-MintMicronaut` function calls `Invoke-QuantumTrinityResearch` first. `Invoke-QuantumTrinityResearch` tries `scripts/research_bot.py` first, then the C++ worker. If web-sourced text is returned, the UI:
- Sets `$script:TurnMeta.Sources.Web = $true`
- Uses the text as the candidate knowledge payload
- Scaffolds a `.micronaut` package via `micronaut_factory.exe`
- Mints a JSON micronaut via `Save-Micronaut`
- Registers it with `MicronautRegister`
- Re-routes the original turn

Authority boundary (frozen):
- The **research workers** only propose candidate text; they never mutate the micronaut registry.
- **BOSS** authors/verifies/promotes contracts.
- **MicronautManager** commits normalized contracts.
- The **model node** may fall back to a focused summary if the web workers are unavailable.

## Example Output

```json
{
  "operation": "research",
  "query": "organic compounds on Saturn",
  "results": [
    "WEB: [1] A new analysis of data from the Cassini space probe has identified organic compounds within jets of water ice erupting from Saturn's moon Enceladus...",
    "Deep learning with transformer architectures",
    "Quantum computing and superposition states",
    "..."
  ],
  "status": "success"
}
```

## Troubleshooting

- **No snippets / bot challenge**: The worker automatically falls back to Wikipedia; ensure outbound HTTPS to `api.duckduckgo.com` and `en.wikipedia.org` is allowed.
- **Semantic Scholar 429**: The Python bot handles this gracefully and falls back to arXiv + Wikipedia + News.
- **Unicode issues in PowerShell**: Use `StandardOutputEncoding = [System.Text.Encoding]::UTF8` when spawning the process.
- **Missing Python deps**: Run `py.exe -m pip install requests feedparser`.

## Micro-Agents Sidecar (`quantum_microagents.exe`)

The `bin/Quantum/quantum_microagents.exe` C++ worker is a **candidate-only** micro-agent orchestrator. It runs six node-level semantic agents—Parser, ELIZA Therapist, ADAM12 Cognitive, Regex Pattern, Quantum Superposition, and Code Analysis—against a user input and returns a ranked list of candidate responses/structures. It never creates, updates, merges, or promotes micronauts.

### Files

| Path | Purpose |
|------|---------|
| `bin/Quantum/src/quantum_trinity_micro_agents.cpp` | Full runtime source |
| `bin/Quantum/include/json.hpp` | nlohmann/json single-header dependency |
| `schemas/node-populations/micro-agents.json` | Optional registry population (overrides hardcoded defaults) |
| `bin/Quantum/quantum_microagents.exe` | Built executable |

### Build

Built automatically by `build.ps1` as part of the Quantum Trinity worker suite.

### Usage

#### Interactive demo

```powershell
.\bin\Quantum\quantum_microagents.exe
```

#### Query as argument

```powershell
.\bin\Quantum\quantum_microagents.exe "I am confused by this class structure"
```

#### JSON-RPC over stdin

```powershell
'{"operation":"process","input":"I am excited about quantum computing","session_id":"user_001","mode":"orchestrated"}' | .\bin\Quantum\quantum_microagents.exe
```

### Operations

| Operation | Parameters | Description |
|-----------|------------|-------------|
| `process` | `input`, `session_id`, `mode` (`orchestrated` or `swarm`) | Runs matching agents and returns `candidates` plus `combined_candidate` |
| `get_agents` | — | Lists loaded agents, templates, and capabilities |
| `get_history` | `session_id` | Returns per-session execution history |
| `get_context` | `session_id` | Returns last input/response context |
| `get_config_paths` | — | Returns the list of paths searched for `micro-agents.json` |

### Authority Boundary

- `quantum_microagents.exe` only emits `candidates` and `combined_candidate` text/JSON.
- It does **not** touch the micronaut registry, `.learning/micronauts`, or any contract store.
- The PowerShell runtime and `MicronautManager` own all persistence and promotion decisions.

### PowerShell Slash Command

In `micronaut-ui.ps1`:

```text
/microagents I am sad about this bug
/microagents swarm find emails and URLs in this text
```

The helper `Invoke-QuantumMicroAgents` is defined in `micronaut-ui.ps1` and spawns the worker via temp-file redirection to avoid Windows pipe-buffer deadlocks.

### Example Output

```json
{
  "status": "success",
  "operation": "process",
  "session_id": "user_001",
  "mode": "orchestrated",
  "authority_boundary": "candidate_only",
  "authority_note": "This worker emits candidate text/structures only. No micronauts were created, updated, merged, or promoted.",
  "candidates": [
    {
      "agent_id": "therapist_2_...",
      "template": "therapist",
      "confidence": 0.95,
      "trust": 0.03,
      "result": {
        "emotion_detected": "excited",
        "response": "Your energy is contagious! What's exciting you?"
      }
    },
    {
      "agent_id": "quantum_5_...",
      "template": "quantum",
      "result": { "collapsed_state": "Quantum interpretation", ... }
    }
  ],
  "combined_candidate": "Your energy is contagious! What's exciting you?",
  "matching_agents": 2
}
```

## MoE expert GEMM sidecar

In addition to the research/personality workers, the Quantum Trinity build also
produces `bin/Quantum/asx_gemm.exe`. It loads GPT-OSS MoE expert `.xshard` tiles
(class `expert`) and performs D3D11 GEMM/GEMV validation against a CPU reference.

```powershell
py.exe scripts\gguf_experts_to_xshard.py `
  "C:\Users\canna\.lmstudio\models\lmstudio-community\gpt-oss-20b-GGUF\gpt-oss-20b-MXFP4.gguf" `
  --out "E:\models\GPT-OSS\experts" --layers 0

.\bin\asx_gemm.exe "E:\models\GPT-OSS\experts\layer_00\gate.xshard" 0,1,2,3 1
```

See `docs/gpt-oss-shard-bridge.md` and `docs/asx-ram-attention.md` for details.
