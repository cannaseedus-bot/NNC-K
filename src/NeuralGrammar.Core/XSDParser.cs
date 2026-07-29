using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Schema;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// XSD structural grammar authority for Neural Grammar / K'UHUL contracts.
    /// Loads schema structure, preserves namespaces, and validates XML without
    /// silently accepting schema-load errors.
    /// </summary>
    public class XSDParser
    {
        private readonly Dictionary<string, XSDSchema> _schemas =
            new(StringComparer.OrdinalIgnoreCase);

        public XSDSchema LoadSchema(string xsdPath)
        {
            if (string.IsNullOrWhiteSpace(xsdPath))
                throw new ArgumentException("XSD path is required", nameof(xsdPath));

            var fullPath = Path.GetFullPath(xsdPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("XSD schema not found", fullPath);

            var errors = new List<string>();
            XmlSchema xmlSchema;

            var readSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            using (var reader = XmlReader.Create(fullPath, readSettings))
            {
                xmlSchema = XmlSchema.Read(reader, (sender, args) =>
                {
                    errors.Add(args.Message);
                });
            }

            if (xmlSchema == null)
                throw new InvalidDataException($"Unable to read XSD schema '{fullPath}'");

            if (errors.Count > 0)
                throw new InvalidDataException(
                    "XSD schema errors: " + string.Join(" | ", errors));

            var schema = new XSDSchema
            {
                FilePath = fullPath,
                Name = Path.GetFileNameWithoutExtension(fullPath),
                TargetNamespace = xmlSchema.TargetNamespace
            };

            foreach (XmlQualifiedName ns in xmlSchema.Namespaces.ToArray())
                schema.Namespaces[ns.Name ?? ""] = ns.Namespace ?? "";

            foreach (XmlSchemaObject item in xmlSchema.Items)
            {
                if (item is XmlSchemaElement element)
                    schema.Elements.Add(ReadElement(element));
            }

            _schemas[schema.Name] = schema;
            return schema;
        }

        public bool Validate(string xmlPath, string schemaPath)
        {
            return ValidateDetailed(xmlPath, schemaPath).IsValid;
        }

        public XSDValidationResult ValidateDetailed(string xmlPath, string schemaPath)
        {
            var result = new XSDValidationResult();

            try
            {
                if (!File.Exists(xmlPath))
                {
                    result.Errors.Add($"XML file not found: {xmlPath}");
                    return result;
                }

                if (!File.Exists(schemaPath))
                {
                    result.Errors.Add($"XSD schema not found: {schemaPath}");
                    return result;
                }

                var schema = LoadSchema(schemaPath);

                var settings = new XmlReaderSettings
                {
                    ValidationType = ValidationType.Schema,
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };

                settings.Schemas.Add(schema.TargetNamespace, schema.FilePath);
                settings.ValidationFlags |=
                    XmlSchemaValidationFlags.ReportValidationWarnings;

                settings.ValidationEventHandler += (sender, args) =>
                {
                    var message = args.Message;
                    if (args.Exception != null && args.Exception.LineNumber > 0)
                        message = $"line {args.Exception.LineNumber}:{args.Exception.LinePosition} {message}";

                    if (args.Severity == XmlSeverityType.Warning)
                        result.Warnings.Add(message);
                    else
                        result.Errors.Add(message);
                };

                using var reader = XmlReader.Create(xmlPath, settings);
                while (reader.Read()) { }

                result.IsValid = result.Errors.Count == 0;
                result.SchemaName = schema.Name;
                result.TargetNamespace = schema.TargetNamespace;
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
                result.IsValid = false;
            }

            return result;
        }

        public XSDSchema BuildGrammar(string xsdPath)
        {
            // The schema itself is the authority. Do not fabricate a second
            // Fold/Node/Micronaut/NNC grammar beside what the XSD declares.
            return LoadSchema(xsdPath);
        }

        public XSDSchema GetLoadedSchema(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _schemas.TryGetValue(name, out var schema) ? schema : null;
        }

        private static XSDElement ReadElement(XmlSchemaElement element)
        {
            var info = new XSDElement
            {
                Name = element.Name ?? element.RefName.Name,
                Type = !element.SchemaTypeName.IsEmpty
                    ? element.SchemaTypeName.ToString()
                    : element.ElementSchemaType?.QualifiedName.ToString(),
                IsRequired = element.MinOccurs > 0,
                MinOccurs = DecimalToInt(element.MinOccurs, 0),
                MaxOccurs = element.MaxOccursString == "unbounded"
                    ? -1
                    : DecimalToInt(element.MaxOccurs, 1)
            };

            if (element.SchemaType is XmlSchemaComplexType complex)
            {
                foreach (XmlSchemaAttribute attr in complex.Attributes.OfType<XmlSchemaAttribute>())
                {
                    var attrName = attr.Name ?? attr.RefName.Name;
                    var attrType = !attr.SchemaTypeName.IsEmpty
                        ? attr.SchemaTypeName.ToString()
                        : "xs:string";

                    if (!string.IsNullOrWhiteSpace(attrName))
                        info.Attributes[attrName] = attrType;
                }

                ReadParticle(complex.Particle, info.Children);
            }

            return info;
        }

        private static void ReadParticle(
            XmlSchemaParticle particle,
            List<XSDElement> children)
        {
            if (particle == null) return;

            if (particle is XmlSchemaElement element)
            {
                children.Add(ReadElement(element));
                return;
            }

            if (particle is XmlSchemaGroupBase group)
            {
                foreach (XmlSchemaObject item in group.Items)
                {
                    if (item is XmlSchemaParticle child)
                        ReadParticle(child, children);
                }
            }
        }

        private static int DecimalToInt(decimal value, int fallback)
        {
            if (value > int.MaxValue) return int.MaxValue;
            if (value < int.MinValue) return int.MinValue;
            try { return decimal.ToInt32(value); }
            catch { return fallback; }
        }
    }

    public class XSDValidationResult
    {
        public bool IsValid { get; set; }
        public string SchemaName { get; set; }
        public string TargetNamespace { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class XSDSchema
    {
        public string Name { get; set; }
        public string FilePath { get; set; }
        public string TargetNamespace { get; set; }
        public List<XSDElement> Elements { get; set; } = new();
        public Dictionary<string, string> Namespaces { get; set; } = new();
    }

    public class XSDElement
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public Dictionary<string, string> Attributes { get; set; } = new();
        public List<XSDElement> Children { get; set; } = new();
        public bool IsRequired { get; set; }
        public int MinOccurs { get; set; }
        public int MaxOccurs { get; set; } = 1;
    }
}
