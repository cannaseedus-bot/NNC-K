using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// XJSON Parser — converts XJSON surface syntax into a canonical AST.
    /// Implements the lowering pipeline from asx-xjson-language.manifest.json:
    /// normalize → strip_comments → lex_lines → build_indent_tree → lower_to_ast → assign_paths → canonicalize_hash
    /// </summary>
    public class XJSONParser
    {
        /// <summary>Result of parsing and lowering</summary>
        public class ParseResult
        {
            public bool Success { get; set; }
            public List<ParseError> Errors { get; set; } = new();
            public ASTNode Root { get; set; }
            public string ASTHash { get; set; }
            public string CanonicalJSON { get; set; }
        }

        public class ParseError
        {
            public string Code { get; set; }
            public string Message { get; set; }
            public int? Line { get; set; }
        }

        /// <summary>Canonical AST node — matches xjson://schema/ast-node/v1</summary>
        public class ASTNode
        {
            public string Type { get; set; }      // document, exec, param, label, object, array, literal, expr, ref, comment
            public string Value { get; set; }
            public List<ASTNode> Children { get; set; } = new();
            public Dictionary<string, string> Attrs { get; set; } = new();
            public string Path { get; set; }       // canonical path e.g. "/0/1"
            public int? Line { get; set; }
        }

        private class LexLine
        {
            public int LineNumber;
            public int Indent;
            public string Content;
            public string Original;
        }

        // ---- Lowering pipeline ----

        /// <summary>Parse XJSON surface syntax into canonical AST</summary>
        public ParseResult Parse(string source)
        {
            var result = new ParseResult();

            try
            {
                if (source == null)
                {
                    result.Errors.Add(new ParseError
                    {
                        Code = "E_PARSE_NULL",
                        Message = "XJSON source cannot be null"
                    });
                    return result;
                }

                // 1. Normalize
                var normalized = Normalize(source);
                // 2. Strip full-line comments
                var stripped = StripComments(normalized);
                // 3. Lex lines
                var lines = LexLines(stripped, result);
                if (result.Errors.Any(e => e.Code == "E_PARSE_VERB")) return result;
                // 4. Build indent tree
                var indentTree = BuildIndentTree(lines, result);
                if (result.Errors.Any()) return result;
                // 5. Lower to AST
                var ast = LowerToAST(indentTree);
                // 6. Assign paths
                AssignPaths(ast, "");
                // 7. Canonicalize once: this representation is the verifier input.
                result.CanonicalJSON = CanonicalJSON(ast);
                result.ASTHash = HashCanonicalJSON(result.CanonicalJSON);

                result.Success = true;
                result.Root = ast;
            }
            catch (Exception ex)
            {
                result.Errors.Add(new ParseError
                {
                    Code = "E_PARSE_INTERNAL",
                    Message = ex.Message
                });
            }

            return result;
        }

        /// <summary>Serialize AST to canonical JSON</summary>
        public string ASTToCanonicalJSON(ASTNode node)
        {
            return CanonicalJSON(node);
        }

        /// <summary>Compute the canonical hash of an AST</summary>
        public string ComputeHash(ASTNode node)
        {
            return CanonicalizeHash(node);
        }

        // ---- Pipeline stages ----

        private string Normalize(string source)
        {
            // Replace \r\n with \n, trim trailing whitespace per line
            var lines = source.Replace("\r\n", "\n").Split('\n');
            return string.Join("\n", lines.Select(l => l.TrimEnd()));
        }

        private string StripComments(string source)
        {
            var lines = source.Split('\n');
            var result = new List<string>();

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("#"))
                    continue;

                bool inSingle = false;
                bool inDouble = false;
                bool escaped = false;
                int exprDepth = 0;
                int cut = -1;

                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];

                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\' && (inSingle || inDouble))
                    {
                        escaped = true;
                        continue;
                    }

                    if (!inDouble && c == '\'') { inSingle = !inSingle; continue; }
                    if (!inSingle && c == '"') { inDouble = !inDouble; continue; }

                    if (inSingle || inDouble) continue;

                    if (i + 1 < line.Length && line[i] == '{' && line[i + 1] == '{')
                    {
                        exprDepth++;
                        i++;
                        continue;
                    }

                    if (i + 1 < line.Length && line[i] == '}' && line[i + 1] == '}' && exprDepth > 0)
                    {
                        exprDepth--;
                        i++;
                        continue;
                    }

                    if (exprDepth == 0 &&
                        i + 1 < line.Length &&
                        line[i] == '/' &&
                        line[i + 1] == '/')
                    {
                        cut = i;
                        break;
                    }
                }

                result.Add(cut >= 0 ? line.Substring(0, cut).TrimEnd() : line);
            }

            return string.Join("\n", result);
        }

        private List<LexLine> LexLines(string source, ParseResult result)
        {
            var lines = source.Split('\n');
            var lexed = new List<LexLine>();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var indent = line.TakeWhile(char.IsWhiteSpace).Count();
                var content = line.Trim();

                // Tab check
                if (line.TakeWhile(char.IsWhiteSpace).Any(c => c == '\t'))
                {
                    result.Errors.Add(new ParseError
                    {
                        Code = "E_PARSE_VERB",
                        Message = "Tabs are forbidden for indentation",
                        Line = i + 1
                    });
                    return lexed;
                }

                lexed.Add(new LexLine
                {
                    LineNumber = i + 1,
                    Indent = indent,
                    Content = content,
                    Original = line
                });
            }

            return lexed;
        }

        private List<LexLine> BuildIndentTree(List<LexLine> lines, ParseResult result)
        {
            // Validate indentation geometry: two-space lanes, no skipped parent level.
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Indent % 2 != 0)
                {
                    result.Errors.Add(new ParseError
                    {
                        Code = "E_PARSE_INDENT",
                        Message = $"Indentation must be multiples of 2 spaces, found {lines[i].Indent}",
                        Line = lines[i].LineNumber
                    });
                    return lines;
                }

                if (i > 0 && lines[i].Indent > lines[i - 1].Indent + 2)
                {
                    result.Errors.Add(new ParseError
                    {
                        Code = "E_PARSE_INDENT_JUMP",
                        Message = $"Indentation jumps from {lines[i - 1].Indent} to {lines[i].Indent}; a parent level is missing",
                        Line = lines[i].LineNumber
                    });
                    return lines;
                }
            }

            return lines;
        }

        private ASTNode LowerToAST(List<LexLine> lines)
        {
            var root = new ASTNode { Type = "document", Children = new List<ASTNode>() };
            var stack = new Stack<(ASTNode Node, int Indent)>();
            stack.Push((root, -2));

            foreach (var line in lines)
            {
                var node = ParseLine(line);

                // Pop stack until we find the right parent
                while (stack.Peek().Indent >= line.Indent)
                    stack.Pop();

                stack.Peek().Node.Children.Add(node);
                stack.Push((node, line.Indent));
            }

            return root;
        }

        private ASTNode ParseLine(LexLine line)
        {
            var content = line.Content;

            // Exec line: @verb
            var verbMatch = Regex.Match(content, @"^(@[a-zA-Z][a-zA-Z0-9._]*)");
            if (verbMatch.Success)
            {
                var node = new ASTNode
                {
                    Type = "exec",
                    Value = verbMatch.Value,
                    Line = line.LineNumber
                };

                // Parse rest of line for inline params (simple form)
                var rest = content.Substring(verbMatch.Length).Trim();
                if (!string.IsNullOrEmpty(rest))
                {
                    // Could be a label or inline param
                    if (rest[0] == ':')
                    {
                        // Label
                        node.Attrs["label"] = rest.Substring(1).Trim();
                    }
                }

                return node;
            }

            // Param line: key: value
            var colonMatch = Regex.Match(content, @"^([a-zA-Z_][a-zA-Z0-9_]*)\s*:\s*(.*)");
            if (colonMatch.Success)
            {
                return new ASTNode
                {
                    Type = "param",
                    Value = colonMatch.Groups[1].Value,
                    Attrs = new Dictionary<string, string> { { "value", colonMatch.Groups[2].Value } },
                    Line = line.LineNumber
                };
            }

            // Expression block: {{ ... }}
            var exprMatch = Regex.Match(content, @"^\{\{(.*)\}\}$");
            if (exprMatch.Success)
            {
                return new ASTNode
                {
                    Type = "expr",
                    Value = exprMatch.Groups[1].Value.Trim(),
                    Line = line.LineNumber
                };
            }

            // Label: then, else, do, case, default, on_error, on_complete
            var labelMatch = Regex.Match(content, @"^(then|else|do|case|default|on_error|on_complete):?$");
            if (labelMatch.Success)
            {
                return new ASTNode
                {
                    Type = "label",
                    Value = labelMatch.Groups[1].Value,
                    Line = line.LineNumber
                };
            }

            // Object literal: { ... }
            if (content.StartsWith("{") && content.EndsWith("}"))
            {
                return new ASTNode
                {
                    Type = "object",
                    Value = content,
                    Line = line.LineNumber
                };
            }

            // Array literal: [ ... ]
            if (content.StartsWith("[") && content.EndsWith("]"))
            {
                return new ASTNode
                {
                    Type = "array",
                    Value = content,
                    Line = line.LineNumber
                };
            }

            // Literal fallback
            return new ASTNode
            {
                Type = "literal",
                Value = content,
                Line = line.LineNumber
            };
        }

        private void AssignPaths(ASTNode node, string prefix)
        {
            node.Path = prefix;
            for (int i = 0; i < node.Children.Count; i++)
                AssignPaths(node.Children[i], prefix + "/" + i);
        }

        private string CanonicalJSON(ASTNode node)
        {
            var sb = new StringBuilder();
            SerializeNode(node, sb);
            return sb.ToString();
        }

        private void SerializeNode(ASTNode node, StringBuilder sb)
        {
            sb.Append("{\"t\":\"");
            sb.Append(node.Type);
            sb.Append('"');

            if (node.Value != null)
            {
                sb.Append(",\"v\":");
                sb.Append(JsonEscape(node.Value));
            }

            if (node.Children.Count > 0)
            {
                sb.Append(",\"c\":[");
                for (int i = 0; i < node.Children.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    SerializeNode(node.Children[i], sb);
                }
                sb.Append(']');
            }

            if (node.Attrs.Count > 0)
            {
                sb.Append(",\"a\":{");
                bool first = true;
                foreach (var kv in node.Attrs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append('"');
                    sb.Append(kv.Key);
                    sb.Append("\":");
                    sb.Append(JsonEscape(kv.Value));
                }
                sb.Append('}');
            }

            if (node.Path != null)
            {
                sb.Append(",\"p\":\"");
                sb.Append(node.Path);
                sb.Append('"');
            }

            sb.Append('}');
        }

        private string CanonicalizeHash(ASTNode node) =>
            HashCanonicalJSON(CanonicalJSON(node));

        private string HashCanonicalJSON(string json)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(json ?? ""));
            return "sha256:" + BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        private string JsonEscape(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }
}
