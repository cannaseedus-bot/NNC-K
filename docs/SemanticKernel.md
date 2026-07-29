# Semantic Kernel Index

This document maps every C# source file under `src/NeuralGrammar.Core/` to its role in the semantic/runtime kernel.

| File | Namespace | Primary Types | Role Summary |
|---|---|---|---|
| `src/NeuralGrammar.Core/AdvancedMath.cs` | `NeuralGrammar.Core` | AdvancedMath | (no doc summary) |
| `src/NeuralGrammar.Core/ChatFeed.cs` | `NeuralGrammar.Core` | ChatFeed, ChatSession, ChatTurn, TurnMetrics | ChatFeed — serializes conversation history as XSD-validated XML. Consumable by XSDParser, HybridSearch, LoRAAdapter, and CascadeRouter. |
| `src/NeuralGrammar.Core/CodeAnalyzer.cs` | `NeuralGrammar.Core` | CodeAnalyzer, DotnetPaths, CodeResult, AstInfo, AgentRoute | CodeAnalyzer — wraps dotnet SDK tools for compilation, formatting, analysis, and code generation. Routes code tasks to micronaut_coder.exe or Roslyn. Supports micro-agent delega... |
| `src/NeuralGrammar.Core/Console.cs` | `NeuralGrammar.Core` | MicronautConsole, EntryCategory, ConsoleEntry | Micronaut Console — high-performance register buffer for system messages, tool calls, phase changes, and model events. Replaces the PowerShell ArrayList with a managed ring buff... |
| `src/NeuralGrammar.Core/FoldTensor.cs` | `NeuralGrammar.Core` | FoldPhase, FoldTensor | Apply a fold's tensor transform without claiming a runtime transition. This is tensor mathematics, not semantic routing. |
| `src/NeuralGrammar.Core/GasNode.cs` | `NeuralGrammar.Core` | GasNode, RemoteMicronaut, GasResponse | GAS Node — Google Apps Script Network Client Connects the local C# runtime to the global micronaut network via a GAS web app. The GAS endpoint acts as a rendezvous/registry — it... |
| `src/NeuralGrammar.Core/GoogleOAuth.cs` | `NeuralGrammar.Core` | INetworkIdentityProvider, GoogleOAuth | Transport-level identity provider for network-authenticated requests. XCFE/K'UHUL still owns admission policy; this covers credential binding. |
| `src/NeuralGrammar.Core/GPUComputePipeline.cs` | `NeuralGrammar.Core.XCFE` | GPUComputePipeline, KernelType, GpuKernel, ComputeResult | GPU Compute Pipeline — generates and dispatches compute kernels across D3D11_1 (HLSL cs_5_0), WebGL2 (GLSL ES 300), and OpenCL (OpenCL C 1.2). Falls back to CPU when no GPU back... |
| `src/NeuralGrammar.Core/HtmlViewer.cs` | `NeuralGrammar.Core` | provides, HtmlViewer | Generates HTML/JS wrappers for K'UHUL program visualization in browser-based modals. The PowerShell UI creates the WPF window and WebBrowser control; this class provides the con... |
| `src/NeuralGrammar.Core/HybridSearch.cs` | `NeuralGrammar.Core` | ModelBackend, BackendType, ModelCapability, ModelTier, ModelInfo | Model Backend — DeepSeek cloud API + llama.cpp GGUF local + tool calling Provides unified inference, embeddings, and chat completion across backends. |
| `src/NeuralGrammar.Core/JsonRuntime.cs` | `NeuralGrammar.Core` | JsonRuntime, ManifestSet, BatchJob, BatchStageResult, ThreadPoolStats, RpcRequest, RpcResponse, RpcError | JsonRuntime — Unified runtime that reads all manifests (batches, threads, rpc, server, api) and orchestrates batch scheduling, thread pools, RPC handling, and fold-phase executi... |
| `src/NeuralGrammar.Core/Kast.cs` | `NeuralGrammar.Core` | KastDocument, KastNode, KastOperand, KastEdge, KastNodeKind | KAST — K'UHUL Abstract Syntax Tree.  Canonical structural bridge: frontend projection -> KAST -> XCFE -> K'UHUL π -> SCXQ2  KAST describes semantic structure. It does not schedu... |
| `src/NeuralGrammar.Core/KuhulConformanceTest.cs` | `NeuralGrammar.Core` | KuhulConformanceTest | K'UHUL Program Conformance Test.  Runs each .kuhul program through every stage of the compiler pipeline and reports pass/fail per workload.  source       → parse → validate → KA... |
| `src/NeuralGrammar.Core/KuhulMathEngine.cs` | `NeuralGrammar.Core.Kuhul` | KuhulMathEngine, Token, TokenType, ASTNode, NodeType, MathResult | K'UHUL Math Engine — expression parser, AST builder, safe math executor. Matches asx-kuhul-math-engine.manifest.json Pipeline: Pop(parse) -> Wo(build AST) -> Sek(compile+execute... |
| `src/NeuralGrammar.Core/KuhulPi.cs` | `Kuhul.Pi` | KuhulPi | K'UHUL π host kernel.  Closed semantic cycle: Pop -> Wo -> Yax -> Sek -> Ch'en -> Xul -> Pop  Pop() is the only public semantic entry point. XCFE owns control/arbitration outsid... |
| `src/NeuralGrammar.Core/KuhulScxq2Lowering.cs` | `NeuralGrammar.Core` | KuhulScxq2Lowering | K'UHUL -> SCXQ2 Lowering.  Converts a compiled K'UHUL program (.kprog) into a sequence of SCXQ2 lanes. Each fold becomes one lane; the fold graph's next pointers become the exec... |
| `src/NeuralGrammar.Core/LoRAAdapter.cs` | `NeuralGrammar.Core.XCFE` | LoRAAdapter | Create a LoRA adapter seeded from a micronaut node's topic. |
| `src/NeuralGrammar.Core/MathMLEngine.cs` | `NeuralGrammar.Core` | MathMLEngine, SemanticNode | MathML/K'UHUL semantic tree engine. Parsing/classification is side-effect free; XCFE/Sek owns tool effects. |
| `src/NeuralGrammar.Core/MCPServer.cs` | `NeuralGrammar.Core` | MCPServer, MCPRequest, MCPResponse, MCPError, MCPTool, MCPResource, MCPPrompt | MCP (Model Context Protocol) Server — hosts tools, resources, prompts via stdio and HTTP/SSE transports. Connects to local and remote MCP servers. https://modelcontextprotocol.io/ |
| `src/NeuralGrammar.Core/MicronautIndex.cs` | `NeuralGrammar.Core` | MicronautIndex | MicronautIndex — typed identity index for O(1) micronaut resolution. Owned by MicronautManager. Maps every micronaut identity (name, engine, capability, program path) to a Micro... |
| `src/NeuralGrammar.Core/MicronautManager.cs` | `NeuralGrammar.Core` | MicronautManager | Micronaut semantic filesystem curator.  Responsibilities: discover -> inspect -> ask semantic reshaper -> normalize -> validate -> hash -> commit -> optionally publish  The mana... |
| `src/NeuralGrammar.Core/MicronautNetworkNode.cs` | `NeuralGrammar.Core` | MicronautNetworkNode | (no doc summary) |
| `src/NeuralGrammar.Core/MicronautStore.cs` | `NeuralGrammar.Core` | MicronautStore | MicronautStore — Thread-safe global state registry for the C# backend. Provides a central key-value store with typed access, change events, and optional disk persistence. Access... |
| `src/NeuralGrammar.Core/MicronautWizard.cs` | `NeuralGrammar.Core` | MicronautWizard | MicronautWizard — UI-facing builder for compiling .kuhul personality programs into .kprog executable graphs.  Provides: template listing, property configuration, compilation, an... |
| `src/NeuralGrammar.Core/NDArray.cs` | `NeuralGrammar.Core` | owns, NDArray | Dense N-dimensional numerical tensor substrate for Neural Grammar Core. This class owns numeric shape/data algebra only; K'UHUL/XCFE owns control flow. |
| `src/NeuralGrammar.Core/NNCInterpreter.cs` | `NeuralGrammar.Core` | NNCInterpreter | NNC Interpreter — Executes neural network programs from JSON |
| `src/NeuralGrammar.Core/NodeCognitionKernel.cs` | `NeuralGrammar.Core` | NodeCognitionKernel | Reference engine for @node semantic cognition.  This engine implements the local thinking cycle that classic ELIZA pioneered — recognize, capture, relate, recall, decide, articu... |
| `src/NeuralGrammar.Core/NodeContribution.cs` | `NeuralGrammar.Core` | NodeContribution | Canonical envelope for one @node contribution inside a @fold.  A Micronaut response is not a mysterious blob. It is the accumulation of node contributions produced by the K'UHUL... |
| `src/NeuralGrammar.Core/ReasoningPipeline.cs` | `NeuralGrammar.Core` | performs, ReasoningPipeline | Micronaut reasoning pipeline as C# node operators.  Implements the four kernel operations that previously lived in PowerShell:  RECOGNIZE → RELATE → REMEMBER → ARTICULATE  plus ... |
| `src/NeuralGrammar.Core/SemanticArtifact.cs` | `NeuralGrammar.Core` | ArtifactKind, AdmissionStatus, NotationStatus, SemanticArtifactStore | Lifecycle state for semantic notations. |
| `src/NeuralGrammar.Core/SemanticDataset.cs` | `NeuralGrammar.Core` | SemanticDataset | (no doc summary) |
| `src/NeuralGrammar.Core/SemanticInference.cs` | `NeuralGrammar.Core` | SemanticInference, InferenceRecord, RouteDecision | SemanticInference — Wraps ModelBackend with XCFE phase awareness, capability scoring, multi-model routing, and inference history. Bridges the SemanticTensorEngine phase matrix t... |
| `src/NeuralGrammar.Core/SemanticInvariants.cs` | `NeuralGrammar.Core` | SemanticInvariantLearner | Learns invariant relationships, constraints, transformations, and state across fold phases |
| `src/NeuralGrammar.Core/SemanticNode.cs` | `NeuralGrammar.Core` | SemanticNode | A generic @node for semantic cognition.  The node model is intentionally close to classic ELIZA mechanics, because ELIZA is a proven reference implementation of local node-level... |
| `src/NeuralGrammar.Core/SemanticTensorEngine.cs` | `NeuralGrammar.Core` | SemanticTensorEngine | Unified integration of all 31 files — learns invariants, constraints, transformations, state |
| `src/NeuralGrammar.Core/ServiceWorker.cs` | `NeuralGrammar.Core` | ServiceWorker, ServiceState, ServiceInfo, HealthMonitor, HealthEntry | ServiceWorker — Manages lifecycle of all runtime services. Handles startup sequencing, health monitoring, graceful shutdown, and automatic restart of failed services. Reads serv... |
| `src/NeuralGrammar.Core/SessionCache.cs` | `NeuralGrammar.Core` | SessionCache | Bounded session cache for the Pop fold working set.  Stores recent retrievals, notation references, note IDs, and artifact handles. Evicts by LRU when the limit is reached. The ... |
| `src/NeuralGrammar.Core/SkinningEngine.cs` | `NeuralGrammar.Core` | SkinningEngine | Skin through a fold-algebraic phase transformation. |
| `src/NeuralGrammar.Core/Supernaut.cs` | `NeuralGrammar.Core` | Supernaut | Integrated NNC-K runtime composition root. Receives a Google ID, posts it to UserDatabase, and starts the Micronaut runtime. XCFE/K'UHUL remain the semantic admission/execution ... |
| `src/NeuralGrammar.Core/TaskPlanner.cs` | `NeuralGrammar.Core` | TaskPlanner | Supernaut Planning Skill — decomposes high-level objectives into deterministic task graphs with dependencies, priorities, validation points, and follow-up skill recommendations.... |
| `src/NeuralGrammar.Core/Trainer.cs` | `NeuralGrammar.Core` | NeuralTrainer | Neural Network Trainer — Backpropagation and optimization |
| `src/NeuralGrammar.Core/UserDatabase.cs` | `NeuralGrammar.Core` | UserDatabase, User, Session, ApiKey | UserDatabase — Receives Google IDs and maintains per-user runtime data, API configuration, capabilities, preferences, and buddy-network policy. Persists to disk as JSON. |
| `src/NeuralGrammar.Core/XCFEBrains.cs` | `NeuralGrammar.Core.XCFE` | BrainRouter, MicronautProfile, IntentDef, RouteResult, IntentItem | BrainRouter — n-gram intent matching for 9 micronaut profiles. Matches asx-micronaut-brains.manifest.json |
| `src/NeuralGrammar.Core/XCFEFolds.cs` | `NeuralGrammar.Core` | FoldAlgebra | K'UHUL closed-loop fold algebra.  Control law: Pop -> Wo -> Yax -> Sek -> Ch'en -> Xul -> Pop  Gravity may rank legal destinations, but it may never bypass the transition law. E... |
| `src/NeuralGrammar.Core/XCFEGlyphs.cs` | `NeuralGrammar.Core.XCFE` | GlyphRegistry, FoldGlyph, ResolvedNotation | Glyph Registry — resolves glyphs to folds, lanes, and opcodes. Matches asx-runtime-glyphs.manifest.json |
| `src/NeuralGrammar.Core/XCFEGPU.cs` | `NeuralGrammar.Core.XCFE` | GPUProviderRegistry | (no doc summary) |
| `src/NeuralGrammar.Core/XCFEMicronaut.cs` | `NeuralGrammar.Core.XCFE` | MicronautRuntime, MicronautRoute | Micronaut Runtime — factory pipeline, worker host dispatch, route binding. Matches asx-runtime-micronaut.manifest.json |
| `src/NeuralGrammar.Core/XCFEMutation.cs` | `NeuralGrammar.Core` | XCFEMutation | XCFE Mutation Engine — processes .learning/ logs, generates new micronauts, tracks per-model quality, and auto-evolves the knowledge base from interactions. Called by phase-brid... |
| `src/NeuralGrammar.Core/XCFEPolicy.cs` | `NeuralGrammar.Core.XCFE` | XCFEPolicy | XCFE admission policy.  Policy does not execute verbs. It proves that an operation is admitted under the current K'UHUL fold, lane, capability grants, determinism law, and runti... |
| `src/NeuralGrammar.Core/XCFEReplay.cs` | `NeuralGrammar.Core` | CoverageEntry, XCFEExecState | A single execution coverage record emitted by the XCFE fold wheel. Tracks one fold step within a turn for replay/coverage analysis. |
| `src/NeuralGrammar.Core/XCFERuntime.cs` | `NeuralGrammar.Core` | XCFERuntime | Execution coverage state — metrics, replay, contract manager input. |
| `src/NeuralGrammar.Core/XCFEStdlib.cs` | `NeuralGrammar.Core.XCFE` | XCFEStdlib | XCFE Standard Library — registry of all @-verbs by category. Matches the stdlib contract from asx-xcfe-authority.manifest.json |
| `src/NeuralGrammar.Core/XCFEUnifiedIR.cs` | `NeuralGrammar.Core` | UnifiedIREngine, IRTensor, IRSvgPath, IRSurface, IRCluster, ReplayLog, ReplayEntry | Unified IR Engine — KIMD tensor compute, SVG geometry, D3D11 projection, replay log. Matches asx-unified-ir-vector-surface.manifest.json (IR-1.0) |
| `src/NeuralGrammar.Core/XCFEVerifier.cs` | `NeuralGrammar.Core` | XCFEVerifier, VerifierResult, VerifierDiagnostic | XCFE Static Verifier — checks XJSON programs before execution. Implements all rules from asx-xcfe-authority.manifest.json: known_verbs_only, capability_binding, schema_params, b... |
| `src/NeuralGrammar.Core/XJSONParser.cs` | `NeuralGrammar.Core` | XJSONParser, ParseResult, ParseError, ASTNode, LexLine | XJSON Parser — converts XJSON surface syntax into a canonical AST. Implements the lowering pipeline from asx-xjson-language.manifest.json: normalize → strip_comments → lex_lines... |
| `src/NeuralGrammar.Core/XQuery.cs` | `NeuralGrammar.Core` | XQueryEngine, CompiledQuery, XPathType | XQuery Engine — evaluates FLWOR-style queries over XML documents. Acts as a feed: accept XML source, execute query, stream results. Schema context from XSDParser is optional but... |
| `src/NeuralGrammar.Core/XSDParser.cs` | `NeuralGrammar.Core` | XSDParser | XSD structural grammar authority for Neural Grammar / K'UHUL contracts. Loads schema structure, preserves namespaces, and validates XML without silently accepting schema-load er... |
| `src/NeuralGrammar.Core/Flux/FluxTrace.cs` | `NeuralGrammar.Core.Flux` | FluxTrace | One @flux execution trace: the observable record of a single tick through the K'UHUL fold wheel. Kept intentionally tolerant so PowerShell can store arbitrary micro-contribution... |
| `src/NeuralGrammar.Core/Flux/FluxTraceStore.cs` | `NeuralGrammar.Core.Flux` | FluxTraceStore | Durable @flux lineage store. Saves per-tick execution traces under <c>{dataRoot}/flux/{sessionId}/{tick}.json</c> and supports replay recovery. Authority remains with XCFE/K'UHU... |
| `src/NeuralGrammar.Core/Runtime/BossPromotion.cs` | `NeuralGrammar.Core.Runtime` | BossPromotion | BOSS promotion record. Authority-backed contract elevation. Requires a history of CHEESE records; BOSS cannot manufacture proof. |
| `src/NeuralGrammar.Core/Runtime/CheeseJudge.cs` | `NeuralGrammar.Core.Runtime` | CheeseJudge | CHEESE reinforcement authority. Positioned after K'UHUL Xul. Judges CollapseProof edges and emits CheeseRecords. Invariants: - Sheogorath cannot CHEESE itself. - CHEESE cannot a... |
| `src/NeuralGrammar.Core/Runtime/CheeseRecord.cs` | `NeuralGrammar.Core.Runtime` | CheeseRecord, CheeseJudgment, CheeseVerdict | CHEESE judgment emitted after Xul. References CollapseProof by hash. Reinforcement is edge-level, not response-level. |
| `src/NeuralGrammar.Core/Runtime/CollapseProof.cs` | `NeuralGrammar.Core.Runtime` | CollapseProof, CollapsedEdge, EdgeGuard | Post-Xul collapse artifact. Describes what K'UHUL selected, not what was proposed. CHEESE references this structure for reinforcement; it never mutates it. |
| `src/NeuralGrammar.Core/Runtime/ProvenanceStore.cs` | `NeuralGrammar.Core.Runtime` | ProvenanceStore | Durable store for CollapseProof, CheeseRecord, and BossPromotion artifacts. Writes are append-only; artifacts are immutable once sealed. |
| `src/NeuralGrammar.Core/Validation/NodeContributionValidator.cs` | `NeuralGrammar.Core.Validation` | NodeContributionValidator | Lightweight validator for the canonical <c>node-contribution-v1.json</c> schema. Interprets a practical subset of JSON Schema draft-07 (type, required, enum, minimum/maximum, mi... |

## Authority / subsystem groupings

| Grouping | Files |
|---|---|
| Node cognition / reasoning | NodeCognitionKernel.cs, ReasoningPipeline.cs, SemanticNode.cs, SemanticInference.cs, NodeContribution.cs |
| XCFE runtime / fold routing | XCFERuntime.cs, XCFEFolds.cs, XCFEBrains.cs, XCFEMicronaut.cs, XCFEMutation.cs, XCFEReplay.cs, XCFEPolicy.cs |
| Micronaut registry / storage | MicronautManager.cs, MicronautIndex.cs, MicronautStore.cs, MicronautNetworkNode.cs, MicronautWizard.cs |
| Semantic tensors / search | SemanticTensorEngine.cs, HybridSearch.cs, FoldTensor.cs, SemanticDataset.cs |
| K'UHUL execution | KuhulPi.cs, KuhulMathEngine.cs, KuhulScxq2Lowering.cs, JsonRuntime.cs, Kast.cs |
| Flux / provenance / CHEESE | Flux/FluxTrace.cs, Flux/FluxTraceStore.cs, Runtime/CollapseProof.cs, Runtime/CheeseRecord.cs, Runtime/CheeseJudge.cs, Runtime/BossPromotion.cs, Runtime/ProvenanceStore.cs |
| Validation / schema | Validation/NodeContributionValidator.cs |
| UI / console / feed helpers | ChatFeed.cs, Console.cs, HtmlViewer.cs, SkinningEngine.cs |
| Math / compute | AdvancedMath.cs, NDArray.cs, MathMLEngine.cs, GPUComputePipeline.cs |
| Services / adapters | MCPServer.cs, ServiceWorker.cs, GoogleOAuth.cs, LoRAAdapter.cs, Trainer.cs |
| Data / persistence | UserDatabase.cs, SessionCache.cs, SemanticArtifact.cs, SemanticInvariants.cs |
| Parsing / codecs | XJSONParser.cs, XQuery.cs, XSDParser.cs, XCFEUnifiedIR.cs, XCFEStdlib.cs, XCFEGlyphs.cs, XCFEVerifier.cs |

## Wiring status (PowerShell UI / runtime)

- `micronaut-ui.ps1` now initializes per-turn metadata, renders the chat bubble badge, and implements `Research-And-MintMicronaut` for semantic page faults.
- `Research-And-MintMicronaut` may use `micronaut_factory.exe create <domain>` to scaffold a `.micronaut` package, then populate it with web/model-node research data. The model node proposes text only; the runtime performs all registry writes.
- `scripts/xcfe_router.py` routes requests to the local model server and should load `scripts/folds.toml` for semantic micronaut selection.
- **BOSS** acts as the Quality Inspector: the runtime decides when quality checks run, BOSS re-words and merges micronauts (e.g. `organic_compounds` + `organics`), and BOSS verifies/promotes contracts. BOSS does not create micronauts on its own.
- `MicronautManager.cs` is the runtime curator that commits normalized micronaut contracts.
- **Merges preserve all data** from both source micronauts; size is not currently a concern.

## Factory integration

`bin/micronaut_factory.exe` (source: `bin/micronaut-factory/`) accepts:

```text
micronaut_factory.exe scan
micronaut_factory.exe create <domain>
micronaut_factory.exe list
```

It scaffolds a `.micronaut` directory under its configured base path. Domains added to `PersonalityGenerator::initialize_domain_keywords()` in `bin/micronaut-factory/src/factory_core.cpp`:

- `space`
- `climate`
- `biology`
- `xcfe`
- `eliza`
- `chemistry`

The factory source was rebuilt with CMake/Visual Studio Build Tools and the updated `bin/micronaut_factory.exe`, `micronaut_factory_core.dll`, and `micronaut_evolution.dll` were copied to `bin/`.

## Quantum Trinity schema (non-authority reference)

`schemas/quantum-trinity-web-research.schema.json` is saved as a reference contract. It is not an authority; it is mergeable into BOSS quality checks, `MicronautManager` normalization, or model-node context. NNC-K authority mapping:

| Quantum Trinity field | NNC-K owner | Notes |
|---|---|---|
| `web_research` | Runtime web-search node | Currently DuckDuckGo fallback; extensible |
| `deep_learning` | Model node (MM-1) | Token-signal generator only |
| `memory_system` | `MicronautStore`, `FluxTraceStore`, `SessionCache`, `ProvenanceStore` | Persistence owned by runtime |
| `ngram_analysis` | `BrainRouter`, `PersonalityGenerator` | Intent routing, factory domain keywords |
| `notation_semantics` | K'UHUL fold names (`Pop`, `Wo`, `Yax`, `Sek`, `Ch'en`, `Xul`), glyphs, `@flux` | Semantic notation, not authority |
| `quantum_weights` | CHEESE reinforcement | Edge-level rewards after Xul collapse |
| `kuhul_vm` integration | `KuhulPi`, `XCFERuntime`, `Kast` | Execution authority |
| `asx_prime` integration | Supernaut / GAS / contract graph | Orchestration authority |

## Provisional knowledge lifecycle

| Confidence | Authority meaning |
|---|---|
| 0.00 | Unknown / no usable relationship |
| 0.50 | Newly admitted provisional relationship; model/research node proposed it, but it has not yet been reinforced |
| < 0.50 | Weakening / contradicted candidate; likely to be rejected by CHEESE |
| > 0.50 | Increasingly supported by repeated Xul collapses and CHEESE records |
| near 1.0 | Strongly established; BOSS may promote it to a contract |

```text
unresolved
    ↓
research / model node proposes candidate
    ↓
candidate micronaut created @ confidence = 0.50
    ↓
registered as provisional
    ↓
future chat supplies evidence / corrections
    ↓
K'UHUL/XCFE collapses
    ↓
CHEESE evaluates post-Xul semantic edges
    ↓
BOSS verifies/promotes sufficiently proven contracts
```

## Authority boundary (frozen)

| Authority | May do | May NOT do |
|---|---|---|
| Model node | Propose candidate knowledge | Route, collapse, reinforce, promote, define K'UHUL |
| Sheogorath | Compose / imagine | Judge or reinforce |
| K'UHUL/XCFE | Orchestrate folds, collapse | Generate new speculative content |
| CHEESE | Judge collapsed relationships | Propose, collapse, promote |
| BOSS | Verify / promote contracts | Manufacture proof |

Swapping GPT, LFM, Qwen, Roslyn, or a custom-trained generator into the model node must remain boring — none of them owns K'UHUL semantics.

## UI visibility

See `docs/chat-bubble-metadata.md` for the per-turn metadata badge that must appear below each assistant bubble: tick, model, brain, fold, intent, confidence, sources, micronaut actions, and timestamp. The badge makes the model node observable without granting it authority.
