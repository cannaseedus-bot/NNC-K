using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NeuralGrammar.Core.Runtime
{
    /// <summary>
    /// BOSS promotion record. Authority-backed contract elevation.
    /// Requires a history of CHEESE records; BOSS cannot manufacture proof.
    /// </summary>
    public sealed class BossPromotion
    {
        public string ContractId { get; set; } = string.Empty;
        public string EdgeSource { get; set; } = string.Empty;
        public string EdgeRelation { get; set; } = string.Empty;
        public string EdgeTarget { get; set; } = string.Empty;
        public List<string> BackingCheeseHashes { get; set; } = new List<string>();
        public int RequiredVerifications { get; set; } = 3;
        public DateTimeOffset PromotedAt { get; set; }
        public string ProvenanceHash { get; set; } = string.Empty;

        public bool IsPromotable() => BackingCheeseHashes != null && BackingCheeseHashes.Count >= RequiredVerifications;

        public string ComputeHash()
        {
            var sb = new StringBuilder();
            sb.Append(ContractId).Append('|')
              .Append(EdgeSource).Append("--[").Append(EdgeRelation).Append("]-->").Append(EdgeTarget).Append('|')
              .Append(RequiredVerifications);
            foreach (var h in BackingCheeseHashes.OrderBy(x => x))
                sb.Append('|').Append(h);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public void Seal()
        {
            ProvenanceHash = ComputeHash();
        }
    }
}
