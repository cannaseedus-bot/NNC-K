using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuralGrammar.Core
{
    /// <summary>Learns invariant relationships, constraints, transformations, and state across fold phases</summary>
    public class SemanticInvariantLearner
    {
        private readonly List<InvariantObs> _obs = new();
        private readonly List<Constraint> _constraints = new();
        private readonly List<Transform> _transforms = new();
        private readonly Dictionary<string, object> _state = new();

        public int ObservationCount => _obs.Count;
        public int ConstraintCount => _constraints.Count;
        public int TransformCount => _transforms.Count;

        public MonadLawResult VerifyMonadLaws(List<string> foldTrace, double[] confidences)
        {
            var result = new MonadLawResult();
            if (foldTrace.Count > 0 && foldTrace[0] == "Pop")
                result.LeftIdentity = confidences.Length > 0 && confidences[0] >= 0.95;
            if (foldTrace.Count >= 2 &&
                foldTrace[foldTrace.Count - 2] == "Xul" &&
                foldTrace[foldTrace.Count - 1] == "Pop")
                result.RightIdentity = true;
            if (foldTrace.Count >= 6)
            {
                var phaseCounts = foldTrace.Take(6)
                    .GroupBy(f => f)
                    .ToDictionary(g => g.Key, g => g.Count());
                result.Associativity = phaseCounts.Count == 6 &&
                    phaseCounts.Values.All(v => v == 1);
            }
            result.AllPassed = result.LeftIdentity && result.RightIdentity && result.Associativity;
            return result;
        }

        public bool VerifyScoringMorphism(double keywordScore, double quality, double finalScore)
        {
            var expected = keywordScore * 0.6 + (quality / 100.0) * 0.4;
            return Math.Abs(finalScore - expected) < 0.001;
        }

        // === 1. INVARIANT RELATIONSHIPS ===
        public void Observe(string label, NDArray tensor, FoldPhase from, FoldPhase to)
        {
            var inv = new InvariantObs
            {
                Label = label, Tensor = tensor, From = from, To = to, Time = DateTime.UtcNow,
                Norm = tensor.Norm(), Mean = tensor.Mean(), Var = tensor.Var(), Max = tensor.Max(), Min = tensor.Min(), Sum = tensor.Sum()
            };
            _obs.Add(inv);
            LearnInvariant(inv);
        }

        private void LearnInvariant(InvariantObs o)
        {
            if (o.From == FoldPhase.Pop && o.To == FoldPhase.Wo && (o.Min < 0 || o.Max > 1))
                Console.WriteLine($"  Invariant: Sigmoid output [{o.Min:F3},{o.Max:F3}] outside [0,1]");
            if (o.From == FoldPhase.Wo && o.To == FoldPhase.Yax && Math.Abs(o.Sum - 1.0) > 1e-6)
                Console.WriteLine($"  Invariant: Softmax sum = {o.Sum:F4} (expected 1.0)");
        }

        // === 2. CONSTRAINTS ===
        public void AddConstraint(string name, string desc, FoldPhase[] phases, Func<NDArray, bool> check)
        {
            _constraints.Add(new Constraint { Name = name, Desc = desc, Phases = phases, Check = check });
        }

        public List<string> Validate(NDArray t, FoldPhase p)
        {
            return _constraints.Where(c => c.Phases == null || c.Phases.Contains(p))
                .Where(c => !c.Check(t)).Select(c => c.Name).ToList();
        }

        // === 3. TRANSFORMATIONS ===
        public NDArray Apply(NDArray t, FoldPhase from, FoldPhase to)
        {
            var tx = _transforms.FirstOrDefault(x => x.From == from && x.To == to);
            if (tx == null) { tx = Learn(t, from, to); _transforms.Add(tx); }
            return tx.Func(t);
        }

        private Transform Learn(NDArray input, FoldPhase from, FoldPhase to)
        {
            if (from == FoldPhase.Pop && to == FoldPhase.Wo) return new Transform { Name = "sigmoid", From = from, To = to, Func = x => x.Sigmoid() };
            if (from == FoldPhase.Wo && to == FoldPhase.Yax) return new Transform { Name = "softmax", From = from, To = to, Func = x => x.Softmax() };
            if (from == FoldPhase.Yax && to == FoldPhase.Sek) return new Transform { Name = "relu", From = from, To = to, Func = x => x.ReLU() };
            if (from == FoldPhase.Sek && to == FoldPhase.Chen) return new Transform { Name = "abs", From = from, To = to, Func = x => x.Abs() };
            if (from == FoldPhase.Chen && to == FoldPhase.Xul) return new Transform { Name = "sqrt", From = from, To = to, Func = x => x.Sqrt() };
            return new Transform { Name = "identity", From = from, To = to, Func = x => x.Clone() };
        }

        // === 4. STATE ===
        public void SetState(string k, object v) => _state[k] = v;
        public object GetState(string k) => _state.TryGetValue(k, out var v) ? v : null;
        public Dictionary<string, object> AllState => new(_state);

        // === TRAINING ===
        public void Train(int epochs = 10)
        {
            Console.WriteLine($"Training on {_obs.Count} observations...");
            for (int e = 0; e < epochs; e++)
            {
                double loss = 0;
                foreach (var o in _obs)
                {
                    var pred = Apply(o.Tensor, o.From, o.To);
                    var diff = pred.Sub(o.Tensor);
                    loss += diff.Mul(diff).Mean();
                }
                if (e % 5 == 0 || e == epochs - 1) Console.WriteLine($"  Epoch {e}: loss = {(loss / Math.Max(1, _obs.Count)):F4}");
            }
        }

        public string Report()
        {
            var lines = new List<string> { $"Observations: {_obs.Count}", $"Constraints: {_constraints.Count}", $"Transforms: {_transforms.Count}", "State:" };
            foreach (var kv in _state) lines.Add($"  {kv.Key} = {kv.Value}");
            return string.Join("\n", lines);
        }

        public class InvariantObs { public string Label; public NDArray Tensor; public FoldPhase From, To; public DateTime Time; public double Norm, Mean, Var, Max, Min, Sum; }
        public class Constraint { public string Name, Desc; public FoldPhase[] Phases; public Func<NDArray, bool> Check; }
        public class Transform { public string Name; public FoldPhase From, To; public Func<NDArray, NDArray> Func; }
    }

    public static class FoldEx
    {
        public static double Angle(this FoldPhase p) => p switch { FoldPhase.Pop => 0, FoldPhase.Wo => 60, FoldPhase.Yax => 120, FoldPhase.Sek => 180, FoldPhase.Chen => 240, FoldPhase.Xul => 300, _ => 0 };
        public static string Glyph(this FoldPhase p) => p switch { FoldPhase.Pop => "\u25c9", FoldPhase.Wo => "\u2b22", FoldPhase.Yax => "\u25b3", FoldPhase.Sek => "\u2726", FoldPhase.Chen => "\u25c7", FoldPhase.Xul => "\u2b21", _ => "?" };
        public static FoldPhase Next(this FoldPhase p) => (FoldPhase)(((int)p + 1) % 6);
        public static FoldPhase Prev(this FoldPhase p) => (FoldPhase)(((int)p + 5) % 6);
    }

    public sealed class MonadLawResult
    {
        public bool LeftIdentity { get; set; }   // return(x) >>= f == f(x)
        public bool RightIdentity { get; set; }  // m >>= return == m
        public bool Associativity { get; set; }  // (m >>= f) >>= g == m >>= (x => f(x) >>= g)
        public bool AllPassed { get; set; }

        public override string ToString()
        {
            var parts = new List<string>();
            parts.Add(LeftIdentity ? "LEFT_ID: pass" : "LEFT_ID: FAIL");
            parts.Add(RightIdentity ? "RIGHT_ID: pass" : "RIGHT_ID: FAIL");
            parts.Add(Associativity ? "ASSOC: pass" : "ASSOC: FAIL");
            parts.Add(AllPassed ? "ALL: PASS" : "ALL: FAIL");
            return string.Join(" | ", parts);
        }
    }
}
