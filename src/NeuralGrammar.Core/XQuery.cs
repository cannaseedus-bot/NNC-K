#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// XQuery Engine — evaluates FLWOR-style queries over XML documents.
    /// Acts as a feed: accept XML source, execute query, stream results.
    /// Schema context from XSDParser is optional but enables type-aware filtering.
    /// </summary>
    public sealed class XQueryEngine
    {
        private readonly XSDParser? _schemaContext;

        public XQueryEngine(XSDParser? schemaContext = null)
        {
            _schemaContext = schemaContext;
        }

        /// <summary>Run an XQuery expression against an XML string and return results as a feed.</summary>
        public XQueryFeed Execute(string xml, string query)
        {
            if (string.IsNullOrWhiteSpace(xml))
                return XQueryFeed.CreateFailure("XML source is empty");

            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml);
            }
            catch (XmlException ex)
            {
                return XQueryFeed.CreateFailure($"XML parse error: {ex.Message}");
            }

            return Execute(doc, query);
        }

        /// <summary>Run an XQuery expression against an XDocument and return results as a feed.</summary>
        public XQueryFeed Execute(XDocument doc, string query)
        {
            if (doc == null)
                return XQueryFeed.CreateFailure("XML document is null");
            if (string.IsNullOrWhiteSpace(query))
                return XQueryFeed.CreateFailure("XQuery expression is empty");

            try
            {
                var compiled = Compile(query);
                var results = Evaluate(doc, compiled);
                return XQueryFeed.CreateSuccess(results, compiled.Expression, doc.Root?.Name?.LocalName ?? "");
            }
            catch (Exception ex)
            {
                return XQueryFeed.CreateFailure($"XQuery error: {ex.Message}");
            }
        }

        // ── Compilation ──────────────────────────────────────────────────────

        private sealed class CompiledQuery
        {
            public string Expression { get; init; } = "";
            public XPathType Type { get; init; } = XPathType.Simple;
            public string? Path { get; init; }
            public string? Condition { get; init; } // predicate after [...]
            public string? OrderBy { get; init; }
            public bool Descending { get; init; }
            public int Limit { get; init; } = 0;
            public string[]? ReturnFields { get; init; }
        }

        private enum XPathType { Simple, Filtered, FLWOR }

        private static CompiledQuery Compile(string query)
        {
            var trimmed = query.Trim();

            // FLWOR-style: for $x in /path where ... order by ... return ...
            var flworMatch = Regex.Match(trimmed,
                @"^for\s+\$\w+\s+in\s+(.+?)(?:\s+where\s+(.+?))?(?:\s+order\s+by\s+(.+?))?(?:\s+return\s+(.+))?$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (flworMatch.Success)
            {
                var path = flworMatch.Groups[1].Value.Trim();
                var condition = flworMatch.Groups[2].Success ? flworMatch.Groups[2].Value.Trim() : null;
                var orderBy = flworMatch.Groups[3].Success ? flworMatch.Groups[3].Value.Trim() : null;
                var returnClause = flworMatch.Groups[4].Success ? flworMatch.Groups[4].Value.Trim() : null;

                var descending = orderBy != null &&
                    orderBy.StartsWith("descending", StringComparison.OrdinalIgnoreCase);
                if (descending && orderBy != null)
                    orderBy = orderBy["descending".Length..].Trim();
                var ascending = orderBy != null &&
                    orderBy.StartsWith("ascending", StringComparison.OrdinalIgnoreCase);
                if (ascending && orderBy != null)
                    orderBy = orderBy["ascending".Length..].Trim();

                return new CompiledQuery
                {
                    Expression = trimmed,
                    Type = XPathType.FLWOR,
                    Path = path,
                    Condition = condition,
                    OrderBy = orderBy,
                    Descending = descending,
                    ReturnFields = ParseReturnFields(returnClause)
                };
            }

            // Simple XPath: /root/element or //element[/condition]
            var predicateMatch = Regex.Match(trimmed, @"^(.+?)\[(.+)\]$");
            if (predicateMatch.Success)
            {
                return new CompiledQuery
                {
                    Expression = trimmed,
                    Type = XPathType.Filtered,
                    Path = predicateMatch.Groups[1].Value.Trim(),
                    Condition = predicateMatch.Groups[2].Value.Trim()
                };
            }

            // Simple XPath
            return new CompiledQuery
            {
                Expression = trimmed,
                Type = XPathType.Simple,
                Path = trimmed
            };
        }

        private static string[]? ParseReturnFields(string? returnClause)
        {
            if (string.IsNullOrWhiteSpace(returnClause))
                return null;

            // $x/element1, $x/element2 or just element1, element2
            var parts = returnClause.Split(',', StringSplitOptions.RemoveEmptyEntries);
            return parts
                .Select(p => p.Trim().TrimStart('$').Trim())
                .Where(p => p.Length > 0)
                .Select(p => Regex.Replace(p, @"^\w+/", "")) // strip variable prefix
                .ToArray();
        }

        // ── Evaluation ───────────────────────────────────────────────────────

        private XQueryFeedResult Evaluate(XDocument doc, CompiledQuery q)
        {
            var elements = q.Path != null
                ? ResolvePath(doc, q.Path)
                : doc.Descendants();

            // Apply predicate filter
            if (q.Condition != null)
                elements = ApplyPredicate(elements, q.Condition);

            // Apply ordering
            if (q.OrderBy != null)
                elements = ApplyOrdering(elements, q.OrderBy, q.Descending);

            // Apply limit
            if (q.Limit > 0)
                elements = elements.Take(q.Limit);

            // Build result items
            var items = elements.Select(el => XQueryFeedItem.FromElement(el, q.ReturnFields)).ToList();

            return new XQueryFeedResult
            {
                Items = items,
                TotalCount = items.Count,
                Query = q.Expression
            };
        }

        private static IEnumerable<XElement> ResolvePath(XDocument doc, string path)
        {
            path = path.Trim().TrimStart('$').Trim();

            // Handle //descendant syntax
            if (path.StartsWith("//"))
            {
                var name = path[2..].Trim();
                return string.IsNullOrWhiteSpace(name)
                    ? doc.Descendants()
                    : doc.Descendants().Where(e => e.Name.LocalName == name || e.Name.ToString() == name);
            }

            // Handle /root/element absolute path
            if (path.StartsWith("/"))
            {
                var parts = path.Trim('/').Split('/');
                IEnumerable<XElement>? current = null;
                foreach (var part in parts)
                {
                    if (current == null)
                        current = doc.Root != null && (doc.Root.Name.LocalName == part || doc.Root.Name.ToString() == part)
                            ? new[] { doc.Root }
                            : doc.Descendants().Where(e => e.Name.LocalName == part || e.Name.ToString() == part);
                    else
                        current = current.SelectMany(e => e.Descendants())
                            .Where(e => e.Name.LocalName == part || e.Name.ToString() == part);
                }
                return current ?? Enumerable.Empty<XElement>();
            }

            // Relative element name
            return doc.Descendants().Where(e =>
                e.Name.LocalName == path || e.Name.ToString() == path);
        }

        private static IEnumerable<XElement> ApplyPredicate(
            IEnumerable<XElement> elements, string condition)
        {
            return elements.Where(el =>
            {
                condition = condition.Trim();

                // @attr='value'
                var attrMatch = Regex.Match(condition,
                    @"^@(\w+)\s*=\s*['""](.+?)['""]$");
                if (attrMatch.Success)
                {
                    var attr = el.Attribute(attrMatch.Groups[1].Value);
                    return attr != null && attr.Value == attrMatch.Groups[2].Value;
                }

                // @attr (has attribute)
                if (Regex.IsMatch(condition, @"^@(\w+)$"))
                {
                    var attrName = Regex.Match(condition, @"^@(\w+)$").Groups[1].Value;
                    return el.Attribute(attrName) != null;
                }

                // text() = 'value'
                var textMatch = Regex.Match(condition,
                    @"^text\(\)\s*=\s*['""](.+?)['""]$");
                if (textMatch.Success)
                    return el.Value == textMatch.Groups[1].Value;

                // contains(text(), 'value')
                var containsMatch = Regex.Match(condition,
                    @"^contains\(text\(\),\s*['""](.+?)['""]\)$");
                if (containsMatch.Success)
                    return el.Value.Contains(containsMatch.Groups[1].Value);

                // number comparison: @attr > 5 or price > 10
                var numCompare = Regex.Match(condition,
                    @"^(.+?)\s*(>|<|>=|<=|!=|==)\s*(.+)$");
                if (numCompare.Success)
                {
                    var left = ResolveValue(el, numCompare.Groups[1].Value.Trim());
                    var op = numCompare.Groups[2].Value;
                    var right = numCompare.Groups[3].Value.Trim();
                    return CompareValues(left, right, op);
                }

                // Simple text or element existence
                if (condition.Equals("true", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (condition.Equals("false", StringComparison.OrdinalIgnoreCase))
                    return false;

                // Child element existence
                if (el.Element(condition) != null)
                    return true;

                // Child element with matching text
                var child = el.Element(condition);
                if (child != null)
                    return true;

                return false;
            });
        }

        private static string ResolveValue(XElement el, string expr)
        {
            if (expr.StartsWith("@"))
                return el.Attribute(expr[1..])?.Value ?? "";
            if (expr == "text()" || expr == ".")
                return el.Value;
            var child = el.Element(expr);
            return child?.Value ?? expr;
        }

        private static bool CompareValues(string left, string right, string op)
        {
            // Try numeric comparison first
            if (double.TryParse(left, out var ld) && double.TryParse(right, out var rd))
            {
                return op switch
                {
                    ">" => ld > rd,
                    "<" => ld < rd,
                    ">=" => ld >= rd,
                    "<=" => ld <= rd,
                    "==" => Math.Abs(ld - rd) < 0.0001,
                    "!=" => Math.Abs(ld - rd) >= 0.0001,
                    _ => false
                };
            }

            // String comparison
            return op switch
            {
                "==" => left == right,
                "!=" => left != right,
                ">" => string.Compare(left, right, StringComparison.Ordinal) > 0,
                "<" => string.Compare(left, right, StringComparison.Ordinal) < 0,
                ">=" => string.Compare(left, right, StringComparison.Ordinal) >= 0,
                "<=" => string.Compare(left, right, StringComparison.Ordinal) <= 0,
                _ => false
            };
        }

        private static IEnumerable<XElement> ApplyOrdering(
            IEnumerable<XElement> elements, string field, bool descending)
        {
            field = field.Trim().TrimStart('$').Trim();
            field = Regex.Replace(field, @"^\w+/", ""); // strip variable prefix

            if (descending)
                return elements.OrderByDescending(el => el.Element(field)?.Value ?? el.Value);
            else
                return elements.OrderBy(el => el.Element(field)?.Value ?? el.Value);
        }
    }

    // ── Feed Types ──────────────────────────────────────────────────────────

    /// <summary>Result of an XQuery evaluation — the "feed" of matching items.</summary>
    public sealed class XQueryFeed
    {
        public bool Success { get; init; }
        public string? Error { get; init; }
        public XQueryFeedResult? Result { get; init; }
        public string SourceName { get; init; } = "";
        public DateTime Timestamp { get; init; } = DateTime.UtcNow;

        public static XQueryFeed CreateSuccess(XQueryFeedResult result, string query, string sourceName) => new()
        {
            Success = true,
            Result = result,
            SourceName = sourceName
        };

        public static XQueryFeed CreateFailure(string error) => new()
        {
            Success = false,
            Error = error
        };
    }

    public sealed class XQueryFeedResult
    {
        public List<XQueryFeedItem> Items { get; init; } = new();
        public int TotalCount { get; init; }
        public string Query { get; init; } = "";
    }

    public sealed class XQueryFeedItem
    {
        public string ElementName { get; init; } = "";
        public Dictionary<string, string> Attributes { get; init; } = new();
        public string? Value { get; init; }
        public Dictionary<string, string> Fields { get; init; } = new(); // projected return fields
        public string Xml { get; init; } = "";

        public static XQueryFeedItem FromElement(XElement el, string[]? returnFields = null)
        {
            var attrs = new Dictionary<string, string>();
            foreach (var attr in el.Attributes())
                attrs[attr.Name.LocalName] = attr.Value;

            var fields = new Dictionary<string, string>();
            if (returnFields != null)
            {
                foreach (var field in returnFields)
                {
                    var child = el.Element(field);
                    if (child != null)
                        fields[field] = child.Value;
                }
            }

            return new XQueryFeedItem
            {
                ElementName = el.Name.LocalName,
                Attributes = attrs,
                Value = el.Value,
                Fields = fields,
                Xml = el.ToString(SaveOptions.DisableFormatting)
            };
        }
    }
}
