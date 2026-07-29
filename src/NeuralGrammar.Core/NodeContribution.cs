#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// Canonical envelope for one @node contribution inside a @fold.
    ///
    /// A Micronaut response is not a mysterious blob. It is the accumulation
    /// of node contributions produced by the K'UHUL fold cycle:
    ///
    ///     recognize → decompose → relate → recall → evidence → contradict → articulate
    ///
    /// Each contribution carries its own recognition, captures, relations,
    /// evidence, affect, and provenance so the Runtime Inspector can show
    /// *which nodes caused the reasoning trajectory*, not merely which
    /// micronaut answered.
    /// </summary>
    public sealed class NodeContribution
    {
        /// <summary>Identity of this contribution.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 16);

        /// <summary>Owning @node id.</summary>
        public string NodeId { get; set; } = "";

        /// <summary>Human-readable node name, e.g. "hypothesis", "contradiction".</summary>
        public string NodeName { get; set; } = "";

        /// <summary>@fold this contribution inhabits.</summary>
        public string Fold { get; set; } = "Pop";

        /// <summary>Semantic neighborhood, e.g. THOUGHT, FAMILY, EMOTION, SPACE.</summary>
        public string Neighborhood { get; set; } = "";

        /// <summary>Micronaut subject this contribution is attached to.</summary>
        public string Subject { get; set; } = "";

        /// <summary>Capability tag for downstream grouping. Not a runtime authority.</summary>
        public string Capability { get; set; } = "";

        // ── Recognition ─────────────────────────────────────────────────────

        /// <summary>What input pattern triggered this node.</summary>
        public string Recognition { get; set; } = "";

        /// <summary>Named capture slots extracted from the input.</summary>
        public Dictionary<string, string> Captures { get; set; } = new();

        /// <summary>Semantic relation triples asserted by this node.</summary>
        public List<string[]> Relations { get; set; } = new();

        // ── Evidence ────────────────────────────────────────────────────────

        /// <summary>Evidence references (artifact ids, memory ids, facts).</summary>
        public List<string> Evidence { get; set; } = new();

        /// <summary>Domain / neighborhood tags for grouping and inspection.</summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>Confidence [0,1] of this contribution.</summary>
        public double Confidence { get; set; }

        // ── Affect ──────────────────────────────────────────────────────────

        /// <summary>Optional computational affect state at the moment of contribution.</summary>
        public AffectiveState? Affect { get; set; }

        // ── State ───────────────────────────────────────────────────────────

        /// <summary>Input state snapshot before this node fired.</summary>
        public Dictionary<string, object> InputState { get; set; } = new();

        /// <summary>Output state after this node fired.</summary>
        public Dictionary<string, object> OutputState { get; set; } = new();

        // ── Provenance ──────────────────────────────────────────────────────

        /// <summary>Source label, e.g. "node-engine", "legacy-eliza", "persona".</summary>
        public string Source { get; set; } = "node-engine";

        /// <summary>Deterministic hash of node + match + input for replay verification.</summary>
        public string ProvenanceHash { get; set; } = "";

        /// <summary>UTC timestamp of contribution.</summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // ── Convenience projections ─────────────────────────────────────────

        /// <summary>Default display text: the articulation produced by this node.</summary>
        public string Text { get; set; } = "";

        /// <summary>Intent label, e.g. "reflective_question", "hypothesis_probe".</summary>
        public string Intent { get; set; } = "";

        /// <summary>Convert to a KAST structural node.</summary>
        public KastNode ToKastNode()
        {
            var attrs = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["node_name"] = NodeName,
                ["neighborhood"] = Neighborhood,
                ["intent"] = Intent,
                ["recognition"] = Recognition,
                ["confidence"] = Confidence.ToString("G9"),
                ["source"] = Source,
                ["provenance"] = ProvenanceHash
            };
            foreach (var kv in Captures)
                attrs[$"capture:{kv.Key}"] = kv.Value;

            return new KastNode
            {
                Id = $"node:{NodeId}:{Id}",
                Kind = KastNodeKind.Decision,
                Fold = Fold,
                Lane = "agent",
                Glyph = "⧉",
                Opcode = "classify",
                Symbol = NodeName,
                Type = "node-contribution",
                Attributes = new ReadOnlyDictionary<string, string>(attrs)
            };
        }

        /// <summary>Convert to a semantic artifact for the information plane.</summary>
        public SemanticArtifact ToArtifact()
        {
            return new SemanticArtifact
            {
                Id = Id,
                Kind = ArtifactKind.Evidence,
                Status = AdmissionStatus.Pending,
                Subject = NodeName,
                Content = Text,
                Confidence = Confidence,
                Source = Source,
                Tags = Tags?.Count > 0 ? Tags.ToList() : new List<string> { Neighborhood, Capability, Fold },
                Evidence = Evidence.ToList(),
                Relations = Relations.Select(r => string.Join(" ", r)).ToList(),
                Metadata = Captures.ToDictionary(kv => kv.Key, kv => kv.Value)
            };
        }

        /// <summary>Compute a deterministic provenance hash for this contribution.</summary>
        public static string ComputeHash(NodeContribution c)
        {
            var sb = new StringBuilder();
            sb.Append(c.NodeId).Append('|');
            sb.Append(c.NodeName).Append('|');
            sb.Append(c.Fold).Append('|');
            sb.Append(c.Recognition).Append('|');
            foreach (var kv in c.Captures.OrderBy(x => x.Key, StringComparer.Ordinal))
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append('|');
            foreach (var r in c.Relations)
                sb.Append(string.Join(" ", r)).Append('|');

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
            return "sha256:" + Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }

    /// <summary>Computational affect state carried across node contributions.</summary>
    public sealed class AffectiveState
    {
        public double Valence { get; set; }
        public double Arousal { get; set; }
        public double Curiosity { get; set; }
        public double Confidence { get; set; }
        public double Concern { get; set; }
        public double Frustration { get; set; }
        public double Skepticism { get; set; }
        public double Attachment { get; set; }
    }
}
