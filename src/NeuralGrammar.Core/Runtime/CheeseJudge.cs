using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeuralGrammar.Core.Validation;

namespace NeuralGrammar.Core.Runtime
{
    /// <summary>
    /// CHEESE reinforcement authority. Positioned after K'UHUL Xul.
    /// Judges CollapseProof edges and emits CheeseRecords.
    /// Invariants:
    ///   - Sheogorath cannot CHEESE itself.
    ///   - CHEESE cannot alter Xul.
    ///   - CHEESE cannot promote contracts.
    ///   - BOSS cannot manufacture proof.
    /// </summary>
    public sealed class CheeseJudge
    {
        private readonly string _contractPath;
        private readonly List<string> _invariants;

        public CheeseJudge(string contractPath = null)
        {
            _contractPath = contractPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "contracts", "cheese.contract.xjson");
            _invariants = new List<string>
            {
                "Sheogorath cannot CHEESE itself.",
                "CHEESE cannot alter Xul.",
                "CHEESE cannot promote contracts.",
                "BOSS cannot manufacture proof."
            };
        }

        public bool ValidateContract(out string error)
        {
            error = null;
            if (!File.Exists(_contractPath))
            {
                error = $"Contract not found: {_contractPath}";
                return false;
            }
            var json = File.ReadAllText(_contractPath);
            var validator = new NodeContributionValidator(_contractPath);
            var report = validator.ValidateJson(json);
            error = report.IsValid ? null : string.Join("; ", report.Errors);
            return report.IsValid;
        }

        public CheeseRecord Judge(CollapseProof proof, IReadOnlyList<NodeContribution> contributions = null)
        {
            if (proof == null) throw new ArgumentNullException(nameof(proof));

            var record = new CheeseRecord
            {
                SessionId = proof.SessionId,
                Tick = proof.Tick,
                CollapseProofHash = proof.ComputeHash(),
                Invariants = _invariants.ToList(),
                JudgedAt = DateTimeOffset.UtcNow
            };

            foreach (var edge in proof.SelectedEdges)
            {
                var verdict = Classify(edge);
                var reward = Reward(edge, verdict, contributions);
                record.Judgments.Add(new CheeseJudgment
                {
                    Edge = CloneEdge(edge),
                    Verdict = verdict,
                    Reward = reward,
                    Rationale = Rationale(edge, verdict)
                });
            }

            record.Seal();
            return record;
        }

        private static CheeseVerdict Classify(CollapsedEdge edge)
        {
            if (edge.Guard == EdgeGuard.Rejected) return CheeseVerdict.Rejected;
            if (edge.Guard == EdgeGuard.Guarded) return CheeseVerdict.Guarded;
            if (edge.Guard == EdgeGuard.Accepted) return CheeseVerdict.Accepted;

            // Default policy: reward precision, reject overreach.
            var overreachTerms = new[] { "life-present", "life-detected", "biosignature-confirmation" };
            if (overreachTerms.Any(t => edge.Target.Equals(t, StringComparison.OrdinalIgnoreCase)))
                return CheeseVerdict.Rejected;
            if (edge.Relation.Contains("may") || edge.Relation.Contains("possible") || edge.Relation.Contains("indicator"))
                return CheeseVerdict.Guarded;
            return CheeseVerdict.Accepted;
        }

        private static double Reward(CollapsedEdge edge, CheeseVerdict verdict, IReadOnlyList<NodeContribution> contributions)
        {
            return verdict switch
            {
                CheeseVerdict.Rejected => 0.0,
                CheeseVerdict.Guarded => 0.5,
                CheeseVerdict.Accepted => 1.0,
                _ => 0.0
            };
        }

        private static string Rationale(CollapsedEdge edge, CheeseVerdict verdict)
        {
            return verdict switch
            {
                CheeseVerdict.Rejected => $"Rejected overreach: {edge.Source} does not imply {edge.Target}.",
                CheeseVerdict.Guarded => $"Guarded relationship: {edge.Source} under specified conditions may relate to {edge.Target}.",
                CheeseVerdict.Accepted => $"Accepted edge: {edge.Source} {edge.Relation} {edge.Target}.",
                _ => "No rationale."
            };
        }

        private static CollapsedEdge CloneEdge(CollapsedEdge edge) => new CollapsedEdge
        {
            Source = edge.Source,
            Relation = edge.Relation,
            Target = edge.Target,
            Guard = edge.Guard,
            Weight = edge.Weight
        };
    }
}
