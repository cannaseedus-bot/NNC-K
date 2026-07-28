# Repository Guidelines

## Project Structure & Module Organization

The primary package is an ES module runtime. Public JavaScript APIs live in `src/`, with focused modules under directories such as `src/runtime/`, `src/xvm/`, and `src/gpu/`. TypeScript compiler work is split between `compiler/` and `kuhul/compiler/`; language tests and examples are in `kuhul/tests/` and `kuhul/examples/`. Root-level Jest tests are under `src/__tests__/`.

Supporting components include Python utilities in `scripts/` and `tools/`, .NET projects in `dotnet/`, `dotnet-workers/`, and `dotnet-bridge/`, native code in `native/`, and GPU kernels in `shaders/`. Contracts, schemas, manifests, documentation, and agent skills belong in `contracts/`, `schemas/`, root `*.manifest.json` files, `docs/`, and `skills/`, respectively. Do not commit generated directories such as `node_modules/`, `coverage/`, native build output, or Python `__pycache__/`.

## Build, Test, and Development Commands

- `npm install` installs the root package dependencies.
- `npm run build` runs `tsc` using `tsconfig.json`.
- `npm test` runs the Jest suite and writes coverage reports to `coverage/`.
- `npm start` starts the main runtime from `src/index.js`.
- `npm run doctor` performs the CLI environment and installation checks.
- `npx eslint src compiler kuhul` runs ESLint on core JavaScript/TypeScript; adapt the paths to the directories touched.

Run component-specific .NET, Python, or native commands from that component’s documented project or build file rather than assuming the root npm scripts cover it.

## Coding Style & Naming Conventions

Use ES modules, semicolons, single quotes, and the existing file’s indentation (normally two spaces for JavaScript/TypeScript and four for Python). Prefer `camelCase` for functions and variables, `PascalCase` for classes, and kebab-case filenames such as `cpu-cluster.js`. Keep TypeScript and generated/compatibility JavaScript counterparts synchronized where both are tracked. Follow `.eslintrc.json`; avoid unrelated formatting churn.

## Testing Guidelines

Jest discovers `*.test.*`, `*.spec.*`, and files under `__tests__/`. Name tests after the unit or behavior, for example `src/__tests__/runtime.test.js`. Add fixtures under the nearest test tree. No numeric coverage threshold is configured, but new behavior and regressions should receive focused tests. Run `npm test` and `npm run build` before opening a pull request.

## Commit & Pull Request Guidelines

Recent history uses Conventional Commit-style subjects such as `feat(trainers): ...`, `fix(nav): ...`, and `docs: ...`. Keep commits scoped and imperative. Pull requests should explain intent, affected subsystems, validation commands, and any manifest or schema changes; link relevant issues and include screenshots for UI changes.
