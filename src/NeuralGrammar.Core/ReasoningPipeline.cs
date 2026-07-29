#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// Micronaut reasoning pipeline as C# node operators.
    ///
    /// Implements the four kernel operations that previously lived in PowerShell:
    ///
    ///     RECOGNIZE → RELATE → REMEMBER → ARTICULATE
    ///
    /// plus the AFFECT operator that modulates traversal and articulation.
    ///
    /// This keeps the critical reasoning path inside NodeCognitionKernel and
    /// removes the PowerShell↔C# boundary crossing. The pipeline is stateless:
    /// callers supply a micronaut context (patterns, relations, memories) and
    /// optional existing affect; the pipeline returns a fully-populated
    /// NodeContribution.
    ///
    /// Authority boundaries:
    ///   - This class performs local node-level reasoning only.
    ///   - It does not schedule folds, admit artifacts, dispatch backends, or
    ///     mutate the filesystem.
    /// </summary>
    public sealed class ReasoningPipeline
    {
        private readonly NodeCognitionKernel _nodeCognition;
        private readonly Random _rng;

        public ReasoningPipeline(NodeCognitionKernel nodeCognition, int? seed = null)
        {
            _nodeCognition = nodeCognition ?? throw new ArgumentNullException(nameof(nodeCognition));
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public NodeCognitionKernel NodeCognition => _nodeCognition;

        /// <summary>
        /// Run the full reasoning pipeline for a query against a micronaut context.
        /// Returns null when nothing fires.
        /// </summary>
        public NodeContribution? Reason(
            ReasoningContext context,
            string query,
            string fold,
            AffectiveState? priorAffect = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(query)) return null;

            var affect = priorAffect ?? context.AffectiveState ?? new AffectiveState
            {
                Valence = 0.0, Arousal = 0.5, Curiosity = 0.6,
                Confidence = 0.65, Concern = 0.3, Frustration = 0.0,
                Skepticism = 0.4, Attachment = 0.0
            };

            // RECOGNIZE — affect broadens detection threshold.
            var recognition = Recognize(context, query, affect);
            if (recognition == null) return null;

            // RELATE — curiosity decides traversal depth.
            var relation = Relate(context, recognition);

            // REMEMBER — concern and skepticism filter memories.
            var memory = Remember(context, recognition, affect);

            // AFFECT — stimulus updates state.
            affect = StepAffect(context, recognition, affect);
            var modulation = ApplyAffectiveModulation(affect, relation);

            // ARTICULATE
            var articulation = Articulate(context, recognition, relation, memory, affect);

            // Apply modulation tags only if not already present.
            if (modulation.HedgeResponse && !articulation.Text.StartsWith("Perhaps ", StringComparison.OrdinalIgnoreCase))
                articulation.Text = "Perhaps " + articulation.Text;
            if (modulation.SeekVerification && !articulation.Text.StartsWith("[verifying] ", StringComparison.OrdinalIgnoreCase))
                articulation.Text = "[verifying] " + articulation.Text;

            // Build the canonical contribution.
            var contribution = new NodeContribution
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 16),
                NodeId = recognition.BestMatch?.NodeId ?? "reasoning_kernel",
                NodeName = "reasoning_kernel",
                Fold = fold,
                Neighborhood = recognition.BestMatch?.Neighborhood ?? "OPEN",
                Subject = context.Subject ?? "",
                Capability = context.Capability ?? "",
                Recognition = recognition.BestMatch?.Match ?? "",
                Captures = recognition.Captures ?? new Dictionary<string, string>(),
                Relations = relation?.Related?.Select(r => r.Relation).ToList() ?? new List<string[]>(),
                Evidence = memory?.Recalled.Select(m => m.Memory).ToList() ?? new List<string>(),
                Confidence = recognition.Confidence,
                Affect = affect,
                Text = articulation.Text,
                Intent = articulation.Intent,
                Source = "reasoning-pipeline",
                ProvenanceHash = ComputeProvenanceHash(context, query, recognition)
            };

            return contribution;
        }

        // ── RECOGNIZE ────────────────────────────────────────────────────────

        public RecognitionResult? Recognize(ReasoningContext context, string query, AffectiveState? affect = null)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            // Prefer explicit node patterns from the micronaut context.
            var nodeMatches = RecognizeFromContext(context, query);

            // Fallback: generic node cognition over the query.
            if (nodeMatches.Count == 0)
            {
                var generic = _nodeCognition.Recognize(query);
                if (generic.Count > 0)
                    nodeMatches = generic;
            }

            if (nodeMatches.Count == 0) return null;

            var best = nodeMatches[0];
            var arousal = affect?.Arousal ?? 0.5;
            var confidence = best.Confidence;
            if (arousal > 0.55 && nodeMatches.Count > 1)
                confidence = Math.Min(1.0, confidence + 0.05);

            var captures = new Dictionary<string, string>(best.Captures);
            var entities = nodeMatches.Select(m => m.NodeName).Distinct().ToList();

            return new RecognitionResult
            {
                BestMatch = best,
                Matches = nodeMatches,
                Entities = entities,
                Captures = captures,
                Confidence = confidence
            };
        }

        private IReadOnlyList<NodeMatch> RecognizeFromContext(ReasoningContext context, string query)
        {
            var results = new List<NodeMatch>();
            if (context.Patterns == null || context.Patterns.Count == 0)
                return results;

            var qlower = query.ToLowerInvariant();
            foreach (var pattern in context.Patterns)
            {
                if (pattern.Synonyms == null || pattern.Synonyms.Count == 0) continue;
                foreach (var syn in pattern.Synonyms)
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(qlower,
                        $@"\b{System.Text.RegularExpressions.Regex.Escape(syn.ToLowerInvariant())}\b"))
                        continue;

                    results.Add(new NodeMatch
                    {
                        NodeId = pattern.Id,
                        NodeName = pattern.Name,
                        Pattern = pattern.Pattern,
                        Match = syn,
                        Captures = new Dictionary<string, string> { ["0"] = syn },
                        Confidence = pattern.Confidence,
                        Neighborhood = pattern.Neighborhood,
                        Intent = pattern.Intent
                    });
                    break;
                }
            }

            return results.OrderByDescending(r => r.Confidence).ToList();
        }

        // ── RELATE ──────────────────────────────────────────────────────────

        public RelationResult Relate(ReasoningContext context, RecognitionResult recognition)
        {
            var related = new List<RelatedTriple>();
            if (context.Relations == null || recognition?.Entities == null)
                return new RelationResult { Related = related };

            foreach (var r in context.Relations)
            {
                if (r == null || r.Length < 3) continue;
                var rStr = string.Join(" ", r).ToLowerInvariant();
                foreach (var e in recognition.Entities)
                {
                    var eName = e.Split(':').LastOrDefault() ?? e;
                    if (rStr.Contains(eName.ToLowerInvariant()))
                    {
                        related.Add(new RelatedTriple
                        {
                            Entity = eName,
                            Relation = r,
                            Direction = string.Equals(r[0], eName, StringComparison.OrdinalIgnoreCase) ? "subject" : "object"
                        });
                    }
                }
            }

            return new RelationResult
            {
                Related = related,
                Confidence = related.Count > 0 ? Math.Min(0.85, 0.30 + related.Count * 0.15) : 0
            };
        }

        // ── REMEMBER ────────────────────────────────────────────────────────

        public MemoryResult Remember(
            ReasoningContext context,
            RecognitionResult recognition,
            AffectiveState? affect = null)
        {
            var recalled = new List<ScoredMemory>();
            if (context.Memories == null || recognition?.Entities == null)
                return new MemoryResult { Recalled = recalled };

            foreach (var m in context.Memories)
            {
                var score = 0;
                foreach (var e in recognition.Entities)
                {
                    var eName = e.Split(':').LastOrDefault()?.Replace('_', ' ') ?? e;
                    if (m.ToLowerInvariant().Contains(eName.ToLowerInvariant())) score++;
                }
                if (score > 0) recalled.Add(new ScoredMemory { Memory = m, Score = score });
            }

            var result = recalled.OrderByDescending(r => r.Score).ToList();

            // Concern boosts risk/conflict memory priority.
            if ((affect?.Concern ?? 0) > 0.50)
            {
                var risk = result.Where(r => System.Text.RegularExpressions.Regex.IsMatch(
                    r.Memory, "collapse|danger|critical|error|contradiction|problem",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
                if (risk.Count > 0)
                    result = risk.Concat(result.Except(risk)).ToList();
            }

            return new MemoryResult
            {
                Recalled = result,
                Confidence = result.Count > 0 ? Math.Min(0.80, 0.30 + result.Count * 0.20) : 0
            };
        }

        // ── AFFECT ──────────────────────────────────────────────────────────

        public AffectiveState StepAffect(
            ReasoningContext context,
            RecognitionResult recognition,
            AffectiveState? existing = null)
        {
            var state = existing ?? new AffectiveState();
            var names = recognition?.Matches?.Select(m => m.NodeName).ToList() ?? new List<string>();

            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var lower = name.ToLowerInvariant();

                if (System.Text.RegularExpressions.Regex.IsMatch(lower, "contradiction|error|inconsistent"))
                {
                    state.Frustration = Math.Min(1.0, state.Frustration + 0.08);
                    state.Curiosity = Math.Min(1.0, state.Curiosity + 0.12);
                }
                if (System.Text.RegularExpressions.Regex.IsMatch(lower, "new|novel|discover"))
                {
                    state.Curiosity = Math.Min(1.0, state.Curiosity + 0.20);
                    state.Arousal = Math.Min(1.0, state.Arousal + 0.10);
                }
                if (System.Text.RegularExpressions.Regex.IsMatch(lower, "confirm|verify|match"))
                {
                    state.Confidence = Math.Min(1.0, state.Confidence + 0.10);
                    state.Skepticism = Math.Max(0.0, state.Skepticism - 0.05);
                }
                if (System.Text.RegularExpressions.Regex.IsMatch(lower, "uncertain|unknown|ambiguous"))
                {
                    state.Confidence = Math.Max(0.0, state.Confidence - 0.15);
                    state.Curiosity = Math.Min(1.0, state.Curiosity + 0.15);
                }
                if (System.Text.RegularExpressions.Regex.IsMatch(lower, "sad|unhappy|depressed|lonely|worry|anxious|fear|afraid"))
                {
                    state.Concern = Math.Min(1.0, state.Concern + 0.12);
                    state.Valence = Math.Max(-1.0, state.Valence - 0.15);
                    state.Arousal = Math.Min(1.0, state.Arousal + 0.18);
                }
                if (System.Text.RegularExpressions.Regex.IsMatch(lower, "happy|glad|joy|better"))
                {
                    state.Valence = Math.Min(1.0, state.Valence + 0.12);
                    state.Arousal = Math.Max(0.0, state.Arousal - 0.05);
                    state.Confidence = Math.Min(1.0, state.Confidence + 0.05);
                }
                if (System.Text.RegularExpressions.Regex.IsMatch(lower, "problem|issue|trouble|difficult|hard|help|need|want"))
                {
                    state.Concern = Math.Min(1.0, state.Concern + 0.10);
                    state.Frustration = Math.Min(1.0, state.Frustration + 0.05);
                }
                if (System.Text.RegularExpressions.Regex.IsMatch(lower, "friend|people|someone|everyone|relationship|family|mother|father"))
                {
                    state.Attachment = Math.Min(1.0, state.Attachment + 0.08);
                    state.Valence = Math.Min(1.0, state.Valence + 0.05);
                }
            }

            // Decay toward baseline.
            state.Arousal = state.Arousal * 0.95 + 0.5 * 0.05;
            state.Frustration = state.Frustration * 0.90;
            state.Skepticism = state.Skepticism * 0.97 + 0.4 * 0.03;

            // Clamp.
            state.Valence = Math.Max(-1.0, Math.Min(1.0, state.Valence));
            state.Arousal = Math.Max(0.0, Math.Min(1.0, state.Arousal));
            state.Curiosity = Math.Max(0.0, Math.Min(1.0, state.Curiosity));
            state.Confidence = Math.Max(0.0, Math.Min(1.0, state.Confidence));
            state.Concern = Math.Max(0.0, Math.Min(1.0, state.Concern));
            state.Frustration = Math.Max(0.0, Math.Min(1.0, state.Frustration));
            state.Skepticism = Math.Max(0.0, Math.Min(1.0, state.Skepticism));
            state.Attachment = Math.Max(0.0, Math.Min(1.0, state.Attachment));

            return state;
        }

        public AffectiveModulation ApplyAffectiveModulation(AffectiveState state, RelationResult? relation)
        {
            return new AffectiveModulation
            {
                ExtraRelationTraversal = state.Curiosity > 0.75,
                HedgeResponse = state.Confidence < 0.35,
                SeekVerification = state.Skepticism > 0.60 || state.Concern > 0.55,
                AbandonPath = state.Frustration > 0.70,
                ElaborateMore = state.Curiosity > 0.80 && state.Arousal > 0.60,
                Collaborative = state.Attachment > 0.30
            };
        }

        // ── ARTICULATE ──────────────────────────────────────────────────────

        public ArticulationResult Articulate(
            ReasoningContext context,
            RecognitionResult recognition,
            RelationResult? relation,
            MemoryResult? memory,
            AffectiveState? affect = null)
        {
            var cap = context.Capability?.ToLowerInvariant() ?? "";
            var best = recognition.BestMatch;

            // Conversation-style articulation: use response templates from the matched node.
            if (cap == "eliza" || cap == "conversation")
            {
                var templates = best?.ResponseTemplates?.ToList();
                if (templates == null || templates.Count == 0)
                    templates = new List<string> { "Can you tell me more about that?" };

                var text = templates[_rng.Next(templates.Count)];
                text = FillTemplate(text, recognition.Captures);

                if (affect != null)
                {
                    if (affect.Concern > 0.60) text = "I sense this concerns you. " + text;
                    if (affect.Valence < -0.20) text += " This seems difficult for you.";
                    if (affect.Curiosity > 0.80) text += " What else comes to mind?";
                }

                return new ArticulationResult
                {
                    Text = text,
                    Intent = best?.Intent ?? "reflective_question",
                    Match = best?.Match ?? "",
                    Confidence = best?.Confidence ?? 0.75,
                    Evidence = new List<string>()
                };
            }

            // Knowledge carrier: compose from memories + relations.
            var facts = new List<string>();
            if (memory?.Recalled != null)
                facts.AddRange(memory.Recalled.Take(2).Select(m => m.Memory));
            if (relation?.Related != null)
                facts.AddRange(relation.Related.Take(2).Select(r => string.Join(" ", r.Relation)));

            var bestFact = facts.Count > 0
                ? facts[0]
                : (context.Response ?? "").Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";

            return new ArticulationResult
            {
                Text = bestFact,
                Intent = "knowledge_lookup",
                Match = recognition.Entities.FirstOrDefault() ?? "",
                Confidence = Math.Max(relation?.Confidence ?? 0, memory?.Confidence ?? 0),
                Evidence = facts
            };
        }

        private static string FillTemplate(string template, Dictionary<string, string> captures)
        {
            var text = template;
            for (int i = 0; i < 10; i++)
            {
                var placeholder = $"({i})";
                if (captures.TryGetValue(i.ToString(), out var val))
                    text = text.Replace(placeholder, val);
            }
            foreach (var kv in captures)
            {
                text = text.Replace($"{{capture:{kv.Key}}}", kv.Value);
                text = text.Replace($"{{{kv.Key}}}", kv.Value);
            }
            return text;
        }

        /// <summary>
        /// Semantic retrieval: match query terms against a knowledge carrier text
        /// and return the best sentence as a NodeContribution.
        /// Implements the default keyword-match fallback from Invoke-Micronaut
        /// as a proper @node operator.
        /// </summary>
        public NodeContribution? Retrieve(
            string query,
            string responseText,
            string subject,
            string capability,
            string fold)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(responseText))
                return null;
            if (responseText.Length < 20)
                return null;

            var qWords = query
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length >= 3)
                .Select(w => w.ToLowerInvariant())
                .Distinct()
                .ToList();

            if (qWords.Count == 0) return null;

            var pattern = string.Join("|", qWords.Select(System.Text.RegularExpressions.Regex.Escape));
            var matches = System.Text.RegularExpressions.Regex.Matches(
                responseText, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (matches.Count == 0) return null;

            var sentences = responseText
                .Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            var bestSentence = sentences
                .Select(s => new
                {
                    Sentence = s,
                    Score = qWords.Count(w => s.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Sentence.Length)
                .FirstOrDefault();

            var best = bestSentence?.Sentence ?? sentences.FirstOrDefault() ?? responseText;
            var wordCount = responseText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var confidence = Math.Min(0.95, Math.Max(0.30, matches.Count / (Math.Max(1, wordCount) * 0.15)));
            var facts = matches.Select(m => m.Value).Distinct().Take(3).ToList();
            var provenance = ComputeProvenanceHash(query, subject, capability, best, facts);

            return new NodeContribution
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 16),
                NodeId = "semantic_retrieval",
                NodeName = "semantic_retrieval",
                Fold = fold,
                Neighborhood = "MEMORY",
                Subject = subject,
                Capability = capability,
                Recognition = query,
                Captures = new Dictionary<string, string> { ["0"] = facts.FirstOrDefault() ?? query },
                Relations = new List<string[]> { new[] { query, "retrieved_from", subject } },
                Evidence = facts,
                Confidence = Math.Round(confidence, 3),
                Text = best,
                Intent = "knowledge_lookup",
                Source = "semantic-retrieval",
                ProvenanceHash = provenance
            };
        }

        private static string ComputeProvenanceHash(string query, string subject, string capability, string best, IReadOnlyList<string> facts)
        {
            var canonical = $"{query}|{subject}|{capability}|{best}|{string.Join("|", facts)}";
            var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
            return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string ComputeProvenanceHash(ReasoningContext context, string query, RecognitionResult recognition)
        {
            var canonical = $"{context.Subject}|{context.Capability}|{query}|{recognition.BestMatch?.NodeName}|{recognition.BestMatch?.Match}";
            var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
            return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }

    // ── Supporting result types ───────────────────────────────────────────

    public sealed class RecognitionResult
    {
        public NodeMatch? BestMatch { get; set; }
        public IReadOnlyList<NodeMatch> Matches { get; set; } = new List<NodeMatch>();
        public IReadOnlyList<string> Entities { get; set; } = new List<string>();
        public Dictionary<string, string> Captures { get; set; } = new();
        public double Confidence { get; set; }
    }

    public sealed class RelationResult
    {
        public IReadOnlyList<RelatedTriple> Related { get; set; } = new List<RelatedTriple>();
        public double Confidence { get; set; }
    }

    public sealed class RelatedTriple
    {
        public string Entity { get; set; } = "";
        public string[] Relation { get; set; } = Array.Empty<string>();
        public string Direction { get; set; } = "";
    }

    public sealed class MemoryResult
    {
        public IReadOnlyList<ScoredMemory> Recalled { get; set; } = new List<ScoredMemory>();
        public double Confidence { get; set; }
    }

    public sealed class ScoredMemory
    {
        public string Memory { get; set; } = "";
        public int Score { get; set; }
    }

    public sealed class ArticulationResult
    {
        public string Text { get; set; } = "";
        public string Intent { get; set; } = "";
        public string Match { get; set; } = "";
        public double Confidence { get; set; }
        public List<string> Evidence { get; set; } = new();
    }

    public sealed class AffectiveModulation
    {
        public bool ExtraRelationTraversal { get; set; }
        public bool HedgeResponse { get; set; }
        public bool SeekVerification { get; set; }
        public bool AbandonPath { get; set; }
        public bool ElaborateMore { get; set; }
        public bool Collaborative { get; set; }
    }

    /// <summary>
    /// Serializable context object supplied by callers (e.g. loaded from a
    /// micronaut JSON file). Mirrors the shape of .learning/micronauts/*.json.
    /// </summary>
    public sealed class ReasoningContext
    {
        public string Id { get; set; } = "";
        public string Subject { get; set; } = "";
        public string Capability { get; set; } = "";
        public string Fold { get; set; } = "Pop";
        public string Response { get; set; } = "";
        public List<ContextPattern> Patterns { get; set; } = new();
        public List<string[]> Relations { get; set; } = new();
        public List<string> Memories { get; set; } = new();
        public AffectiveState? Temperament { get; set; }
        public AffectiveState? AffectiveState { get; set; }
        public Dictionary<string, object> Extra { get; set; } = new();
    }

    public sealed class ContextPattern
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Pattern { get; set; } = "";
        public List<string> Synonyms { get; set; } = new();
        public string Neighborhood { get; set; } = "OPEN";
        public string Intent { get; set; } = "general_prompt";
        public List<string> ResponseTemplates { get; set; } = new();
        public double Confidence { get; set; } = 0.75;
    }
}
