using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// MathML/K'UHUL semantic tree engine.
    /// Parsing/classification is side-effect free; XCFE/Sek owns tool effects.
    /// </summary>
    public class MathMLEngine
    {
        private readonly Dictionary<string, object> _variables = new();
        private readonly Dictionary<string, double[,]> _tensors = new();

        public sealed class SemanticNode
        {
            public string Operator { get; set; }
            public string Domain { get; set; }
            public Dictionary<string, string> Arguments { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
            public List<SemanticNode> Children { get; set; } = new();
            public string Fold { get; set; } = "Yax";
            public bool RequiresModel { get; set; }
            public double Confidence { get; set; }
        }

        public Tensor Evaluate(string expression, Dictionary<string, object> context = null)
        {
            if (context != null)
                foreach (var kv in context) _variables[kv.Key] = kv.Value;

            if (LooksLikeXml(expression))
                return EvaluateMathML(XElement.Parse(expression));

            return EvaluateLegacy(expression);
        }

        public void SetTensor(string name, double[,] value)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Tensor name required", nameof(name));
            _tensors[name] = value ?? throw new ArgumentNullException(nameof(value));
        }

        public SemanticNode ParseSemantic(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return Unresolved("", "empty");

            if (!LooksLikeXml(expression))
                return ParseCommandAlias(expression);

            var root = XElement.Parse(expression);
            if (root.Name.LocalName.Equals("math", StringComparison.OrdinalIgnoreCase) &&
                root.Elements().Any())
                root = root.Elements().First();

            return ParseSemanticElement(root);
        }

        private SemanticNode ParseSemanticElement(XElement element)
        {
            if (!element.Name.LocalName.Equals("apply", StringComparison.OrdinalIgnoreCase))
                return new SemanticNode {
                    Operator = element.Name.LocalName,
                    Arguments = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
                    { ["value"] = element.Value.Trim() },
                    Confidence = 1.0
                };

            var parts = element.Elements().ToList();
            if (parts.Count == 0) return Unresolved("", "empty apply");

            var op = parts[0].Name.LocalName.ToLowerInvariant();
            var node = new SemanticNode {
                Operator = op,
                Fold = op == "call" ? "Sek" : "Yax",
                Confidence = 1.0
            };

            foreach (var arg in parts.Skip(1))
            {
                var key = arg.Attribute("name")?.Value ?? arg.Name.LocalName;
                var unresolved = string.Equals(
                    arg.Attribute("unresolved")?.Value, "true",
                    StringComparison.OrdinalIgnoreCase);

                if (arg.Name.LocalName.Equals("apply", StringComparison.OrdinalIgnoreCase))
                {
                    node.Children.Add(ParseSemanticElement(arg));
                    continue;
                }

                var value = arg.Value.Trim();
                node.Arguments[key] = value;

                if (arg.Name.LocalName.Equals("web", StringComparison.OrdinalIgnoreCase) ||
                    arg.Name.LocalName.Equals("news", StringComparison.OrdinalIgnoreCase))
                    node.Domain = arg.Name.LocalName.ToLowerInvariant();

                if (unresolved || string.IsNullOrWhiteSpace(value))
                {
                    node.RequiresModel = true;
                    node.Confidence = Math.Min(node.Confidence, 0.5);
                }
            }

            if (string.IsNullOrWhiteSpace(node.Domain) && op == "search")
                node.Domain = "web";

            return node;
        }

        public SemanticNode ParseCommandAlias(string text)
        {
            var raw = (text ?? "").Trim();
            var lower = raw.ToLowerInvariant();

            var aliases = new (string Phrase, string Operator, string Domain)[]
            {
                ("search the news for ", "search", "news"),
                ("search news for ", "search", "news"),
                ("latest news on ", "search", "news"),
                ("search the web for ", "search", "web"),
                ("search web for ", "search", "web"),
                ("web search ", "search", "web"),
                ("find online ", "search", "web"),
                ("look up ", "search", "web"),
                ("search for ", "search", "web")
            };

            foreach (var alias in aliases.OrderByDescending(x => x.Phrase.Length))
            {
                var index = lower.IndexOf(alias.Phrase, StringComparison.Ordinal);
                if (index < 0) continue;

                var query = raw.Substring(index + alias.Phrase.Length).Trim();
                return new SemanticNode {
                    Operator = alias.Operator,
                    Domain = alias.Domain,
                    Fold = "Yax",
                    Arguments = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
                    { ["query"] = query },
                    RequiresModel = string.IsNullOrWhiteSpace(query),
                    Confidence = string.IsNullOrWhiteSpace(query) ? 0.5 : 1.0
                };
            }

            return Unresolved(raw, "no deterministic command alias matched");
        }

        public SemanticNode Resolve(SemanticNode node, string argument, string value)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            node.Arguments[argument] = value ?? "";
            node.RequiresModel = node.Arguments.Values.Any(string.IsNullOrWhiteSpace);
            if (!node.RequiresModel) node.Confidence = 1.0;
            return node;
        }

        public string ToCanonicalMathML(SemanticNode node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));

            var apply = new XElement("apply", new XElement(node.Operator ?? "unresolved"));
            if (!string.IsNullOrWhiteSpace(node.Domain))
                apply.Add(new XElement(node.Domain));

            foreach (var kv in node.Arguments)
            {
                var arg = new XElement("arg", kv.Value ?? "");
                arg.SetAttributeValue("name", kv.Key);
                if (string.IsNullOrWhiteSpace(kv.Value))
                    arg.SetAttributeValue("unresolved", "true");
                apply.Add(arg);
            }

            foreach (var child in node.Children)
                apply.Add(XElement.Parse(ToCanonicalMathML(child)));

            return apply.ToString(SaveOptions.DisableFormatting);
        }

        private SemanticNode Unresolved(string text, string reason) =>
            new SemanticNode {
                Operator = "unresolved",
                Domain = "semantic",
                Fold = "Yax",
                Arguments = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
                { ["text"] = text ?? "", ["reason"] = reason ?? "" },
                RequiresModel = true,
                Confidence = 0.0
            };

        private Tensor EvaluateMathML(XElement node)
        {
            var name = node.Name.LocalName.ToLowerInvariant();
            if (name == "math" && node.Elements().Any())
                return EvaluateMathML(node.Elements().First());
            if (name == "cn") return Scalar(ParseNumber(node.Value));
            if (name == "ci") return GetTensor(node.Value.Trim());
            if (name != "apply")
                throw new InvalidOperationException($"Unsupported MathML node: {name}");

            var parts = node.Elements().ToList();
            if (parts.Count == 0) throw new InvalidOperationException("Empty <apply>");

            var op = parts[0].Name.LocalName.ToLowerInvariant();
            var args = parts.Skip(1).ToList();

            return op switch {
                "matmul" or "times" when args.Count == 2 =>
                    MatrixMultiply(EvaluateMathML(args[0]), EvaluateMathML(args[1])),
                "relu" when args.Count == 1 => Relu(EvaluateMathML(args[0])),
                "sigmoid" when args.Count == 1 => Sigmoid(EvaluateMathML(args[0])),
                _ => throw new InvalidOperationException(
                    $"'{op}' is semantic/control or unsupported math; use ParseSemantic for control.")
            };
        }

        private Tensor EvaluateLegacy(string expr)
        {
            var text = (expr ?? "").Trim();
            if (text.StartsWith("matmul(", StringComparison.OrdinalIgnoreCase)) {
                var args = BetweenParens(text).Split(',');
                if (args.Length != 2) throw new FormatException("matmul requires 2 operands");
                return MatrixMultiply(GetTensor(args[0].Trim()), GetTensor(args[1].Trim()));
            }
            if (text.StartsWith("relu(", StringComparison.OrdinalIgnoreCase))
                return Relu(GetTensor(BetweenParens(text).Trim()));
            if (text.StartsWith("sigmoid(", StringComparison.OrdinalIgnoreCase))
                return Sigmoid(GetTensor(BetweenParens(text).Trim()));
            return Scalar(ParseNumber(text));
        }

        private static string BetweenParens(string text)
        {
            var open = text.IndexOf('(');
            var close = text.LastIndexOf(')');
            if (open < 0 || close <= open) throw new FormatException("Expected (...)");
            return text.Substring(open + 1, close - open - 1);
        }

        private Tensor GetTensor(string name)
        {
            name = (name ?? "").Trim();
            if (_tensors.TryGetValue(name, out var tensor)) return new Tensor(tensor);
            return Scalar(ParseNumber(name));
        }

        private Tensor MatrixMultiply(Tensor a, Tensor b)
        {
            if (a.Cols != b.Rows) throw new InvalidOperationException("Matrix dimensions do not align");
            var result = new double[a.Rows, b.Cols];
            for (int i = 0; i < a.Rows; i++)
                for (int j = 0; j < b.Cols; j++)
                    for (int k = 0; k < a.Cols; k++)
                        result[i,j] += a.Data[i,k] * b.Data[k,j];
            return new Tensor(result);
        }

        public Tensor Relu(Tensor t)
        {
            var result = new double[t.Rows,t.Cols];
            for (int i=0;i<t.Rows;i++) for (int j=0;j<t.Cols;j++)
                result[i,j] = Math.Max(0,t.Data[i,j]);
            return new Tensor(result);
        }

        public Tensor Sigmoid(Tensor t)
        {
            var result = new double[t.Rows,t.Cols];
            for (int i=0;i<t.Rows;i++) for (int j=0;j<t.Cols;j++)
                result[i,j] = 1.0 / (1.0 + Math.Exp(-t.Data[i,j]));
            return new Tensor(result);
        }

        private static Tensor Scalar(double value) => new(new double[,] {{ value }});
        private static double ParseNumber(string value) =>
            double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        private static bool LooksLikeXml(string value) =>
            !string.IsNullOrWhiteSpace(value) && value.TrimStart().StartsWith("<");
    }

    public class Tensor
    {
        public double[,] Data { get; set; }
        public int Rows => Data.GetLength(0);
        public int Cols => Data.GetLength(1);
        public Tensor(double[,] data) => Data = data;
        public Tensor(int rows, int cols) => Data = new double[rows,cols];
        public double this[int i,int j] { get => Data[i,j]; set => Data[i,j] = value; }

        public Tensor Flatten()
        {
            var flat = new double[1, Rows * Cols];
            for (int i=0;i<Rows;i++) for (int j=0;j<Cols;j++)
                flat[0,i*Cols+j] = Data[i,j];
            return new Tensor(flat);
        }

        public override string ToString()
        {
            var lines = new List<string>();
            for (int i=0;i<Rows;i++) {
                var row = new List<string>();
                for (int j=0;j<Cols;j++) row.Add(Data[i,j].ToString("F4"));
                lines.Add("[" + string.Join(", ", row) + "]");
            }
            return "[" + string.Join(", ", lines) + "]";
        }
    }
}
