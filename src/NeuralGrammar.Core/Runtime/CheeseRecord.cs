using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NeuralGrammar.Core.Runtime
{
    /// <summary>
    /// CHEESE judgment emitted after Xul. References CollapseProof by hash.
    /// Reinforcement is edge-level, not response-level.
    /// </summary>
    public sealed class CheeseRecord
    {
        public string SessionId { get; set; } = string.Empty;
        public long Tick { get; set; }
        public string CollapseProofHash { get; set; } = string.Empty;
        public List<CheeseJudgment> Judgments { get; set; } = new List<CheeseJudgment>();
        public List<string> Invariants { get; set; } = new List<string>();
        public DateTimeOffset JudgedAt { get; set; }
        public string ProvenanceHash { get; set; } = string.Empty;

        public string ComputeHash()
        {
            var sb = new StringBuilder();
            sb.Append(SessionId).Append('|').Append(Tick).Append('|').Append(CollapseProofHash);
            foreach (var j in Judgments.OrderBy(x => x.Edge.Source).ThenBy(x => x.Edge.Relation).ThenBy(x => x.Edge.Target))
                sb.Append('|').Append(j.Verdict).Append(':').Append(j.Edge.ToString()).Append(':').Append(j.Reward.ToString("F6"));
            foreach (var i in Invariants.OrderBy(x => x))
                sb.Append('|').Append(i);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public void Seal()
        {
            ProvenanceHash = ComputeHash();
        }
    }

    public sealed class CheeseJudgment
    {
        public CollapsedEdge Edge { get; set; } = new CollapsedEdge();
        public CheeseVerdict Verdict { get; set; }
        public double Reward { get; set; }
        public string Rationale { get; set; } = string.Empty;
    }

    public enum CheeseVerdict
    {
        Accepted,
        Rejected,
        Guarded
    }
}
