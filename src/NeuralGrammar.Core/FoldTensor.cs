using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NeuralGrammar.Core.XCFE;

namespace NeuralGrammar.Core
{
    public enum FoldPhase
    {
        Pop, Wo, Yax, Sek, Chen, Xul
    }

    public class FoldTensor
    {
        public NDArray Tensor { get; private set; }
        public FoldPhase Phase { get; private set; }
        public double Angle { get; private set; }
        public string Label { get; private set; }
        public Dictionary<string, object> Metadata { get; set; } = new();

        private static readonly Dictionary<FoldPhase, Func<NDArray, NDArray>> _phaseOps = new()
        {
            { FoldPhase.Pop, x => x },
            { FoldPhase.Wo, x => x.Sigmoid() },
            { FoldPhase.Yax, x => x.Softmax() },
            { FoldPhase.Sek, x => x.ReLU() },
            { FoldPhase.Chen, x => x.Abs() },
            { FoldPhase.Xul, x => x.Sqrt() }
        };

        private static readonly Dictionary<FoldPhase, string> _symbols = new()
        {
            { FoldPhase.Pop, "\u25c9" }, { FoldPhase.Wo, "\u2b22" }, { FoldPhase.Yax, "\u25b3" },
            { FoldPhase.Sek, "\u2726" }, { FoldPhase.Chen, "\u25c7" }, { FoldPhase.Xul, "\u2b21" }
        };

        private static readonly Dictionary<FoldPhase, string> _names = new()
        {
            { FoldPhase.Pop, "Pop" }, { FoldPhase.Wo, "Wo" }, { FoldPhase.Yax, "Yax" },
            { FoldPhase.Sek, "Sek" }, { FoldPhase.Chen, "Ch'en" }, { FoldPhase.Xul, "Xul" }
        };

        public FoldTensor(NDArray tensor, FoldPhase phase = FoldPhase.Pop, string label = null)
        {
            Tensor = tensor ?? throw new ArgumentNullException(nameof(tensor));
            Phase = phase;
            Label = label ?? $"fold_{Guid.NewGuid():N}".Substring(0, 12);
            Angle = (int)phase * 60.0;
        }

        public FoldTensor(double[,] matrix, FoldPhase phase = FoldPhase.Pop, string label = null) : this(new NDArray(matrix), phase, label) { }
        public FoldTensor(double[] vector, FoldPhase phase = FoldPhase.Pop, string label = null) : this(new NDArray(vector, vector.Length), phase, label) { }

        public string Symbol => _symbols.GetValueOrDefault(Phase, "?");
        public string PhaseName => _names.GetValueOrDefault(Phase, Phase.ToString());
        public double Mean => Tensor.Mean();
        public double Std => Tensor.Std();
        public double Norm => Tensor.Norm();
        public double Min => Tensor.Min();
        public double Max => Tensor.Max();

        /// <summary>
        /// Apply a fold's tensor transform without claiming a runtime transition.
        /// This is tensor mathematics, not semantic routing.
        /// </summary>
        public FoldTensor TransformAs(FoldPhase target)
        {
            var op = _phaseOps.GetValueOrDefault(target, x => x);
            var t = op(Tensor);
            return new FoldTensor(t, target, Label)
            {
                Metadata = new Dictionary<string, object>(Metadata)
                {
                    ["transform_only"] = true,
                    ["source_phase"] = PhaseName,
                    ["target_phase"] = CanonicalName(target)
                }
            };
        }

        /// <summary>
        /// Move this tensor with the canonical K'UHUL wheel. FoldAlgebra owns
        /// transition legality; FoldTensor only applies the admitted transform.
        /// </summary>
        public FoldTensor RouteToPhase(FoldPhase target, FoldAlgebra algebra)
        {
            if (algebra == null)
                throw new ArgumentNullException(nameof(algebra));

            var sourceName = CanonicalName(Phase);
            var targetName = CanonicalName(target);

            if (!string.Equals(algebra.CurrentFold, sourceName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"FoldTensor is at {sourceName} but FoldAlgebra is at {algebra.CurrentFold}");

            if (!FoldAlgebra.IsLegalTransition(algebra.CurrentFold, targetName))
                throw new InvalidOperationException(
                    $"Illegal K'UHUL transition {algebra.CurrentFold} -> {targetName}");

            // TransitionTo is the control authority and records the proof.
            algebra.TransitionTo(targetName);

            var routed = TransformAs(target);
            routed.Metadata["transition_admitted"] = true;
            routed.Metadata["fold_from"] = sourceName;
            routed.Metadata["fold_to"] = targetName;
            return routed;
        }

        [Obsolete("Semantic routing belongs to XCFE/K'UHUL. Use RouteToPhase(target, FoldAlgebra) after admission.", true)]
        public FoldTensor RouteByIntent(string intent) =>
            throw new NotSupportedException("FoldTensor no longer performs semantic intent routing.");

        private static string CanonicalName(FoldPhase phase) =>
            phase == FoldPhase.Chen ? "Ch'en" : phase.ToString();

        public FoldTensor PhaseDot(FoldTensor other)
        {
            if (Tensor.Rank != 2 || other.Tensor.Rank != 2) throw new InvalidOperationException("PhaseDot requires 2D");
            var r = Tensor.Dot(other.Tensor);
            var p = (FoldPhase)(((int)Phase + (int)other.Phase) % 6);
            return new FoldTensor(r, p, $"{Label}\u00b7{other.Label}") { Metadata = new Dictionary<string, object>(Metadata) };
        }

        public FoldTensor FoldNorm()
        {
            var r = new NDArray(1); r[0] = Tensor.Norm();
            return new FoldTensor(r, Phase, $"norm_{Label}");
        }

        public FoldTensor RotateByPhase(double degrees)
        {
            var rad = degrees * Math.PI / 180.0;
            var d = Tensor.ToArray();
            var rotated = new double[d.Length];
            var factor = Math.Cos(rad) - Math.Sin(rad);
            for (int i = 0; i < d.Length; i++)
                rotated[i] = d[i] * factor;

            var r = new NDArray(rotated, Tensor.Shape);
            return new FoldTensor(r, Phase, $"rot_{Label}");
        }

        public FoldTensor Cascade(FoldAlgebra algebra, params FoldPhase[] phases)
        {
            if (algebra == null) throw new ArgumentNullException(nameof(algebra));
            var c = this;
            foreach (var p in phases)
                c = c.RouteToPhase(p, algebra);
            return c;
        }

        public string ToJson() => JsonSerializer.Serialize(new
        {
            label = Label, phase = Phase.ToString(), symbol = Symbol, angle = Angle,
            shape = Tensor.Shape, size = Tensor.Size, rank = Tensor.Rank,
            mean = Mean, std = Std, norm = Norm, min = Min, max = Max
        }, new JsonSerializerOptions { WriteIndented = true });

        public override string ToString() => $"{Symbol} {PhaseName} [{Label}] sz={Tensor.Size}";

        public string ToDetailedString() => $@"
  {Symbol} {PhaseName} [{Label}]
  Shape: {string.Join("\u00d7", Tensor.Shape)}  Size: {Tensor.Size}  Rank: {Tensor.Rank}
  Stats: \u03bc={Mean:F4}  \u03c3={Std:F4}  ||x||={Norm:F4}
  Range: [{Min:F4}, {Max:F4}]";

        public static FoldTensor operator +(FoldTensor a, FoldTensor b)
        { var r = a.Tensor.Add(b.Tensor); var p = (FoldPhase)(((int)a.Phase + (int)b.Phase) % 6); return new FoldTensor(r, p, $"{a.Label}+{b.Label}"); }
        public static FoldTensor operator -(FoldTensor a, FoldTensor b)
        { var r = a.Tensor.Sub(b.Tensor); var p = (FoldPhase)(((int)a.Phase + (int)b.Phase) % 6); return new FoldTensor(r, p, $"{a.Label}-{b.Label}"); }
        public static FoldTensor operator *(FoldTensor a, FoldTensor b)
        { var r = a.Tensor.Mul(b.Tensor); var p = (FoldPhase)(((int)a.Phase + (int)b.Phase) % 6); return new FoldTensor(r, p, $"{a.Label}\u00d7{b.Label}"); }
    }

    public static class SemanticMath
    {
        public static FoldTensor PhaseDot(FoldTensor a, FoldTensor b, FoldPhase target) =>
            a.PhaseDot(b).TransformAs(target);

        public static FoldTensor FoldNorm(FoldTensor a, FoldPhase target) =>
            a.FoldNorm().TransformAs(target);

        public static FoldTensor RotateByPhase(FoldTensor a, double deg, FoldPhase target) =>
            a.RotateByPhase(deg).TransformAs(target);

        public static FoldTensor Cascade(FoldTensor a, FoldAlgebra algebra, params FoldPhase[] phases) =>
            a.Cascade(algebra, phases);
        public static string VisualizeRoute(FoldTensor from, FoldTensor to)
        {
            var symbols = new[] { "\u25c9", "\u2b22", "\u25b3", "\u2726", "\u25c7", "\u2b21" };
            var names = new[] { "Pop", "Wo", "Yax", "Sek", "Chen", "Xul" };
            var ops = new[] { "identity", "sigmoid", "softmax", "relu", "abs", "sqrt" };
            return $"Phase Route: {from.Label} \u2192 {to.Label}\n{string.Join(" \u2192 ", symbols.Select((s, i) => $"{s}{names[i]}"))}\nOps: {string.Join(" \u2192 ", ops)}\n\u03bc: {from.Mean:F3} \u2192 {to.Mean:F3}, \u03c3: {from.Std:F3} \u2192 {to.Std:F3}";
        }
    }

    // ── Micronaut algebraic node ──────────────────────────────────────────

    /// <summary>
    /// Micronaut as a fold-algebraic node — seed, daemon, or active routing node.
    /// Bridges the JSON micronaut format into the FoldPhase/FoldTensor system so
    /// micronauts participate in phase routing, tensor operations, and daemon execution.
    /// </summary>
    public sealed class MicronautNode
    {
        public string Id { get; init; } = "";
        public string Subject { get; init; } = "";
        public string Capability { get; init; } = "";
        public string Brain { get; init; } = "";
        public FoldPhase Phase { get; init; } = FoldPhase.Pop;
        public double Quality { get; init; } = 100.0;
        public bool IsSeed { get; init; }
        public bool IsDaemon { get; init; }
        public string Source { get; init; } = "unknown";
        public Dictionary<string, object> Metadata { get; init; } = new();

        /// <summary>Project the micronaut into a FoldTensor for algebraic operations.</summary>
        public FoldTensor ToFoldTensor() => new(
            new NDArray(new double[] { Subject.GetHashCode() }, 1),
            Phase,
            Subject);

        /// <summary>Phase-shift this micronaut through the fold manifold.</summary>
        public MicronautNode TransformTo(FoldPhase target) => new()
        {
            Id = Id,
            Subject = Subject,
            Capability = Capability,
            Brain = Brain,
            Phase = target,
            Quality = Quality,
            IsSeed = IsSeed,
            IsDaemon = IsDaemon,
            Source = Source,
            Metadata = Metadata
        };

        /// <summary>Daemon execution signal — returns the fold operation for this node.</summary>
        public string DaemonOp => Phase switch
        {
            FoldPhase.Pop => "retrieve",
            FoldPhase.Wo  => "bind",
            FoldPhase.Yax => "validate",
            FoldPhase.Sek => "execute",
            FoldPhase.Chen => "evaluate",
            FoldPhase.Xul => "persist",
            _ => "idle"
        };

        /// <summary>The micronaut's routing key for the register.</summary>
        public string RouteKey => $"{Subject}/{Phase}/{Capability}/{Brain}";

        public override string ToString() =>
            $"{(IsSeed ? "[seed] " : "")}{(IsDaemon ? "[daemon] " : "")}{Subject}:{Phase} q={Quality:F0}";
    }

    /// <summary>
    /// Micronaut register — a fold-aware registry of micronauts as algebraic nodes.
    /// Seeds, daemons, and mutation-generated nodes coexist and route by phase.
    /// </summary>
    public sealed class MicronautRegister
    {
        private readonly List<MicronautNode> _nodes = new();
        private readonly Dictionary<FoldPhase, List<MicronautNode>> _byPhase = new()
        {
            { FoldPhase.Pop, new() },
            { FoldPhase.Wo, new() },
            { FoldPhase.Yax, new() },
            { FoldPhase.Sek, new() },
            { FoldPhase.Chen, new() },
            { FoldPhase.Xul, new() }
        };

        public IReadOnlyList<MicronautNode> All => _nodes.AsReadOnly();

        /// <summary>Register a micronaut node (seed, daemon, or learned).</summary>
        public void Register(MicronautNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            // Replace existing by id
            var existing = _nodes.FindIndex(n => n.Id == node.Id);
            if (existing >= 0)
            {
                _byPhase[_nodes[existing].Phase].Remove(_nodes[existing]);
                _nodes[existing] = node;
            }
            else
            {
                _nodes.Add(node);
            }

            _byPhase[node.Phase].Add(node);
        }

        /// <summary>Get all nodes active in a specific fold phase.</summary>
        public IReadOnlyList<MicronautNode> GetByPhase(FoldPhase phase) =>
            _byPhase.TryGetValue(phase, out var nodes) ? nodes : new List<MicronautNode>();

        /// <summary>Get seeds (base knowledge nodes).</summary>
        public IEnumerable<MicronautNode> Seeds => _nodes.Where(n => n.IsSeed);

        /// <summary>Get daemons (persistent fold processes).</summary>
        public IEnumerable<MicronautNode> Daemons => _nodes.Where(n => n.IsDaemon);

        /// <summary>Route a subject through the register — find matching nodes by phase.</summary>
        public MicronautNode Route(string subject, FoldPhase phase)
        {
            var norm = (subject ?? "").Trim().ToLowerInvariant();
            return _byPhase[phase]
                .OrderByDescending(n => n.Quality)
                .ThenBy(n => n.Subject == norm ? 0 : 1)
                .ThenBy(n => n.Subject.Contains(norm, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .FirstOrDefault();
        }

        /// <summary>Emit all nodes as FoldTensors for batch algebraic operations.</summary>
        public FoldTensor[] ToTensorBatch() =>
            _nodes.Select(n => n.ToFoldTensor()).ToArray();

        public int Count => _nodes.Count;

        /// <summary>List daemon operations for the current cycle.</summary>
        public IReadOnlyList<(MicronautNode Node, string Op)> GetDaemonOps(FoldPhase activePhase) =>
            _byPhase[activePhase]
                .Where(n => n.IsDaemon)
                .Select(n => (n, n.DaemonOp))
                .ToList();
    }
}
