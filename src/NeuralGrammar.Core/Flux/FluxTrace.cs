#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeuralGrammar.Core.Flux
{
    /// <summary>
    /// One @flux execution trace: the observable record of a single tick through
    /// the K'UHUL fold wheel. Kept intentionally tolerant so PowerShell can store
    /// arbitrary micro-contribution shapes without losing them.
    ///
    /// Every property carries an explicit [JsonPropertyName] (PascalCase) so serialize/
    /// deserialize round-trips exactly what the PowerShell UI writes, INDEPENDENT of the
    /// serializer's naming policy. (JsonOptions.Pretty uses SnakeCaseLower, which otherwise
    /// silently dropped multi-word fields like FoldTrace/EndorsedTransitions on Deserialize.)
    /// </summary>
    public sealed class FluxTrace
    {
        [JsonPropertyName("Tick")]                 public int Tick { get; set; }
        [JsonPropertyName("Text")]                 public string Text { get; set; } = "";
        [JsonPropertyName("FoldTrace")]            public List<string> FoldTrace { get; set; } = new();
        [JsonPropertyName("Intent")]               public string Intent { get; set; } = "";
        [JsonPropertyName("Brain")]                public string Brain { get; set; } = "";
        [JsonPropertyName("Confidence")]           public double Confidence { get; set; }
        [JsonPropertyName("MemoryCount")]          public int MemoryCount { get; set; }
        [JsonPropertyName("Memories")]             public List<JsonElement> Memories { get; set; } = new();
        [JsonPropertyName("Contributions")]        public List<NodeContribution> Contributions { get; set; } = new();
        [JsonPropertyName("MicroContributions")]   public List<JsonElement> MicroContributions { get; set; } = new();
        // Causal provenance for @flux semantic learning (Experiment C / FluxFieldLearner):
        // what the field ENDORSED (predicted) vs what actually RESULTED, as [from,to] transition pairs.
        [JsonPropertyName("EndorsedTransitions")]  public List<string[]> EndorsedTransitions { get; set; } = new();
        [JsonPropertyName("ResultTransitions")]    public List<string[]> ResultTransitions { get; set; } = new();
        // Post-Xul CHEESE verdict summary { RecordHash, Edges, Accepted, Guarded, Rejected }; null until
        // the CHEESE chain judges this collapse (never inferred from Success/Confidence).
        [JsonPropertyName("Cheese")]               public JsonElement? Cheese { get; set; }
        [JsonPropertyName("Success")]              public bool Success { get; set; }
        [JsonPropertyName("Fallback")]             public bool Fallback { get; set; }
        [JsonPropertyName("FallbackReason")]       public string FallbackReason { get; set; } = "";
        [JsonPropertyName("Timestamp")]            public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
