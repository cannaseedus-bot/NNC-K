using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NeuralGrammar.Core.Runtime
{
    /// <summary>
    /// INLINE tier of the three-tier semantic field:
    ///   PRIOR      authored bigrams/trigrams (KUHUL_pi, read-only)   -- never mutated here
    ///   INLINE     this store (.learning/field/field.json)           -- learned from CHEESE
    ///   PERSISTENT BOSS / SCXQ2                                       -- deferred (D)
    ///
    /// Maps a collapsed semantic edge -> a weight in [0,1] learned by the @flux rule. Edge identity
    /// matches CollapsedEdge ("{Source}|{Relation}|{Target}"; Guard/Weight are NOT part of identity).
    /// A never-seen edge is provisional at 0.5 (SemanticKernel lifecycle). This store only ever
    /// touches the inline field -- never the model, the authored prior, or promotion.
    /// </summary>
    public sealed class SemanticFieldStore
    {
        public const double ProvisionalWeight = 0.5;
        public const double PromotableThreshold = 0.95;

        private readonly string _path;
        private readonly Dictionary<string, double> _weights =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public SemanticFieldStore(string root = null)
        {
            var dir = root ?? Path.Combine(Directory.GetCurrentDirectory(), ".learning", "field");
            Directory.CreateDirectory(dir);
            _path = Path.Combine(dir, "field.json");
            Load();
        }

        /// <summary>Canonical edge identity -- must match CollapsedEdge's Source/Relation/Target
        /// (NOT its ToString(), which also encodes Guard and Weight).</summary>
        public static string Key(string source, string relation, string target)
            => (source ?? string.Empty) + "|" + (relation ?? string.Empty) + "|" + (target ?? string.Empty);

        public static string Key(CollapsedEdge edge)
            => edge == null ? "||" : Key(edge.Source, edge.Relation, edge.Target);

        /// <summary>Current weight for an edge; provisional 0.5 if never reinforced.</summary>
        public double GetWeight(CollapsedEdge edge) => GetWeight(Key(edge));
        public double GetWeight(string source, string relation, string target)
            => GetWeight(Key(source, relation, target));
        private double GetWeight(string key)
            => _weights.TryGetValue(key, out var w) ? w : ProvisionalWeight;

        /// <summary>@flux inline update: w += lr*(target - w), clamped to [0,1]. Returns the new weight.</summary>
        public double Reinforce(CollapsedEdge edge, double target, double lr)
        {
            var key = Key(edge);
            var w = GetWeight(key);
            w += lr * (target - w);
            if (w < 0.0) w = 0.0;
            else if (w > 1.0) w = 1.0;
            _weights[key] = w;
            return w;
        }

        /// <summary>Read-only promotion signal. BOSS (D) reads this later; this store never promotes.</summary>
        public bool IsPromotable(CollapsedEdge edge, double threshold = PromotableThreshold)
            => GetWeight(edge) >= threshold;

        public int Count => _weights.Count;

        public IReadOnlyDictionary<string, double> Snapshot()
            => new Dictionary<string, double>(_weights);

        public void Load()
        {
            _weights.Clear();
            if (!File.Exists(_path)) return;
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(_path));
                if (data != null)
                    foreach (var kv in data) _weights[kv.Key] = kv.Value;
            }
            catch { /* corrupt/partial field file -> start from provisional */ }
        }

        /// <summary>Persist. Merges against the on-disk copy so a concurrent learner's keys survive
        /// (last-writer-per-key for our own keys; other keys preserved). Single-process assumption.</summary>
        public void Save()
        {
            var merged = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(_path))
            {
                try
                {
                    var existing = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(_path));
                    if (existing != null)
                        foreach (var kv in existing) merged[kv.Key] = kv.Value;
                }
                catch { }
            }
            foreach (var kv in _weights) merged[kv.Key] = kv.Value; // our updates win per key
            File.WriteAllText(_path, JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var kv in merged) _weights[kv.Key] = kv.Value; // adopt others' keys into memory
        }
    }
}
