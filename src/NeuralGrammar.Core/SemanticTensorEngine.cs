using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using NeuralGrammar.Core.XCFE;

namespace NeuralGrammar.Core
{
    /// <summary>Unified integration of all 31 files — learns invariants, constraints, transformations, state</summary>
    public class SemanticTensorEngine
    {
        // Core
        public Kuhul.KuhulMathEngine KuhulMath { get; private set; }
        public MathMLEngine MathML { get; private set; }
        public NNCInterpreter NNC { get; private set; }
        public XSDParser XSD { get; private set; }
        public HybridSearch Search { get; private set; }

        // Tensor
        public SemanticInvariantLearner Invariants { get; private set; }
        public SemanticDataset Dataset { get; private set; }
        public SkinningEngine Skin { get; private set; }

        // Runtime
        public XCFERuntime XCFE { get; private set; }
        public FoldAlgebra Folds { get; private set; }
        public MCPServer MCP { get; private set; }
        public UserDatabase Users { get; private set; }

        // State
        public FoldPhase Phase => ParseFold(Folds?.CurrentFold ?? "Pop");
        public double Entropy { get; private set; } = 1.0;
        public List<FoldPhase> History { get; private set; } = new();
        public Dictionary<string, object> CustomState { get; private set; } = new();
        public int ObsCount { get; private set; }
        public int ConstraintCount { get; private set; }

        // Algebraic node system
        public MicronautRegister MicronautRegister { get; set; }
        public TaskPlanner TaskPlanner { get; set; }
        public XCFEMutation XCFEMutation { get; set; }
        public SessionCache SessionCache { get; set; } = new SessionCache(50);

        public SemanticTensorEngine()
        {
            KuhulMath = new Kuhul.KuhulMathEngine();
            MathML = new MathMLEngine();
            NNC = new NNCInterpreter();
            XSD = new XSDParser();
            Search = new HybridSearch();
            Invariants = new SemanticInvariantLearner();
            Dataset = new SemanticDataset();
            Skin = new SkinningEngine();
            XCFE = new XCFERuntime();
            Folds = new FoldAlgebra();
            MCP = new MCPServer();
            Users = new UserDatabase();
            CustomState["initialized"] = DateTime.UtcNow;
        }

        // === FOLD MANAGEMENT ===
        public FoldPhase[] AllPhases => new[]
        {
            FoldPhase.Pop, FoldPhase.Wo, FoldPhase.Yax,
            FoldPhase.Sek, FoldPhase.Chen, FoldPhase.Xul
        };

        public FoldPhase AdvanceFold()
        {
            var next = NextFold(Folds.CurrentFold);
            Folds.TransitionTo(next);

            var phase = ParseFold(next);
            History.Add(phase);
            CustomState["phase"] = next;
            return phase;
        }

        public FoldPhase AdvanceTo(FoldPhase target)
        {
            var targetName = CanonicalName(target);
            int guard = 0;

            while (!string.Equals(Folds.CurrentFold, targetName, StringComparison.OrdinalIgnoreCase))
            {
                AdvanceFold();
                if (++guard > AllPhases.Length)
                    throw new InvalidOperationException(
                        $"Unable to reach fold '{targetName}' from '{Folds.CurrentFold}'.");
            }

            return Phase;
        }

        [Obsolete("FoldAlgebra owns runtime phase transitions. Use AdvanceFold() or AdvanceTo().", true)]
        public void SetPhase(FoldPhase p) =>
            throw new NotSupportedException("Direct fold mutation is disabled.");

        private static string CanonicalName(FoldPhase phase) =>
            phase == FoldPhase.Chen ? "Ch'en" : phase.ToString();

        private static FoldPhase ParseFold(string fold) =>
            string.Equals(fold, "Ch'en", StringComparison.OrdinalIgnoreCase)
                ? FoldPhase.Chen
                : Enum.TryParse<FoldPhase>(fold, true, out var phase)
                    ? phase
                    : FoldPhase.Pop;

        private static string NextFold(string fold)
        {
            if (string.Equals(fold, "Pop", StringComparison.OrdinalIgnoreCase)) return "Wo";
            if (string.Equals(fold, "Wo", StringComparison.OrdinalIgnoreCase)) return "Yax";
            if (string.Equals(fold, "Yax", StringComparison.OrdinalIgnoreCase)) return "Sek";
            if (string.Equals(fold, "Sek", StringComparison.OrdinalIgnoreCase)) return "Ch'en";
            if (string.Equals(fold, "Ch'en", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fold, "Chen", StringComparison.OrdinalIgnoreCase)) return "Xul";
            if (string.Equals(fold, "Xul", StringComparison.OrdinalIgnoreCase)) return "Pop";
            throw new InvalidOperationException($"Unknown K'UHUL fold '{fold}'.");
        }

        // === 1. INVARIANTS ===
        public void Observe(string label, NDArray t, FoldPhase from, FoldPhase to)
        {
            Invariants.Observe(label, t, from, to);
            ObsCount++;
        }

        // === 2. CONSTRAINTS ===
        public void AddConstraint(string name, string desc, FoldPhase[] phases, Func<NDArray, bool> check)
        {
            Invariants.AddConstraint(name, desc, phases, check);
            ConstraintCount++;
        }

        public bool Validate(NDArray t, FoldPhase p) => Invariants.Validate(t, p).Count == 0;

        // === 3. TRANSFORMATIONS ===
        public NDArray Transform(NDArray t, FoldPhase from, FoldPhase to)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            return Invariants.Apply(t, from, to);
        }

        public FoldTensor Cascade(FoldTensor f, FoldPhase target)
        {
            if (f == null) throw new ArgumentNullException(nameof(f));

            var c = f;
            int guard = 0;

            while (c.Phase != target)
            {
                var next = ParseFold(NextFold(CanonicalName(c.Phase)));
                var transformed = Invariants.Apply(c.Tensor, c.Phase, next);

                c = new FoldTensor(transformed, c.Phase, c.Label)
                {
                    Metadata = new Dictionary<string, object>(c.Metadata)
                };
                c = c.RouteToPhase(next, Folds);

                History.Add(next);
                CustomState["phase"] = CanonicalName(next);

                if (++guard > AllPhases.Length)
                    throw new InvalidOperationException(
                        $"Cascade could not reach {CanonicalName(target)}.");
            }

            return c;
        }

        // === 4. STATE ===
        public void SetState(string k, object v) { CustomState[k] = v; Invariants.SetState(k, v); }
        public void SpendEntropy(double a) { Entropy = Math.Max(0, Entropy - a); CustomState["entropy"] = Entropy; }
        public void RestoreEntropy(double a) { Entropy = Math.Min(1.0, Entropy + a); CustomState["entropy"] = Entropy; }

        // === SEARCH ===
        public SearchResult SearchQuery(string q, double tw = 0.6, double sw = 0.4)
        {
            if (string.IsNullOrWhiteSpace(q))
                throw new ArgumentException("Search query is required.", nameof(q));

            AdvanceTo(FoldPhase.Yax);
            return Search.Search(q, new HybridSearchConfig
            {
                TextWeight = tw,
                SemanticWeight = sw
            });
        }

        // === TRAINING ===
        public void Train(int epochs = 10) { Invariants.Train(epochs); }

        // === EXPORT ===
        public string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Phase: {Phase.Glyph()} {Phase}  Entropy: {Entropy:F2}");
            sb.AppendLine($"Obs: {ObsCount}  Constraints: {ConstraintCount}  History: {string.Join("->", History.Select(p => p.Glyph()))}");
            foreach (var kv in CustomState) sb.AppendLine($"  {kv.Key} = {kv.Value}");
            return sb.ToString();
        }

        public string ExportJson() => JsonSerializer.Serialize(new {
            phase = Phase.ToString(), entropy = Entropy, history = History.Select(p => p.ToString()),
            obs = ObsCount, constraints = ConstraintCount, state = CustomState
        }, new JsonSerializerOptions { WriteIndented = true });

        public override string ToString() => $"{Phase.Glyph()} {Phase} | E={Entropy:F2} | {ObsCount} obs | {CustomState.Count} keys";
    }
}
