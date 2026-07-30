using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuralGrammar.Core.Runtime
{
    /// <summary>
    /// Production-C orchestrator: post-Xul, build a CollapseProof from the selected semantic edges,
    /// have CHEESE judge it, and persist both artifacts append-only. Keeps judgment in the runtime
    /// (PowerShell only feeds it edges). Does NOT collapse, does NOT promote (BOSS/D is separate).
    /// </summary>
    public sealed class CheesePipeline
    {
        private readonly ProvenanceStore _store;
        private readonly CheeseJudge _judge;

        public CheesePipeline(string provenanceRoot = null)
        {
            _store = new ProvenanceStore(provenanceRoot);
            _judge = new CheeseJudge();
        }

        /// <summary>Build a CollapsedEdge from string parts (PowerShell-friendly). Guard=None so
        /// CheeseJudge applies its heuristic policy (v0).</summary>
        public static CollapsedEdge Edge(string source, string relation, string target, double weight = 1.0)
            => new CollapsedEdge
            {
                Source = source ?? string.Empty,
                Relation = relation ?? string.Empty,
                Target = target ?? string.Empty,
                Guard = EdgeGuard.None,
                Weight = weight
            };

        /// <summary>Judge one collapsed turn: proof -> CheeseJudge -> persist(proof)+persist(record).
        /// Returns the sealed CheeseRecord (or null if there are no edges to judge).</summary>
        // Non-generic IEnumerable params so PowerShell object[] binds cleanly (PS arrays don't
        // coerce to IEnumerable<T>); Cast<>/OfType<> reconstitute the typed values.
        public CheeseRecord JudgeTurn(
            string sessionId, long tick, string intent, string brain, double confidence,
            System.Collections.IEnumerable selectedEdges,
            System.Collections.IEnumerable rejectedNodeIds = null,
            System.Collections.IEnumerable contributionHashes = null,
            string foldTraceHash = null)
        {
            var edges = selectedEdges == null
                ? new List<CollapsedEdge>()
                : selectedEdges.Cast<object>().OfType<CollapsedEdge>().ToList();
            if (edges.Count == 0) return null;   // nothing collapsed -> nothing to judge (truthful)

            var proof = new CollapseProof
            {
                SessionId = sessionId ?? string.Empty,
                Tick = tick,
                Intent = intent ?? string.Empty,
                Brain = brain ?? string.Empty,
                Confidence = confidence,
                SelectedEdges = edges,
                RejectedNodeIds = ToStringList(rejectedNodeIds),
                ContributionHashes = ToStringList(contributionHashes),
                FoldTraceHash = foldTraceHash ?? string.Empty,
                CollapsedAt = DateTimeOffset.UtcNow
            };

            var record = _judge.Judge(proof, null);   // v0: contributions arg unused by Reward
            _store.Save(proof);
            _store.Save(record);
            return record;
        }

        private static List<string> ToStringList(System.Collections.IEnumerable src)
            => src == null
                ? new List<string>()
                : src.Cast<object>().Select(x => x?.ToString() ?? string.Empty)
                     .Where(s => !string.IsNullOrEmpty(s)).ToList();

        /// <summary>Compact per-turn verdict summary for @flux / the chat badge.</summary>
        public static CheeseTurnSummary Summarize(CheeseRecord record)
        {
            if (record == null) return null;
            return new CheeseTurnSummary
            {
                RecordHash = record.ProvenanceHash,
                Edges = record.Judgments.Count,
                Accepted = record.Judgments.Count(j => j.Verdict == CheeseVerdict.Accepted),
                Guarded = record.Judgments.Count(j => j.Verdict == CheeseVerdict.Guarded),
                Rejected = record.Judgments.Count(j => j.Verdict == CheeseVerdict.Rejected)
            };
        }
    }

    public sealed class CheeseTurnSummary
    {
        public string RecordHash { get; set; } = string.Empty;
        public int Edges { get; set; }
        public int Accepted { get; set; }
        public int Guarded { get; set; }
        public int Rejected { get; set; }
    }
}
