# Worker + @flux Integration Guide

This document describes the standalone artifacts added for dispatching micronaut
jobs through real worker sidecars and persisting execution lineage. The main
PowerShell UI scripts are treated as locked by the primary planner; integration
into those scripts should be performed by the model that owns them.

## New standalone artifacts

| File | Purpose |
|---|---|
| `scripts/NNCK-Runtime.psm1` | PowerShell module: `@flux` helpers, worker availability, worker dispatch, NodeContribution validation. |
| `build-dotnet-workers.ps1` | Builds the .NET stdio + HTTP worker executables. |
| `src/NeuralGrammar.Core/Flux/FluxTrace.cs` | Typed `@flux` trace model. |
| `src/NeuralGrammar.Core/Flux/FluxTraceStore.cs` | Durable per-tick trace store. |
| `src/NeuralGrammar.Core/Validation/NodeContributionValidator.cs` | Practical JSON Schema validator for `node-contribution-v1.json`. |

## C# changes

- `src/NeuralGrammar.Core/XCFEMicronaut.cs`
  - Worker path resolution now searches the project `bin/` tree (including
    `bin/dotnet-workers/...`) and walks ancestors, so it works whether the C#
    runtime is loaded from the project root or a nested test/output directory.
  - `EnsureDataRoot()` no longer throws when `AppDomain.BaseDirectory` is
    non-writable (e.g. `C:\Program Files\PowerShell\7\`).
  - Added `GetAvailability()` returning `WorkerAvailability`.
  - Added `DispatchJob(object job, MicronautSpec manifest, string httpUrl)`
    that tries HTTP first, then stdio, then returns a structured error.
- `src/NeuralGrammar.Core/XCFERuntime.cs`
  - `MicronautRuntime` is now lazily initialized so constructing an
    `XCFERuntime` from PowerShell does not eagerly touch the PS install folder.

## Tests

- `tests/NeuralGrammar.Core.Tests/MicronautRuntimeTests.cs`
  - `GetAvailability_DiscoversBuiltWorkers`
  - `RunWorker_StdioWorker_EchoesJob`
  - `RunWorker_ReturnsErrorWhenBinaryMissing`
  - `DispatchJob_UsesStdioWorkerWhenAvailable`
  - `DispatchJob_FallsBackWhenNoTransport`

## Required integration into locked scripts

### 1. `micronaut-ui.ps1` — import the module

Add near the top, after the C# `Add-Type` block:

```powershell
$runtimeModule = Join-Path $PSScriptRoot "scripts\NNCK-Runtime.psm1"
if (Test-Path $runtimeModule) { Import-Module $runtimeModule -Force -DisableNameChecking -ErrorAction SilentlyContinue }
```

The `-DisableNameChecking` switch suppresses the unapproved-verb warning that
would otherwise appear because the module exports `Invoke-WorkerDispatch` and
similar compound-name verbs.

### 2. `micronaut-ui.ps1` — initialize @flux store

After `$script:DataDir` is defined:

```powershell
$script:FluxStore = $null
$script:FluxSessionId = "default"
# ...
try {
    $script:FluxStore = [NeuralGrammar.Core.Flux.FluxTraceStore]::new($script:DataDir)
    $script:FluxStore.SessionId = $script:FluxSessionId
    $fluxLoaded = Import-FluxTraces -Store $script:FluxStore
    foreach ($kv in $fluxLoaded.GetEnumerator()) { $script:ExecutionTraces[$kv.Key] = $kv.Value | ConvertFrom-Json }
} catch { }
```

### 3. `micronaut-ui.ps1` — slash-command handler

Add `Invoke-FluxCommand` and friends (or import from the module). Hook into
`$sendBtn.Add_Click` right after reading `$msg`:

```powershell
$msg = $inputBox.Text.Trim(); if (-not $msg) { return }; $inputBox.Text = ""
if (Invoke-FluxCommand $msg) { return }
```

Supported commands (already implemented in the script functions / module):

- `/flux save`
- `/flux load <session-id>`
- `/flux sessions`
- `/flux export`
- `/validate <json>`
- `/worker test <query>`

### 4. `micronaut-ui.ps1` — worker dispatch in chat loop

Where micronaut contributions are collected (around the
`foreach ($m in $relevantMicros)` block), after `Invoke-Micronaut` returns a
contribution:

```powershell
$workerResult = Dispatch-MicronautWorker $m $msg $contribution
if ($workerResult) {
    $contribution.Text = $workerResult.Text
    $contribution.Confidence = $workerResult.Confidence
    if ($workerResult.Evidence) { $contribution.Evidence = $workerResult.Evidence }
    $contribution | Add-Member -NotePropertyName 'Transport' -Value $workerResult.Transport -Force
}
```

`Dispatch-MicronautWorker` uses the C# `MicronautRuntime` directly; it prefers
HTTP when `$d.workerUrl` is present, otherwise falls back to stdio.

### 5. `micronaut-ui.ps1` — Runtime Inspector

- Title: `@flux — Runtime Inspector`
- Width: `720` (to fit a third column)
- Add a **Worker Transports** panel showing factory / stdio / http status with
  tooltip paths.

### 6. `build.ps1` — build matrix

After the C# compile gate, call the worker build script:

```powershell
$workerBuildScript = Join-Path $root "build-dotnet-workers.ps1"
if (Test-Path $workerBuildScript) {
    Write-Host "Building .NET worker sidecars..." -ForegroundColor Green
    & pwsh -File $workerBuildScript 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Host "Worker build warning" -ForegroundColor Yellow }
    else { Write-Host "Worker sidecars: PASS" -ForegroundColor Green }
}
```

Also copy `.psm1` modules from `scripts/` to `$root` alongside `.ps1` modules.

## Verification commands

```powershell
pwsh -File _compile_test.ps1
pwsh -File build.ps1
dotnet test tests/NeuralGrammar.Core.Tests/NeuralGrammar.Core.Tests.csproj -c Release
```


## PowerShell pitfall: inline `if` inside string concatenation

Do **not** write:

```powershell
Write-Host ("status: " + (if ($x) { 'OK' } else { 'MISS' }) + "]")
```

PowerShell parses the file successfully but fails at runtime with:
`The term 'if' is not recognized as the name of a cmdlet...`

Instead, assign the result first or use a subexpression:

```powershell
$status = if ($x) { 'OK' } else { 'MISS' }
Write-Host ("status: " + $status + "]")
# or
Write-Host "status: $(if ($x) { 'OK' } else { 'MISS' })"
```


## CHEESE authority boundary

```text
Sheogorath
   │ COMPOSE
   ▼
candidate geometry
   │
   ▼
K'UHUL
Pop → Wo → Yax → Sek → Ch'en → Xul
                              │
                         collapse proof
                              ▼
                           CHEESE
                         REINFORCE
                              │
             accepted / rejected / guarded
                              ▼
                       provenance store
                              │
                    repeated verification
                              ▼
                            BOSS
                           PROMOTE
                              │
                              ▼
                         CONTRACT
```

Critical invariants:

- Sheogorath cannot CHEESE itself.
- CHEESE cannot alter Xul.
- CHEESE cannot promote contracts.
- BOSS cannot manufacture proof.

Objects:

- `NodeContribution` = proposal / evidence
- `CollapseProof` = what Xul selected
- `CheeseRecord` = judgment of CollapseProof edges
- `BossPromotion` = promotion backed by CheeseRecord history
