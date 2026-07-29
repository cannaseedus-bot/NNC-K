#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// A generic @node for semantic cognition.
    ///
    /// The node model is intentionally close to classic ELIZA mechanics, because
    /// ELIZA is a proven reference implementation of local node-level thinking:
    ///
    ///     ELIZA keyword recognition  →  recognize
    ///     ELIZA decomposition        →  capture
    ///     ELIZA synonym classes        →  relate
    ///     ELIZA transformations        →  rewrite
    ///     ELIZA memory queue           →  recall
    ///     ELIZA rule selection         →  decide
    ///     ELIZA reassembly             →  articulate
    ///
    /// But this is a *node* abstraction, not an "ELIZA" subsystem. The same
    /// mechanics power hypothesis nodes, contradiction nodes, evidence nodes,
    /// comparison nodes, etc. Domains compose those nodes; no domain is called
    /// "eliza".
    /// </summary>
    public sealed class SemanticNode
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";

        /// <summary>Recognition pattern (regex, case-insensitive).</summary>
        public string Pattern { get; set; } = "";

        /// <summary>Synonym expansion table: canonical → aliases.</summary>
        public Dictionary<string, string[]> Synonyms { get; set; } = new();

        /// <summary>Semantic neighborhood, e.g. THOUGHT, FAMILY, EMOTION, SPACE.</summary>
        public string Neighborhood { get; set; } = "OPEN";

        /// <summary>K'UHUL fold tag: Pop, Wo, Yax, Sek, Ch'en, Xul.</summary>
        public string Fold { get; set; } = "Pop";

        /// <summary>Domain tags for downstream grouping (e.g. space, code, research).</summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>Capture slot definitions: slot name → semantic meaning.</summary>
        public Dictionary<string, string> Captures { get; set; } = new();

        /// <summary>Relation triples asserted when this node fires.</summary>
        public List<string[]> Relations { get; set; } = new();

        /// <summary>Articulation templates. May contain capture placeholders like (2).</summary>
        public List<string> ResponseTemplates { get; set; } = new();

        /// <summary>Intent label produced by this node.</summary>
        public string Intent { get; set; } = "general_prompt";

        /// <summary>Priority rank for ordering matches.</summary>
        public int Rank { get; set; } = 0;

        /// <summary>Base confidence multiplier [0,1].</summary>
        public double Confidence { get; set; } = 0.75;

        /// <summary>Where this node came from.</summary>
        public string Source { get; set; } = "node-engine";

        /// <summary>Opaque provenance metadata.</summary>
        public Dictionary<string, string> Metadata { get; set; } = new();

        public static SemanticNode FromPattern(
            string id,
            string name,
            string pattern,
            string neighborhood,
            string intent,
            params string[] responseTemplates)
        {
            return new SemanticNode
            {
                Id = id,
                Name = name,
                Pattern = pattern,
                Neighborhood = neighborhood,
                Intent = intent,
                ResponseTemplates = responseTemplates?.ToList() ?? new List<string>()
            };
        }
    }
}
