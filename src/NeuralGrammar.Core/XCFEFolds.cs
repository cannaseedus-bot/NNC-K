using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// K'UHUL closed-loop fold algebra.
    ///
    /// Control law:
    ///   Pop -> Wo -> Yax -> Sek -> Ch'en -> Xul -> Pop
    ///
    /// Gravity may rank legal destinations, but it may never bypass the
    /// transition law. Every committed transition receives a deterministic
    /// proof hash chained to the previous transition.
    /// </summary>
    public class FoldAlgebra
    {
        private static readonly string[] _canonical =
            { "Pop", "Wo", "Yax", "Sek", "Ch'en", "Xul" };

        private static readonly Dictionary<string, FoldDef> _folds =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string[]> _legalTransitions =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, string[]> _nodesByFold =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, GravityWellDef> _gravityWells =
            new(StringComparer.OrdinalIgnoreCase);

        static FoldAlgebra()
        {
            var meanings = new[]
            {
                "load/input",
                "state/representation",
                "resolve/classify",
                "execute",
                "verify/notify",
                "collapse/decision"
            };

            var glyphs = new[] { "Pop", "Wo", "Yax", "Sek", "Ch'en", "Xul" };

            for (int i = 0; i < _canonical.Length; i++)
            {
                var name = _canonical[i];
                var next = _canonical[(i + 1) % _canonical.Length];

                _folds[name] = new FoldDef
                {
                    Name = name,
                    Meaning = meanings[i],
                    Glyph = glyphs[i],
                    Phase = i,
                    Angle = $"{i}π/3",
                    Radians = i * Math.PI / 3.0
                };

                // The execution wheel itself is strict. Branching belongs in
                // nodes/plans inside a fold, not by skipping fold law.
                _legalTransitions[name] = new[] { next };

                _nodesByFold[name] = new[]
                {
                    $"node_{NormalizeId(name)}_1",
                    $"node_{NormalizeId(name)}_2"
                };

                _gravityWells[name] = new GravityWellDef
                {
                    FoldName = name,
                    Mass = 0.85 + i * 0.03,
                    Radius = $"{100 - i * 8}%",
                    Attracts = new[] { next },
                    Repels = new[] { _canonical[(i + 4) % _canonical.Length] },
                    Collapse = _canonical[(i + 5) % _canonical.Length]
                };
            }
        }

        private string _currentFold = "Pop";
        private int _rotationCount;
        private string _lastProofHash = "GENESIS";
        private readonly List<FoldTransition> _history = new();

        public string CurrentFold => _currentFold;
        public int RotationCount => _rotationCount;
        public string LastProofHash => _lastProofHash;
        public IReadOnlyList<FoldTransition> History => _history.AsReadOnly();
        public static IReadOnlyList<string> CanonicalCycle => Array.AsReadOnly(_canonical);

        public static FoldDef GetFold(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _folds.TryGetValue(Canonicalize(name), out var fold) ? fold : null;
        }

        public static string[] LegalTransitions(string from)
        {
            if (string.IsNullOrWhiteSpace(from)) return Array.Empty<string>();
            return _legalTransitions.TryGetValue(Canonicalize(from), out var transitions)
                ? transitions.ToArray()
                : Array.Empty<string>();
        }

        public static bool IsLegalTransition(string from, string to)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                return false;

            var canonicalFrom = Canonicalize(from);
            var canonicalTo = Canonicalize(to);

            return _legalTransitions.TryGetValue(canonicalFrom, out var legal) &&
                   legal.Contains(canonicalTo, StringComparer.OrdinalIgnoreCase);
        }

        public static string[] NodesForFold(string fold)
        {
            if (string.IsNullOrWhiteSpace(fold)) return Array.Empty<string>();
            return _nodesByFold.TryGetValue(Canonicalize(fold), out var nodes)
                ? nodes.ToArray()
                : Array.Empty<string>();
        }

        public static GravityWellDef GetGravityWell(string fold)
        {
            if (string.IsNullOrWhiteSpace(fold)) return null;
            return _gravityWells.TryGetValue(Canonicalize(fold), out var well)
                ? CloneWell(well)
                : null;
        }

        public static IReadOnlyDictionary<string, GravityWellDef> AllWells =>
            _gravityWells.ToDictionary(
                kv => kv.Key,
                kv => CloneWell(kv.Value),
                StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Deterministically advances one legal edge around the closed wheel.
        /// </summary>
        public string Advance()
        {
            return TransitionTo(SelectNextFold(), "advance");
        }

        /// <summary>
        /// Commits a transition only if it is admitted by fold law.
        /// </summary>
        public string TransitionTo(string targetFold, string reason = null)
        {
            var target = Canonicalize(targetFold);

            if (!_folds.ContainsKey(target))
                throw new ArgumentException($"Unknown fold '{targetFold}'", nameof(targetFold));

            if (!IsLegalTransition(_currentFold, target))
            {
                throw new InvalidOperationException(
                    $"Illegal K'UHUL fold transition: {_currentFold} -> {target}. " +
                    $"Legal: {string.Join(", ", LegalTransitions(_currentFold))}");
            }

            var from = _currentFold;
            var rotation = _rotationCount + 1;
            var previousProof = _lastProofHash;

            var transition = new FoldTransition
            {
                From = from,
                To = target,
                RotationNumber = rotation,
                Timestamp = DateTime.UtcNow,
                Reason = reason ?? "",
                PreviousProofHash = previousProof
            };

            transition.ProofHash = ComputeTransitionProof(
                transition.From,
                transition.To,
                transition.RotationNumber,
                transition.Reason,
                transition.PreviousProofHash);

            _history.Add(transition);
            _currentFold = target;
            _rotationCount = rotation;
            _lastProofHash = transition.ProofHash;

            return _currentFold;
        }

        /// <summary>
        /// Scores a candidate relative to current state. Illegal destinations
        /// receive score zero and can never win selection.
        /// </summary>
        public GravityScore ScoreFold(string fold, double massMultiplier = 1.0)
        {
            if (massMultiplier < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(massMultiplier),
                    "Mass multiplier cannot be negative");

            var candidate = Canonicalize(fold);

            if (!_gravityWells.TryGetValue(candidate, out var well))
                return new GravityScore { FoldName = candidate, Score = 0, Legal = false };

            var legal = IsLegalTransition(_currentFold, candidate);
            var mass = well.Mass * massMultiplier;

            if (!legal)
            {
                return new GravityScore
                {
                    FoldName = candidate,
                    Score = 0,
                    Mass = mass,
                    Affinity = 0,
                    Repulsion = well.Repels.Contains(_currentFold, StringComparer.OrdinalIgnoreCase)
                        ? -0.3
                        : 0,
                    SaturationPenalty = 0,
                    Legal = false
                };
            }

            var affinity = 0.5;
            var repulsion =
                well.Repels.Contains(_currentFold, StringComparer.OrdinalIgnoreCase)
                    ? -0.3
                    : 0.0;

            var saturation =
                _history.Count(h =>
                    string.Equals(h.To, candidate, StringComparison.OrdinalIgnoreCase)) * 0.05;

            var score = Clamp01(mass + affinity + repulsion - saturation);

            return new GravityScore
            {
                FoldName = candidate,
                Score = score,
                Mass = mass,
                Affinity = affinity,
                Repulsion = repulsion,
                SaturationPenalty = saturation,
                Legal = true
            };
        }

        /// <summary>
        /// Gravity ranks only destinations admitted by the current fold law.
        /// With the canonical wheel this resolves to exactly one next fold,
        /// while preserving the scoring API for future node-level geometry.
        /// </summary>
        public string SelectNextFold()
        {
            var legal = LegalTransitions(_currentFold);
            if (legal.Length == 0)
                throw new InvalidOperationException(
                    $"Fold '{_currentFold}' has no legal outgoing transition");

            return legal
                .Select(name => ScoreFold(name))
                .OrderByDescending(score => score.Score)
                .ThenBy(score => GetFold(score.FoldName)?.Phase ?? int.MaxValue)
                .Select(score => score.FoldName)
                .First();
        }

        /// <summary>
        /// Verifies the complete fold history without mutating runtime state.
        /// Timestamp is intentionally excluded from the proof digest so replay
        /// proof is based on semantic/control state rather than wall-clock time.
        /// </summary>
        public bool VerifyHistory(out string error)
        {
            var expectedFrom = "Pop";
            var expectedRotation = 1;
            var previousProof = "GENESIS";

            foreach (var transition in _history)
            {
                if (!string.Equals(
                        transition.From,
                        expectedFrom,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error =
                        $"Fold history discontinuity at rotation {transition.RotationNumber}: " +
                        $"expected From={expectedFrom}, got {transition.From}";
                    return false;
                }

                if (transition.RotationNumber != expectedRotation)
                {
                    error =
                        $"Rotation discontinuity: expected {expectedRotation}, " +
                        $"got {transition.RotationNumber}";
                    return false;
                }

                if (!IsLegalTransition(transition.From, transition.To))
                {
                    error =
                        $"Illegal recorded transition: {transition.From} -> {transition.To}";
                    return false;
                }

                if (!string.Equals(
                        transition.PreviousProofHash,
                        previousProof,
                        StringComparison.Ordinal))
                {
                    error =
                        $"Proof chain mismatch at rotation {transition.RotationNumber}";
                    return false;
                }

                var expectedProof = ComputeTransitionProof(
                    transition.From,
                    transition.To,
                    transition.RotationNumber,
                    transition.Reason ?? "",
                    transition.PreviousProofHash);

                if (!string.Equals(
                        transition.ProofHash,
                        expectedProof,
                        StringComparison.Ordinal))
                {
                    error =
                        $"Proof hash mismatch at rotation {transition.RotationNumber}";
                    return false;
                }

                expectedFrom = transition.To;
                previousProof = transition.ProofHash;
                expectedRotation++;
            }

            if (!string.Equals(
                    expectedFrom,
                    _currentFold,
                    StringComparison.OrdinalIgnoreCase))
            {
                error =
                    $"Current fold mismatch: history resolves to {expectedFrom}, " +
                    $"runtime says {_currentFold}";
                return false;
            }

            if (!string.Equals(previousProof, _lastProofHash, StringComparison.Ordinal))
            {
                error = "Runtime proof head does not match fold history";
                return false;
            }

            error = null;
            return true;
        }

        public FoldSnapshot Snapshot()
        {
            return new FoldSnapshot
            {
                CurrentFold = _currentFold,
                RotationCount = _rotationCount,
                ProofHead = _lastProofHash,
                CurrentRadians = GetFold(_currentFold)?.Radians ?? 0,
                LegalNext = LegalTransitions(_currentFold)
            };
        }

        public void Reset()
        {
            _currentFold = "Pop";
            _rotationCount = 0;
            _lastProofHash = "GENESIS";
            _history.Clear();
        }

        private static string Canonicalize(string fold)
        {
            if (string.IsNullOrWhiteSpace(fold)) return "";

            var normalized = fold.Trim()
                .Replace("Chen", "Ch'en", StringComparison.OrdinalIgnoreCase);

            return _canonical.FirstOrDefault(
                       f => string.Equals(
                           f,
                           normalized,
                           StringComparison.OrdinalIgnoreCase))
                   ?? normalized;
        }

        private static string NormalizeId(string value)
        {
            return new string(
                (value ?? "")
                    .ToLowerInvariant()
                    .Where(char.IsLetterOrDigit)
                    .ToArray());
        }

        private static double Clamp01(double value) =>
            Math.Max(0.0, Math.Min(1.0, value));

        private static string ComputeTransitionProof(
            string from,
            string to,
            int rotation,
            string reason,
            string previousProof)
        {
            var canonical =
                $"{Canonicalize(from)}|{Canonicalize(to)}|{rotation}|" +
                $"{reason ?? ""}|{previousProof ?? "GENESIS"}";

            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));

            return Convert.ToHexString(bytes).ToLowerInvariant();
        }

        private static GravityWellDef CloneWell(GravityWellDef source)
        {
            if (source == null) return null;

            return new GravityWellDef
            {
                FoldName = source.FoldName,
                Mass = source.Mass,
                Radius = source.Radius,
                Attracts = source.Attracts?.ToArray() ?? Array.Empty<string>(),
                Repels = source.Repels?.ToArray() ?? Array.Empty<string>(),
                Collapse = source.Collapse
            };
        }

        public class FoldDef
        {
            public string Name { get; set; }
            public string Meaning { get; set; }
            public string Glyph { get; set; }
            public int Phase { get; set; }
            public string Angle { get; set; }
            public double Radians { get; set; }
        }

        public class FoldTransition
        {
            public string From { get; set; }
            public string To { get; set; }
            public int RotationNumber { get; set; }
            public DateTime Timestamp { get; set; }
            public string Reason { get; set; }
            public string PreviousProofHash { get; set; }
            public string ProofHash { get; set; }
        }

        public class GravityWellDef
        {
            public string FoldName { get; set; }
            public double Mass { get; set; }
            public string Radius { get; set; }
            public string[] Attracts { get; set; }
            public string[] Repels { get; set; }
            public string Collapse { get; set; }
        }

        public class GravityScore
        {
            public string FoldName { get; set; }
            public double Score { get; set; }
            public double Mass { get; set; }
            public double Affinity { get; set; }
            public double Repulsion { get; set; }
            public double SaturationPenalty { get; set; }
            public bool Legal { get; set; }
        }

        public class FoldSnapshot
        {
            public string CurrentFold { get; set; }
            public int RotationCount { get; set; }
            public string ProofHead { get; set; }
            public double CurrentRadians { get; set; }
            public string[] LegalNext { get; set; }
        }
    }
}
