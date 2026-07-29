using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// KAST — K'UHUL Abstract Syntax Tree.
    ///
    /// Canonical structural bridge:
    ///   frontend projection -> KAST -> XCFE -> K'UHUL π -> SCXQ2
    ///
    /// KAST describes semantic structure. It does not schedule work,
    /// execute backends, perform network I/O, or bypass XCFE admission.
    /// </summary>
    public sealed class KastDocument
    {
        public const string Protocol = "kast/1";

        public string ProtocolId { get; init; } = Protocol;
        public string RegistryHash { get; init; } = "";
        public string SourceKind { get; init; } = "";
        public string SourceId { get; init; } = "";
        public string EntryNodeId { get; init; } = "";

        public IReadOnlyList<KastNode> Nodes { get; init; } = Array.Empty<KastNode>();
        public IReadOnlyList<KastEdge> Edges { get; init; } = Array.Empty<KastEdge>();

        [JsonIgnore]
        public string SemanticHash => KastHasher.Hash(this);

        public KastValidationResult Validate(KastRegistry registry = null)
            => KastValidator.Validate(this, registry);
    }

    /// <summary>
    /// A KAST node preserves identity across frontend projections.
    /// Fold, lane, glyph and opcode are resolved semantic coordinates,
    /// not execution side effects.
    /// </summary>
    public sealed class KastNode
    {
        public string Id { get; init; } = "";
        public KastNodeKind Kind { get; init; } = KastNodeKind.Operation;

        public string Fold { get; init; } = "";
        public string Lane { get; init; } = "";
        public string Glyph { get; init; } = "";
        public string Opcode { get; init; } = "";

        public string Symbol { get; init; } = "";
        public string Type { get; init; } = "";

        public IReadOnlyList<KastOperand> Operands { get; init; } = Array.Empty<KastOperand>();
        public IReadOnlyDictionary<string, string> Attributes { get; init; }
            = new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(StringComparer.Ordinal));
    }

    public sealed class KastOperand
    {
        public string Name { get; init; } = "";
        public KastValueKind Kind { get; init; } = KastValueKind.Literal;
        public string Value { get; init; } = "";
        public string Type { get; init; } = "";
    }

    /// <summary>
    /// Explicit graph relationship. Control transitions are represented as
    /// edges rather than hidden in source-language syntax.
    /// </summary>
    public sealed class KastEdge
    {
        public string From { get; init; } = "";
        public string To { get; init; } = "";
        public KastEdgeKind Kind { get; init; } = KastEdgeKind.Flow;
        public string Label { get; init; } = "";
        public int Ordinal { get; init; }
    }

    public enum KastNodeKind
    {
        Document,
        Fold,
        Operation,
        Value,
        Tensor,
        Memory,
        Agent,
        Model,
        File,
        Event,
        Decision,
        Projection
    }

    public enum KastValueKind
    {
        Literal,
        Symbol,
        NodeRef,
        TensorRef,
        MemoryRef,
        ArtifactRef
    }

    public enum KastEdgeKind
    {
        Flow,
        Transition,
        Data,
        Control,
        Dependency,
        Projection,
        Replay
    }

    /// <summary>
    /// Canonical K'UHUL vocabulary used to resolve KAST.
    /// The registry is descriptive/validating; XCFE remains control authority.
    /// </summary>
    public sealed class KastRegistry
    {
        private readonly Dictionary<string, KastFoldDefinition> _folds =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, KastGlyphDefinition> _glyphs =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> _lanes =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _opcodes =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, KastFoldDefinition> Folds => _folds;
        public IReadOnlyDictionary<string, KastGlyphDefinition> Glyphs => _glyphs;
        public IReadOnlyCollection<string> Lanes => _lanes;
        public IReadOnlyCollection<string> Opcodes => _opcodes;

        public string RegistryHash => KastHasher.HashRegistry(this);

        public static KastRegistry CreateCanonical()
        {
            var r = new KastRegistry();

            // Closed π/3 fold manifold.
            r.RegisterFold("Pop",   "Load / Perceive",     "◉", 0, "0",    0.0);
            r.RegisterFold("Wo",    "Represent / Build",   "⬢", 1, "π/3",  Math.PI / 3.0);
            r.RegisterFold("Yax",   "Plan / Predict",      "△", 2, "2π/3", 2.0 * Math.PI / 3.0);
            r.RegisterFold("Sek",   "Execute / Transform", "✦", 3, "π",    Math.PI);
            r.RegisterFold("Ch'en", "Project / Reflect",   "◇", 4, "4π/3", 4.0 * Math.PI / 3.0);
            r.RegisterFold("Xul",   "Collapse / Replay",   "⬡", 5, "5π/3", 5.0 * Math.PI / 3.0);

            // Structural glyphs.
            r.RegisterGlyph("⟁", KastGlyphClass.Structural, "System Fold");
            r.RegisterGlyph("⨀", KastGlyphClass.Structural, "Tensor Core");
            r.RegisterGlyph("⨗", KastGlyphClass.Structural, "Compression");
            r.RegisterGlyph("⨕", KastGlyphClass.Structural, "Fold Gate");
            r.RegisterGlyph("⤸", KastGlyphClass.Structural, "Golden Spiral");
            r.RegisterGlyph("⥀", KastGlyphClass.Structural, "Recursive Learning");
            r.RegisterGlyph("⧉", KastGlyphClass.Structural, "Fibonacci Window");

            // Geometry glyphs.
            r.RegisterGlyph("∼", KastGlyphClass.Geometry, "Similarity");
            r.RegisterGlyph("≅", KastGlyphClass.Geometry, "Geometric Similarity");
            r.RegisterGlyph("↻", KastGlyphClass.Geometry, "Rotation");
            r.RegisterGlyph("↔", KastGlyphClass.Geometry, "Reflection");
            r.RegisterGlyph("⟿", KastGlyphClass.Geometry, "Flow");

            // Constants.
            r.RegisterGlyph("π", KastGlyphClass.Constant, "Phase Manifold");
            r.RegisterGlyph("φ", KastGlyphClass.Constant, "Golden Ratio");

            foreach (var lane in new[]
            {
                "phase", "memory", "tensor", "gpu",
                "agent", "model", "file", "event"
            })
                r.RegisterLane(lane);

            // KuhulToKast emits these semantic node opcodes.
            foreach (var semanticOp in new[]
            {
                "+=", "assign", "literal", "ref", "call", "emit", "if",
                "invoke", "classify", "resolve", "lookup", "create",
                "decompose", "estimate", "compare", "detect",
                "admit", "mount", "bind", "collapse", "fold",
                "dispatch", "validate", "inspect", "mem_load", "mem_save",
                "score", "expand", "execute", "inference",
                "accept", "derive", "choose", "reduce", "repair"
            })
                r.RegisterOpcode(semanticOp);

            foreach (var opcode in new[]
            {
                "Pop", "Wo", "Yax", "Sek", "Ch'en", "Xul",
                "PHASE", "FOLD", "TRANSITION"
            })
                r.RegisterOpcode(opcode);

            return r;
        }

        public KastRegistry RegisterFold(
            string name,
            string meaning,
            string glyph,
            int ordinal,
            string angleSymbol,
            double radians)
        {
            Require(name, nameof(name));
            Require(glyph, nameof(glyph));

            _folds[name] = new KastFoldDefinition(
                name, meaning ?? "", glyph, ordinal, angleSymbol ?? "", radians);

            _glyphs[glyph] = new KastGlyphDefinition(
                glyph, KastGlyphClass.Fold, name + " — " + (meaning ?? ""));

            _opcodes.Add(name);
            return this;
        }

        public KastRegistry RegisterGlyph(
            string glyph,
            KastGlyphClass glyphClass,
            string meaning)
        {
            Require(glyph, nameof(glyph));
            _glyphs[glyph] = new KastGlyphDefinition(
                glyph, glyphClass, meaning ?? "");
            return this;
        }

        public KastRegistry RegisterLane(string lane)
        {
            Require(lane, nameof(lane));
            _lanes.Add(lane);
            return this;
        }

        public KastRegistry RegisterOpcode(string opcode)
        {
            Require(opcode, nameof(opcode));
            _opcodes.Add(opcode);
            return this;
        }

        public bool HasFold(string fold) =>
            !string.IsNullOrWhiteSpace(fold) && _folds.ContainsKey(fold);

        public bool HasGlyph(string glyph) =>
            !string.IsNullOrWhiteSpace(glyph) && _glyphs.ContainsKey(glyph);

        public bool HasLane(string lane) =>
            !string.IsNullOrWhiteSpace(lane) && _lanes.Contains(lane);

        public bool HasOpcode(string opcode) =>
            !string.IsNullOrWhiteSpace(opcode) && _opcodes.Contains(opcode);

        public bool IsLegalFoldTransition(string from, string to)
        {
            if (!HasFold(from) || !HasFold(to)) return false;
            var a = _folds[from].Ordinal;
            var b = _folds[to].Ordinal;
            return b == ((a + 1) % 6);
        }

        private static void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Value is required.", name);
        }
    }

    public sealed record KastFoldDefinition(
        string Name,
        string Meaning,
        string Glyph,
        int Ordinal,
        string AngleSymbol,
        double Radians);

    public sealed record KastGlyphDefinition(
        string Glyph,
        KastGlyphClass Class,
        string Meaning);

    public enum KastGlyphClass
    {
        Fold,
        Structural,
        Geometry,
        Constant,
        Opcode
    }

    /// <summary>
    /// Side-effect-free structural validator. It proves whether a KAST graph
    /// is legal enough to present to XCFE; it does not execute or admit it.
    /// </summary>
    public static class KastValidator
    {
        public static KastValidationResult Validate(
            KastDocument document,
            KastRegistry registry = null)
        {
            if (document == null)
                return KastValidationResult.Fail("document:null");

            registry ??= KastRegistry.CreateCanonical();
            var errors = new List<string>();

            if (!string.Equals(document.ProtocolId, KastDocument.Protocol, StringComparison.Ordinal))
                errors.Add("protocol:unsupported");

            if (!string.IsNullOrWhiteSpace(document.RegistryHash) &&
                !FixedEquals(document.RegistryHash, registry.RegistryHash))
                errors.Add("registry:hash_mismatch");

            var nodes = document.Nodes ?? Array.Empty<KastNode>();
            var edges = document.Edges ?? Array.Empty<KastEdge>();

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                if (node == null)
                {
                    errors.Add("node:null");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(node.Id))
                    errors.Add("node:id_missing");
                else if (!ids.Add(node.Id))
                    errors.Add("node:duplicate:" + node.Id);

                if (!string.IsNullOrWhiteSpace(node.Fold) && !registry.HasFold(node.Fold))
                    errors.Add("node:unknown_fold:" + node.Id + ":" + node.Fold);

                if (!string.IsNullOrWhiteSpace(node.Lane) && !registry.HasLane(node.Lane))
                    errors.Add("node:unknown_lane:" + node.Id + ":" + node.Lane);

                if (!string.IsNullOrWhiteSpace(node.Glyph) && !registry.HasGlyph(node.Glyph))
                    errors.Add("node:unknown_glyph:" + node.Id + ":" + node.Glyph);

                if (!string.IsNullOrWhiteSpace(node.Opcode) && !registry.HasOpcode(node.Opcode))
                    errors.Add("node:unknown_opcode:" + node.Id + ":" + node.Opcode);
            }

            if (!string.IsNullOrWhiteSpace(document.EntryNodeId) &&
                !ids.Contains(document.EntryNodeId))
                errors.Add("entry:unknown_node:" + document.EntryNodeId);

            foreach (var edge in edges)
            {
                if (edge == null)
                {
                    errors.Add("edge:null");
                    continue;
                }

                if (!ids.Contains(edge.From))
                    errors.Add("edge:unknown_from:" + edge.From);
                if (!ids.Contains(edge.To))
                    errors.Add("edge:unknown_to:" + edge.To);

                if (edge.Kind == KastEdgeKind.Transition &&
                    ids.Contains(edge.From) &&
                    ids.Contains(edge.To))
                {
                    var from = nodes.First(n => n.Id == edge.From);
                    var to = nodes.First(n => n.Id == edge.To);

                    if (!string.IsNullOrWhiteSpace(from.Fold) &&
                        !string.IsNullOrWhiteSpace(to.Fold) &&
                        !registry.IsLegalFoldTransition(from.Fold, to.Fold))
                    {
                        errors.Add(
                            "transition:illegal:" +
                            from.Fold + "->" + to.Fold);
                    }
                }
            }

            return errors.Count == 0
                ? KastValidationResult.Pass()
                : KastValidationResult.Fail(errors.ToArray());
        }

        private static bool FixedEquals(string a, string b)
        {
            var x = Encoding.UTF8.GetBytes(Normalize(a));
            var y = Encoding.UTF8.GetBytes(Normalize(b));
            return x.Length == y.Length &&
                   CryptographicOperations.FixedTimeEquals(x, y);
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var v = value.Trim().ToLowerInvariant();
            return v.StartsWith("sha256:", StringComparison.Ordinal)
                ? v.Substring(7)
                : v;
        }
    }

    public sealed class KastValidationResult
    {
        public bool Ok { get; init; }
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

        public static KastValidationResult Pass() =>
            new() { Ok = true };

        public static KastValidationResult Fail(params string[] errors) =>
            new()
            {
                Ok = false,
                Errors = errors?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
                    ?? Array.Empty<string>()
            };
    }

    /// <summary>
    /// Deterministic KAST hashing. Hashes canonical structural content only.
    /// </summary>
    public static class KastHasher
    {
        public static string Hash(KastDocument document)
        {
            if (document == null)
                throw new ArgumentNullException(nameof(document));

            var canonical = new
            {
                protocol = document.ProtocolId,
                registry = NormalizeHash(document.RegistryHash),
                source_kind = document.SourceKind ?? "",
                source_id = document.SourceId ?? "",
                entry = document.EntryNodeId ?? "",
                nodes = (document.Nodes ?? Array.Empty<KastNode>())
                    .OrderBy(n => n.Id, StringComparer.Ordinal)
                    .Select(n => new
                    {
                        id = n.Id ?? "",
                        kind = n.Kind.ToString(),
                        fold = n.Fold ?? "",
                        lane = n.Lane ?? "",
                        glyph = n.Glyph ?? "",
                        opcode = n.Opcode ?? "",
                        symbol = n.Symbol ?? "",
                        type = n.Type ?? "",
                        operands = (n.Operands ?? Array.Empty<KastOperand>())
                            .Select(o => new
                            {
                                name = o.Name ?? "",
                                kind = o.Kind.ToString(),
                                value = o.Value ?? "",
                                type = o.Type ?? ""
                            }),
                        attributes = (n.Attributes ?? new Dictionary<string,string>())
                            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                            .Select(kv => new { key = kv.Key, value = kv.Value })
                    }),
                edges = (document.Edges ?? Array.Empty<KastEdge>())
                    .OrderBy(e => e.From, StringComparer.Ordinal)
                    .ThenBy(e => e.Ordinal)
                    .ThenBy(e => e.To, StringComparer.Ordinal)
                    .Select(e => new
                    {
                        from = e.From ?? "",
                        to = e.To ?? "",
                        kind = e.Kind.ToString(),
                        label = e.Label ?? "",
                        ordinal = e.Ordinal
                    })
            };

            return Sha(JsonSerializer.Serialize(canonical));
        }

        public static string HashRegistry(KastRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            var canonical = new
            {
                folds = registry.Folds.Values
                    .OrderBy(f => f.Ordinal)
                    .Select(f => new
                    {
                        f.Name, f.Meaning, f.Glyph,
                        f.Ordinal, f.AngleSymbol, f.Radians
                    }),
                glyphs = registry.Glyphs.Values
                    .OrderBy(g => g.Glyph, StringComparer.Ordinal)
                    .Select(g => new
                    {
                        g.Glyph,
                        Class = g.Class.ToString(),
                        g.Meaning
                    }),
                lanes = registry.Lanes.OrderBy(x => x, StringComparer.Ordinal),
                opcodes = registry.Opcodes.OrderBy(x => x, StringComparer.Ordinal)
            };

            return Sha(JsonSerializer.Serialize(canonical));
        }

        private static string Sha(string value) =>
            Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value ?? ""))
            ).ToLowerInvariant();

        private static string NormalizeHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var v = value.Trim().ToLowerInvariant();
            return v.StartsWith("sha256:", StringComparison.Ordinal)
                ? v.Substring(7)
                : v;
        }
    }

    /// <summary>
    /// Builder used by frontends such as Roslyn, KXML, MathML and JSONL.
    /// Frontends author KAST; they do not directly author SCXQ2 execution.
    /// </summary>
    public sealed class KastBuilder
    {
        private readonly KastRegistry _registry;
        private readonly List<KastNode> _nodes = new();
        private readonly List<KastEdge> _edges = new();

        public KastBuilder(KastRegistry registry = null)
        {
            _registry = registry ?? KastRegistry.CreateCanonical();
        }

        public KastBuilder Node(KastNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            _nodes.Add(node);
            return this;
        }

        public KastBuilder Edge(KastEdge edge)
        {
            if (edge == null) throw new ArgumentNullException(nameof(edge));
            _edges.Add(edge);
            return this;
        }

        public KastDocument Build(
            string sourceKind,
            string sourceId,
            string entryNodeId)
        {
            var document = new KastDocument
            {
                RegistryHash = _registry.RegistryHash,
                SourceKind = sourceKind ?? "",
                SourceId = sourceId ?? "",
                EntryNodeId = entryNodeId ?? "",
                Nodes = _nodes.ToArray(),
                Edges = _edges.ToArray()
            };

            var validation = document.Validate(_registry);
            if (!validation.Ok)
                throw new KastException(
                    "Invalid KAST: " + string.Join("; ", validation.Errors));

            return document;
        }
    }

    public sealed class KastException : Exception
    {
        public KastException(string message) : base(message) { }
    }

    // ────────────────────────────────────────────────────────────────────────
    // K'UHUL JSON AST — fold-sequential source grammar
    //
    // These types represent the K'UHUL source AST as a JSON document.
    // Unlike KastDocument (which is a validated semantic graph), these
    // types encode the six-fold closed-loop law structurally:
    //
    //   Pop -> Wo -> Yax -> Sek -> Ch'en -> Xul
    //    ^                               |
    //    +-------------------------------+
    //
    // The runtime supplies the control algebra; the AST represents the
    // semantic tree. This is the input format read by KuhulPi.
    // ────────────────────────────────────────────────────────────────────────

    public sealed class KuhulProgram
    {
        public string Kuhul { get; init; } = "1.0";
        public string Type { get; init; } = "program";
        public Dictionary<string, object> Meta { get; init; } = new();
        public List<KuhulFold> Folds { get; init; } = new();

        /// <summary>Parse a K'UHUL JSON AST from a string.</summary>
        public static KuhulProgram Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new KastException("K'UHUL AST JSON is empty");

            return JsonSerializer.Deserialize<KuhulProgram>(json, Options)
                ?? throw new KastException("Failed to parse K'UHUL AST JSON");
        }

        /// <summary>
        /// Build a KuhulProgram from a .kuhul, .khl, or .json file.
        /// Reads, parses, validates the fold sequence, and returns the program.
        /// </summary>
        public static KuhulProgram Build(string path, KastRegistry registry = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new KastException("Source path is required");

            if (!File.Exists(path))
                throw new KastException("Source file not found: " + path);

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".kuhul" && ext != ".khl" && ext != ".json")
                throw new KastException(
                    "Unsupported extension: " + ext +
                    " (expected .kuhul, .khl, or .json)");

            var json = File.ReadAllText(path);
            var program = Parse(json);

            var validation = KuhulValidator.Validate(program);
            if (!validation.IsValid)
                throw new KastException("K'UHUL validation failed:\n" + validation);

            if (registry != null)
                KuhulToKast.Convert(program, registry);

            return program;
        }

        /// <summary>
        /// Build and convert to a validated KastDocument in one call.
        /// </summary>
        public static KastDocument BuildKast(string path, KastRegistry registry = null)
        {
            var program = Build(path, registry);
            return program.ToKastDocument(registry);
        }

        public KastDocument ToKastDocument(KastRegistry registry = null)
            => KuhulToKast.Convert(this, registry);

        public string ToJson() => JsonSerializer.Serialize(this, Options);

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public sealed class KuhulFold
    {
        public string Type { get; init; } = "fold";
        public string Name { get; init; } = "";
        public List<KuhulAstNode> Nodes { get; init; } = new();
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
    [JsonDerivedType(typeof(KuhulAssignment), "assign")]
    [JsonDerivedType(typeof(KuhulOperation),  "op")]
    [JsonDerivedType(typeof(KuhulCondition),  "if")]
    [JsonDerivedType(typeof(KuhulCall),       "call")]
    [JsonDerivedType(typeof(KuhulEmit),       "emit")]
    [JsonDerivedType(typeof(KuhulReference),  "ref")]
    [JsonDerivedType(typeof(KuhulLiteral),    "literal")]
    [JsonDerivedType(typeof(KuhulBlock),      "block")]
    public abstract class KuhulAstNode
    {
        public abstract string NodeType { get; }
    }

    public sealed class KuhulAssignment : KuhulAstNode
    {
        public override string NodeType => "assign";
        public string Target { get; init; } = "";
        public KuhulAstNode Value { get; init; }
    }

    public sealed class KuhulOperation : KuhulAstNode
    {
        public override string NodeType => "op";
        public string Op { get; init; } = "";
        public List<KuhulAstNode> Args { get; init; } = new();
    }

    public sealed class KuhulCondition : KuhulAstNode
    {
        public override string NodeType => "if";
        public KuhulAstNode Test { get; init; }
        public KuhulBlock Then { get; init; }
        public KuhulBlock Else { get; init; }
    }

    public sealed class KuhulCall : KuhulAstNode
    {
        public override string NodeType => "call";
        public string Name { get; init; } = "";
        public List<KuhulAstNode> Args { get; init; } = new();
    }

    public sealed class KuhulEmit : KuhulAstNode
    {
        public override string NodeType => "emit";
        public KuhulAstNode Value { get; init; }
    }

    public sealed class KuhulReference : KuhulAstNode
    {
        public override string NodeType => "ref";
        public string Name { get; init; } = "";
    }

    public sealed class KuhulLiteral : KuhulAstNode
    {
        public override string NodeType => "literal";
        public object Value { get; init; }
    }

    public sealed class KuhulBlock : KuhulAstNode
    {
        public override string NodeType => "block";
        public List<KuhulAstNode> Nodes { get; init; } = new();
    }

    // ────────────────────────────────────────────────────────────────────────
    // K'UHUL -> KAST converter
    //
    // Converts a KuhulProgram JSON AST into a validated KastDocument graph.
    // Each fold becomes a KastNode with KastNodeKind.Fold; each instruction
    // becomes a KastNode with its resolved kind/lane/glyph/opcode.
    // ────────────────────────────────────────────────────────────────────────

    public static class KuhulToKast
    {
        private static readonly string[] CanonicalFolds =
            { "Pop", "Wo", "Yax", "Sek", "Ch'en", "Xul" };

        public static KastDocument Convert(
            KuhulProgram program,
            KastRegistry registry = null)
        {
            if (program == null)
                throw new KastException("K'UHUL program is null");

            registry = registry ?? KastRegistry.CreateCanonical();
            var builder = new KastBuilder(registry);
            string entryNodeId = null;

            for (int i = 0; i < program.Folds.Count; i++)
            {
                var fold = program.Folds[i];
                if (i < CanonicalFolds.Length && fold.Name != CanonicalFolds[i])
                    throw new KastException(
                        "K'UHUL fold sequence violation: expected '" +
                        CanonicalFolds[i] + "', got '" + fold.Name + "'");

                var foldNodeId = "fold_" + i + "_" + fold.Name;
                entryNodeId = entryNodeId ?? foldNodeId;

                var foldNode = new KastNode
                {
                    Id = foldNodeId,
                    Kind = KastNodeKind.Fold,
                    Fold = fold.Name,
                    Symbol = fold.Name,
                    Type = "fold"
                };
                builder.Node(foldNode);

                int counter = 0;
                ConvertNodes(fold.Nodes, foldNodeId, fold.Name, builder, registry, i, ref counter);
            }

            return builder.Build("kuhul_json", "ast", entryNodeId ?? "");
        }

        private static void ConvertNodes(
            List<KuhulAstNode> nodes,
            string parentId,
            string foldName,
            KastBuilder builder,
            KastRegistry registry,
            int foldOrdinal,
            ref int counter)
        {
            foreach (var node in nodes)
            {
                var nodeId = parentId + "_n" + counter;
                ConvertNode(node, nodeId, parentId, foldName, builder, registry, counter, ref counter);
                counter++;
            }
        }

        private static void ConvertNode(
            KuhulAstNode node,
            string nodeId,
            string parentId,
            string foldName,
            KastBuilder builder,
            KastRegistry registry,
            int ordinal,
            ref int counter)
        {
            switch (node)
            {
                case KuhulAssignment assign:
                    builder.Node(new KastNode
                    {
                        Id = nodeId,
                        Kind = KastNodeKind.Operation,
                        Fold = foldName,
                        Opcode = "assign",
                        Symbol = assign.Target ?? "",
                        Type = "assign",
                        Operands = new[] { ResolveOperand(assign.Value) }
                    });
                    break;

                case KuhulOperation op:
                    builder.Node(new KastNode
                    {
                        Id = nodeId,
                        Kind = KastNodeKind.Operation,
                        Fold = foldName,
                        Opcode = op.Op ?? "",
                        Symbol = op.Op ?? "",
                        Type = "op",
                        Operands = (op.Args ?? new List<KuhulAstNode>())
                            .Select(ResolveOperand)
                            .ToArray()
                    });
                    break;

                case KuhulEmit emit:
                    builder.Node(new KastNode
                    {
                        Id = nodeId,
                        Kind = KastNodeKind.Event,
                        Fold = foldName,
                        Opcode = "emit",
                        Symbol = "emit",
                        Type = "emit",
                        Operands = new[] { new KastOperand
                        {
                            Name = "value",
                            Kind = KastValueKind.Literal,
                            Value = emit.Value is KuhulReference emitRef
                                ? emitRef.Name ?? "" : node.NodeType
                        }}
                    });
                    break;

                case KuhulReference r:
                    builder.Node(new KastNode
                    {
                        Id = nodeId,
                        Kind = KastNodeKind.Value,
                        Fold = foldName,
                        Opcode = "ref",
                        Symbol = r.Name ?? "",
                        Type = "ref"
                    });
                    break;

                case KuhulLiteral lit:
                    builder.Node(new KastNode
                    {
                        Id = nodeId,
                        Kind = KastNodeKind.Value,
                        Fold = foldName,
                        Opcode = "literal",
                        Symbol = lit.Value?.ToString() ?? "",
                        Type = "literal",
                        Operands = new[] { new KastOperand
                        {
                            Name = "value",
                            Kind = KastValueKind.Literal,
                            Value = lit.Value?.ToString() ?? ""
                        }}
                    });
                    break;

                case KuhulCall call:
                    builder.Node(new KastNode
                    {
                        Id = nodeId,
                        Kind = KastNodeKind.Operation,
                        Fold = foldName,
                        Opcode = "call",
                        Symbol = call.Name ?? "",
                        Type = "call",
                        Operands = (call.Args ?? new List<KuhulAstNode>())
                            .Select(ResolveOperand)
                            .ToArray()
                    });
                    break;

                case KuhulCondition cond:
                    builder.Node(new KastNode
                    {
                        Id = nodeId,
                        Kind = KastNodeKind.Decision,
                        Fold = foldName,
                        Opcode = "if",
                        Symbol = "if",
                        Type = "condition",
                        Operands = new[] { ResolveOperand(cond.Test) }
                    });
                    if (cond.Then?.Nodes != null)
                        ConvertNodes(cond.Then.Nodes, nodeId,
                            foldName, builder, registry, ordinal, ref counter);
                    if (cond.Else?.Nodes != null)
                        ConvertNodes(cond.Else.Nodes, nodeId,
                            foldName, builder, registry, ordinal, ref counter);
                    break;

                case KuhulBlock block:
                    ConvertNodes(block.Nodes ?? new List<KuhulAstNode>(),
                        nodeId, foldName, builder, registry, ordinal, ref counter);
                    break;
            }

            builder.Edge(new KastEdge
            {
                From = parentId,
                To = nodeId,
                Kind = KastEdgeKind.Flow,
                Ordinal = ordinal
            });
        }

        private static KastOperand ResolveOperand(KuhulAstNode node)
        {
            if (node is KuhulLiteral lit)
            {
                return new KastOperand
                {
                    Name = "literal",
                    Kind = KastValueKind.Literal,
                    Value = lit.Value?.ToString() ?? ""
                };
            }

            if (node is KuhulReference r)
            {
                return new KastOperand
                {
                    Name = r.Name ?? "",
                    Kind = KastValueKind.Symbol,
                    Value = r.Name ?? ""
                };
            }

            if (node is KuhulOperation op)
            {
                return new KastOperand
                {
                    Name = op.Op ?? "",
                    Kind = KastValueKind.NodeRef,
                    Value = op.Op ?? ""
                };
            }

            if (node is KuhulCall call)
            {
                return new KastOperand
                {
                    Name = call.Name ?? "",
                    Kind = KastValueKind.NodeRef,
                    Value = call.Name ?? ""
                };
            }

            return new KastOperand
            {
                Name = node?.NodeType ?? "unknown",
                Kind = KastValueKind.Literal,
                Value = ""
            };
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // K'UHUL AST Validator — structural validation before KAST conversion
    // ────────────────────────────────────────────────────────────────────────

    public static class KuhulValidator
    {
        private static readonly string[] RequiredFolds =
            { "Pop", "Wo", "Yax", "Sek", "Ch'en", "Xul" };

        public static KuhulValidationResult Validate(KuhulProgram program)
        {
            var result = new KuhulValidationResult();
            if (program == null)
            {
                result.Errors.Add("Program is null");
                return result;
            }

            if (string.IsNullOrWhiteSpace(program.Kuhul))
                result.Warnings.Add("Missing kuhul version; assuming 1.0");

            if (program.Folds.Count == 0)
            {
                result.Errors.Add("Program has no folds");
                return result;
            }

            if (program.Folds.Count != 6)
                result.Warnings.Add(
                    "Program has " + program.Folds.Count +
                    " folds; canonical cycle expects 6");

            for (int i = 0; i < program.Folds.Count; i++)
            {
                var fold = program.Folds[i];
                var ctx = "Fold[" + i + "] " + (fold.Name ?? "?");

                if (string.IsNullOrWhiteSpace(fold.Name))
                    result.Errors.Add(ctx + ": name is missing");

                if (i < RequiredFolds.Length && fold.Name != RequiredFolds[i])
                    result.Errors.Add(ctx + ": expected '" + RequiredFolds[i] +
                        "', got '" + fold.Name + "'");

                if (fold.Nodes == null)
                {
                    result.Errors.Add(ctx + ": nodes list is null");
                    continue;
                }

                if (fold.Nodes.Count == 0)
                    result.Warnings.Add(ctx + ": no nodes");

                for (int j = 0; j < fold.Nodes.Count; j++)
                    ValidateNode(fold.Nodes[j], ctx + ".nodes[" + j + "]", result);
            }

            result.IsValid = result.Errors.Count == 0;
            return result;
        }

        private static void ValidateNode(
            KuhulAstNode node,
            string path,
            KuhulValidationResult result)
        {
            if (node == null)
            {
                result.Errors.Add(path + ": node is null");
                return;
            }

            switch (node)
            {
                case KuhulAssignment assign:
                    if (string.IsNullOrWhiteSpace(assign.Target))
                        result.Errors.Add(path + ": assign target is empty");
                    if (assign.Value == null)
                        result.Errors.Add(path + ": assign value is null");
                    else
                        ValidateNode(assign.Value, path + ".value", result);
                    break;

                case KuhulOperation op:
                    if (string.IsNullOrWhiteSpace(op.Op))
                        result.Errors.Add(path + ": opcode is empty");
                    if (op.Args == null)
                        result.Errors.Add(path + ": args list is null");
                    else if (op.Args.Count == 0)
                        result.Warnings.Add(path + ": operation '" + op.Op + "' has no args");
                    else
                        for (int i = 0; i < op.Args.Count; i++)
                            ValidateNode(op.Args[i], path + ".args[" + i + "]", result);
                    break;

                case KuhulCondition cond:
                    if (cond.Test == null)
                        result.Errors.Add(path + ": condition test is null");
                    else
                        ValidateNode(cond.Test, path + ".test", result);
                    if (cond.Then == null)
                        result.Errors.Add(path + ": then block is null");
                    else
                        ValidateBlock(cond.Then, path + ".then", result);
                    if (cond.Else != null)
                        ValidateBlock(cond.Else, path + ".else", result);
                    break;

                case KuhulCall call:
                    if (string.IsNullOrWhiteSpace(call.Name))
                        result.Errors.Add(path + ": call name is empty");
                    if (call.Args != null)
                        for (int i = 0; i < call.Args.Count; i++)
                            ValidateNode(call.Args[i], path + ".args[" + i + "]", result);
                    break;

                case KuhulEmit emit:
                    if (emit.Value == null)
                        result.Errors.Add(path + ": emit value is null");
                    else
                        ValidateNode(emit.Value, path + ".value", result);
                    break;

                case KuhulReference r:
                    if (string.IsNullOrWhiteSpace(r.Name))
                        result.Errors.Add(path + ": reference name is empty");
                    break;

                case KuhulLiteral lit:
                    break;

                case KuhulBlock block:
                    ValidateBlock(block, path, result);
                    break;
            }
        }

        private static void ValidateBlock(
            KuhulBlock block,
            string path,
            KuhulValidationResult result)
        {
            if (block.Nodes == null)
            {
                result.Errors.Add(path + ": block nodes list is null");
                return;
            }

            if (block.Nodes.Count == 0)
                result.Warnings.Add(path + ": empty block");

            for (int i = 0; i < block.Nodes.Count; i++)
                ValidateNode(block.Nodes[i], path + ".nodes[" + i + "]", result);
        }
    }

    public sealed class KuhulValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        public override string ToString()
        {
            var parts = new List<string>();
            if (IsValid) parts.Add("Valid");
            else parts.Add("Invalid (" + Errors.Count + " error(s))");

            foreach (var e in Errors) parts.Add("  ERROR: " + e);
            foreach (var w in Warnings) parts.Add("  WARN: " + w);

            return string.Join("\n", parts);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // K'UHUL Compiled Program — resolved, numbered, closed-loop executable
    //
    // This is the output of the K'UHUL compiler. Unlike KuhulProgram (source
    // AST), this representation has explicit fold IDs, next pointers, and
    // preserves the closed-loop algebra: Xul.next = Pop.
    //
    //   .kuhul source  ->  KuhulProgram  ->  Compile()  ->  .kprog
    //                                                        |
    //                                              SCXQ2 / Micronaut / KBUILD
    //                                                        |
    //                                                    XCFE executes
    // ────────────────────────────────────────────────────────────────────────

    public sealed class KuhulCompiledProgram
    {
        public string Type { get; init; } = "kuhul.program";
        public string Id { get; init; } = "";
        public int Entry { get; init; }
        public Dictionary<string, object> Metadata { get; init; } = new();
        public List<CompiledFold> Folds { get; init; } = new();
        public List<CompiledNode> Nodes { get; init; } = new();
        public Dictionary<string, object> State { get; init; } = new();
        public List<CompiledContract> Contracts { get; init; } = new();

        public string ToJson() => JsonSerializer.Serialize(this, Options);

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, ToJson());
        }

        public static KuhulCompiledProgram Load(string path)
        {
            if (!File.Exists(path))
                throw new KastException("Program not found: " + path);
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<KuhulCompiledProgram>(json, Options)
                ?? throw new KastException("Failed to load .kprog: " + path);
        }

        public int FoldCount => Folds.Count;

        public bool IsClosedLoop => Entry == 0 &&
            Folds.Count > 0 &&
            Folds[Folds.Count - 1].Phase == "Xul" &&
            Folds[Folds.Count - 1].Next == 0;

        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public sealed class CompiledFold
    {
        public int Id { get; init; }
        public string Phase { get; init; } = "";
        public int Next { get; init; }
        public int TrueBranch { get; set; } = -1;
        public int FalseBranch { get; set; } = -1;
        public string Op { get; set; }
        public string Target { get; set; }
        public object Value { get; set; }
        public List<string> Args { get; set; }
        public string Decision { get; set; }
        public List<int> NodeIds { get; init; } = new();
    }

    public sealed class CompiledNode
    {
        public int Id { get; init; }
        public string Kind { get; set; } = "";
        public string Phase { get; set; } = "";
        public string Op { get; set; }
        public string Target { get; set; }
        public object Value { get; set; }
        public List<CompiledOperand> Operands { get; set; } = new();
        public int Next { get; set; } = -1;
    }

    public sealed class CompiledOperand
    {
        public string Name { get; init; } = "";
        public string Kind { get; init; } = "literal";
        public string Value { get; init; } = "";
    }

    public sealed class CompiledContract
    {
        public string Phase { get; init; } = "";
        public string Requirement { get; init; } = "";
        public string Assertion { get; init; } = "";
    }

    // ────────────────────────────────────────────────────────────────────────
    // K'UHUL Compiler — source -> resolved executable graph
    //
    // Compile() resolves the KuhulProgram into a flat, numbered graph with
    // explicit next pointers. Xul.next = Pop is enforced as invariant.
    // ────────────────────────────────────────────────────────────────────────

    public static class KuhulCompiler
    {
        public static KuhulCompiledProgram Compile(KuhulProgram program)
        {
            if (program == null)
                throw new KastException("Program is null");

            var validation = KuhulValidator.Validate(program);
            if (!validation.IsValid)
                throw new KastException(
                    "Compilation aborted: failed validation.\n" + validation);

            var compiled = new KuhulCompiledProgram
            {
                Id = program.Folds.Count > 0
                    ? (program.Folds[0].Nodes.FirstOrDefault() is KuhulLiteral lit
                        ? lit.Value?.ToString() ?? "program"
                        : "program")
                    : "program",
                Entry = 0,
                Metadata = new Dictionary<string, object>(program.Meta ?? new())
            };

            int nodeCounter = 0;

            for (int i = 0; i < program.Folds.Count; i++)
            {
                var fold = program.Folds[i];
                var compiledFold = new CompiledFold
                {
                    Id = i,
                    Phase = fold.Name,
                    Next = (i == program.Folds.Count - 1) ? 0 : i + 1
                };

                foreach (var node in fold.Nodes)
                {
                    var compiledNode = CompileNode(node, fold.Name, nodeCounter);
                    compiledNode.Phase = fold.Name;
                    compiledFold.NodeIds.Add(nodeCounter);
                    compiled.Nodes.Add(compiledNode);
                    nodeCounter++;
                }

                CompileFoldOp(compiledFold, fold.Nodes.FirstOrDefault());
                compiled.Folds.Add(compiledFold);
            }

            for (int i = 0; i < compiled.Folds.Count; i++)
            {
                var f = compiled.Folds[i];
                compiled.Contracts.Add(new CompiledContract
                {
                    Phase = f.Phase,
                    Requirement = f.Phase == "Pop" ? "entry_point" : "fold_sequence",
                    Assertion = "next=" + f.Next
                });
            }

            return compiled;
        }

        public static KuhulCompiledProgram Build(string sourcePath, string outputPath)
        {
            var program = KuhulProgram.Build(sourcePath);
            var compiled = Compile(program);
            compiled.Save(outputPath);
            return compiled;
        }

        public static (KuhulCompiledProgram Program, KastDocument Kast) BuildWithKast(
            string sourcePath)
        {
            var program = KuhulProgram.Build(sourcePath);
            var compiled = Compile(program);
            var kast = program.ToKastDocument();
            return (compiled, kast);
        }

        private static CompiledNode CompileNode(KuhulAstNode node, string phase, int id)
        {
            var compiled = new CompiledNode { Id = id, Phase = phase };

            switch (node)
            {
                case KuhulAssignment assign:
                    compiled.Kind = "assign";
                    compiled.Target = assign.Target ?? "";
                    compiled.Value = ResolveValue(assign.Value);
                    compiled.Op = "=";
                    break;
                case KuhulOperation op:
                    compiled.Kind = "op";
                    compiled.Op = op.Op ?? "";
                    if (op.Args != null)
                        compiled.Operands = op.Args.Select((a, i) => new CompiledOperand
                        {
                            Name = "arg" + i,
                            Kind = ResolveKind(a),
                            Value = ResolveValueStr(a)
                        }).ToList();
                    break;
                case KuhulCall call:
                    compiled.Kind = "call";
                    compiled.Op = call.Name ?? "";
                    if (call.Args != null)
                        compiled.Operands = call.Args.Select((a, i) => new CompiledOperand
                        {
                            Name = "arg" + i,
                            Kind = ResolveKind(a),
                            Value = ResolveValueStr(a)
                        }).ToList();
                    break;
                case KuhulEmit emit:
                    compiled.Kind = "emit";
                    compiled.Op = "emit";
                    compiled.Value = ResolveValue(emit.Value);
                    break;
                case KuhulCondition cond:
                    compiled.Kind = "condition";
                    compiled.Op = "if";
                    compiled.Value = ResolveValue(cond.Test);
                    break;
                case KuhulReference r:
                    compiled.Kind = "ref";
                    compiled.Target = r.Name ?? "";
                    compiled.Value = r.Name ?? "";
                    break;
                case KuhulLiteral lit:
                    compiled.Kind = "literal";
                    compiled.Value = lit.Value;
                    break;
                case KuhulBlock block:
                    compiled.Kind = "block";
                    compiled.Op = "block";
                    break;
            }
            return compiled;
        }

        private static void CompileFoldOp(CompiledFold fold, KuhulAstNode firstNode)
        {
            if (firstNode == null) return;
            switch (firstNode)
            {
                case KuhulLiteral lit:
                    fold.Op = "label"; fold.Value = lit.Value; break;
                case KuhulAssignment assign:
                    fold.Op = "assign"; fold.Target = assign.Target ?? "";
                    fold.Value = ResolveValue(assign.Value); break;
                case KuhulOperation op:
                    fold.Op = op.Op ?? "";
                    if (op.Args != null)
                        fold.Args = op.Args.Select(ResolveValueStr).ToList();
                    break;
                case KuhulCall call:
                    fold.Op = "invoke"; fold.Target = call.Name ?? "";
                    if (call.Args != null)
                        fold.Args = call.Args.Select(ResolveValueStr).ToList();
                    break;
                case KuhulCondition cond:
                    fold.Op = "when"; fold.Decision = ResolveValueStr(cond.Test);
                    fold.TrueBranch = 1; fold.FalseBranch = 0; break;
            }
        }

        private static object ResolveValue(KuhulAstNode node) => node switch
        {
            KuhulLiteral lit => lit.Value,
            KuhulReference r => r.Name ?? "",
            KuhulOperation op => ("op:" + op.Op),
            KuhulCall call => ("call:" + call.Name),
            _ => node?.NodeType ?? ""
        };

        private static string ResolveValueStr(KuhulAstNode node) => node switch
        {
            KuhulLiteral lit => lit.Value?.ToString() ?? "",
            KuhulReference r => r.Name ?? "",
            _ => node?.NodeType ?? ""
        };

        private static string ResolveKind(KuhulAstNode node) => node switch
        {
            KuhulLiteral => "literal",
            KuhulReference => "symbol",
            KuhulOperation => "expr",
            KuhulCall => "call",
            _ => "unknown"
        };
    }
}
