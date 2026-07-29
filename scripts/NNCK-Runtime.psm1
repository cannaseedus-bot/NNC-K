# ============================================================================
# NNCK-Runtime — PowerShell module for @flux, worker dispatch, and validation helpers.
#
# This module wraps the C# runtime primitives so that micronaut-ui.ps1 can stay
# focused on WPF. All functions are stateless with respect to the module; callers
# pass in their script-scope state.
# ============================================================================

function ConvertFrom-JsonSafe {
    param([Parameter(ValueFromPipeline)] $InputObject, [int]$Depth = 64)
    process {
        $s = [string]$InputObject
        if ([string]::IsNullOrWhiteSpace($s)) { return $null }
        try { return ($s | ConvertFrom-Json -Depth $Depth -ErrorAction Stop) } catch { }
        $o = $s.IndexOf('{'); $c = $s.LastIndexOf('}'); $ob = $s.IndexOf('['); $cb = $s.LastIndexOf(']')
        $sub = if ($o -ge 0 -and $c -gt $o) { $s.Substring($o, $c - $o + 1) }
               elseif ($ob -ge 0 -and $cb -gt $ob) { $s.Substring($ob, $cb - $ob + 1) }
               else { $null }
        if ($sub) { try { return ($sub | ConvertFrom-Json -Depth $Depth -ErrorAction Stop) } catch { } }
        return $null
    }
}

function New-FluxStore {
    param([string]$DataRoot = (Join-Path (Get-Location).Path ".learning"),
          [string]$SessionId = "default")
    $store = [NeuralGrammar.Core.Flux.FluxTraceStore]::new($DataRoot)
    $store.SessionId = $SessionId
    return $store
}

function Export-FluxSession {
    param($Store, [string]$Path)
    if (-not $Store) { throw "FluxStore required" }
    $json = $Store.ExportSession()
    $Path = if ($Path) { $Path } else { Join-Path (Get-Location).Path ("flux-{0}-{1:yyyyMMddHHmmss}.json" -f $Store.SessionId, (Get-Date)) }
    Set-Content -Path $Path -Value $json -Encoding UTF8
    return $Path
}

function Save-FluxTraces {
    param($Store, [hashtable]$Traces)
    if (-not $Store) { throw "FluxStore required" }
    foreach ($tick in $Traces.Keys) {
        try {
            $json = $Traces[$tick] | ConvertTo-Json -Depth 6
            $Store.Save($tick, $json)
        } catch { }
    }
}

function Import-FluxTraces {
    param($Store)
    if (-not $Store) { throw "FluxStore required" }
    $result = @{}
    $raw = $Store.LoadSessionRawJson()
    foreach ($kv in $raw.GetEnumerator()) {
        $result[$kv.Key] = $kv.Value | ConvertFrom-JsonSafe
    }
    return $result
}

function Get-WorkerAvailability {
    param($Runtime)
    if (-not $Runtime) { throw "MicronautRuntime required" }
    return $Runtime.GetAvailability()
}

function Invoke-WorkerDispatch {
    param($Runtime, [hashtable]$Job, [object]$Manifest = $null, [string]$HttpUrl = "")
    if (-not $Runtime) { throw "MicronautRuntime required" }
    return $Runtime.DispatchJob($Job, $Manifest, $HttpUrl)
}

function Test-NodeContributionJson {
    param([string]$Json, [string]$SchemaPath = (Join-Path (Get-Location).Path "schemas\node-contribution-v1.json"))
    $validator = [NeuralGrammar.Core.Validation.NodeContributionValidator]::new($SchemaPath)
    return $validator.ValidateJson($Json)
}

function Invoke-QuantumMicroAgents {
    param(
        [Parameter(Mandatory)] [string]$InputText,
        [string]$Mode = 'orchestrated',
        [string]$SessionId = 'default',
        [string]$WorkerRoot = (Get-Location).Path,
        [int]$TimeoutMs = 30000
    )
    $exe = Join-Path $WorkerRoot "bin\Quantum\quantum_microagents.exe"
    if (-not (Test-Path $exe)) { throw "quantum_microagents.exe not found: $exe" }
    $tmpOut = Join-Path $env:TEMP ("qma_out_" + [System.Guid]::NewGuid().ToString("N") + ".json")
    $tmpErr = Join-Path $env:TEMP ("qma_err_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
    $json = @{ operation = 'process'; input = $InputText; session_id = $SessionId; mode = $Mode } | ConvertTo-Json -Compress
    $proc = Start-Process -FilePath $exe -ArgumentList "--quiet", $json -NoNewWindow -PassThru `
        -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr -WorkingDirectory $WorkerRoot
    $exited = $proc.WaitForExit($TimeoutMs)
    if (-not $exited) { try { $proc.Kill() } catch { }; throw "quantum_microagents timed out after ${TimeoutMs}ms" }
    if ($proc.ExitCode -ne 0) {
        $err = Get-Content $tmpErr -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
        throw "quantum_microagents exit $($proc.ExitCode): $err"
    }
    $out = Get-Content $tmpOut -Raw -Encoding UTF8
    Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
    return $out | ConvertFrom-JsonSafe
}

function Invoke-GptOssLayerForward {
    param(
        [Parameter(Mandatory)] [string]$ModelDir,
        [string]$OutDir,
        [int]$Layer = 0,
        [int]$SeqLen = 64,
        [double]$ScaleEmbed = 0.0001,
        [int]$Seed = 42
    )
    if (-not $OutDir) { $OutDir = Join-Path $ModelDir "activations" }
    $py = Get-Command py.exe -ErrorAction SilentlyContinue
    $python = if ($py) { "py.exe" } else { "python.exe" }
    $args = @("scripts\gptoss_layer_forward.py", "`"$ModelDir`"", "--out-dir", "`"$OutDir`"", "--layer", $Layer, "--seq-len", $SeqLen, "--scale-embed", $ScaleEmbed, "--seed", $Seed)
    $tmpOut = Join-Path $env:TEMP ("gptoss_fwd_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
    $tmpErr = Join-Path $env:TEMP ("gptoss_fwd_err_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
    $proc = Start-Process -FilePath $python -ArgumentList $args -NoNewWindow -PassThru `
        -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr -WorkingDirectory (Get-Location).Path
    $exited = $proc.WaitForExit(120000)
    if (-not $exited) { try { $proc.Kill() } catch { }; throw "gptoss_layer_forward timed out" }
    $out = Get-Content $tmpOut -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    $err = Get-Content $tmpErr -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
    if ($proc.ExitCode -ne 0) { throw "gptoss_layer_forward failed: $err" }
    return [PSCustomObject]@{
        ExitCode = $proc.ExitCode
        StdOut = $out
        StdErr = $err
        QShard = Join-Path $OutDir "layer_$($Layer.ToString('00'))_q.xshard"
        KShard = Join-Path $OutDir "layer_$($Layer.ToString('00'))_k.xshard"
        VShard = Join-Path $OutDir "layer_$($Layer.ToString('00'))_v.xshard"
        Config = Join-Path $OutDir "model_config.json"
    }
}

function Invoke-AsxRam {
    param(
        [Parameter(Mandatory)] [string]$QShard,
        [Parameter(Mandatory)] [string]$KShard,
        [Parameter(Mandatory)] [string]$VShard,
        [string]$Config = 'model_config.json',
        [string]$WorkerRoot = (Get-Location).Path,
        [int]$Passes = 1,
        [switch]$Prefetch,
        [int]$TimeoutMs = 120000
    )
    $exe = Join-Path $WorkerRoot "bin\asx_ram_v2.exe"
    if (-not (Test-Path $exe)) {
        $exe = Join-Path $WorkerRoot "bin\asx_ram.exe"
    }
    if (-not (Test-Path $exe)) { throw "asx_ram executable not found in $WorkerRoot\bin" }
    $q = Resolve-Path (Join-Path $WorkerRoot $QShard) -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Path
    $k = Resolve-Path (Join-Path $WorkerRoot $KShard) -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Path
    $v = Resolve-Path (Join-Path $WorkerRoot $VShard) -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Path
    if (-not $q) { $q = $QShard }
    if (-not $k) { $k = $KShard }
    if (-not $v) { $v = $VShard }
    $tmpOut = Join-Path $env:TEMP ("asxram_out_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
    $tmpErr = Join-Path $env:TEMP ("asxram_err_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
    $args = @($q, $k, $v)
    if ($exe -match 'asx_ram_v2') { $args += $Config }
    $args += [string]$Passes
    if ($Prefetch) { $args += "--prefetch" }
    $proc = Start-Process -FilePath $exe -ArgumentList $args -NoNewWindow -PassThru `
        -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr -WorkingDirectory $WorkerRoot
    $exited = $proc.WaitForExit($TimeoutMs)
    if (-not $exited) { try { $proc.Kill() } catch { }; throw "asx_ram timed out after ${TimeoutMs}ms" }
    $out = Get-Content $tmpOut -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    $err = Get-Content $tmpErr -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
    return [PSCustomObject]@{
        ExitCode = $proc.ExitCode
        StdOut = $out
        StdErr = $err
        QShard = $q
        KShard = $k
        VShard = $v
        Config = $Config
        Passes = $Passes
        Prefetch = $Prefetch.IsPresent
    }
}

function Invoke-AsxGemm {
    param(
        [Parameter(Mandatory)] [string]$Shard,
        [string]$Experts = "0",
        [int]$Passes = 1,
        [string]$WorkerRoot = (Get-Location).Path,
        [int]$TimeoutMs = 300000
    )
    $exe = Join-Path $WorkerRoot "bin\asx_gemm.exe"
    if (-not (Test-Path $exe)) { $exe = Join-Path $WorkerRoot "bin\Quantum\asx_gemm.exe" }
    if (-not (Test-Path $exe)) { throw "asx_gemm executable not found" }
    $shardPath = Resolve-Path (Join-Path $WorkerRoot $Shard) -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Path
    if (-not $shardPath) { $shardPath = $Shard }
    $tmpOut = Join-Path $env:TEMP ("asxgemm_out_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
    $tmpErr = Join-Path $env:TEMP ("asxgemm_err_" + [System.Guid]::NewGuid().ToString("N") + ".txt")
    $proc = Start-Process -FilePath $exe -ArgumentList @($shardPath, $Experts, [string]$Passes) -NoNewWindow -PassThru `
        -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr -WorkingDirectory $WorkerRoot
    $exited = $proc.WaitForExit($TimeoutMs)
    if (-not $exited) { try { $proc.Kill() } catch { }; throw "asx_gemm timed out after ${TimeoutMs}ms" }
    $out = Get-Content $tmpOut -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    $err = Get-Content $tmpErr -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
    return [PSCustomObject]@{
        ExitCode = $proc.ExitCode
        StdOut = $out
        StdErr = $err
        Shard = $shardPath
        Experts = $Experts
        Passes = $Passes
    }
}

function Invoke-NncKRequest {
    param(
        [Parameter(Mandatory)] [string]$Prompt,
        [string]$Url = 'http://127.0.0.1:1235/v1/chat/completions',
        [string]$Model = 'gpt-oss-20b',
        [int]$MaxTokens = 32,
        [double]$Temperature = 0.7,
        [int]$TimeoutMs = 300000
    )
    $body = @{
        model = $Model
        messages = @(@{ role = 'user'; content = $Prompt })
        max_tokens = $MaxTokens
        temperature = $Temperature
    } | ConvertTo-Json -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($body)
    $req = [System.Net.HttpWebRequest]::Create($Url)
    $req.Method = 'POST'; $req.ContentType = 'application/json'; $req.Timeout = $TimeoutMs
    $req.ContentLength = $bytes.Length
    $s = $req.GetRequestStream(); $s.Write($bytes, 0, $bytes.Length); $s.Close()
    $rs = $req.GetResponse().GetResponseStream()
    $rd = New-Object System.IO.StreamReader($rs)
    $json = $rd.ReadToEnd(); $rd.Close()
    return $json | ConvertFrom-JsonSafe
}

function Invoke-FileDropIngest {
    param(
        [Parameter(Mandatory)] [string[]]$Paths,
        [string]$WorkerRoot = (Get-Location).Path,
        [int]$TimeoutMs = 30000
    )
    $results = @()
    foreach ($path in $Paths) {
        $resolved = Resolve-Path $path -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Path
        if (-not $resolved) { $resolved = $path }
        $ext = [System.IO.Path]::GetExtension($resolved).ToLower()
        $size = if (Test-Path $resolved) { (Get-Item $resolved).Length } else { 0 }
        $lane = switch -Regex ($ext) {
            '\.(dll|exe|so|dylib|bin|sys|drv)$' { 'binary_analysis' }
            '\.(cs|cpp|c|h|hpp|py|js|ts|ps1|psm1|kuhul|khl)$' { 'code_analysis' }
            '\.(txt|md|json|toml|xml|csv|yaml|yml)$' { 'semantic_ingest' }
            '\.(xshard|shard)$' { 'compute_attention' }
            default { 'semantic_ingest' }
        }
        $candidate = @{ type = 'file_summary'; extension = $ext; size = $size }
        if ($lane -eq 'binary_analysis' -and (Test-Path $resolved)) {
            try {
                $bytes = [System.IO.File]::ReadAllBytes($resolved)
                $magic = [System.BitConverter]::ToString($bytes[0..7]) -replace '-',''
                $candidate.magic = $magic
                $candidate.pe_offset = [System.BitConverter]::ToUInt32($bytes, 0x3C)
            } catch { }
        }
        $results += [PSCustomObject]@{
            Path = $resolved
            Lane = $lane
            Size = $size
            Candidate = $candidate
            AuthorityBoundary = 'candidate_only'
        }
    }
    return $results
}

function Format-ChatMetadata {
    param(
        [long]$Tick = 0,
        [string]$Model = "",
        [double]$Confidence = 0.0,
        [string]$Brain = "",
        [string]$Fold = "",
        [string]$Intent = "",
        [string[]]$MicronautActions = @(),
        [hashtable]$Sources = @{},
        [string]$Timestamp = ""
    )
    $parts = @("Tick#$Tick")
    if ($Model) { $parts += $Model }
    if ($Brain) { $parts += "brain:$Brain" }
    if ($Fold) { $parts += "fold:$Fold" }
    if ($Intent) { $parts += "intent:$Intent" }
    if ($Confidence -gt 0) { $parts += "conf:$([Math]::Round($Confidence,2))" }
    if ($Sources.Count -gt 0) {
        $srcTags = @()
        if ($Sources['Web']) { $srcTags += 'web' }
        if ($Sources['Boss']) { $srcTags += 'BOSS' }
        if ($Sources['Micronauts']) { $srcTags += 'micronauts' }
        if ($Sources['Replay']) { $srcTags += 'replay' }
        if ($Sources['MutationPlus']) { $srcTags += 'mutation+' }
        if ($Sources['Cheese']) { $srcTags += 'CHEESE' }
        if ($Sources['Local'] -or $srcTags.Count -eq 0) { $srcTags += 'local' }
        $parts += ($srcTags -join '|')
    }
    if ($MicronautActions.Count -gt 0) { $parts += ($MicronautActions -join '; ') }
    if (-not $Timestamp) { $Timestamp = (Get-Date -Format 'HH:mm:ss') }
    $parts += $Timestamp
    return "[ " + ($parts -join " | ") + " ]"
}

function New-ChatMetadataBadge {
    param(
        [long]$Tick = 0,
        [string]$Model = "",
        [double]$Confidence = 0.0,
        [string]$Brain = "",
        [string]$Fold = "",
        [string]$Intent = "",
        [string[]]$MicronautActions = @(),
        [hashtable]$Sources = @{},
        [string]$Timestamp = ""
    )
    $text = Format-ChatMetadata @PSBoundParameters
    try {
        $tb = [System.Windows.Controls.TextBlock]::new()
        $tb.Text = $text
        $tb.FontSize = 9
        $tb.Foreground = '#8b949e'
        $tb.Margin = '0,2,0,6'
        $tb.ToolTip = 'Tick, model, confidence, brain, fold, intent, sources, micronaut actions, timestamp'
        return $tb
    } catch {
        return $text
    }
}

Export-ModuleMember -Function @(
    'ConvertFrom-JsonSafe',
    'New-FluxStore','Export-FluxSession','Save-FluxTraces','Import-FluxTraces',
    'Get-WorkerAvailability','Invoke-WorkerDispatch','Test-NodeContributionJson',
    'Invoke-QuantumMicroAgents','Invoke-AsxRam','Invoke-AsxGemm','Invoke-NncKRequest','Invoke-FileDropIngest',
    'Format-ChatMetadata','New-ChatMetadataBadge','Invoke-GptOssLayerForward'
)
