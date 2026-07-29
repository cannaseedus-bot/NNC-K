using System;
using System.Collections.Generic;
using System.Linq;
using NeuralGrammar.Core;

namespace NeuralGrammar.Core.XCFE
{
    public class LoRAAdapter
    {
        private readonly int _rank = 16;
        private readonly int _inputDim = 64;
        private Tensor _loraA;
        private Tensor _loraB;
        private double _learnedBias;
        private string _topic;
        private int _trainingSamples;

        private readonly Dictionary<string, string> _tokenCache = new();

        public string Topic => _topic;
        public int Rank => _rank;
        public int TrainingSamples => _trainingSamples;
        public IReadOnlyDictionary<string, string> TokenCache => _tokenCache;
        public MicronautNode SourceNode { get; set; }

        /// <summary>Create a LoRA adapter seeded from a micronaut node's topic.</summary>
        public static LoRAAdapter FromMicronaut(MicronautNode node, int inputDim = 64, int rank = 16)
        {
            var adapter = new LoRAAdapter(node.Subject, inputDim, rank)
            {
                SourceNode = node
            };

            // Pre-train with the micronaut's subject tokens.
            var subjectTokens = node.Subject
                .Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.ToLowerInvariant())
                .ToArray();

            if (subjectTokens.Length > 0)
                adapter.Train(subjectTokens);

            return adapter;
        }

        public LoRAAdapter(string topic, int inputDim = 64, int rank = 16)
        {
            _topic = topic;
            _inputDim = inputDim;
            _rank = Math.Min(rank, inputDim);
            _loraA = new Tensor(_inputDim, _rank);
            _loraB = new Tensor(_rank, _inputDim);
            _learnedBias = 0;
        }

        public void Train(string[] tokens)
        {
            if (tokens == null || tokens.Length == 0) return;
            _trainingSamples++;

            var cooccurrence = new Dictionary<string, Dictionary<string, int>>();
            for (int i = 0; i < tokens.Length - 1; i++)
            {
                var a = tokens[i].ToLowerInvariant();
                var b = tokens[i + 1].ToLowerInvariant();
                if (!cooccurrence.ContainsKey(a)) cooccurrence[a] = new Dictionary<string, int>();
                if (!cooccurrence[a].ContainsKey(b)) cooccurrence[a][b] = 0;
                cooccurrence[a][b]++;
                if (!_tokenCache.ContainsKey(a))
                    _tokenCache[a] = string.Join(" ", tokens);
            }
            if (tokens.Length > 0)
            {
                var last = tokens[^1].ToLowerInvariant();
                if (!_tokenCache.ContainsKey(last))
                    _tokenCache[last] = string.Join(" ", tokens);
            }

            var uniqueTokens = cooccurrence.Keys.ToList();
            int n = Math.Min(uniqueTokens.Count, _inputDim);
            var data = new double[_inputDim, _inputDim];
            for (int i = 0; i < n; i++)
            {
                var token = uniqueTokens[i];
                if (!cooccurrence.ContainsKey(token)) continue;
                foreach (var kv in cooccurrence[token])
                {
                    int j = uniqueTokens.IndexOf(kv.Key);
                    if (j >= 0 && j < _inputDim)
                        data[i, j] = kv.Value;
                }
            }

            var u = new Tensor(_inputDim, _rank);
            var vT = new Tensor(_rank, _inputDim);
            for (int r = 0; r < _rank; r++)
            {
                var vec = new double[_inputDim];
                var rng = new Random(r * 42 + _trainingSamples);
                for (int i = 0; i < _inputDim; i++)
                    vec[i] = rng.NextDouble() * 2 - 1;

                for (int iter = 0; iter < 20; iter++)
                {
                    var newVec = new double[_inputDim];
                    for (int i = 0; i < _inputDim; i++)
                    {
                        double sum = 0;
                        for (int j = 0; j < _inputDim; j++)
                            sum += data[i, j] * vec[j];
                        newVec[i] = sum;
                    }
                    double norm = Math.Sqrt(newVec.Sum(x => x * x));
                    if (norm > 1e-10)
                        for (int i = 0; i < _inputDim; i++)
                            vec[i] = newVec[i] / norm;
                    else break;
                }

                double singularValue = 0;
                for (int i = 0; i < _inputDim; i++)
                {
                    double rowSum = 0;
                    for (int j = 0; j < _inputDim; j++)
                        rowSum += data[i, j] * vec[j];
                    singularValue += rowSum * vec[i];
                    u.Data[i, r] = vec[i];
                }
                double sv = Math.Sqrt(Math.Abs(singularValue));
                for (int i = 0; i < _inputDim; i++)
                    for (int j = 0; j < _inputDim; j++)
                        data[i, j] -= sv * u.Data[i, r] * u.Data[j, r];
                for (int j = 0; j < _inputDim; j++)
                    vT.Data[r, j] = u.Data[j, r] * sv;
            }
            _loraA = u;
            _loraB = vT;

            double totalTerms = tokens.Length;
            double totalDistinct = tokens.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            _learnedBias = Math.Tanh(totalDistinct / Math.Max(totalTerms, 1));
        }

        public void TrainFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var tokens = text.ToLowerInvariant()
                .Split(new[] { ' ', '\n', '\r', '\t', '.', ',', '!', '?', ';', ':' },
                       StringSplitOptions.RemoveEmptyEntries);
            Train(tokens);
        }

        public Tensor Apply(Tensor input)
        {
            var cols = input.Data.GetLength(1);
            var rows = input.Data.GetLength(0);
            int inputLen = Math.Min(cols, _inputDim);

            var flat = new double[1, _inputDim];
            for (int i = 0; i < inputLen; i++)
                flat[0, i] = input.Data[0, i];

            var intermediate = new double[1, _rank];
            for (int i = 0; i < 1; i++)
                for (int j = 0; j < _rank; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < _inputDim; k++)
                        sum += flat[i, k] * _loraA.Data[k, j];
                    intermediate[i, j] = sum;
                }

            var delta = new double[1, _inputDim];
            for (int i = 0; i < 1; i++)
                for (int j = 0; j < _inputDim; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < _rank; k++)
                        sum += intermediate[i, k] * _loraB.Data[k, j];
                    delta[i, j] = sum;
                }

            double alpha = 0.5 + _learnedBias * 0.5;
            var output = new double[1, _inputDim];
            for (int i = 0; i < _inputDim; i++)
                output[0, i] = flat[0, i] + alpha * delta[0, i];

            return new Tensor(output);
        }

        public bool TryGetCached(string token, out string content)
        {
            return _tokenCache.TryGetValue(token.ToLowerInvariant(), out content);
        }

        public bool AnyTokenCached(string[] queryTokens, out string content)
        {
            content = null;
            foreach (var token in queryTokens)
            {
                if (_tokenCache.TryGetValue(token.ToLowerInvariant(), out content))
                    return true;
            }
            return false;
        }
    }

    public class CascadeRouter
    {
        private const double TIER1_THRESHOLD = 0.8;
        private const double TIER2_THRESHOLD = 0.3;

        private readonly BrainRouter _brainRouter;
        private readonly Dictionary<string, LoRAAdapter> _topicAdapters = new();
        private readonly List<string> _routingHistory = new();

        public CascadeRouter(BrainRouter brainRouter = null)
        {
            _brainRouter = brainRouter ?? new BrainRouter();
        }

        public enum RouteTier { Tier1_Exact, Tier2_LoRA, Tier3_Expansion }

        public class CascadeResult
        {
            public RouteTier Tier { get; set; }
            public string Intent { get; set; }
            public string Target { get; set; }
            public string Fold { get; set; }
            public double Confidence { get; set; }
            public string Content { get; set; }
            public string[] Tools { get; set; }
            public string Topic { get; set; }
            public string Explanation { get; set; }
            public bool Admitted { get; set; }
            public string AdmissionReason { get; set; }
        }

        public void RegisterTopic(string topic, string[] tokens)
        {
            var adapter = new LoRAAdapter(topic);
            adapter.Train(tokens);
            _topicAdapters[topic.ToLowerInvariant()] = adapter;
        }

        public void RegisterTopicFromText(string topic, string text)
        {
            var adapter = new LoRAAdapter(topic);
            adapter.TrainFromText(text);
            _topicAdapters[topic.ToLowerInvariant()] = adapter;
        }

        public bool HasTopic(string topic) =>
            _topicAdapters.ContainsKey(topic.ToLowerInvariant());

        public LoRAAdapter GetAdapter(string topic) =>
            _topicAdapters.TryGetValue(topic.ToLowerInvariant(), out var a) ? a : null;

        public IReadOnlyDictionary<string, LoRAAdapter> AllAdapters => _topicAdapters;

        /// <summary>
        /// Confidence escalation after XCFE/K'UHUL has already selected and
        /// admitted the semantic route. CascadeRouter does not own intent routing.
        /// </summary>
        public CascadeResult RouteAdmitted(
            string text,
            BrainRouter.RouteResult brainResult,
            bool admitted,
            string admissionReason = null,
            HybridSearch searchEngine = null)
        {
            if (!admitted)
                return Denied(text, admissionReason ?? "XCFE admission denied");

            if (brainResult == null)
                return Denied(text, "No admitted BrainRouter result supplied");

            if (string.IsNullOrWhiteSpace(text))
                return Tier3FromRoute(brainResult, "Empty input", "No content to adapt");

            var queryTokens = text.ToLowerInvariant()
                .Split(new[] { ' ', '\n', '\r', '\t', '.', ',', '!', '?', ';', ':' },
                       StringSplitOptions.RemoveEmptyEntries);

            if (brainResult.Confidence >= TIER1_THRESHOLD &&
                brainResult.MatchedNgrams != null &&
                brainResult.MatchedNgrams.Length > 0)
            {
                foreach (var adapter in _topicAdapters.Values)
                {
                    if (adapter.AnyTokenCached(queryTokens, out var cachedContent))
                    {
                        LogRoute("Tier1", text, brainResult.Target);
                        return new CascadeResult
                        {
                            Tier = RouteTier.Tier1_Exact,
                            Intent = brainResult.Intent,
                            Target = brainResult.Target,
                            Fold = brainResult.Fold,
                            Confidence = brainResult.Confidence,
                            Content = cachedContent,
                            Tools = brainResult.Tools,
                            Topic = adapter.Topic,
                            Admitted = true,
                            AdmissionReason = admissionReason,
                            Explanation = $"Exact token match. No inference. Cache hit: {adapter.Topic}"
                        };
                    }
                }

                LogRoute("Tier1", text, brainResult.Target);
                return new CascadeResult
                {
                    Tier = RouteTier.Tier1_Exact,
                    Intent = brainResult.Intent,
                    Target = brainResult.Target,
                    Fold = brainResult.Fold,
                    Confidence = brainResult.Confidence,
                    Tools = brainResult.Tools,
                    Admitted = true,
                    AdmissionReason = admissionReason,
                    Explanation = $"High-confidence admitted route ({brainResult.Confidence:F2})."
                };
            }

            if (brainResult.Confidence >= TIER2_THRESHOLD)
            {
                LoRAAdapter bestAdapter = null;
                string bestTopic = null;
                int bestMatches = 0;

                foreach (var kv in _topicAdapters)
                {
                    int matches = queryTokens.Count(token => kv.Value.TokenCache.ContainsKey(token));
                    if (matches > bestMatches)
                    {
                        bestMatches = matches;
                        bestAdapter = kv.Value;
                        bestTopic = kv.Key;
                    }
                }

                LogRoute("Tier2", text, brainResult.Target);
                return new CascadeResult
                {
                    Tier = RouteTier.Tier2_LoRA,
                    Intent = brainResult.Intent,
                    Target = brainResult.Target,
                    Fold = brainResult.Fold,
                    Confidence = brainResult.Confidence,
                    Tools = brainResult.Tools,
                    Topic = bestTopic,
                    Admitted = true,
                    AdmissionReason = admissionReason,
                    Explanation = bestAdapter != null
                        ? $"LoRA adaptation on '{bestTopic}' ({bestMatches} tokens). Confidence: {brainResult.Confidence:F2}."
                        : $"Tier2 admitted route ({brainResult.Confidence:F2}). No topic adapter found."
                };
            }

            LogRoute("Tier3", text, brainResult.Target);
            return Tier3FromRoute(
                brainResult,
                text,
                brainResult.FallbackReason ?? "Low confidence — admitted expansion");
        }

        [Obsolete("XCFE/K'UHUL owns semantic routing. Supply an admitted BrainRouter result to RouteAdmitted().", true)]
        public CascadeResult Route(string text, HybridSearch searchEngine = null) =>
            throw new NotSupportedException(
                "CascadeRouter no longer performs top-level semantic routing.");

        public CascadeResult SearchAcrossTopics(string query, HybridSearch engine)
        {
            if (engine == null)
                return Denied(query, "HybridSearch engine not provided");

            var result = engine.Search(query);

            if (result.Results.Count > 0 && result.Results[0].Score >= TIER1_THRESHOLD)
            {
                var top = result.Results[0];
                return new CascadeResult
                {
                    Tier = RouteTier.Tier1_Exact,
                    Confidence = top.Score,
                    Content = top.Content,
                    Explanation = $"Hybrid search exact. Score: {top.Score:F2}. Doc: {top.DocId}",
                    Intent = "retrieve",
                    Admitted = true,
                    AdmissionReason = "HybridSearch retrieval result"
                };
            }

            if (result.Results.Count > 0 && result.Results[0].Score >= TIER2_THRESHOLD)
            {
                var top = result.Results[0];
                return new CascadeResult
                {
                    Tier = RouteTier.Tier2_LoRA,
                    Confidence = top.Score,
                    Content = top.Explanation.Preview,
                    Explanation = $"Hybrid search semantic. Score: {top.Score:F2}. Doc: {top.DocId}",
                    Intent = "retrieve",
                    Admitted = true,
                    AdmissionReason = "HybridSearch retrieval result"
                };
            }

            return new CascadeResult
            {
                Tier = RouteTier.Tier3_Expansion,
                Intent = "retrieve",
                Confidence = 0,
                Admitted = true,
                AdmissionReason = "HybridSearch completed",
                Explanation = $"No strong matches ({result.TotalMatches} candidates)"
            };
        }

        public IReadOnlyList<string> RoutingHistory => _routingHistory;
        public int TotalRoutes => _routingHistory.Count;
        public int Tier1Count => _routingHistory.Count(h => h.Contains("[Tier1]"));
        public int Tier2Count => _routingHistory.Count(h => h.Contains("[Tier2]"));
        public int Tier3Count => _routingHistory.Count(h => h.Contains("[Tier3]"));

        private CascadeResult Tier3FromRoute(
            BrainRouter.RouteResult route,
            string text,
            string reason)
        {
            return new CascadeResult
            {
                Tier = RouteTier.Tier3_Expansion,
                Intent = route?.Intent ?? "expand",
                Target = route?.Target ?? "XM-1",
                Fold = route?.Fold ?? "PATTERN_FOLD",
                Tools = route?.Tools ?? Array.Empty<string>(),
                Confidence = route?.Confidence ?? 0,
                Admitted = true,
                Explanation = $"Tier3 — {reason}. Using the already-admitted expansion route."
            };
        }

        private CascadeResult Denied(string text, string reason)
        {
            LogRoute("DENIED", text ?? "", "none");
            return new CascadeResult
            {
                Tier = RouteTier.Tier3_Expansion,
                Intent = "denied",
                Target = null,
                Fold = null,
                Tools = Array.Empty<string>(),
                Confidence = 0,
                Admitted = false,
                AdmissionReason = reason,
                Explanation = $"Cascade denied — {reason}"
            };
        }

        private void LogRoute(string tier, string input, string target)
        {
            string truncated = input.Length > 60 ? input.Substring(0, 60) + "..." : input;
            _routingHistory.Add($"[{tier}] \"{truncated}\" -> {target}");
        }
    }
}
