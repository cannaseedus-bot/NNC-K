using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NeuralGrammar.Core.Runtime
{
    /// <summary>
    /// Durable store for CollapseProof, CheeseRecord, and BossPromotion artifacts.
    /// Writes are append-only; artifacts are immutable once sealed.
    /// </summary>
    public sealed class ProvenanceStore
    {
        private readonly string _root;

        public ProvenanceStore(string root)
        {
            _root = root ?? Path.Combine(Directory.GetCurrentDirectory(), ".learning", "provenance");
            Directory.CreateDirectory(_root);
        }

        public string Save(CollapseProof proof)
        {
            if (proof == null) throw new ArgumentNullException(nameof(proof));
            var path = GetPath("collapse", proof.ComputeHash());
            File.WriteAllText(path, JsonSerializer.Serialize(proof, new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }

        public string Save(CheeseRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            var path = GetPath("cheese", record.ProvenanceHash);
            File.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }

        public string Save(BossPromotion promotion)
        {
            if (promotion == null) throw new ArgumentNullException(nameof(promotion));
            if (!promotion.IsPromotable()) throw new InvalidOperationException("BOSS cannot promote without sufficient CHEESE history.");
            var path = GetPath("boss", promotion.ComputeHash());
            File.WriteAllText(path, JsonSerializer.Serialize(promotion, new JsonSerializerOptions { WriteIndented = true }));
            return path;
        }

        public IReadOnlyList<CheeseRecord> LoadCheeseHistory(string edgeSource, string edgeRelation, string edgeTarget)
        {
            var results = new List<CheeseRecord>();
            var dir = Path.Combine(_root, "cheese");
            if (!Directory.Exists(dir)) return results;
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var record = JsonSerializer.Deserialize<CheeseRecord>(json);
                    if (record == null) continue;
                    if (record.Judgments.Any(j =>
                        j.Edge.Source.Equals(edgeSource, StringComparison.OrdinalIgnoreCase) &&
                        j.Edge.Relation.Equals(edgeRelation, StringComparison.OrdinalIgnoreCase) &&
                        j.Edge.Target.Equals(edgeTarget, StringComparison.OrdinalIgnoreCase)))
                        results.Add(record);
                }
                catch { }
            }
            return results.OrderBy(r => r.JudgedAt).ToList();
        }

        private string GetPath(string kind, string hash)
        {
            var dir = Path.Combine(_root, kind);
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"{hash}.json");
        }
    }
}
