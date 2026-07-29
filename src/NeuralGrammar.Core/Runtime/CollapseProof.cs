using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NeuralGrammar.Core.Runtime
{
    /// <summary>
    /// Post-Xul collapse artifact. Describes what K'UHUL selected, not what was proposed.
    /// CHEESE references this structure for reinforcement; it never mutates it.
    /// </summary>
    public sealed class CollapseProof
    {
        public string SessionId { get; set; } = string.Empty;
        public long Tick { get; set; }
        public string Intent { get; set; } = string.Empty;
        public string Brain { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public List<CollapsedEdge> SelectedEdges { get; set; } = new List<CollapsedEdge>();
        public List<string> RejectedNodeIds { get; set; } = new List<string>();
        public List<string> ContributionHashes { get; set; } = new List<string>();
        public string FoldTraceHash { get; set; } = string.Empty;
        public DateTimeOffset CollapsedAt { get; set; }

        public string ComputeHash()
        {
            var sb = new StringBuilder();
            sb.Append(SessionId).Append('|').Append(Tick).Append('|')
              .Append(Intent).Append('|').Append(Brain).Append('|')
              .Append(Confidence.ToString("F6")).Append('|')
              .Append(FoldTraceHash);
            foreach (var e in SelectedEdges.OrderBy(x => x.Source).ThenBy(x => x.Relation).ThenBy(x => x.Target))
                sb.Append('|').Append(e.ToString());
            foreach (var h in ContributionHashes.OrderBy(x => x))
                sb.Append('|').Append(h);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }
    }

    public sealed class CollapsedEdge
    {
        public string Source { get; set; } = string.Empty;
        public string Relation { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public EdgeGuard Guard { get; set; } = EdgeGuard.None;
        public double Weight { get; set; }

        public override string ToString() => $"{Source}--[{Relation}:{Guard}]-->{Target}:{Weight:F4}";
    }

    public enum EdgeGuard
    {
        None,
        Accepted,
        Rejected,
        Guarded
    }
}
