# windows-binaries.zip — prebuilt native binaries

Prebuilt Windows x64 executables, kept zipped because loose binaries don't belong
in the working tree. Source for these lives under `bin/Quantum/` (see `bin/Quantum/BUILD.md`).

## Unzip

From the repo root (`C:\Users\canna\.NNC-K`), in PowerShell:

```powershell
Expand-Archive -Path bin\windows-binaries.zip -DestinationPath . -Force
```

The archive preserves paths, so the exes land at:

```
bin\asx_gemm.exe
bin\asx_ram_v2.exe
bin\Quantum\quantum_hybrid.exe        <- CHEESE code-edge emitter (extract_relations op)
bin\Quantum\quantum_grammar.exe
bin\Quantum\quantum_microagents.exe
bin\Quantum\quantum_personality.exe
bin\Quantum\quantum_trinity.exe
```

`micronaut-ui.ps1` resolves `quantum_hybrid.exe` from `bin\Quantum\` (unzipped here)
or `bin\Quantum\build\` (a fresh dev build), whichever exists. To rebuild any binary
from source instead of unzipping, follow `bin/Quantum/BUILD.md`.

## Updating the zip after a rebuild

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::Open("bin\windows-binaries.zip",'Update')
$z.GetEntry('bin/Quantum/quantum_hybrid.exe').Delete()
[System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
    $z, "bin\Quantum\build\quantum_hybrid.exe", 'bin/Quantum/quantum_hybrid.exe',
    [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
$z.Dispose()
```
