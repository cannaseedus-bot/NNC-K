using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NeuralGrammar.Core.XCFE
{
    /// <summary>
    /// Glyph Registry — resolves glyphs to folds, lanes, and opcodes.
    /// Matches asx-runtime-glyphs.manifest.json
    /// </summary>
    public class GlyphRegistry
    {
        private readonly Dictionary<string, FoldGlyph> _folds = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _structural = new();
        private readonly Dictionary<string, string> _geometry = new();
        private readonly Dictionary<string, string> _constants = new();
        private readonly HashSet<string> _lanes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _opcodes = new(StringComparer.OrdinalIgnoreCase);

        // Reverse lookup: glyph → fold name
        private readonly Dictionary<string, string> _glyphToFold = new();

        // Notation pattern: Glyph[Angle:Lane] or FoldName[Angle:Lane] or Φn.αn.λlane
        private static readonly Regex _readableNotation = new(
            @"^([A-Za-z◉⬢△✦◇⬡⟁⨀⨗⨕⤸⥀⧉∼≅↻↔⟿]+)\[([^:]+):([^\]]+)\]$");

        private static readonly Regex _compactNotation = new(
            @"^Φ(\d+)\.α(\d+(?:\.\d+)?)\.λ([a-zA-Z_]+)$");

        private static readonly Regex _glyphCompactNotation = new(
            @"^([◉⬢△✦◇⬡⟁⨀⨗⨕⤸⥀⧉∼≅↻↔⟿])\[([^:]+):([^\]]+)\]$");

        public GlyphRegistry()
        {
            LoadCanonicalRegistry();
        }

        // ---- Fold glyph access ----

        public class FoldGlyph
        {
            public string Name { get; set; }          // "Pop", "Wo", etc.
            public string Meaning { get; set; }       // "Load / Perceive"
            public string Glyph { get; set; }         // "◉"
            public int Phase { get; set; }             // 0-5
            public string Angle { get; set; }          // "0", "π/3", etc.
            public double Radians { get; set; }
        }

        public FoldGlyph GetFold(string name) =>
            _folds.TryGetValue(name, out var f) ? f : null;

        public FoldGlyph GetFoldByGlyph(string glyph) =>
            _glyphToFold.TryGetValue(glyph, out var name) && _folds.TryGetValue(name, out var f) ? f : null;

        public string FoldNameFromGlyph(string glyph) =>
            _glyphToFold.TryGetValue(glyph, out var name) ? name : null;

        public IReadOnlyDictionary<string, FoldGlyph> AllFolds => new Dictionary<string, FoldGlyph>(_folds, StringComparer.OrdinalIgnoreCase);

        // ---- Structural glyphs ----

        public string GetStructuralMeaning(string glyph) =>
            _structural.TryGetValue(glyph, out var m) ? m : null;

        public IReadOnlyDictionary<string, string> AllStructural => new Dictionary<string, string>(_structural);

        // ---- Geometry glyphs ----

        public string GetGeometryMeaning(string glyph) =>
            _geometry.TryGetValue(glyph, out var m) ? m : null;

        public IReadOnlyDictionary<string, string> AllGeometry => new Dictionary<string, string>(_geometry);

        // ---- Constants ----

        public string GetConstantMeaning(string symbol) =>
            _constants.TryGetValue(symbol, out var m) ? m : null;

        public double ResolveConstant(string symbol) => symbol switch
        {
            "π" => Math.PI,
            "φ" => 1.618033988749895,  // Golden ratio
            _ => double.NaN
        };

        // ---- Lanes ----

        public bool IsKnownLane(string lane) => _lanes.Contains(lane);
        public IReadOnlySet<string> AllLanes => _lanes;

        // ---- Opcodes ----

        public bool IsKnownOpcode(string opcode) => _opcodes.Contains(opcode);
        public IReadOnlySet<string> AllOpcodes => _opcodes;

        // ---- Notation parsing ----

        public class ResolvedNotation
        {
            public string FoldName { get; set; }   // "Pop", "Wo", etc.
            public string Glyph { get; set; }       // "◉"
            public string Angle { get; set; }       // e.g. "contract"
            public string Lane { get; set; }        // e.g. "manifest"
            public string NotationType { get; set; } // "readable", "compact", "glyph_compact"
        }

        public class AdmissionResult
        {
            public bool Admitted { get; set; }
            public string Reason { get; set; }
            public string FoldName { get; set; }
            public string Glyph { get; set; }
            public string Lane { get; set; }
            public string Opcode { get; set; }
            public string Angle { get; set; }
            public double Radians { get; set; }
            public string NotationType { get; set; }
            public string CurrentFold { get; set; }
            public string[] LegalNext { get; set; } = Array.Empty<string>();
        }

        /// <summary>Parse any fold notation format into a resolved triple</summary>
        public ResolvedNotation ParseNotation(string notation)
        {
            if (string.IsNullOrWhiteSpace(notation)) return null;
            notation = notation.Trim();

            // Try glyph_compact: ◉[contract:manifest]
            var m3 = _glyphCompactNotation.Match(notation);
            if (m3.Success)
            {
                var glyph = m3.Groups[1].Value;
                var foldName = FoldNameFromGlyph(glyph);
                return new ResolvedNotation
                {
                    FoldName = foldName ?? glyph,
                    Glyph = glyph,
                    Angle = m3.Groups[2].Value,
                    Lane = m3.Groups[3].Value,
                    NotationType = "glyph_compact"
                };
            }

            // Try readable: Pop[contract:manifest]
            var m1 = _readableNotation.Match(notation);
            if (m1.Success)
            {
                var foldKey = m1.Groups[1].Value;
                var fold = GetFold(foldKey) ?? GetFoldByGlyph(foldKey);
                return new ResolvedNotation
                {
                    FoldName = fold?.Name ?? foldKey,
                    Glyph = fold?.Glyph ?? foldKey,
                    Angle = m1.Groups[2].Value,
                    Lane = m1.Groups[3].Value,
                    NotationType = "readable"
                };
            }

            // Try compact: Φ0.α0.λmanifest
            var m2 = _compactNotation.Match(notation);
            if (m2.Success)
            {
                var phase = int.Parse(m2.Groups[1].Value);
                var fold = _folds.Values.FirstOrDefault(f => f.Phase == phase);
                if (fold == null) return null;
                return new ResolvedNotation
                {
                    FoldName = fold.Name,
                    Glyph = fold.Glyph,
                    Angle = m2.Groups[2].Value,
                    Lane = m2.Groups[3].Value,
                    NotationType = "compact"
                };
            }

            return null;
        }

        /// <summary>Format a fold into readable notation</summary>
        public string FormatReadable(string foldName, string angle, string lane)
        {
            var fold = GetFold(foldName);
            return $"{fold?.Glyph ?? foldName}[{angle}:{lane}]";
        }

        /// <summary>Format into compact notation</summary>
        public string FormatCompact(string foldName, double angle, string lane)
        {
            var fold = GetFold(foldName);
            if (fold == null) return null;
            return $"Φ{fold.Phase}.α{angle:F1}.λ{lane}";
        }

        /// <summary>
        /// Compatibility check for registry membership. This does not replace
        /// runtime fold-state admission; use Admit() when a FoldAlgebra exists.
        /// </summary>
        public bool IsAdmissible(string glyph, string lane, string opcode)
        {
            var fold = GetFoldByGlyph(glyph);
            return fold != null &&
                   IsKnownLane(lane) &&
                   IsKnownOpcode(opcode) &&
                   IsOpcodeLegalForFold(fold.Name, lane, opcode);
        }

        /// <summary>
        /// Resolve notation and prove that glyph, lane, opcode, angle and the
        /// current K'UHUL fold state form an admissible execution tuple.
        /// </summary>
        public AdmissionResult Admit(
            string notation,
            string opcode,
            FoldAlgebra algebra,
            bool requireCurrentFold = true)
        {
            var resolved = ParseNotation(notation);
            if (resolved == null)
                return Deny("Notation could not be resolved", null, opcode, algebra);

            var fold = GetFold(resolved.FoldName);
            if (fold == null)
                return Deny($"Unknown fold '{resolved.FoldName}'", resolved, opcode, algebra);

            if (!string.Equals(fold.Glyph, resolved.Glyph, StringComparison.Ordinal))
                return Deny("Glyph does not resolve to the declared fold", resolved, opcode, algebra);

            if (!IsKnownLane(resolved.Lane))
                return Deny($"Unknown lane '{resolved.Lane}'", resolved, opcode, algebra);

            if (!IsKnownOpcode(opcode))
                return Deny($"Unknown opcode '{opcode}'", resolved, opcode, algebra);

            if (!AngleMatchesFold(resolved.Angle, fold))
                return Deny(
                    $"Angle '{resolved.Angle}' does not match {fold.Name} ({fold.Angle})",
                    resolved, opcode, algebra);

            if (!IsOpcodeLegalForFold(fold.Name, resolved.Lane, opcode))
                return Deny(
                    $"Opcode '{opcode}' is not legal for {fold.Name}:{resolved.Lane}",
                    resolved, opcode, algebra);

            if (algebra != null && requireCurrentFold &&
                !string.Equals(algebra.CurrentFold, fold.Name, StringComparison.OrdinalIgnoreCase))
            {
                return Deny(
                    $"Runtime is at {algebra.CurrentFold}; notation requests {fold.Name}",
                    resolved, opcode, algebra);
            }

            return new AdmissionResult
            {
                Admitted = true,
                Reason = "admitted",
                FoldName = fold.Name,
                Glyph = fold.Glyph,
                Lane = resolved.Lane,
                Opcode = opcode,
                Angle = fold.Angle,
                Radians = fold.Radians,
                NotationType = resolved.NotationType,
                CurrentFold = algebra?.CurrentFold,
                LegalNext = algebra == null
                    ? Array.Empty<string>()
                    : FoldAlgebra.LegalTransitions(algebra.CurrentFold)
            };
        }

        /// <summary>
        /// Transition notation is separately checked against the closed-loop law.
        /// This prevents notation from becoming a fold-jump mechanism.
        /// </summary>
        public AdmissionResult AdmitTransition(
            string targetNotation,
            FoldAlgebra algebra)
        {
            if (algebra == null)
                return Deny("FoldAlgebra is required for transition admission", null, "TRANSITION", null);

            var resolved = ParseNotation(targetNotation);
            if (resolved == null)
                return Deny("Target notation could not be resolved", null, "TRANSITION", algebra);

            var fold = GetFold(resolved.FoldName);
            if (fold == null)
                return Deny($"Unknown target fold '{resolved.FoldName}'", resolved, "TRANSITION", algebra);

            if (!IsKnownLane(resolved.Lane))
                return Deny($"Unknown lane '{resolved.Lane}'", resolved, "TRANSITION", algebra);

            if (!AngleMatchesFold(resolved.Angle, fold))
                return Deny($"Target angle does not match fold {fold.Name}", resolved, "TRANSITION", algebra);

            if (!FoldAlgebra.IsLegalTransition(algebra.CurrentFold, fold.Name))
                return Deny(
                    $"Illegal K'UHUL transition {algebra.CurrentFold} -> {fold.Name}",
                    resolved, "TRANSITION", algebra);

            return new AdmissionResult
            {
                Admitted = true,
                Reason = "transition admitted",
                FoldName = fold.Name,
                Glyph = fold.Glyph,
                Lane = resolved.Lane,
                Opcode = "TRANSITION",
                Angle = fold.Angle,
                Radians = fold.Radians,
                NotationType = resolved.NotationType,
                CurrentFold = algebra.CurrentFold,
                LegalNext = FoldAlgebra.LegalTransitions(algebra.CurrentFold)
            };
        }

        private bool IsOpcodeLegalForFold(string foldName, string lane, string opcode)
        {
            if (string.IsNullOrWhiteSpace(foldName) ||
                string.IsNullOrWhiteSpace(lane) ||
                string.IsNullOrWhiteSpace(opcode))
                return false;

            // Fold opcodes are identity-bound.
            if (_folds.ContainsKey(opcode))
                return string.Equals(foldName, opcode, StringComparison.OrdinalIgnoreCase);

            switch (opcode.ToUpperInvariant())
            {
                case "PHASE":
                case "FOLD":
                    return true;

                case "TRANSITION":
                    // Transition legality also requires FoldAlgebra state and is
                    // therefore finalized by AdmitTransition().
                    return true;

                case "SEARCH":
                    return FoldIs(foldName, "Yax", "Sek") &&
                           LaneIs(lane, "memory", "file", "web", "news");

                case "FETCH":
                case "READ":
                    return FoldIs(foldName, "Pop", "Wo") &&
                           LaneIs(lane, "memory", "file", "web", "news", "network");

                case "WEB_SEARCH":
                    return FoldIs(foldName, "Sek") && LaneIs(lane, "web");

                case "NEWS_SEARCH":
                    return FoldIs(foldName, "Sek") && LaneIs(lane, "news");

                case "CALL":
                case "TOOL_CALL":
                    return FoldIs(foldName, "Sek") &&
                           LaneIs(lane, "tool", "agent", "model", "network");

                default:
                    return false;
            }
        }

        private static bool FoldIs(string actual, params string[] allowed) =>
            allowed.Any(x => string.Equals(actual, x, StringComparison.OrdinalIgnoreCase));

        private static bool LaneIs(string actual, params string[] allowed) =>
            allowed.Any(x => string.Equals(actual, x, StringComparison.OrdinalIgnoreCase));

        private static bool AngleMatchesFold(string angle, FoldGlyph fold)
        {
            if (fold == null || string.IsNullOrWhiteSpace(angle)) return false;

            var value = angle.Trim().Replace(" ", "");

            // Canonical symbolic notation.
            if (string.Equals(value, fold.Angle.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                return true;

            // Compact Φn.αx notation may carry radians numerically.
            if (double.TryParse(
                    value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var numeric))
            {
                return Math.Abs(numeric - fold.Radians) <= 1e-6;
            }

            return false;
        }

        private AdmissionResult Deny(
            string reason,
            ResolvedNotation resolved,
            string opcode,
            FoldAlgebra algebra)
        {
            return new AdmissionResult
            {
                Admitted = false,
                Reason = reason,
                FoldName = resolved?.FoldName,
                Glyph = resolved?.Glyph,
                Lane = resolved?.Lane,
                Opcode = opcode,
                Angle = resolved?.Angle,
                Radians = resolved != null && GetFold(resolved.FoldName) != null
                    ? GetFold(resolved.FoldName).Radians
                    : double.NaN,
                NotationType = resolved?.NotationType,
                CurrentFold = algebra?.CurrentFold,
                LegalNext = algebra == null
                    ? Array.Empty<string>()
                    : FoldAlgebra.LegalTransitions(algebra.CurrentFold)
            };
        }

        // ---- Load contract data ----

        private void LoadCanonicalRegistry()
        {
            // Folds
            RegisterFold("Pop",  "Load / Perceive",     "◉", 0, "0",        0);
            RegisterFold("Wo",   "Represent / Build",   "⬢", 1, "π/3",      Math.PI / 3);
            RegisterFold("Yax",  "Plan / Predict",      "△", 2, "2π/3",     2 * Math.PI / 3);
            RegisterFold("Sek",  "Execute / Transform", "✦", 3, "π",        Math.PI);
            RegisterFold("Ch'en","Project / Reflect",   "◇", 4, "4π/3",     4 * Math.PI / 3);
            RegisterFold("Xul",  "Collapse / Replay",   "⬡", 5, "5π/3",     5 * Math.PI / 3);

            // Structural
            RegisterStructural("⟁", "System Fold");
            RegisterStructural("⨀", "Tensor Core");
            RegisterStructural("⨗", "Compression");
            RegisterStructural("⨕", "Fold Gate");
            RegisterStructural("⤸", "Golden Spiral");
            RegisterStructural("⥀", "Recursive Learning");
            RegisterStructural("⧉", "Fibonacci Window");

            // Geometry
            RegisterGeometry("∼", "Similarity");
            RegisterGeometry("≅", "Geometric Similarity");
            RegisterGeometry("↻", "Rotation");
            RegisterGeometry("↔", "Reflection");
            RegisterGeometry("⟿", "Flow");

            // Constants
            RegisterConstant("π", "Phase Manifold");
            RegisterConstant("φ", "Golden Ratio");

            // Lanes
            foreach (var lane in new[] { "phase","manifest","memory","tensor","gpu","agent","model","file","event","web","news","tool","network" })
                _lanes.Add(lane);

            // Opcodes
            foreach (var op in new[] { "Pop","Wo","Yax","Sek","Ch'en","Xul","PHASE","FOLD","TRANSITION",
                "SEARCH","FETCH","READ","CALL","NEWS_SEARCH","WEB_SEARCH","TOOL_CALL" })
                _opcodes.Add(op);
        }

        private void RegisterFold(string name, string meaning, string glyph, int phase, string angle, double radians)
        {
            _folds[name] = new FoldGlyph
            {
                Name = name, Meaning = meaning, Glyph = glyph,
                Phase = phase, Angle = angle, Radians = radians
            };
            _glyphToFold[glyph] = name;
        }

        private void RegisterStructural(string glyph, string meaning) => _structural[glyph] = meaning;
        private void RegisterGeometry(string glyph, string meaning) => _geometry[glyph] = meaning;
        private void RegisterConstant(string symbol, string meaning) => _constants[symbol] = meaning;
    }
}
