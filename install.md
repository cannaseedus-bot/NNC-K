# Installation

## Prerequisites

- Windows 10 or newer
- Node.js 14 or newer
- npm

Install the JavaScript dependencies from the repository root:

```powershell
npm install
```

## Restore the Windows binaries

Executable files are distributed in `bin/windows-binaries.zip` instead of as
duplicate loose files throughout the repository. The archive already contains
the required `bin/` directory layout.

From the repository root, extract it with:

```powershell
Expand-Archive -LiteralPath .\bin\windows-binaries.zip -DestinationPath . -Force
```

Do not extract the archive directly into `bin\`; doing so would create an
incorrect `bin\bin\` directory. After extraction, the native tools are grouped
under `bin\` and `bin\Quantum\`.

The archive contains the verified Windows builds for `asx_gemm`,
`asx_ram_v2`, `quantum_grammar`, `quantum_hybrid`, `quantum_microagents`,
`quantum_personality`, and `quantum_trinity`.

## Verify the installation

Run the package checks and build:

```powershell
npm run doctor
npm run build
npm test
```

If Windows blocks a downloaded executable, open its file properties and select
**Unblock**, or run `Unblock-File` only after verifying the repository source.
