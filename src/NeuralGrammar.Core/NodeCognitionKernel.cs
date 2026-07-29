#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// Reference engine for @node semantic cognition.
    ///
    /// This engine implements the local thinking cycle that classic ELIZA
    /// pioneered — recognize, capture, relate, recall, decide, articulate —
    /// but applies it to generic semantic nodes (hypothesis, contradiction,
    /// evidence, comparison, etc.).
    ///
    /// Authority model:
    ///   - This engine recognizes patterns and emits NodeContribution records.
    ///   - It does NOT own fold scheduling, admission control, backend dispatch,
    ///     durable learning, or domain selection.
    ///   - FoldAlgebra / XCFERuntime owns the fold wheel.
    ///   - XCFE policy / verifier owns admission.
    ///   - SemanticInference / ModelBackend owns backend dispatch.
    ///   - Promotion.cs (future) owns durable learning gates.
    ///
    /// The engine is stateless with respect to conversation history. Affect and
    /// memory are supplied by callers so authority boundaries remain clean.
    /// </summary>
    public sealed class NodeCognitionKernel
    {
        private readonly List<SemanticNode> _nodes = new();
        private readonly object _sync = new();
        private readonly Random _rng;
        private readonly Dictionary<string, Regex> _regexCache = new();
        private readonly ReasoningPipeline _reasoning;

        public NodeCognitionKernel(int? seed = null)
        {
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
            _reasoning = new ReasoningPipeline(this, seed);
        }

        /// <summary>Full reasoning pipeline (RECOGNIZE → RELATE → REMEMBER → ARTICULATE + AFFECT).</summary>
        public ReasoningPipeline Reasoning => _reasoning;

        public IReadOnlyList<SemanticNode> Nodes
        {
            get { lock (_sync) return _nodes.ToList(); }
        }

        public int Count { get { lock (_sync) return _nodes.Count; } }

        // ── Node registration ────────────────────────────────────────────────

        public void Register(SemanticNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (string.IsNullOrWhiteSpace(node.Id))
                node.Id = $"node_{ComputeHash(node.Pattern + node.Neighborhood).Substring(0, 12)}";

            lock (_sync)
            {
                var existing = _nodes.FindIndex(n => n.Id == node.Id);
                if (existing >= 0) _nodes[existing] = node;
                else _nodes.Add(node);
                _regexCache.Remove(node.Id);
            }
        }

        public void RegisterRange(IEnumerable<SemanticNode> nodes)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            foreach (var n in nodes) Register(n);
        }

        public bool Remove(string id)
        {
            lock (_sync)
            {
                var idx = _nodes.FindIndex(n => n.Id == id);
                if (idx < 0) return false;
                _nodes.RemoveAt(idx);
                _regexCache.Remove(id);
                return true;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _nodes.Clear();
                _regexCache.Clear();
            }
        }

        // ── Recognition ────────────────────────────────────────────────────

        public IReadOnlyList<NodeMatch> Recognize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return Array.Empty<NodeMatch>();

            var results = new List<NodeMatch>();
            List<SemanticNode> snapshot;
            lock (_sync) snapshot = _nodes.ToList();

            foreach (var node in snapshot)
            {
                var regex = GetRegex(node);
                if (regex == null) continue;

                foreach (Match m in regex.Matches(input))
                {
                    if (!m.Success) continue;
                    var captures = ExtractCaptures(m, node);
                    results.Add(new NodeMatch
                    {
                        NodeId = node.Id,
                        NodeName = node.Name,
                        Pattern = node.Pattern,
                        Match = m.Value,
                        MatchIndex = m.Index,
                        MatchLength = m.Length,
                        Captures = captures,
                        Confidence = ScoreMatch(node, m, input, captures),
                        Neighborhood = node.Neighborhood,
                        Intent = node.Intent,
                        Tags = node.Tags.ToList(),
                        ResponseTemplates = node.ResponseTemplates.ToList()
                    });
                }
            }

            return results
                .OrderByDescending(r => r.Confidence)
                .ThenByDescending(r => snapshot.First(n => n.Id == r.NodeId).Rank)
                .ToList();
        }

        public NodeContribution? Emit(string input, IReadOnlyDictionary<string, object>? context = null)
        {
            var matches = Recognize(input);
            if (matches.Count == 0) return null;
            return Articulate(matches[0], input, context);
        }

        public IReadOnlyList<NodeContribution> EmitAll(string input, IReadOnlyDictionary<string, object>? context = null)
        {
            var matches = Recognize(input);
            return matches.Select(m => Articulate(m, input, context)).ToList();
        }

        /// <summary>
        /// Convenience: run the full reasoning pipeline using only generic node-population
        /// patterns. For context-specific reasoning use Reasoning.Reason().
        /// </summary>
        public NodeContribution? EmitReasoned(string input, ReasoningContext? context = null)
        {
            var ctx = context ?? new ReasoningContext();
            return _reasoning.Reason(ctx, input, "Pop", null);
        }

        // ── Articulation ───────────────────────────────────────────────────

        public NodeContribution Articulate(
            NodeMatch match,
            string input,
            IReadOnlyDictionary<string, object>? context = null)
        {
            SemanticNode? node;
            lock (_sync) node = _nodes.FirstOrDefault(n => n.Id == match.NodeId);
            if (node == null) throw new ArgumentException($"Unknown node id {match.NodeId}", nameof(match));

            var templates = node.ResponseTemplates?.Count > 0
                ? node.ResponseTemplates
                : new List<string> { "Can you tell me more about that?" };

            var template = templates[_rng.Next(templates.Count)];
            var text = FillTemplate(template, match.Captures, context);
            var relations = ResolveRelations(node, match);
            var provenance = ComputeProvenanceHash(node, match, input);

            return new NodeContribution
            {
                NodeId = node.Id,
                NodeName = node.Name,
                Fold = node.Fold,
                Neighborhood = node.Neighborhood,
                Intent = node.Intent,
                Text = text,
                Recognition = match.Match,
                Confidence = match.Confidence,
                Captures = new Dictionary<string, string>(match.Captures),
                Relations = relations,
                Tags = node.Tags.ToList(),
                Source = node.Source,
                ProvenanceHash = provenance
            };
        }

        // ── KAST bridge ──────────────────────────────────────────────────────

        public KastDocument ToKastDocument(string input)
        {
            var contributions = EmitAll(input);
            var nodes = contributions.Select(c => c.ToKastNode()).ToList();
            var edges = new List<KastEdge>();

            for (int i = 0; i < nodes.Count; i++)
            {
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    if (nodes[i].Fold == nodes[j].Fold ||
                        nodes[i].Attributes["neighborhood"] == nodes[j].Attributes["neighborhood"])
                    {
                        edges.Add(new KastEdge
                        {
                            From = nodes[i].Id,
                            To = nodes[j].Id,
                            Kind = KastEdgeKind.Projection,
                            Label = "cohere"
                        });
                    }
                }
            }

            return new KastDocument
            {
                ProtocolId = KastDocument.Protocol,
                SourceKind = "node-cognition",
                SourceId = ComputeHash(input).Substring(0, 16),
                EntryNodeId = nodes.FirstOrDefault()?.Id ?? "",
                Nodes = nodes,
                Edges = edges
            };
        }

        // ── Persistence ────────────────────────────────────────────────────

        public void SaveToDirectory(string directory)
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "semantic_nodes.json");
            var json = JsonSerializer.Serialize(Nodes, JsonOptions.Pretty);
            File.WriteAllText(path, json);
        }

        public int LoadFromDirectory(string directory)
        {
            var path = Path.Combine(directory, "semantic_nodes.json");
            if (!File.Exists(path)) return 0;
            var json = File.ReadAllText(path);
            var nodes = JsonSerializer.Deserialize<List<SemanticNode>>(json, JsonOptions.Pretty);
            if (nodes == null) return 0;
            RegisterRange(nodes);
            return nodes.Count;
        }

        /// <summary>Import legacy ELIZA-style micronaut JSON or NodeContribution JSON as generic semantic nodes.</summary>
        public int ImportLegacyMicronaut(string path)
        {
            if (!File.Exists(path)) return 0;
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var imported = 0;

            // Path 1: legacy "patterns" block (e.g. eliza-therapist.json).
            if (root.TryGetProperty("patterns", out var patternsProp))
            {
                foreach (var prop in patternsProp.EnumerateObject())
                {
                    var id = prop.Name;
                    var p = prop.Value;
                    var synonyms = p.TryGetProperty("synonyms", out var syns)
                        ? syns.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray()
                        : Array.Empty<string>();
                    var neighborhood = p.TryGetProperty("neighborhood", out var nb) ? nb.GetString() ?? "OPEN" : "OPEN";
                    var intent = p.TryGetProperty("intent", out var intProp) ? intProp.GetString() ?? "general_prompt" : "general_prompt";
                    var responses = p.TryGetProperty("responses", out var resp)
                        ? resp.EnumerateArray().Select(x => x.GetString() ?? "").Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                        : new List<string>();

                    var pattern = synonyms.Length > 0
                        ? $"\\b({string.Join("|", synonyms.Select(EscapeRegex))})\\b"
                        : $"\\b{EscapeRegex(id)}\\b";

                    Register(new SemanticNode
                    {
                        Id = $"legacy_{id.Replace(":", "_")}",
                        Name = id,
                        Pattern = pattern,
                        Synonyms = new Dictionary<string, string[]> { [id] = synonyms },
                        Neighborhood = neighborhood,
                        Intent = intent,
                        ResponseTemplates = responses,
                        Source = $"legacy-import:{Path.GetFileName(path)}"
                    });
                    imported++;
                }
            }

            // Path 2: canonical "contributions" block written by Save-Micronaut.
            if (root.TryGetProperty("contributions", out var contribProp) && contribProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in contribProp.EnumerateArray())
                {
                    var nodeName = c.TryGetProperty("node_name", out var nn) ? nn.GetString() ?? "" : "";
                    var recognition = c.TryGetProperty("recognition", out var rec) ? rec.GetString() ?? "" : "";
                    var neighborhood = c.TryGetProperty("neighborhood", out var nb2) ? nb2.GetString() ?? "OPEN" : "OPEN";
                    var fold = c.TryGetProperty("fold", out var f2) ? f2.GetString() ?? "Pop" : "Pop";
                    var intent = c.TryGetProperty("intent", out var int2) ? int2.GetString() ?? "general_prompt" : "general_prompt";
                    var text = c.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";

                    if (string.IsNullOrWhiteSpace(nodeName) || string.IsNullOrWhiteSpace(recognition)) continue;

                    var pattern = $"\\b{EscapeRegex(recognition)}\\b";

                    var tags = new List<string>();
                    if (c.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
                        foreach (var t in tagsProp.EnumerateArray()) tags.Add(t.GetString() ?? "");

                    var relations = new List<string[]>();
                    if (c.TryGetProperty("relations", out var relProp) && relProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var r in relProp.EnumerateArray())
                        {
                            var triple = r.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
                            if (triple.Length >= 3) relations.Add(triple);
                        }
                    }

                    Register(new SemanticNode
                    {
                        Id = $"contrib_{nodeName.ToLowerInvariant().Replace(":", "_")}_{ComputeHash(path).Substring(0, 8)}",
                        Name = nodeName,
                        Pattern = pattern,
                        Neighborhood = neighborhood,
                        Fold = fold,
                        Intent = intent,
                        ResponseTemplates = new List<string> { text },
                        Tags = tags,
                        Relations = relations,
                        Source = $"contrib-import:{Path.GetFileName(path)}"
                    });
                    imported++;
                }
            }

            return imported;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private Regex? GetRegex(SemanticNode node)
        {
            lock (_sync)
            {
                if (_regexCache.TryGetValue(node.Id, out var cached)) return cached;
            }

            var expanded = ExpandPattern(node);
            if (string.IsNullOrWhiteSpace(expanded)) return null;

            Regex? rx = null;
            try
            {
                rx = new Regex(expanded,
                    RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch { }

            if (rx != null)
            {
                lock (_sync) _regexCache[node.Id] = rx;
            }
            return rx;
        }

        private string ExpandPattern(SemanticNode node)
        {
            var pattern = node.Pattern ?? "";
            if (string.IsNullOrWhiteSpace(pattern)) return "";
            var sb = new StringBuilder(pattern);
            foreach (var kv in node.Synonyms)
            {
                var all = new[] { kv.Key }.Concat(kv.Value).Select(EscapeRegex);
                sb.Replace($"@{kv.Key}", $"({string.Join("|", all)})");
            }
            return sb.ToString();
        }

        private static Dictionary<string, string> ExtractCaptures(Match m, SemanticNode node)
        {
            var captures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["0"] = m.Value.Trim()
            };
            foreach (var groupName in m.Groups.Keys)
            {
                if (groupName == "0") continue;
                captures[groupName] = m.Groups[groupName].Value.Trim();
            }
            foreach (var slot in node.Captures.Keys)
            {
                if (captures.ContainsKey(slot)) continue;
                captures[slot] = ExtractWindow(m.Value, 4);
            }
            return captures;
        }

        private static string ExtractWindow(string matchText, int maxWords)
        {
            var words = matchText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", words.Take(maxWords));
        }

        private double ScoreMatch(SemanticNode node, Match m, string input, Dictionary<string, string> captures)
        {
            var score = node.Confidence;
            score *= Math.Min(1.0, 0.7 + (m.Length / (double)Math.Max(1, input.Length)) * 0.6);
            if (captures.Count > 1) score *= Math.Min(1.0, 0.9 + (captures.Count - 1) * 0.05);
            return Math.Min(1.0, Math.Max(0.0, score));
        }

        private static string FillTemplate(string template, Dictionary<string, string> captures, IReadOnlyDictionary<string, object>? context)
        {
            var text = template;
            for (int i = 0; i < 10; i++)
            {
                var placeholder = $"({i})";
                if (captures.TryGetValue(i.ToString(), out var val))
                    text = text.Replace(placeholder, val);
                else if (text.Contains(placeholder) && context != null && context.TryGetValue(i.ToString(), out var ctxVal))
                    text = text.Replace(placeholder, ctxVal?.ToString() ?? "");
            }
            foreach (var kv in captures)
            {
                text = text.Replace($"{{capture:{kv.Key}}}", kv.Value);
                text = text.Replace($"{{{kv.Key}}}", kv.Value);
            }
            return text;
        }

        private static List<string[]> ResolveRelations(SemanticNode node, NodeMatch match)
        {
            var resolved = new List<string[]>();
            foreach (var r in node.Relations)
            {
                if (r == null || r.Length < 3) continue;
                resolved.Add(new[] { Substitute(r[0], match), Substitute(r[1], match), Substitute(r[2], match) });
            }
            return resolved;
        }

        private static string Substitute(string template, NodeMatch match)
        {
            var s = template;
            s = s.Replace("{match}", match.Match);
            s = s.Replace("{node}", match.NodeName);
            s = s.Replace("{neighborhood}", match.Neighborhood);
            foreach (var kv in match.Captures)
            {
                s = s.Replace($"{{{kv.Key}}}", kv.Value);
                s = s.Replace($"{{capture:{kv.Key}}}", kv.Value);
            }
            return s;
        }

        private string ComputeProvenanceHash(SemanticNode node, NodeMatch match, string input)
        {
            var canonical = $"{node.Id}|{node.Pattern}|{match.Match}|{input}";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
            return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static string EscapeRegex(string text) => Regex.Escape(text);

        private static string ComputeHash(string input)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input ?? ""));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }

    /// <summary>Internal firing record for a semantic node.</summary>
    public sealed class NodeMatch
    {
        public string NodeId { get; set; } = "";
        public string NodeName { get; set; } = "";
        public string Pattern { get; set; } = "";
        public string Match { get; set; } = "";
        public int MatchIndex { get; set; }
        public int MatchLength { get; set; }
        public Dictionary<string, string> Captures { get; set; } = new();
        public double Confidence { get; set; }
        public string Neighborhood { get; set; } = "";
        public string Intent { get; set; } = "";
        public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> ResponseTemplates { get; set; } = Array.Empty<string>();
    }
}
