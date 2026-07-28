# Micronaut UI

`micronaut-ui.ps1` is the Windows WPF control surface for NNC-K. It combines
chat, model routing, Micronaut execution, K'UHUL programs, runtime traces,
local persistence, file ingestion, and native GPU sidecars in one desktop
application.

## Requirements

- Windows 10 or newer
- PowerShell 7 (`pwsh`)
- Python available as `py.exe`
- Python packages from `requirements.txt`
- Extracted Windows tools from `bin/windows-binaries.zip`; see `install.md`

Install the software dependencies from the repository root:

```powershell
python -m pip install -r requirements.txt
npm install
Expand-Archive .\bin\windows-binaries.zip -DestinationPath . -Force
```

## Launch

```powershell
pwsh -NoProfile -File .\micronaut-ui.ps1
```

The UI loads C# sources from `src/NeuralGrammar.Core/`, imports
`scripts/NNCK-Runtime.psm1` when present, reads the dark theme from
`schemas/themes/dark.json`, and stores runtime data below `.learning/`.

## Interface

- The model selector chooses the active inference route.
- The chat feed displays user, model, runtime, and system events.
- The sidebar manages saved chats and user profiles.
- `[W]` opens the K'UHUL Shaman program builder.
- `[V]` opens the phase/SVG visualizer.
- `[I]` opens the runtime inspector and per-tick traces.
- Console, Runtime, Errors, Inference, and Network tabs expose execution state.
- The attachment button routes dropped files through the file-ingestion lane.

Chats are stored in `.learning/chats/`, Micronauts in
`.learning/micronauts/`, and exported traces in `.learning/traces/`.

## Runtime Flow

```text
message
  -> intent and fold selection
  -> relevant Micronaut lookup
  -> recognize / relate / remember / articulate
  -> model, tool, worker, or K'UHUL dispatch
  -> response and execution trace
  -> governed persistence
```

The model may propose candidate text, but runtime code owns registry writes,
refinement, promotion, and persistence.

## Local Inference and GPU Tools

The default inference endpoint is `http://127.0.0.1:1235`. Start the local
server independently when needed:

```powershell
py.exe .\scripts\nnc_k_server.py --model-dir "D:\models\GPT-OSS" --port 1235
```

The server can also scan LM Studio's default model root:

```text
%USERPROFILE%\.lmstudio\models
```

Gemma GGUF weights are downloaded separately from Hugging Face. See
`NNC-K.md#gemma-model-family` for the verified repositories and commands.

The UI can invoke `bin\asx_ram_v2.exe` for Q/K/V attention shards and
`bin\asx_gemm.exe` for MoE expert shards. These programs are compute-only:
they return results and metrics but do not mutate Micronauts.

## Troubleshooting

- If an executable is missing, re-extract `bin/windows-binaries.zip` at the
  repository root, not inside `bin\`.
- If inference is offline, check `http://127.0.0.1:1235/health`.
- If Python is not found, install Python Launcher or replace `py.exe` with the
  correct interpreter in your local launch configuration.
- If WPF assemblies fail to load, use PowerShell 7 on Windows rather than a
  non-Windows PowerShell host.
- Treat hard-coded example model paths as examples; pass a local model path.
