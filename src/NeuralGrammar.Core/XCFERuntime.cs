using System;
using System.Collections.Generic;
using System.Linq;
using NeuralGrammar.Core.XCFE;

namespace NeuralGrammar.Core
{
    public class XCFERuntime
    {
        private readonly XCFEPolicy _policy;
        private readonly XCFEVerifier _verifier;
        private readonly Dictionary<string, object> _state = new();
        private readonly Dictionary<string, object> _store = new();
        private readonly Dictionary<string, List<Dictionary<string, object>>> _idb = new();
        private readonly HashSet<Capability> _caps = new();
        private readonly List<RuntimeTask> _tasks = new();
        private readonly List<RuntimeEvent> _events = new();
        private int _recursionDepth;
        private bool _halted;
        private readonly Random _rng;

        // Turn-level semantic bridge. BrainRouter owns intent routing;
        // NodeCognitionKernel owns local @node semantic recognition; XCFE owns scheduling.
        private readonly BrainRouter _brainRouter;
        private readonly NodeCognitionKernel _nodeCognition;
        private readonly FoldAlgebra _foldAlgebra;
        private MicronautRuntime _micronautRuntime;
        public MicronautRegister MicronautRegister { get; set; }
        public MicronautRuntime MicronautRuntime
        {
            get => _micronautRuntime ??= new MicronautRuntime();
            set => _micronautRuntime = value;
        }

        /// <summary>Execution coverage state — metrics, replay, contract manager input.</summary>
        public XCFEExecState ExecState { get; } = new XCFEExecState();

        /// <summary>@node semantic cognition kernel exposed for loading nodes and tests.</summary>
        public NodeCognitionKernel NodeCognition => _nodeCognition;

        public XCFERuntime(XCFEPolicy policy = null, int? seed = null)
        {
            _policy = policy ?? new XCFEPolicy().GrantAll();
            _verifier = new XCFEVerifier(_policy);
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
            _brainRouter = new BrainRouter();
            _nodeCognition = new NodeCognitionKernel(seed);
            _foldAlgebra = new FoldAlgebra();
            foreach (var cap in _policy.Grants) _caps.Add(cap);
            foreach (var n in new[]{"users","sessions","entries","memories","files","chat_history","rlhf","agents","skills","micronauts","replay","learning"})
                _idb[n] = new List<Dictionary<string, object>>();
        }

        public void RegisterNode(string name) { if (!_idb.ContainsKey(name)) _idb[name] = new List<Dictionary<string, object>>(); }
        public string[] ListNodeNames() => _idb.Keys.ToArray();
        public void NodeInsert(string node, Dictionary<string, object> entry) { if (_idb.TryGetValue(node, out var list)) list.Add(entry); }
        public List<Dictionary<string, object>> NodeQuery(string node, string key = null, object value = null)
        {
            if (!_idb.TryGetValue(node, out var list)) return new List<Dictionary<string, object>>();
            if (key == null) return new List<Dictionary<string, object>>(list);
            return list.Where(d => d.TryGetValue(key, out var v) && (v == null || v.Equals(value))).ToList();
        }
        public List<Dictionary<string, object>> NodeSearch(string node, string term)
        {
            if (!_idb.TryGetValue(node, out var list)) return new List<Dictionary<string, object>>();
            var t = term.ToLowerInvariant();
            return list.Where(d => d.Values.Any(v => v?.ToString()?.ToLowerInvariant()?.Contains(t) == true)).ToList();
        }

        /// <summary>
        /// Route one natural-language turn through the native semantic engine.
        /// This does not select or invoke a model. It emits the semantic,
        /// memory, fold, and capability contract consumed by the model router.
        /// </summary>
        // Fold handlers are deliberately registered by fold name. RouteTurn does not
        // encode the Pop -> Wo -> Yax -> Sek -> Ch'en -> Xul sequence; FoldAlgebra does.
        private Dictionary<string, Func<XCFETurnContext, FoldStepResult>> CreateFoldHandlers()
        {
            return new Dictionary<string, Func<XCFETurnContext, FoldStepResult>>(StringComparer.OrdinalIgnoreCase)
            {
                ["Pop"] = ExecutePopFold,
                ["Wo"] = ExecuteWoFold,
                ["Yax"] = ExecuteYaxFold,
                ["Sek"] = ExecuteSekFold,
                ["Ch'en"] = ExecuteChenFold,
                ["Chen"] = ExecuteChenFold, // compatibility spelling only
                ["Xul"] = ExecuteXulFold
            };
        }

        /// <summary>
        /// Route one turn by riding the FoldAlgebra wheel. The runtime asks the
        /// algebra where execution is, dispatches that fold's handler, and advances
        /// only after the handler admits the transition.
        /// </summary>
        public XCFETurnResult RouteTurn(string text, int memoryLimit = 4)
        {
            var turn = new XCFETurnResult();
            if (string.IsNullOrWhiteSpace(text))
            {
                turn.Success = false;
                turn.Error = "Turn text is empty.";
                return turn;
            }

            var popCount = 0; var woCount = 0; var yaxCount = 0;
            var sekCount = 0; var chenCount = 0; var xulCount = 0;

            try
            {
                _foldAlgebra.Reset();
                var ctx = new XCFETurnContext(text, Math.Max(0, memoryLimit), turn);
                var handlers = CreateFoldHandlers();
                const int maxFoldSteps = 24; // policy guard against accidental orbit loops


                while (!ctx.Complete && ctx.StepCount < maxFoldSteps)
                {
                    var fold = _foldAlgebra.CurrentFold ?? "Pop";
                    turn.FoldTrace.Add(fold);
                    ctx.StepCount++;
                    switch (fold) { case "Pop": popCount++; break; case "Wo": woCount++; break; case "Yax": yaxCount++; break; case "Sek": sekCount++; break; case "Ch'en": chenCount++; break; case "Xul": xulCount++; break; }

                    if (!handlers.TryGetValue(fold, out var handler))
                        throw new InvalidOperationException($"No XCFE handler registered for fold '{fold}'.");

                    var step = handler(ctx) ?? FoldStepResult.Reject("Fold handler returned no result.");
                    ctx.LastStep = step;

                    if (!step.Accepted)
                    {
                        turn.Success = false;
                        turn.Error = string.IsNullOrWhiteSpace(step.Reason)
                            ? $"Fold '{fold}' rejected the transition."
                            : step.Reason;
                        return turn;
                    }

                    // Xul collapses the turn. Otherwise the algebra—not RouteTurn—
                    // determines the next location on the wheel.
                    if (!ctx.Complete)
                        _foldAlgebra.Advance();
                }

                if (!ctx.Complete)
                    throw new InvalidOperationException($"Fold scheduler exceeded {maxFoldSteps} steps without Xul collapse.");

                turn.Success = true;
                ExecState.RecordTurn(true, turn.Confidence,
                    popCount, woCount, yaxCount, sekCount, chenCount, xulCount,
                    turn.Memories.Count, turn.Memories.Count, 0);

                return turn;
            }
            catch (Exception ex)
            {
                turn.Success = false;
                turn.Error = ex.Message;
                ExecState.RecordTurn(false, turn.Confidence,
                    popCount, woCount, yaxCount, sekCount, chenCount, xulCount,
                    0, 0, 0);
                return turn;
            }
        }

        private FoldStepResult ExecutePopFold(XCFETurnContext ctx)
        {
            // Pop: perceive/route and page relevant semantic memory.
            var route = _brainRouter.Route(ctx.Text);
            var turn = ctx.Turn;

            turn.Intent = route.Intent ?? "";
            turn.Brain = (route.Target == "NONE") ? "" : (route.Target ?? "");
            turn.Fold = route.Fold ?? "";
            turn.Confidence = route.Confidence;
            turn.Routed = route.Routed;
            turn.FallbackReason = route.FallbackReason ?? "";
            turn.Fallback = !string.IsNullOrWhiteSpace(turn.FallbackReason);
            if (route.MatchedNgrams != null) turn.MatchedNgrams.AddRange(route.MatchedNgrams);
            if (route.Tools != null) turn.Tools.AddRange(route.Tools);

            var queryTerms = TokenizeForMemory(ctx.Text);
            var candidates = new List<XCFEMemoryRef>();

            // File-based memory scan (fallback).
            CollectMemoryCandidates("micronauts", queryTerms, candidates);
            CollectMemoryCandidates("memories", queryTerms, candidates);
            CollectMemoryCandidates("learning", queryTerms, candidates);

            // MicronautRegister query — higher authority than file scan.
            if (MicronautRegister != null)
            {
                foreach (FoldPhase phase in Enum.GetValues(typeof(FoldPhase)))
                {
                    var phaseNodes = MicronautRegister.GetByPhase(phase);
                    foreach (var node in phaseNodes)
                    {
                        var haystack = (node.Subject + " " + node.Capability + " " + node.Brain).ToLowerInvariant();
                        var matched = queryTerms
                            .Where(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();
                        if (matched.Count == 0) continue;
                        var baseScore = Math.Min(1.0, (double)matched.Count / queryTerms.Count);
                        var qualityMul = node.Quality / 100.0;
                        var weightedScore = Math.Min(1.0, baseScore * 0.6 + qualityMul * 0.4);
                        candidates.Add(new XCFEMemoryRef
                        {
                            Id = node.Id,
                            Node = "register/" + phase.ToString(),
                            Score = weightedScore,
                            MatchedTerms = matched,
                            Data = new Dictionary<string, object>
                            {
                                ["subject"] = node.Subject,
                                ["capability"] = node.Capability,
                                ["brain"] = node.Brain,
                                ["phase"] = phase.ToString(),
                                ["quality"] = node.Quality,
                                ["is_seed"] = node.IsSeed,
                                ["is_daemon"] = node.IsDaemon
                            }
                        });
                    }
                }
            }

            turn.Memories = candidates
                .OrderByDescending(m => m.Score)
                .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .Take(ctx.MemoryLimit)
                .ToList();

            // @node semantic cognition (ELIZA-style local pattern recognition).
            // These are contributions, not routed agents. XCFE may use them when
            // resolving domain programs or capability contracts.
            var contributions = _nodeCognition.EmitAll(ctx.Text);
            if (contributions.Count > 0)
                turn.Contributions.AddRange(contributions);

            ExecState.RecordFoldStep("Pop", true, intent: ctx.Turn.Intent, brain: ctx.Turn.Brain, confidence: ctx.Turn.Confidence, memoryCount: ctx.Turn.Memories.Count, foldStepIndex: ctx.StepCount);
            return FoldStepResult.Accept("Pop admitted semantic route, memory page, and node contributions.");
        }

        private FoldStepResult ExecuteWoFold(XCFETurnContext ctx)
        {
            // Wo: bind the routed semantic state into the resident runtime.
            var turn = ctx.Turn;
            _state["turn.text"] = ctx.Text;
            _state["turn.intent"] = turn.Intent;
            _state["turn.brain"] = turn.Brain;
            _state["turn.route_confidence"] = turn.Confidence;
            _state["turn.memory_count"] = turn.Memories.Count;
                        ExecState.RecordFoldStep("Wo", true, intent: ctx.Turn.Intent, brain: ctx.Turn.Brain, confidence: ctx.Turn.Confidence, memoryCount: ctx.Turn.Memories.Count, foldStepIndex: ctx.StepCount);
            return FoldStepResult.Accept("Wo bound turn state.");
        }

        private FoldStepResult ExecuteYaxFold(XCFETurnContext ctx)
        {
            // Yax: resolve semantic posture into a backend capability contract.
            ctx.Turn.Requirements = BuildCapabilityRequest(ctx.Text, ctx.Turn);
                        ExecState.RecordFoldStep("Yax", true, intent: ctx.Turn.Intent, brain: ctx.Turn.Brain, confidence: ctx.Turn.Confidence, memoryCount: ctx.Turn.Memories.Count, foldStepIndex: ctx.StepCount);
            return FoldStepResult.Accept("Yax resolved capability posture.");
        }

        private FoldStepResult ExecuteSekFold(XCFETurnContext ctx)
        {
            // Sek: execution boundary. A backend may consume this contract, but it
            // never owns the fold wheel.
            _state["turn.requirements"] = ctx.Turn.Requirements;
            ctx.YieldedAtSek = true;
                        ExecState.RecordFoldStep("Sek", true, intent: ctx.Turn.Intent, brain: ctx.Turn.Brain, confidence: ctx.Turn.Confidence, memoryCount: ctx.Turn.Memories.Count, foldStepIndex: ctx.StepCount);
            return FoldStepResult.Accept("Sek emitted execution contract.");
        }

        private FoldStepResult ExecuteChenFold(XCFETurnContext ctx)
        {
            // Ch'en: evaluation/replay candidate. Durable learning is still gated.
            var turn = ctx.Turn;
            var replay = new Dictionary<string, object>
            {
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                ["text"] = ctx.Text,
                ["intent"] = turn.Intent,
                ["brain"] = turn.Brain,
                ["confidence"] = turn.Confidence,
                ["memory_count"] = turn.Memories.Count,
                ["stage"] = "evaluation"
            };
            NodeInsert("replay", replay);
                        ExecState.RecordFoldStep("Ch'en", true, intent: ctx.Turn.Intent, brain: ctx.Turn.Brain, confidence: ctx.Turn.Confidence, memoryCount: ctx.Turn.Memories.Count, foldStepIndex: ctx.StepCount);
            return FoldStepResult.Accept("Ch'en admitted evaluation candidate.");
        }

        private FoldStepResult ExecuteXulFold(XCFETurnContext ctx)
        {
            // Xul: collapse this orbit. Future mutation/reward policy hooks belong here.
            _state["turn.complete"] = true;
            _state["turn.fold_steps"] = ctx.StepCount;
            ctx.Complete = true;
                        ExecState.RecordFoldStep("Xul", true, intent: ctx.Turn.Intent, brain: ctx.Turn.Brain, confidence: ctx.Turn.Confidence, memoryCount: ctx.Turn.Memories.Count, foldStepIndex: ctx.StepCount);
            return FoldStepResult.Accept("Xul collapsed turn.");
        }

        private static HashSet<string> TokenizeForMemory(string text)
        {
            var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the","and","that","this","with","from","what","when","where",
                "which","who","why","how","can","could","would","should","into",
                "about","does","are","was","were","have","has","had","for"
            };

            var cleaned = new string(
                text.ToLowerInvariant()
                    .Select(c => char.IsLetterOrDigit(c) || c == '-' || char.IsWhiteSpace(c) ? c : ' ')
                    .ToArray());

            return cleaned
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3 && !stop.Contains(w))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private void CollectMemoryCandidates(
            string node,
            HashSet<string> queryTerms,
            List<XCFEMemoryRef> output)
        {
            if (!_idb.TryGetValue(node, out var entries) || queryTerms.Count == 0)
                return;

            foreach (var entry in entries)
            {
                var haystack = string.Join(" ", entry.Values
                    .Where(v => v != null)
                    .Select(v => v.ToString()))
                    .ToLowerInvariant();

                var matched = queryTerms
                    .Where(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (matched.Count == 0)
                    continue;

                var score = Math.Min(1.0, (double)matched.Count / queryTerms.Count);
                var id =
                    GetEntryString(entry, "id") ??
                    GetEntryString(entry, "_id") ??
                    GetEntryString(entry, "subject") ??
                    $"{node}:{output.Count}";

                output.Add(new XCFEMemoryRef
                {
                    Id = id,
                    Node = node,
                    Score = score,
                    MatchedTerms = matched,
                    Data = new Dictionary<string, object>(entry)
                });
            }
        }

        private static string GetEntryString(Dictionary<string, object> entry, string key)
        {
            return entry.TryGetValue(key, out var value) && value != null
                ? value.ToString()
                : null;
        }

        private XCFECapabilityRequest BuildCapabilityRequest(string text, XCFETurnResult turn)
        {
            var lower = text.ToLowerInvariant();

            var req = new XCFECapabilityRequest
            {
                Chat = true,
                Code = ContainsAny(lower, "code", "script", "function", "class", "compile", "debug"),
                Math = ContainsAny(lower, "math", "calculate", "equation", "formula", "solve"),
                Tools = turn.Tools.Count > 0 ||
                        ContainsAny(lower, "search", "fetch", "lookup", "tool", "file"),
                Vision = ContainsAny(lower, "image", "picture", "photo", "vision"),
                LongContext = text.Length > 4000 || turn.Memories.Count >= 4
            };

            var reasoning = 0.25;
            reasoning += Math.Min(0.30, turn.Confidence * 0.30);
            reasoning += Math.Min(0.20, turn.Memories.Count * 0.05);

            if (ContainsAny(lower, "why", "explain", "analyze", "compare", "design", "architecture"))
                reasoning += 0.20;
            if (req.Code || req.Math)
                reasoning += 0.15;

            req.Reasoning = Math.Min(1.0, reasoning);
            return req;
        }

        private static bool ContainsAny(string text, params string[] terms)
            => terms.Any(t => text.Contains(t, StringComparison.OrdinalIgnoreCase));

        public RuntimeResult Execute(XJSONParser.ASTNode ast, Dictionary<string, object> inputs = null)
        {
            _recursionDepth = 0; _halted = false;
            if (inputs != null) foreach (var kv in inputs) _state[kv.Key] = kv.Value;
            var result = new RuntimeResult();
            try { WalkNode(ast, result); result.Success = !_halted; result.State = new Dictionary<string, object>(_state); }
            catch (RuntimeHaltException) { result.Success = false; result.Halted = true; }
            catch (Exception ex) { result.Success = false; result.Error = ex.Message; }
            return result;
        }

        private void WalkNode(XJSONParser.ASTNode node, RuntimeResult result)
        {
            if (_halted) throw new RuntimeHaltException();
            if (node.Type == "exec") { _recursionDepth++; if (_recursionDepth > _policy.Limits.MaxRecursion) throw new InvalidOperationException("Max recursion"); DispatchVerb(node, result); _recursionDepth--; }
            foreach (var child in node.Children) WalkNode(child, result);
        }

        private void DispatchVerb(XJSONParser.ASTNode node, RuntimeResult result)
        {
            var verb = node.Value;
            var pr = _policy.CheckVerb(verb);
            if (!pr.Allowed) throw new InvalidOperationException($"Policy denied: {pr.Reason}");

            var p = new Dictionary<string, string>();
            foreach (var c in node.Children)
                if (c.Type == "param" && c.Attrs.TryGetValue("value", out var v)) p[c.Value] = v;

            switch (verb)
            {
                case "@seq":   ExecSeq(node); break;
                case "@par":   ExecPar(node); break;
                case "@if":    ExecIf(node); break;
                case "@throw": ExecThrow(node, p); break;
                case "@halt":  _halted = true; break;
                case "@var": case "@const": ExecVar(node, p); break;
                case "@set":   ExecSet(node, p); break;
                case "@get":   ExecGet(node, p, result); break;
                case "@unset": ExecUnset(node, p); break;
                case "@push":  ExecPush(node, p); break;
                case "@pop":   ExecPop(node, p, result); break;
                case "@merge": ExecMerge(node, p); break;
                case "@calc":  ExecCalc(node, p, result); break;
                case "@hash":  ExecHash(node, p, result); break;
                case "@rand":  ExecRand(node, p, result); break;
                case "@log": case "@trace": ExecLog(node, p); break;
                case "@cap.list": result.Output = _caps.Select(c => c.ToString()).ToList(); break;
                case "@cap.require": ExecCapRequire(node, p); break;
                case "@cap.revoke": ExecCapRevoke(node, p); break;
                case "@on": ExecOn(node, p); break;
                case "@state": result.Output = new { Tasks = _tasks.Count, Events = _events.Count, Caps = _caps.Count }; break;
                case "@coverage": ExecCoverage(result); break;
                case "@node.list": result.Output = _idb.Keys.ToArray(); break;
                case "@node.read": ExecNodeRead(node, p, result); break;
                case "@node.write": ExecNodeWrite(node, p); break;
                case "@node.query": ExecNodeQuery(node, p, result); break;
                case "@node.search": ExecNodeSearch(node, p, result); break;
                default: result.Logs.Add($"Verb '{verb}' acknowledged (no-op)"); break;
            }
        }

        private void ExecSeq(XJSONParser.ASTNode node) { foreach (var c in node.Children) WalkNode(c, new RuntimeResult()); }
        private void ExecPar(XJSONParser.ASTNode node) { ExecSeq(node); }
        private void ExecIf(XJSONParser.ASTNode node)
        {
            var cond = node.Attrs.GetValueOrDefault("condition") ?? node.Children.FirstOrDefault(c => c.Type == "param")?.Attrs.GetValueOrDefault("value");
            if (cond == null) return;
            var target = EvaluateCondition(cond) ? "then" : "else";
            bool inBranch = false;
            foreach (var c in node.Children)
            {
                if (c.Type == "label" && (c.Value == target)) inBranch = true;
                else if (c.Type == "label" && (c.Value == "then" || c.Value == "else")) inBranch = false;
                else if (inBranch) WalkNode(c, new RuntimeResult());
            }
        }
        private void ExecThrow(XJSONParser.ASTNode node, Dictionary<string, string> p) { throw new InvalidOperationException($"XCFE @throw: {p.GetValueOrDefault("message", "error")}"); }
        private void ExecVar(XJSONParser.ASTNode node, Dictionary<string, string> p)
        {
            var name = p.GetValueOrDefault("name") ?? node.Children.FirstOrDefault(c => c.Type == "param")?.Attrs.GetValueOrDefault("value");
            var value = p.GetValueOrDefault("value");
            if (name != null) _state[name] = value;
        }
        private void ExecSet(XJSONParser.ASTNode node, Dictionary<string, string> p) { ExecVar(node, p); }
        private void ExecGet(XJSONParser.ASTNode node, Dictionary<string, string> p, RuntimeResult r)
        {
            var name = p.GetValueOrDefault("name");
            if (name != null && _state.TryGetValue(name, out var v)) r.Output = v;
        }
        private void ExecUnset(XJSONParser.ASTNode node, Dictionary<string, string> p) { var n = p.GetValueOrDefault("name"); if (n != null) _state.Remove(n); }
        private void ExecPush(XJSONParser.ASTNode node, Dictionary<string, string> p)
        {
            var n = p.GetValueOrDefault("name"); var v = p.GetValueOrDefault("value");
            if (n != null) { if (!_state.ContainsKey(n)) _state[n] = new List<string>(); if (_state[n] is List<string> l) l.Add(v ?? ""); }
        }
        private void ExecPop(XJSONParser.ASTNode node, Dictionary<string, string> p, RuntimeResult r)
        {
            var n = p.GetValueOrDefault("name");
            if (n != null && _state[n] is List<string> l && l.Count > 0) { r.Output = l[^1]; l.RemoveAt(l.Count - 1); }
        }
        private void ExecMerge(XJSONParser.ASTNode node, Dictionary<string, string> p) { var n = p.GetValueOrDefault("name"); var v = p.GetValueOrDefault("value"); if (n != null && v != null) _state[n + "_merged"] = v; }
        private void ExecCalc(XJSONParser.ASTNode node, Dictionary<string, string> p, RuntimeResult r)
        {
            var expr = p.GetValueOrDefault("expr");
            if (expr != null) { try { using var dt = new System.Data.DataTable(); r.Output = Convert.ToDouble(dt.Compute(expr, "")); } catch { r.Output = expr; } }
        }
        private void ExecHash(XJSONParser.ASTNode node, Dictionary<string, string> p, RuntimeResult r)
        {
            var d = p.GetValueOrDefault("data");
            if (d != null) { using var sha = System.Security.Cryptography.SHA256.Create(); r.Output = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(d))).Replace("-","").ToLowerInvariant(); }
        }
        private void ExecRand(XJSONParser.ASTNode node, Dictionary<string, string> p, RuntimeResult r)
        {
            var mn = double.TryParse(p.GetValueOrDefault("min"), out var n) ? n : 0.0;
            var mx = double.TryParse(p.GetValueOrDefault("max"), out var x) ? x : 1.0;
            r.Output = mn + _rng.NextDouble() * (mx - mn);
        }
        private void ExecLog(XJSONParser.ASTNode node, Dictionary<string, string> p) { Console.WriteLine($"[XCFE:{node.Value}] {p.GetValueOrDefault("message", node.Value)}"); }
        private void ExecCapRequire(XJSONParser.ASTNode node, Dictionary<string, string> p)
        {
            var cn = p.GetValueOrDefault("capability");
            if (cn != null && Enum.TryParse<Capability>(cn, true, out var c) && !_caps.Contains(c)) throw new InvalidOperationException($"Capability '{cn}' not granted");
        }
        private void ExecCapRevoke(XJSONParser.ASTNode node, Dictionary<string, string> p)
        {
            var cn = p.GetValueOrDefault("capability");
            if (cn != null && Enum.TryParse<Capability>(cn, true, out var c)) _caps.Remove(c);
        }
        private void ExecOn(XJSONParser.ASTNode node, Dictionary<string, string> p) { var en = p.GetValueOrDefault("event"); if (en != null) _events.Add(new RuntimeEvent { Name = en, Handler = node }); }
        private void ExecCoverage(RuntimeResult r)
        {
            var snap = ExecState.GetSnapshot();
            _state["coverage.total_turns"] = snap.TotalTurns;
            _state["coverage.fold_coverage"] = snap.FoldCoverage;
            _state["coverage.success_rate"] = snap.SuccessRate;
            _state["coverage.average_confidence"] = snap.AverageConfidence;
            _state["coverage.memory_recall_rate"] = snap.MemoryRecallRate;
            _state["coverage.needs_refold"] = snap.NeedsRefold ? "true" : "false";
            _state["coverage.successful_turns"] = snap.SuccessfulTurns;
            _state["coverage.failed_turns"] = snap.FailedTurns;
            _state["coverage.pop"] = snap.PopSteps;
            _state["coverage.wo"] = snap.WoSteps;
            _state["coverage.yax"] = snap.YaxSteps;
            _state["coverage.sek"] = snap.SekSteps;
            _state["coverage.chen"] = snap.ChenSteps;
            _state["coverage.xul"] = snap.XulSteps;
            r.Output = snap;
        }

        private void ExecNodeRead(XJSONParser.ASTNode node, Dictionary<string, string> p, RuntimeResult r)
        {
            var n = p.GetValueOrDefault("name");
            if (n != null && _idb.TryGetValue(n, out var l)) r.Output = new { node = n, count = l.Count, entries = l.Take(10).ToList() };
        }
        private void ExecNodeWrite(XJSONParser.ASTNode node, Dictionary<string, string> p)
        {
            var n = p.GetValueOrDefault("name"); var d = p.GetValueOrDefault("data");
            if (n != null && d != null) { try { using var doc = System.Text.Json.JsonDocument.Parse(d); var dict = new Dictionary<string, object>(); foreach (var prop in doc.RootElement.EnumerateObject()) dict[prop.Name] = prop.Value.GetRawText(); NodeInsert(n, dict); } catch { } }
        }
        private void ExecNodeQuery(XJSONParser.ASTNode node, Dictionary<string, string> p, RuntimeResult r)
        {
            var n = p.GetValueOrDefault("name"); var k = p.GetValueOrDefault("key"); var v = p.GetValueOrDefault("value");
            if (n != null) r.Output = NodeQuery(n, k, v).Take(10).ToList();
        }
        private void ExecNodeSearch(XJSONParser.ASTNode node, Dictionary<string, string> p, RuntimeResult r)
        {
            var n = p.GetValueOrDefault("name"); var t = p.GetValueOrDefault("term");
            if (n != null && t != null) r.Output = NodeSearch(n, t).Take(10).ToList();
        }
        private bool EvaluateCondition(string c)
        {
            c = c.Trim().ToLowerInvariant();
            if (c == "true" || c == "1") return true;
            if (c.StartsWith("$") && _state.TryGetValue(c.Substring(1), out var v)) { var s = v?.ToString()?.ToLowerInvariant(); return s == "true" || s == "1"; }
            return false;
        }
        private class RuntimeHaltException : Exception { }
    }

    internal sealed class XCFETurnContext
    {
        public XCFETurnContext(string text, int memoryLimit, XCFETurnResult turn)
        {
            Text = text;
            MemoryLimit = memoryLimit;
            Turn = turn;
        }

        public string Text { get; }
        public int MemoryLimit { get; }
        public XCFETurnResult Turn { get; }
        public int StepCount { get; set; }
        public bool Complete { get; set; }
        public bool YieldedAtSek { get; set; }
        public FoldStepResult LastStep { get; set; }
    }

    internal sealed class FoldStepResult
    {
        public bool Accepted { get; private set; }
        public string Reason { get; private set; }

        public static FoldStepResult Accept(string reason = "")
            => new FoldStepResult { Accepted = true, Reason = reason ?? "" };

        public static FoldStepResult Reject(string reason)
            => new FoldStepResult { Accepted = false, Reason = reason ?? "Fold rejected." };
    }

    /// <summary>
    /// Semantic contract emitted before concrete model selection.
    /// </summary>
    public sealed class XCFETurnResult
    {
        public bool Success { get; set; }
        public bool Routed { get; set; }
        public string Error { get; set; }
        public string Intent { get; set; }
        public string Brain { get; set; }
        public string Fold { get; set; }
        public double Confidence { get; set; }
        public bool Fallback { get; set; }
        public string FallbackReason { get; set; }
        public List<string> MatchedNgrams { get; set; } = new();
        public List<string> Tools { get; set; } = new();
        public List<XCFEMemoryRef> Memories { get; set; } = new();
        public XCFECapabilityRequest Requirements { get; set; } = new();
        public List<string> FoldTrace { get; set; } = new();

        /// <summary>
        /// @node contributions produced during Pop. These are local semantic
        /// recognitions, not routed agents. XCFE may use them when resolving
        /// domain programs or capability contracts.
        /// </summary>
        public List<NodeContribution> Contributions { get; set; } = new();
    }

    public sealed class XCFEMemoryRef
    {
        public string Id { get; set; }
        public string Node { get; set; }
        public double Score { get; set; }
        public List<string> MatchedTerms { get; set; } = new();
        public Dictionary<string, object> Data { get; set; } = new();
    }

    public sealed class XCFECapabilityRequest
    {
        public bool Chat { get; set; }
        public bool Code { get; set; }
        public bool Math { get; set; }
        public bool Tools { get; set; }
        public bool Vision { get; set; }
        public bool LongContext { get; set; }
        public double Reasoning { get; set; }
    }

    public class RuntimeResult
    {
        public bool Success { get; set; }
        public bool Halted { get; set; }
        public string Error { get; set; }
        public object Output { get; set; }
        public List<string> Logs { get; set; } = new();
        public Dictionary<string, object> State { get; set; } = new();
        public List<string> Tasks { get; set; } = new();
        public List<string> Events { get; set; } = new();
    }

    internal class RuntimeTask
    {
        public string Name { get; set; }
        public XJSONParser.ASTNode Handler { get; set; }
    }

    internal class RuntimeEvent
    {
        public string Name { get; set; }
        public XJSONParser.ASTNode Handler { get; set; }
    }
}