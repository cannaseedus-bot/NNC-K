using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// SemanticInference — Wraps ModelBackend with XCFE phase awareness,
    /// capability scoring, multi-model routing, and inference history.
    /// Bridges the SemanticTensorEngine phase matrix to actual model calls.
    /// </summary>
    public class SemanticInference
    {
        private readonly ModelBackend _backend;
        private readonly GravityWellPlanner _planner;
        private string _currentPhase = "Sek";
        private readonly List<InferenceRecord> _history = new();

        public class InferenceRecord
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);
            public string Prompt { get; set; }
            public string Response { get; set; }
            public string Model { get; set; }
            public string Phase { get; set; }
            public double Quality { get; set; }
            public long Tokens { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
            public bool HadTools { get; set; }
            public List<string> ToolsCalled { get; set; } = new();
        }

        public class RouteDecision
        {
            public string ModelName { get; set; }
            public double Score { get; set; }
            public string Phase { get; set; }
            public string Reason { get; set; }
            public InferenceRecord Record { get; set; }
        }

        public SemanticInference()
        {
            _backend = new ModelBackend();
            _planner = new GravityWellPlanner();
        }

        public ModelBackend Backend => _backend;
        public string CurrentPhase => _currentPhase;
        public IReadOnlyList<InferenceRecord> History => _history.AsReadOnly();

        // ---- Phase-aware routing ----

        public void SetPhase(string phase)
        {
            _currentPhase = phase;
            var plan = _planner.AutoPlan($"Phase:{phase}", new[] { "inference" });
        }

        public RouteDecision Route(string prompt, string[] availableModels = null)
        {
            // Compatibility entry point. K'UHUL/XCFE should normally supply the
            // capability contract via the overload below.
            return Route(prompt, BuildCompatibilityRequirements(prompt), availableModels);
        }

        public RouteDecision Route(
            string prompt,
            XCFECapabilityRequest requirements,
            string[] availableModels = null)
        {
            requirements ??= new XCFECapabilityRequest { Chat = true, Reasoning = 0.25 };

            var scores = new List<(string name, double score, string reason)>();
            foreach (var model in availableModels ?? new[] { "local", "deepseek" })
            {
                var score = ScoreModelForRequirements(model, requirements);
                var reason = $"xcfe: reasoning={requirements.Reasoning:F2}, code={requirements.Code}, math={requirements.Math}, tools={requirements.Tools}, vision={requirements.Vision}, long={requirements.LongContext}";
                scores.Add((model, score, reason));
            }

            var best = scores.OrderByDescending(s => s.score).First();
            return new RouteDecision
            {
                ModelName = best.name,
                Score = best.score,
                Phase = _currentPhase,
                Reason = best.reason,
                Record = new InferenceRecord { Prompt = prompt, Model = best.name, Phase = _currentPhase }
            };
        }

        // Retained for callers that still inspect complexity, but it is no longer
        // the control-flow authority. XCFE emits XCFECapabilityRequest.
        public ComplexityScore EstimateComplexity(string prompt)
        {
            var req = BuildCompatibilityRequirements(prompt);
            return new ComplexityScore
            {
                Score = req.Reasoning,
                Length = prompt?.Length ?? 0,
                HasCode = req.Code,
                HasMath = req.Math,
                HasReasoning = req.Reasoning >= 0.45,
                HasToolIntent = req.Tools
            };
        }

        public class ComplexityScore
        {
            public double Score { get; set; }
            public int Length { get; set; }
            public bool HasCode { get; set; }
            public bool HasMath { get; set; }
            public bool HasReasoning { get; set; }
            public bool HasToolIntent { get; set; }
        }

        private static XCFECapabilityRequest BuildCompatibilityRequirements(string prompt)
        {
            var p = (prompt ?? "").ToLowerInvariant();
            var req = new XCFECapabilityRequest
            {
                Chat = true,
                Code = p.Contains("code") || p.Contains("function") || p.Contains("class ") || p.Contains("def "),
                Math = p.Contains("math") || p.Contains("calculate") || p.Contains("equation"),
                Tools = p.Contains("search") || p.Contains("find") || p.Contains("fetch") || p.Contains("read"),
                Vision = p.Contains("image") || p.Contains("picture") || p.Contains("vision"),
                LongContext = (prompt?.Length ?? 0) > 4000,
                Reasoning = 0.25
            };
            if (p.Contains("why") || p.Contains("explain") || p.Contains("reason") || p.Contains("analyze")) req.Reasoning += 0.20;
            if (req.Code || req.Math) req.Reasoning += 0.15;
            if (req.Tools) req.Reasoning += 0.10;
            req.Reasoning = Math.Min(1.0, req.Reasoning);
            return req;
        }

        private double ScoreModelForRequirements(string model, XCFECapabilityRequest req)
        {
            double score = model == "deepseek" ? 0.80 : 0.70;
            if (req.Code) score += model == "deepseek" ? 0.10 : 0.03;
            if (req.Math) score += model == "deepseek" ? 0.07 : 0.03;
            score += req.Reasoning * 0.08;
            if (req.Tools) score += 0.02;
            if (req.LongContext) score += 0.02;
            return Math.Min(1.0, score);
        }

        // ---- Record keeping ----

        public InferenceRecord RecordInference(string prompt, string response, string model, long tokens = 0)
        {
            var record = new InferenceRecord
            {
                Prompt = prompt,
                Response = response,
                Model = model,
                Phase = _currentPhase,
                Tokens = tokens,
                Timestamp = DateTime.UtcNow
            };
            _history.Add(record);
            if (_history.Count > 1000) _history.RemoveAt(0);
            return record;
        }

        public List<InferenceRecord> GetHistoryByPhase(string phase)
        {
            return _history.Where(h => h.Phase == phase).OrderByDescending(h => h.Timestamp).ToList();
        }

        // ---- Phase chain integration ----

        [Obsolete("FoldAlgebra/XCFERuntime owns phase transitions. Use SetPhase only when XCFE explicitly projects a fold into inference.")]
        public string AdvancePhase(string prompt)
        {
            // Compatibility shim: never infer control flow from prompt keywords.
            // The resident K'UHUL/XCFE scheduler is the phase authority.
            return _currentPhase;
        }

    }
}
