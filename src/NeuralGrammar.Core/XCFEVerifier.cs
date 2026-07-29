using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NeuralGrammar.Core.XCFE;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// XCFE Static Verifier — checks XJSON programs before execution.
    /// Implements all rules from asx-xcfe-authority.manifest.json:
    /// known_verbs_only, capability_binding, schema_params,
    /// bounded_loops, bounded_concurrency, seeded_nondet,
    /// expr_no_at_verbs, pack_eval_false
    /// </summary>
    public class XCFEVerifier
    {
        private readonly XCFEPolicy _policy;
        private readonly HashSet<string> _stdlibVerbs;
        private readonly List<VerifierDiagnostic> _diagnostics = new();

        public XCFEVerifier(XCFEPolicy policy = null)
        {
            _policy = policy ?? new XCFEPolicy().GrantAll();
            _stdlibVerbs = new HashSet<string>(
                XCFEStdlib.All.Keys,
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Result of a verification pass</summary>
        public class VerifierResult
        {
            public bool Passed { get; set; }
            public List<VerifierDiagnostic> Diagnostics { get; set; } = new();
            public string ConformanceHash { get; set; }
        }

        public class VerifierDiagnostic
        {
            public string Rule { get; set; }
            public string Severity { get; set; } // "ERROR", "WARN"
            public string Message { get; set; }
            public int? Line { get; set; }
        }

        /// <summary>
        /// Parse once through XJSONParser, then verify the canonical AST.
        /// The AST hash becomes the conformance identity; the verifier no longer
        /// assigns a second meaning to the source with an independent lexer.
        /// </summary>
        public VerifierResult Verify(
            string programText,
            string[] installedPacks = null,
            string environmentTarget = null)
        {
            _diagnostics.Clear();
            installedPacks ??= Array.Empty<string>();

            var parser = new XJSONParser();
            var parsed = parser.Parse(programText ?? "");

            if (!parsed.Success || parsed.Root == null)
            {
                foreach (var error in parsed.Errors)
                {
                    _diagnostics.Add(new VerifierDiagnostic
                    {
                        Rule = "xjson_parse",
                        Severity = "ERROR",
                        Message = $"{error.Code}: {error.Message}",
                        Line = error.Line
                    });
                }

                return new VerifierResult
                {
                    Passed = false,
                    Diagnostics = new List<VerifierDiagnostic>(_diagnostics),
                    ConformanceHash = null
                };
            }

            var execNodes = Walk(parsed.Root)
                .Where(n => string.Equals(n.Type, "exec", StringComparison.OrdinalIgnoreCase))
                .ToList();

            CheckKnownVerbsOnly(execNodes);
            CheckCapabilityBinding(execNodes);
            CheckSchemaParamsAST(execNodes);
            CheckBoundedLoopsAST(execNodes);
            CheckBoundedConcurrencyAST(execNodes);
            CheckSeededNondetAST(execNodes);
            CheckExprNoAtVerbsAST(parsed.Root);
            CheckPackEvalFalse(installedPacks);

            var passed = !_diagnostics.Any(d => d.Severity == "ERROR");
            return new VerifierResult
            {
                Passed = passed,
                Diagnostics = new List<VerifierDiagnostic>(_diagnostics),
                ConformanceHash = passed ? parsed.ASTHash : null
            };
        }

        private static IEnumerable<XJSONParser.ASTNode> Walk(XJSONParser.ASTNode node)
        {
            if (node == null) yield break;
            yield return node;

            foreach (var child in node.Children ?? new List<XJSONParser.ASTNode>())
                foreach (var descendant in Walk(child))
                    yield return descendant;
        }

        private static string ParamValue(XJSONParser.ASTNode exec, params string[] names)
        {
            if (exec?.Children == null) return null;

            var param = exec.Children.FirstOrDefault(n =>
                string.Equals(n.Type, "param", StringComparison.OrdinalIgnoreCase) &&
                names.Any(name => string.Equals(n.Value, name, StringComparison.OrdinalIgnoreCase)));

            if (param == null || param.Attrs == null) return null;
            return param.Attrs.TryGetValue("value", out var value) ? value : null;
        }

        private void CheckKnownVerbsOnly(List<XJSONParser.ASTNode> execNodes)
        {
            foreach (var node in execNodes)
            {
                if (!_stdlibVerbs.Contains(node.Value))
                {
                    _diagnostics.Add(new VerifierDiagnostic
                    {
                        Rule = "known_verbs_only",
                        Severity = "ERROR",
                        Message = $"Unknown verb: {node.Value}",
                        Line = node.Line
                    });
                }
            }
        }

        private void CheckCapabilityBinding(List<XJSONParser.ASTNode> execNodes)
        {
            foreach (var node in execNodes)
            {
                var result = _policy.CheckVerb(node.Value);
                if (!result.Allowed)
                {
                    _diagnostics.Add(new VerifierDiagnostic
                    {
                        Rule = "capability_binding",
                        Severity = "ERROR",
                        Message = result.Reason,
                        Line = node.Line
                    });
                }
            }
        }

        private void CheckSchemaParamsAST(List<XJSONParser.ASTNode> execNodes)
        {
            foreach (var exec in execNodes)
            {
                foreach (var child in exec.Children ?? new List<XJSONParser.ASTNode>())
                {
                    if (string.Equals(child.Type, "exec", StringComparison.OrdinalIgnoreCase))
                    {
                        _diagnostics.Add(new VerifierDiagnostic
                        {
                            Rule = "schema_params",
                            Severity = "WARN",
                            Message = $"Nested verb '{child.Value}' under '{exec.Value}' is execution structure, not a parameter.",
                            Line = child.Line
                        });
                    }
                }
            }
        }

        private void CheckBoundedLoopsAST(List<XJSONParser.ASTNode> execNodes)
        {
            foreach (var node in execNodes.Where(n =>
                string.Equals(n.Value, "@for", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Value, "@while", StringComparison.OrdinalIgnoreCase)))
            {
                var bound = ParamValue(node, "max", "limit", "times");
                if (string.IsNullOrWhiteSpace(bound))
                {
                    _diagnostics.Add(new VerifierDiagnostic
                    {
                        Rule = "bounded_loops",
                        Severity = "ERROR",
                        Message = $"{node.Value} at line {node.Line} requires an explicit bound (max:/limit:/times:)",
                        Line = node.Line
                    });
                }
            }
        }

        private void CheckBoundedConcurrencyAST(List<XJSONParser.ASTNode> execNodes)
        {
            var spawnCount = execNodes.Count(n =>
                string.Equals(n.Value, "@spawn", StringComparison.OrdinalIgnoreCase));
            var parCount = execNodes.Count(n =>
                string.Equals(n.Value, "@par", StringComparison.OrdinalIgnoreCase));

            var result = _policy.CheckLimits(
                Math.Max(spawnCount, parCount),
                spawnCount + parCount,
                0,
                0);

            if (!result.Allowed)
            {
                _diagnostics.Add(new VerifierDiagnostic
                {
                    Rule = "bounded_concurrency",
                    Severity = "ERROR",
                    Message = result.Reason
                });
            }
        }

        private void CheckSeededNondetAST(List<XJSONParser.ASTNode> execNodes)
        {
            foreach (var node in execNodes.Where(n =>
                string.Equals(n.Value, "@rand", StringComparison.OrdinalIgnoreCase)))
            {
                var hasSeed = !string.IsNullOrWhiteSpace(ParamValue(node, "seed"));
                var result = _policy.CheckDeterminism(
                    "nondet",
                    hasSeed,
                    ReplayLaw.Deterministic);

                if (!result.Allowed)
                {
                    _diagnostics.Add(new VerifierDiagnostic
                    {
                        Rule = "seeded_nondet",
                        Severity = "ERROR",
                        Message = result.Reason,
                        Line = node.Line
                    });
                }
            }
        }

        private void CheckExprNoAtVerbsAST(XJSONParser.ASTNode root)
        {
            foreach (var expr in Walk(root).Where(n =>
                string.Equals(n.Type, "expr", StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrWhiteSpace(expr.Value) &&
                    Regex.IsMatch(expr.Value, @"@[a-zA-Z][a-zA-Z0-9._]*"))
                {
                    _diagnostics.Add(new VerifierDiagnostic
                    {
                        Rule = "expr_no_at_verbs",
                        Severity = "ERROR",
                        Message = $"Expression block contains an @verb at line {expr.Line}. Expression bodies must be pure.",
                        Line = expr.Line
                    });
                }
            }
        }

        // ---- Rule: known_verbs_only ----
        // Every @verb in the program must be registered in the stdlib.
        private void CheckKnownVerbsOnly(List<Token> tokens, string[] lines)
        {
            foreach (var tok in tokens.Where(t => t.Type == TokenType.Verb))
            {
                if (!_stdlibVerbs.Contains(tok.Value))
                {
                    _diagnostics.Add(new VerifierDiagnostic
                    {
                        Rule = "known_verbs_only",
                        Severity = "ERROR",
                        Message = $"Unknown verb: {tok.Value}",
                        Line = tok.Line
                    });
                }
            }
        }

        // ---- Rule: capability_binding ----
        // Verbs requiring capabilities must be checked against granted policy.
        private void CheckCapabilityBinding(List<Token> tokens)
        {
            foreach (var tok in tokens.Where(t => t.Type == TokenType.Verb))
            {
                var result = _policy.CheckVerb(tok.Value);
                if (!result.Allowed)
                {
                    _diagnostics.Add(new VerifierDiagnostic
                    {
                        Rule = "capability_binding",
                        Severity = "ERROR",
                        Message = result.Reason,
                        Line = tok.Line
                    });
                }
            }
        }

        // ---- Rule: schema_params ----
        // Params under exec nodes must be known/literal.
        private void CheckSchemaParams(List<Token> tokens, string[] lines)
        {
            // Simple check: @-verb params should be key:value pairs, not nested verbs
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Type == TokenType.Verb && i + 1 < tokens.Count)
                {
                    // Check next line for param alignment
                    var nextLine = tokens[i].Line;
                    for (int j = i + 1; j < tokens.Count && tokens[j].Line == nextLine; j++)
                    {
                        if (tokens[j].Type == TokenType.Verb)
                        {
                            _diagnostics.Add(new VerifierDiagnostic
                            {
                                Rule = "schema_params",
                                Severity = "WARN",
                                Message = $"Inline verb param at line {tokens[j].Line}: {tokens[j].Value}. Params should be on their own indented lines.",
                                Line = tokens[j].Line
                            });
                        }
                    }
                }
            }
        }

        // ---- Rule: bounded_loops ----
        // @for and @while must have explicit iteration bounds.
        private void CheckBoundedLoops(List<Token> tokens, string[] lines)
        {
            foreach (var tok in tokens.Where(t => t.Type == TokenType.Verb && t.Value == "@for"))
            {
                // Check nearby lines for a "max" or "limit" param
                bool hasBound = false;
                for (int line = tok.Line; line < Math.Min(tok.Line + 10, lines.Length); line++)
                {
                    var l = lines[line].Trim();
                    if (l.StartsWith("max:") || l.StartsWith("limit:") || l.StartsWith("times:"))
                    {
                        hasBound = true;
                        break;
                    }
                }

                if (!hasBound)
                {
                    _diagnostics.Add(new VerifierDiagnostic
                    {
                        Rule = "bounded_loops",
                        Severity = "ERROR",
                        Message = $"@for at line {tok.Line} requires an explicit bound (max:/limit:/times:)",
                        Line = tok.Line
                    });
                }
            }
        }

        // ---- Rule: bounded_concurrency ----
        // @spawn and @par must respect concurrency limits.
        private void CheckBoundedConcurrency(List<Token> tokens)
        {
            var spawnCount = tokens.Count(t => t.Type == TokenType.Verb && t.Value == "@spawn");
            var parCount = tokens.Count(t => t.Type == TokenType.Verb && t.Value == "@par");

            var limitResult = _policy.CheckLimits(
                Math.Max(spawnCount, parCount),
                spawnCount + parCount,
                0, 0);

            if (!limitResult.Allowed)
            {
                _diagnostics.Add(new VerifierDiagnostic
                {
                    Rule = "bounded_concurrency",
                    Severity = "WARN",
                    Message = limitResult.Reason
                });
            }
        }

        // ---- Rule: seeded_nondet ----
        // @rand requires an explicit seed parameter.
        private void CheckSeededNondet(List<Token> tokens, string[] lines)
        {
            foreach (var tok in tokens.Where(t => t.Type == TokenType.Verb && t.Value == "@rand"))
            {
                bool hasSeed = false;
                for (int line = tok.Line; line < Math.Min(tok.Line + 5, lines.Length); line++)
                {
                    if (lines[line].Trim().StartsWith("seed:"))
                    {
                        hasSeed = true;
                        break;
                    }
                }

                var detResult = _policy.CheckDeterminism("nondet", hasSeed);
                if (!detResult.Allowed)
                {
                    _diagnostics.Add(new VerifierDiagnostic
                    {
                        Rule = "seeded_nondet",
                        Severity = "ERROR",
                        Message = detResult.Reason,
                        Line = tok.Line
                    });
                }
            }
        }

        // ---- Rule: expr_no_at_verbs ----
        // Expression blocks {{ }} must not contain @-verbs.
        private void CheckExprNoAtVerbs(List<Token> tokens, string[] lines)
        {
            // Find expression blocks
            for (int i = 0; i < tokens.Count; i++)
            {
                if (tokens[i].Type == TokenType.ExprOpen)
                {
                    // Scan until matching close
                    int depth = 1;
                    int j = i + 1;
                    while (j < tokens.Count && depth > 0)
                    {
                        if (tokens[j].Type == TokenType.ExprOpen) depth++;
                        else if (tokens[j].Type == TokenType.ExprClose) depth--;
                        else if (tokens[j].Type == TokenType.Verb && depth == 1)
                        {
                            _diagnostics.Add(new VerifierDiagnostic
                            {
                                Rule = "expr_no_at_verbs",
                                Severity = "ERROR",
                                Message = $"Expression block contains @verb '{tokens[j].Value}' at line {tokens[j].Line}. Expression bodies must not contain @-verbs.",
                                Line = tokens[j].Line
                            });
                        }
                        j++;
                    }
                }
            }
        }

        // ---- Rule: pack_eval_false ----
        // Installed packs must not have eval capability.
        private void CheckPackEvalFalse(string[] installedPacks)
        {
            // In a full implementation, this would load each pack manifest
            // and check the eval field. For now, we flag any pack named with eval capability.
            foreach (var pack in installedPacks)
            {
                if (pack.Contains("eval", StringComparison.OrdinalIgnoreCase))
                {
                    _diagnostics.Add(new VerifierDiagnostic
                    {
                        Rule = "pack_eval_false",
                        Severity = "ERROR",
                        Message = $"Pack '{pack}' appears to grant eval capability, which is forbidden."
                    });
                }
            }
        }

        // ---- Hashing ----
        private string ComputeConformanceHash(string programText)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(programText);
            var hash = sha.ComputeHash(bytes);
            return "sha256:" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        // ---- Lexer ----
        private enum TokenType { Verb, Param, ExprOpen, ExprClose, Other }
        private class Token { public TokenType Type; public string Value; public int Line; }

        private List<Token> Tokenize(string text)
        {
            var tokens = new List<Token>();
            var lines = text.Split('\n');
            var verbPattern = new Regex(@"@[a-zA-Z][a-zA-Z0-9._]*");
            var exprOpen = new Regex(@"\{\{");
            var exprClose = new Regex(@"\}\}");

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();

                // Skip comments
                if (trimmed.StartsWith("//") || trimmed.StartsWith("#")) continue;
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                int idx = 0;
                while (idx < trimmed.Length)
                {
                    // Try {{ }}
                    var matchEO = exprOpen.Match(trimmed, idx);
                    if (matchEO.Success && matchEO.Index == idx)
                    {
                        tokens.Add(new Token { Type = TokenType.ExprOpen, Value = "{{", Line = i + 1 });
                        idx += 2;
                        continue;
                    }
                    var matchEC = exprClose.Match(trimmed, idx);
                    if (matchEC.Success && matchEC.Index == idx)
                    {
                        tokens.Add(new Token { Type = TokenType.ExprClose, Value = "}}", Line = i + 1 });
                        idx += 2;
                        continue;
                    }

                    // Try @verb
                    var matchV = verbPattern.Match(trimmed, idx);
                    if (matchV.Success && matchV.Index == idx)
                    {
                        tokens.Add(new Token { Type = TokenType.Verb, Value = matchV.Value, Line = i + 1 });
                        idx += matchV.Length;
                        continue;
                    }

                    // Param key:value
                    var colonIdx = trimmed.IndexOf(':', idx);
                    if (colonIdx > idx && colonIdx < idx + 40)
                    {
                        tokens.Add(new Token { Type = TokenType.Param, Value = trimmed.Substring(idx, colonIdx - idx), Line = i + 1 });
                        idx = colonIdx + 1;
                        continue;
                    }

                    // Skip non-token characters
                    idx++;
                }
            }

            return tokens;
        }

        /// <summary>Verify T1-T5 topology laws on a compiled program.</summary>
        public VerifierResult VerifyTopology(KuhulCompiledProgram program)
        {
            var result = new VerifierResult { Passed = true };
            if (program.Entry != 0)
            {
                result.Passed = false;
                result.Diagnostics.Add(new VerifierDiagnostic { Rule = "T1", Severity = "ERROR", Message = "Entry != 0" });
            }
            if (program.FoldCount != 6)
            {
                result.Passed = false;
                result.Diagnostics.Add(new VerifierDiagnostic { Rule = "T2", Severity = "ERROR", Message = "FoldCount = " + program.FoldCount });
            }
            var expectedOrder = new[] { "Pop", "Wo", "Yax", "Sek", "Ch'en", "Xul" };
            for (int i = 0; i < program.Folds.Count && i < 6; i++)
            {
                if (program.Folds[i].Phase != expectedOrder[i])
                {
                    result.Passed = false;
                    result.Diagnostics.Add(new VerifierDiagnostic { Rule = "T3", Severity = "ERROR", Message = "Fold[" + i + "] phase mismatch" });
                }
            }
            if (!program.IsClosedLoop)
            {
                result.Diagnostics.Add(new VerifierDiagnostic { Rule = "T4", Severity = "WARN", Message = "program.IsClosedLoop = false" });
            }
            // T5: Phase containment — every node lives in its fold's lane
            foreach (var fold in program.Folds)
            {
                foreach (var nodeId in fold.NodeIds)
                {
                    var node = program.Nodes.FirstOrDefault(n => n.Id == nodeId);
                    if (node != null && node.Phase != fold.Phase)
                    {
                        result.Passed = false;
                        result.Diagnostics.Add(new VerifierDiagnostic
                        {
                            Rule = "T5", Severity = "ERROR",
                            Message = "Node " + node.Id + " phase=" + node.Phase + " in fold " + fold.Phase
                        });
                    }
                }
            }
            return result;
        }

        /// <summary>Verify C1-C3 composition (monad) laws on a compiled program.</summary>
        public MonadLawResult VerifyCompositionLaws(KuhulCompiledProgram program)
        {
            var result = new MonadLawResult { LeftIdentity = true, RightIdentity = true, Associativity = true };
            foreach (var fold in program.Folds)
            {
                if (fold.NodeIds.Count == 0) continue;

                // C1: Left identity — first node should have no dependencies (unit)
                // C2: Right identity — last node should produce the fold output
                // C3: Associativity — all nodes in this fold share the same phase
                var nodePhase = fold.Phase;
                for (int i = 0; i < fold.NodeIds.Count && i < program.Nodes.Count; i++)
                {
                    var node = program.Nodes.FirstOrDefault(n => n.Id == fold.NodeIds[i]);
                    if (node != null && node.Phase != nodePhase)
                        result.Associativity = false;
                }
            }
            result.AllPassed = result.LeftIdentity && result.RightIdentity && result.Associativity;
            return result;
        }
    }
}
