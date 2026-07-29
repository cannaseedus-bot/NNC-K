#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NeuralGrammar.Core.Validation
{
    /// <summary>
    /// Lightweight validator for the canonical <c>node-contribution-v1.json</c> schema.
    /// Interprets a practical subset of JSON Schema draft-07 (type, required, enum,
    /// minimum/maximum, minItems/maxItems, items, properties) so that micronaut
    /// contributions can be admitted or rejected without adding external dependencies.
    /// </summary>
    public sealed class NodeContributionValidator
    {
        private readonly JsonElement _schema;

        public NodeContributionValidator(string schemaPath)
        {
            if (!File.Exists(schemaPath))
                throw new FileNotFoundException("Schema not found", schemaPath);

            var json = File.ReadAllText(schemaPath);
            _schema = JsonDocument.Parse(json).RootElement;
        }

        public NodeContributionValidator(JsonElement schema)
        {
            _schema = schema;
        }

        /// <summary>Validate a JSON string against the schema.</summary>
        public ValidationReport ValidateJson(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return Validate(doc.RootElement);
        }

        /// <summary>Validate a <see cref="NodeContribution"/> by serializing it.</summary>
        public ValidationReport Validate(NodeContribution contribution)
        {
            var json = JsonSerializer.Serialize(contribution, JsonOptions.Pretty);
            return ValidateJson(json);
        }

        /// <summary>Validate a <see cref="JsonElement"/> against the loaded schema.</summary>
        public ValidationReport Validate(JsonElement instance)
        {
            var errors = new List<string>();
            ValidateNode(instance, _schema, "$", errors);
            return new ValidationReport { IsValid = errors.Count == 0, Errors = errors };
        }

        private void ValidateNode(JsonElement instance, JsonElement schema, string path, List<string> errors)
        {
            if (schema.ValueKind == JsonValueKind.False)
            {
                errors.Add($"{path}: schema forbids this value");
                return;
            }
            if (schema.ValueKind != JsonValueKind.Object)
                return;

            // type
            if (schema.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
            {
                var expected = typeProp.GetString();
                if (!InstanceMatchesType(instance, expected))
                {
                    errors.Add($"{path}: expected type '{expected}' but got {InstanceTypeName(instance)}");
                    return;
                }
            }

            // enum
            if (schema.TryGetProperty("enum", out var enumProp) && enumProp.ValueKind == JsonValueKind.Array)
            {
                var matched = false;
                foreach (var e in enumProp.EnumerateArray())
                {
                    if (JsonEquals(instance, e)) { matched = true; break; }
                }
                if (!matched)
                    errors.Add($"{path}: value not in enum [{string.Join(", ", enumProp.EnumerateArray().Select(JsonToString))}]");
            }

            // numeric constraints
            if (instance.ValueKind == JsonValueKind.Number)
            {
                var d = instance.GetDouble();
                if (schema.TryGetProperty("minimum", out var minProp) && minProp.TryGetDouble(out var min) && d < min)
                    errors.Add($"{path}: value {d} is below minimum {min}");
                if (schema.TryGetProperty("maximum", out var maxProp) && maxProp.TryGetDouble(out var max) && d > max)
                    errors.Add($"{path}: value {d} is above maximum {max}");
            }

            // string length
            if (instance.ValueKind == JsonValueKind.String)
            {
                var s = instance.GetString() ?? "";
                if (schema.TryGetProperty("minLength", out var minLenProp) && minLenProp.TryGetInt32(out var minLen) && s.Length < minLen)
                    errors.Add($"{path}: string length {s.Length} is below minLength {minLen}");
                if (schema.TryGetProperty("maxLength", out var maxLenProp) && maxLenProp.TryGetInt32(out var maxLen) && s.Length > maxLen)
                    errors.Add($"{path}: string length {s.Length} is above maxLength {maxLen}");
            }

            // array constraints
            if (instance.ValueKind == JsonValueKind.Array)
            {
                var arr = instance.EnumerateArray().ToList();
                if (schema.TryGetProperty("minItems", out var minItemsProp) && minItemsProp.TryGetInt32(out var minItems) && arr.Count < minItems)
                    errors.Add($"{path}: array has {arr.Count} items, minItems={minItems}");
                if (schema.TryGetProperty("maxItems", out var maxItemsProp) && maxItemsProp.TryGetInt32(out var maxItems) && arr.Count > maxItems)
                    errors.Add($"{path}: array has {arr.Count} items, maxItems={maxItems}");

                if (schema.TryGetProperty("items", out var itemsSchema))
                {
                    for (int i = 0; i < arr.Count; i++)
                        ValidateNode(arr[i], itemsSchema, $"{path}[{i}]", errors);
                }
            }

            // object constraints
            if (instance.ValueKind == JsonValueKind.Object)
            {
                // required
                if (schema.TryGetProperty("required", out var reqProp) && reqProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var r in reqProp.EnumerateArray())
                    {
                        var name = r.GetString();
                        if (string.IsNullOrWhiteSpace(name) || !instance.TryGetProperty(name, out _))
                            errors.Add($"{path}: required property '{name}' is missing");
                    }
                }

                // properties
                if (schema.TryGetProperty("properties", out var propsSchema) && propsSchema.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in propsSchema.EnumerateObject())
                    {
                        if (instance.TryGetProperty(prop.Name, out var child))
                            ValidateNode(child, prop.Value, $"{path}.{prop.Name}", errors);
                    }
                }

                // additionalProperties - we accept any scalar/object; only forbid arrays if declared false-ish
                if (schema.TryGetProperty("additionalProperties", out var addProp) &&
                    addProp.ValueKind == JsonValueKind.False)
                {
                    foreach (var prop in instance.EnumerateObject())
                    {
                        if (!propsSchema.ValueKind.Equals(JsonValueKind.Object) ||
                            !propsSchema.EnumerateObject().Any(p => p.Name == prop.Name))
                        {
                            errors.Add($"{path}: additional property '{prop.Name}' is not allowed");
                        }
                    }
                }
            }
        }

        private static bool InstanceMatchesType(JsonElement instance, string? expected)
        {
            return expected switch
            {
                "object" => instance.ValueKind == JsonValueKind.Object,
                "array" => instance.ValueKind == JsonValueKind.Array,
                "string" => instance.ValueKind == JsonValueKind.String,
                "number" => instance.ValueKind == JsonValueKind.Number,
                "integer" => instance.ValueKind == JsonValueKind.Number && instance.TryGetInt64(out _),
                "boolean" => instance.ValueKind == JsonValueKind.True || instance.ValueKind == JsonValueKind.False,
                "null" => instance.ValueKind == JsonValueKind.Null,
                _ => true
            };
        }

        private static string InstanceTypeName(JsonElement instance) => instance.ValueKind.ToString();

        private static bool JsonEquals(JsonElement a, JsonElement b)
        {
            if (a.ValueKind != b.ValueKind) return false;
            return (a.ValueKind, b.ValueKind) switch
            {
                (JsonValueKind.String, JsonValueKind.String) => a.GetString() == b.GetString(),
                (JsonValueKind.Number, JsonValueKind.Number) => a.GetRawText() == b.GetRawText(),
                (JsonValueKind.True, JsonValueKind.True) => true,
                (JsonValueKind.False, JsonValueKind.False) => true,
                (JsonValueKind.Null, JsonValueKind.Null) => true,
                _ => false
            };
        }

        private static string JsonToString(JsonElement e) => e.ValueKind switch
        {
            JsonValueKind.String => $"\"{e.GetString()}\"",
            JsonValueKind.Number => e.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => e.ValueKind.ToString()
        };
    }

    /// <summary>Result of a JSON schema validation pass.</summary>
    public sealed class ValidationReport
    {
        public bool IsValid { get; set; }
        public IReadOnlyList<string> Errors { get; set; } = new List<string>();
    }
}
