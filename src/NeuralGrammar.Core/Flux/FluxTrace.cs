#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace NeuralGrammar.Core.Flux
{
    /// <summary>
    /// One @flux execution trace: the observable record of a single tick through
    /// the K'UHUL fold wheel. Kept intentionally tolerant so PowerShell can store
    /// arbitrary micro-contribution shapes without losing them.
    /// </summary>
    public sealed class FluxTrace
    {
        public int Tick { get; set; }
        public string Text { get; set; } = "";
        public List<string> FoldTrace { get; set; } = new();
        public string Intent { get; set; } = "";
        public string Brain { get; set; } = "";
        public double Confidence { get; set; }
        public int MemoryCount { get; set; }
        public List<JsonElement> Memories { get; set; } = new();
        public List<NodeContribution> Contributions { get; set; } = new();
        public List<JsonElement> MicroContributions { get; set; } = new();
        // Causal provenance for @flux semantic learning (Experiment C / FluxFieldLearner):
        // what the field ENDORSED (predicted) vs what actually RESULTED, as [from,to] transition pairs.
        // Kept tolerant + defaulted-empty so existing traces/consumers are unaffected.
        public List<string[]> EndorsedTransitions { get; set; } = new();
        public List<string[]> ResultTransitions { get; set; } = new();
        public bool Success { get; set; }
        public bool Fallback { get; set; }
        public string FallbackReason { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
