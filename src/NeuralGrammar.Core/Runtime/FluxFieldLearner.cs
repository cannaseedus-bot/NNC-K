using System;
using System.Collections.Generic;

namespace NeuralGrammar.Core.Runtime
{
    /// <summary>
    /// Reads CHEESE verdicts and updates the INLINE semantic field via the @flux rule. This is the
    /// non-proxy version of Experiment C2: reward is the real CHEESE verdict
    /// (Accepted 1.0 / Guarded 0.5 / Rejected 0.0 = CheeseJudgment.Reward), not the PMI proxy.
    ///
    /// Authority (frozen): consumes CHEESE output only -- never judges, collapses, promotes, or
    /// touches the model / authored prior. Order:
    ///   collapse -> CollapseProof -> CHEESE -> CheeseRecord -> FluxFieldLearner(inline field) -> [BOSS, deferred]
    /// </summary>
    public sealed class FluxFieldLearner
    {
        private readonly SemanticFieldStore _store;
        private readonly double _lr;

        public FluxFieldLearner(SemanticFieldStore store, double lr = 0.1)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            if (lr <= 0.0 || lr > 1.0) throw new ArgumentOutOfRangeException(nameof(lr), "lr must be in (0,1].");
            _lr = lr;
        }

        public SemanticFieldStore Store => _store;
        public double LearningRate => _lr;

        /// <summary>Per-tick: reinforce every judged edge toward its CHEESE reward, then persist.
        /// Returns the number of judgments applied.</summary>
        public int LearnFromRecord(CheeseRecord record)
        {
            if (record == null || record.Judgments == null || record.Judgments.Count == 0) return 0;
            int applied = 0;
            foreach (var j in record.Judgments)
            {
                if (j == null || j.Edge == null) continue;
                _store.Reinforce(j.Edge, j.Reward, _lr);
                applied++;
            }
            if (applied > 0) _store.Save();
            return applied;
        }

        /// <summary>Batch replay (saves once). Pass records oldest-first -- order changes the trajectory.</summary>
        public int LearnFromRecords(IEnumerable<CheeseRecord> records)
        {
            if (records == null) return 0;
            int applied = 0;
            foreach (var record in records)
            {
                if (record == null || record.Judgments == null) continue;
                foreach (var j in record.Judgments)
                {
                    if (j == null || j.Edge == null) continue;
                    _store.Reinforce(j.Edge, j.Reward, _lr);
                    applied++;
                }
            }
            if (applied > 0) _store.Save();
            return applied;
        }

        /// <summary>Single-edge trajectory: replay this edge's CHEESE history in JudgedAt order,
        /// reinforcing ONLY that edge (0.50 -> ... -> ~1.0 for repeated Accepted). Returns count.</summary>
        public int LearnFromHistory(ProvenanceStore provenance, string source, string relation, string target)
        {
            if (provenance == null) return 0;
            var history = provenance.LoadCheeseHistory(source, relation, target); // ordered by JudgedAt
            int applied = 0;
            foreach (var record in history)
            {
                if (record == null || record.Judgments == null) continue;
                foreach (var j in record.Judgments)
                {
                    if (j == null || j.Edge == null) continue;
                    if (!string.Equals(j.Edge.Source, source, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(j.Edge.Relation, relation, StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(j.Edge.Target, target, StringComparison.OrdinalIgnoreCase))
                        continue;
                    _store.Reinforce(j.Edge, j.Reward, _lr);
                    applied++;
                }
            }
            if (applied > 0) _store.Save();
            return applied;
        }
    }
}
