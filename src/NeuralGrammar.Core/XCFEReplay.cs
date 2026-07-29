using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// A single execution coverage record emitted by the XCFE fold wheel.
    /// Tracks one fold step within a turn for replay/coverage analysis.
    /// </summary>
    public sealed class CoverageEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);
        public string Fold { get; set; } = "";
        public string Intent { get; set; } = "";
        public string Brain { get; set; } = "";
        public double Confidence { get; set; }
        public bool Success { get; set; }
        public int MemoryCount { get; set; }
        public int FoldStepIndex { get; set; }
        public string Error { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Aggregated coverage state for the XCFE runtime. Accumulates across turns
    /// and is accessible for contract-manager decisions (refold vs collapse).
    /// Thread-safe for concurrent fold execution.
    /// </summary>
    public sealed class XCFEExecState
    {
        private readonly List<CoverageEntry> _entries = new();
        private readonly object _lock = new();

        // ---- Per-fold step counters ----
        public int PopSteps { get; private set; }
        public int WoSteps { get; private set; }
        public int YaxSteps { get; private set; }
        public int SekSteps { get; private set; }
        public int ChenSteps { get; private set; }
        public int XulSteps { get; private set; }

        // ---- Turn-level metrics ----
        public int TotalTurns { get; private set; }
        public int SuccessfulTurns { get; private set; }
        public int FailedTurns { get; private set; }

        // ---- Memory ----
        public int TotalMemoryLookups { get; private set; }
        public int MemoryHits { get; private set; }
        public int MemoryMisses { get; private set; }

        // ---- Confidence ----
        public double ConfidenceSum { get; private set; }
        public int ConfidenceSamples { get; private set; }

        // ---- Computed coverage ratios ----
        public double FoldCoverage =>
            TotalTurns > 0
                ? (double)(PopSteps + WoSteps + YaxSteps + SekSteps + ChenSteps + XulSteps) / (TotalTurns * 6)
                : 0.0;

        public double SuccessRate =>
            TotalTurns > 0 ? (double)SuccessfulTurns / TotalTurns : 0.0;

        public double AverageConfidence =>
            ConfidenceSamples > 0 ? ConfidenceSum / ConfidenceSamples : 0.0;

        public double MemoryRecallRate =>
            TotalMemoryLookups > 0 ? (double)MemoryHits / TotalMemoryLookups : 0.0;

        /// <summary>
        /// Heuristic: coverage is sufficient when fold coverage > 60% and success rate > 70%.
        /// Below either threshold the contract manager should refold rather than collapse.
        /// </summary>
        public bool NeedsRefold =>
            TotalTurns > 0 && (FoldCoverage < 0.6 || SuccessRate < 0.7);

        public IReadOnlyList<CoverageEntry> Entries
        {
            get { lock (_lock) return _entries.ToList(); }
        }

        /// <summary>Record one fold step within a turn.</summary>
        public void RecordFoldStep(string fold, bool success, string intent = "", string brain = "",
            double confidence = 0, int memoryCount = 0, int foldStepIndex = 0, string error = "")
        {
            var entry = new CoverageEntry
            {
                Fold = fold,
                Intent = intent,
                Brain = brain,
                Confidence = confidence,
                Success = success,
                MemoryCount = memoryCount,
                FoldStepIndex = foldStepIndex,
                Error = error,
                Timestamp = DateTime.UtcNow
            };

            lock (_lock)
            {
                _entries.Add(entry);
            }
        }

        /// <summary>Record a completed turn (after Xul collapse or failure).</summary>
        public void RecordTurn(bool success, double confidence,
            int pop, int wo, int yax, int sek, int chen, int xul,
            int memoryLookups, int hits, int misses)
        {
            lock (_lock)
            {
                TotalTurns++;
                if (success) SuccessfulTurns++; else FailedTurns++;

                PopSteps += pop;
                WoSteps += wo;
                YaxSteps += yax;
                SekSteps += sek;
                ChenSteps += chen;
                XulSteps += xul;

                TotalMemoryLookups += memoryLookups;
                MemoryHits += hits;
                MemoryMisses += misses;

                ConfidenceSum += confidence;
                ConfidenceSamples++;
            }
        }

        /// <summary>Immutable snapshot for the contract manager (BOSS.kprog).</summary>
        public CoverageSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                return new CoverageSnapshot
                {
                    TotalTurns = TotalTurns,
                    SuccessfulTurns = SuccessfulTurns,
                    FailedTurns = FailedTurns,
                    FoldCoverage = FoldCoverage,
                    SuccessRate = SuccessRate,
                    AverageConfidence = AverageConfidence,
                    MemoryRecallRate = MemoryRecallRate,
                    PopSteps = PopSteps,
                    WoSteps = WoSteps,
                    YaxSteps = YaxSteps,
                    SekSteps = SekSteps,
                    ChenSteps = ChenSteps,
                    XulSteps = XulSteps,
                    NeedsRefold = NeedsRefold
                };
            }
        }

        /// <summary>Reset all accumulated state. Used on program reload or reset.</summary>
        public void Reset()
        {
            lock (_lock)
            {
                _entries.Clear();
                PopSteps = WoSteps = YaxSteps = SekSteps = ChenSteps = XulSteps = 0;
                TotalTurns = SuccessfulTurns = FailedTurns = 0;
                TotalMemoryLookups = MemoryHits = MemoryMisses = 0;
                ConfidenceSum = 0;
                ConfidenceSamples = 0;
            }
        }
    }

    /// <summary>
    /// Immutable snapshot of coverage state. Consumed by the contract manager (BOSS.kprog)
    /// to decide refold vs collapse without racing against live mutations.
    /// </summary>
    public sealed class CoverageSnapshot
    {
        public int TotalTurns { get; init; }
        public int SuccessfulTurns { get; init; }
        public int FailedTurns { get; init; }
        public double FoldCoverage { get; init; }
        public double SuccessRate { get; init; }
        public double AverageConfidence { get; init; }
        public double MemoryRecallRate { get; init; }
        public int PopSteps { get; init; }
        public int WoSteps { get; init; }
        public int YaxSteps { get; init; }
        public int SekSteps { get; init; }
        public int ChenSteps { get; init; }
        public int XulSteps { get; init; }
        public bool NeedsRefold { get; init; }
    }

    /// <summary>
    /// Replay event for the semantic event system. Used by the post-admission pipeline
    /// for semantic.link and semantic.cluster events, and by the contract manager.
    /// </summary>
    public sealed class ReplayEvent
    {
        public string Type { get; set; } = "";       // "semantic.link", "semantic.cluster", etc.
        public string SourceId { get; set; } = "";    // originating micronaut id or turn id
        public string TargetId { get; set; } = "";    // linked/clustered target
        public string Label { get; set; } = "";       // link label or cluster name
        public double Score { get; set; }             // similarity or confidence
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
