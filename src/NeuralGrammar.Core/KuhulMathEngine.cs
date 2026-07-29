using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NeuralGrammar.Core.Kuhul
{
    /// <summary>
    /// K'UHUL Math Engine — expression parser, AST builder, safe math executor.
    /// Matches asx-kuhul-math-engine.manifest.json
    /// Pipeline: Pop(parse) -> Wo(build AST) -> Sek(compile+execute) -> Ch'en(render) -> Xul(record)
    /// </summary>
    public class KuhulMathEngine
    {
        // ---- Glyph registry ----

        private static readonly Dictionary<char, string> _arithmeticGlyphs = new()
        {
            {'+', "add"}, {'-', "subtract"}, {'*', "multiply"},
            {'/', "divide"}, {'^', "power"}, {'%', "mod"}
        };

        private static readonly Dictionary<char, (string Op, string Method)> _calculusGlyphs = new()
        {
            {'∫', ("integrate", "simpson")},
            {'∂', ("derivative", "central_difference")},
            {'∑', ("summation", null)},
            {'∏', ("product", null)}
        };

        private static readonly HashSet<string> _mathFunctions = new()
        {
            "sin", "cos", "tan", "asin", "acos", "atan",
            "sinh", "cosh", "tanh",
            "exp", "log", "ln", "log2",
            "sqrt", "cbrt",
            "abs", "floor", "ceil", "round", "trunc", "sign",
            "max", "min"
        };

        private static readonly Dictionary<string, double> _constants = new()
        {
            ["π"] = Math.PI,
            ["e"] = Math.E,
            ["φ"] = 1.618033988749895,
            ["∞"] = double.PositiveInfinity
        };

        // ---- Pipeline stages ----

        public class Token
        {
            public enum TokenType { Number, Operator, Function, Constant, LParen, RParen, Comma, Glyph, Identifier }
            public TokenType Type { get; set; }
            public string Value { get; set; }
        }

        public class ASTNode
        {
            public enum NodeType { Number, UnaryOp, BinaryOp, Function, Constant }
            public NodeType Type { get; set; }
            public string Value { get; set; }
            public ASTNode Left { get; set; }
            public ASTNode Right { get; set; }
            public List<ASTNode> Arguments { get; set; }
        }

        public class MathResult
        {
            public bool Success { get; set; }
            public double Value { get; set; }
            public string Error { get; set; }
            public List<Token> Tokens { get; set; }
            public ASTNode AST { get; set; }
            public string JsExpression { get; set; }
        }

        // ---- Pop: Parse expression into tokens ----

        public MathResult Parse(string expression)
        {
            var result = new MathResult();

            try
            {
                expression = expression?.Trim() ?? "";
                if (string.IsNullOrEmpty(expression))
                {
                    result.Error = "Empty expression";
                    return result;
                }

                var tokens = Tokenize(expression);
                result.Tokens = tokens;

                // Validate tokens
                if (tokens.Count == 0)
                {
                    result.Error = "No valid tokens found";
                    return result;
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = $"Parse error: {ex.Message}";
            }

            return result;
        }

        // ---- Wo: Build AST from tokens ----

        public MathResult BuildAST(List<Token> tokens)
        {
            var result = new MathResult();

            try
            {
                if (tokens == null || tokens.Count == 0)
                {
                    result.Error = "No tokens to build AST";
                    return result;
                }

                int pos = 0;
                var ast = ParseExpression(tokens, ref pos);

                if (pos < tokens.Count)
                    throw new Exception($"Unexpected token at position {pos}: {tokens[pos].Value}");

                result.AST = ast;
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = $"AST build error: {ex.Message}";
            }

            return result;
        }

        // ---- Sek: Compile AST to JS expression string ----

        public MathResult Compile(ASTNode ast)
        {
            var result = new MathResult();

            try
            {
                if (ast == null)
                {
                    result.Error = "No AST to compile";
                    return result;
                }

                result.JsExpression = CompileNode(ast);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = $"Compile error: {ex.Message}";
            }

            return result;
        }

        // ---- Sek: Execute math expression (C# evaluation) ----

        public MathResult Execute(string expression)
        {
            var result = new MathResult();

            try
            {
                // Full pipeline
                var parseResult = Parse(expression);
                if (!parseResult.Success) return parseResult;

                var astResult = BuildAST(parseResult.Tokens);
                if (!astResult.Success) return astResult;

                var compileResult = Compile(astResult.AST);
                if (!compileResult.Success) return compileResult;

                var value = EvaluateAST(astResult.AST);

                result.Success = true;
                result.Value = value;
                result.Tokens = parseResult.Tokens;
                result.AST = astResult.AST;
                result.JsExpression = compileResult.JsExpression;
            }
            catch (Exception ex)
            {
                result.Error = $"Execution error: {ex.Message}";
            }

            return result;
        }

        // ---- Tokenizer ----

        private static readonly Regex _numberPattern = new(@"^-?\d+\.?\d*(?:[eE][+-]?\d+)?");
        private static readonly Regex _functionPattern = new(@"^(sin|cos|tan|asin|acos|atan|sinh|cosh|tanh|exp|log|ln|log2|sqrt|cbrt|abs|floor|ceil|round|trunc|sign|max|min)\b", RegexOptions.IgnoreCase);

        private List<Token> Tokenize(string expr)
        {
            var tokens = new List<Token>();
            int i = 0;

            while (i < expr.Length)
            {
                // Skip whitespace
                if (char.IsWhiteSpace(expr[i])) { i++; continue; }

                // Constants (check before identifiers)
                if (i < expr.Length && _constants.ContainsKey(expr[i].ToString()))
                {
                    tokens.Add(new Token { Type = Token.TokenType.Constant, Value = expr[i].ToString() });
                    i++;
                    continue;
                }

                // Arithmetic glyphs
                if (_arithmeticGlyphs.ContainsKey(expr[i]))
                {
                    tokens.Add(new Token { Type = Token.TokenType.Operator, Value = expr[i].ToString() });
                    i++;
                    continue;
                }

                // Calculus glyphs
                if (_calculusGlyphs.ContainsKey(expr[i]))
                {
                    tokens.Add(new Token { Type = Token.TokenType.Glyph, Value = expr[i].ToString() });
                    i++;
                    continue;
                }

                // "lim" keyword
                if (i + 2 < expr.Length && expr.Substring(i, 3).Equals("lim", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new Token { Type = Token.TokenType.Glyph, Value = "lim" });
                    i += 3;
                    continue;
                }

                // Functions
                var funcMatch = _functionPattern.Match(expr.Substring(i));
                if (funcMatch.Success)
                {
                    tokens.Add(new Token { Type = Token.TokenType.Function, Value = funcMatch.Value.ToLowerInvariant() });
                    i += funcMatch.Length;
                    continue;
                }

                // Numbers
                var numMatch = _numberPattern.Match(expr.Substring(i));
                if (numMatch.Success)
                {
                    tokens.Add(new Token { Type = Token.TokenType.Number, Value = numMatch.Value });
                    i += numMatch.Length;
                    continue;
                }

                // Parentheses and commas
                if (expr[i] == '(') { tokens.Add(new Token { Type = Token.TokenType.LParen, Value = "(" }); i++; continue; }
                if (expr[i] == ')') { tokens.Add(new Token { Type = Token.TokenType.RParen, Value = ")" }); i++; continue; }
                if (expr[i] == ',') { tokens.Add(new Token { Type = Token.TokenType.Comma, Value = "," }); i++; continue; }

                throw new Exception($"Unexpected character at position {i}: '{expr[i]}'");
            }

            return tokens;
        }

        // ---- Recursive descent parser ----
        // Precedence: +- (lowest), */%, ^ (highest), unary, functions, atoms

        private ASTNode ParseExpression(List<Token> tokens, ref int pos)
        {
            return ParseBinary(tokens, ref pos, 0);
        }

        private ASTNode ParseBinary(List<Token> tokens, ref int pos, int minPrecedence)
        {
            var left = ParseUnary(tokens, ref pos);

            while (pos < tokens.Count)
            {
                var tok = tokens[pos];
                int precedence = GetPrecedence(tok);

                if (precedence < minPrecedence) break;

                pos++; // consume operator
                var right = ParseBinary(tokens, ref pos, precedence + 1);

                left = new ASTNode
                {
                    Type = ASTNode.NodeType.BinaryOp,
                    Value = tok.Value,
                    Left = left,
                    Right = right
                };
            }

            return left;
        }

        private ASTNode ParseUnary(List<Token> tokens, ref int pos)
        {
            if (pos >= tokens.Count)
                throw new Exception("Unexpected end of expression");

            var tok = tokens[pos];

            // Unary minus
            if (tok.Type == Token.TokenType.Operator && tok.Value == "-")
            {
                // Check if it's unary (preceded by operator, (, or start)
                if (pos == 0 || tokens[pos - 1].Type == Token.TokenType.Operator ||
                    tokens[pos - 1].Type == Token.TokenType.LParen ||
                    tokens[pos - 1].Type == Token.TokenType.Comma)
                {
                    pos++;
                    var operand = ParseUnary(tokens, ref pos);
                    return new ASTNode
                    {
                        Type = ASTNode.NodeType.UnaryOp,
                        Value = "-",
                        Right = operand
                    };
                }
            }

            // Function call: f( ... )
            if (tok.Type == Token.TokenType.Function)
            {
                pos++;
                if (pos >= tokens.Count || tokens[pos].Type != Token.TokenType.LParen)
                    throw new Exception($"Expected '(' after function '{tok.Value}'");
                pos++; // consume '('

                var args = new List<ASTNode>();
                while (pos < tokens.Count && tokens[pos].Type != Token.TokenType.RParen)
                {
                    if (args.Count > 0)
                    {
                        if (tokens[pos].Type != Token.TokenType.Comma)
                            throw new Exception("Expected ',' between function arguments");
                        pos++; // consume ','
                    }
                    args.Add(ParseExpression(tokens, ref pos));
                }

                if (pos >= tokens.Count || tokens[pos].Type != Token.TokenType.RParen)
                    throw new Exception("Expected ')' after function arguments");
                pos++; // consume ')'

                return new ASTNode
                {
                    Type = ASTNode.NodeType.Function,
                    Value = tok.Value,
                    Arguments = args
                };
            }

            // Parenthesized expression
            if (tok.Type == Token.TokenType.LParen)
            {
                pos++;
                var inner = ParseExpression(tokens, ref pos);
                if (pos >= tokens.Count || tokens[pos].Type != Token.TokenType.RParen)
                    throw new Exception("Expected ')'");
                pos++;
                return inner;
            }

            // Number literal
            if (tok.Type == Token.TokenType.Number)
            {
                pos++;
                return new ASTNode { Type = ASTNode.NodeType.Number, Value = tok.Value };
            }

            // Constant
            if (tok.Type == Token.TokenType.Constant)
            {
                pos++;
                return new ASTNode { Type = ASTNode.NodeType.Constant, Value = tok.Value };
            }

            // Calculus glyph (treat as function for now)
            if (tok.Type == Token.TokenType.Glyph || tok.Type == Token.TokenType.Identifier)
            {
                pos++;
                return new ASTNode { Type = ASTNode.NodeType.Function, Value = tok.Value };
            }

            throw new Exception($"Unexpected token: {tok.Value} ({tok.Type})");
        }

        private int GetPrecedence(Token token)
        {
            if (token.Type != Token.TokenType.Operator) return -1;
            return token.Value switch
            {
                "+" or "-" => 1,
                "*" or "/" or "%" => 2,
                "^" => 3,
                _ => -1
            };
        }

        // ---- AST Compilation to JS expression ----

        private string CompileNode(ASTNode node)
        {
            switch (node.Type)
            {
                case ASTNode.NodeType.Number:
                    return node.Value;

                case ASTNode.NodeType.Constant:
                    return _constants.TryGetValue(node.Value, out var val)
                        ? (double.IsInfinity(val) ? "Infinity" : val.ToString("R"))
                        : node.Value;

                case ASTNode.NodeType.UnaryOp:
                    return $"(-{CompileNode(node.Right)})";

                case ASTNode.NodeType.BinaryOp:
                    var left = CompileNode(node.Left);
                    var right = CompileNode(node.Right);
                    var op = _arithmeticGlyphs.ContainsKey(node.Value[0])
                        ? node.Value
                        : node.Value;
                    return $"({left} {op} {right})";

                case ASTNode.NodeType.Function:
                    if (node.Arguments != null && node.Arguments.Count > 0)
                    {
                        var args = string.Join(", ", node.Arguments.Select(CompileNode));
                        return $"Math.{node.Value}({args})";
                    }
                    return $"Math.{node.Value}()";

                default:
                    return "0";
            }
        }

        // ---- C# AST Evaluation ----

        private double EvaluateAST(ASTNode node)
        {
            switch (node.Type)
            {
                case ASTNode.NodeType.Number:
                    return double.Parse(node.Value);

                case ASTNode.NodeType.Constant:
                    return _constants.GetValueOrDefault(node.Value, 0);

                case ASTNode.NodeType.UnaryOp:
                    return -EvaluateAST(node.Right);

                case ASTNode.NodeType.BinaryOp:
                    var l = EvaluateAST(node.Left);
                    var r = EvaluateAST(node.Right);
                    return node.Value switch
                    {
                        "+" => l + r,
                        "-" => l - r,
                        "*" => l * r,
                        "/" => r != 0 ? l / r : throw new DivideByZeroException(),
                        "^" => Math.Pow(l, r),
                        "%" => l % r,
                        _ => throw new Exception($"Unknown operator: {node.Value}")
                    };

                case ASTNode.NodeType.Function:
                    var args = node.Arguments?.Select(EvaluateAST).ToArray() ?? Array.Empty<double>();
                    var fn = node.Value.ToLowerInvariant();

                    // Calculus glyphs
                    if (fn == "∫") return args.Length >= 3 ? SimpsonIntegrate(args[0], args[1], args.Length > 2 ? (int)args[2] : 100, x => x) : 0;
                    if (fn == "∂" && args.Length >= 2) return CentralDifference(args[0], args[1], x => x);
                    if (fn == "∑" && args.Length >= 3) { double sum = 0; for (double k = args[1]; k <= args[2]; k++) sum += args[0]; return sum; }
                    if (fn == "∏" && args.Length >= 3) { double prod = 1; for (double k = args[1]; k <= args[2]; k++) prod *= args[0]; return prod; }
                    if (fn == "lim") return args.Length >= 2 ? args[0] : 0;

                    // Math functions
                    return fn switch
                    {
                        "sin" => Math.Sin(args[0]),
                        "cos" => Math.Cos(args[0]),
                        "tan" => Math.Tan(args[0]),
                        "asin" => Math.Asin(args[0]),
                        "acos" => Math.Acos(args[0]),
                        "atan" => Math.Atan(args[0]),
                        "sinh" => Math.Sinh(args[0]),
                        "cosh" => Math.Cosh(args[0]),
                        "tanh" => Math.Tanh(args[0]),
                        "exp" => Math.Exp(args[0]),
                        "log" or "log10" => Math.Log10(args[0]),
                        "ln" or "log" => Math.Log(args[0]),
                        "log2" => Math.Log2(args[0]),
                        "sqrt" => Math.Sqrt(args[0]),
                        "cbrt" => Math.Cbrt(args[0]),
                        "abs" => Math.Abs(args[0]),
                        "floor" => Math.Floor(args[0]),
                        "ceil" => Math.Ceiling(args[0]),
                        "round" => Math.Round(args[0]),
                        "trunc" => Math.Truncate(args[0]),
                        "sign" => Math.Sign(args[0]),
                        "max" => args.Length >= 2 ? Math.Max(args[0], args[1]) : args[0],
                        "min" => args.Length >= 2 ? Math.Min(args[0], args[1]) : args[0],
                        _ => throw new Exception($"Unknown function: {fn}")
                    };

                default:
                    return 0;
            }
        }

        // ---- Calculus helpers ----

        private static double SimpsonIntegrate(double a, double b, int n, Func<double, double> f)
        {
            if (n % 2 != 0) n++;
            double h = (b - a) / n;
            double sum = f(a) + f(b);
            for (int i = 1; i < n; i += 2) sum += 4 * f(a + i * h);
            for (int i = 2; i < n - 1; i += 2) sum += 2 * f(a + i * h);
            return sum * h / 3;
        }

        private static double CentralDifference(double x, double h, Func<double, double> f)
        {
            return (f(x + h) - f(x - h)) / (2 * h);
        }


        // ---- K'UHUL semantic control algebra ----

        public class SemanticOperand
        {
            public string Name { get; set; }
            public string Value { get; set; }
            public bool Resolved { get; set; } = true;
        }

        public class SemanticExpression
        {
            public string Operator { get; set; }
            public string Domain { get; set; }
            public List<SemanticOperand> Operands { get; set; } = new();
            public string Fold { get; set; } = "Yax";
            public bool RequiresModel => Operands.Any(x => !x.Resolved);
            public double Confidence { get; set; }
        }

        public SemanticExpression CanonicalizeCommand(string text)
        {
            var raw = (text ?? "").Trim();
            var lower = raw.ToLowerInvariant();

            var aliases = new (string Phrase, string Domain)[]
            {
                ("search the news for ", "news"),
                ("search news for ", "news"),
                ("latest news on ", "news"),
                ("search the web for ", "web"),
                ("search web for ", "web"),
                ("web search ", "web"),
                ("find online ", "web"),
                ("look up ", "web"),
                ("search for ", "web")
            };

            foreach (var alias in aliases.OrderByDescending(x => x.Phrase.Length))
            {
                var p = lower.IndexOf(alias.Phrase, StringComparison.Ordinal);
                if (p < 0) continue;

                var query = raw.Substring(p + alias.Phrase.Length).Trim();
                return new SemanticExpression
                {
                    Operator = "search",
                    Domain = alias.Domain,
                    Fold = "Yax",
                    Confidence = string.IsNullOrWhiteSpace(query) ? 0.5 : 1.0,
                    Operands = new List<SemanticOperand>
                    {
                        new SemanticOperand
                        {
                            Name = "query",
                            Value = query,
                            Resolved = !string.IsNullOrWhiteSpace(query)
                        }
                    }
                };
            }

            return new SemanticExpression
            {
                Operator = "unresolved",
                Domain = "semantic",
                Fold = "Yax",
                Confidence = 0.0,
                Operands = new List<SemanticOperand>
                {
                    new SemanticOperand { Name = "text", Value = raw, Resolved = false }
                }
            };
        }

        public SemanticExpression ResolveOperand(
            SemanticExpression expression, string name, string value)
        {
            if (expression == null) throw new ArgumentNullException(nameof(expression));

            var operand = expression.Operands
                .FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (operand == null)
            {
                operand = new SemanticOperand { Name = name };
                expression.Operands.Add(operand);
            }

            operand.Value = value ?? "";
            operand.Resolved = !string.IsNullOrWhiteSpace(operand.Value);

            if (!expression.RequiresModel)
                expression.Confidence = Math.Max(expression.Confidence, 1.0);

            return expression;
        }

        // ---- Policy check ----

        public static bool IsAllowedOperator(char c) => _arithmeticGlyphs.ContainsKey(c);
        public static bool IsAllowedFunction(string name) => _mathFunctions.Contains(name.ToLowerInvariant());
        public static bool IsKnownConstant(string name) => _constants.ContainsKey(name);
    }
}
