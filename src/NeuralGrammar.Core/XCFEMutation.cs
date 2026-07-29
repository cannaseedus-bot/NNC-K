using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// XCFE Mutation Engine — processes .learning/ logs, generates new micronauts,
    /// tracks per-model quality, and auto-evolves the knowledge base from interactions.
    /// Called by phase-bridge.ps1 via Export-Report or run standalone.
    /// </summary>
    public class XCFEMutation
    {
        private readonly string _learnDir;

        public XCFEMutation(string learnDir = null)
        {
            _learnDir = learnDir ?? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, ".learning");
            if (!Directory.Exists(_learnDir))
                Directory.CreateDirectory(_learnDir);
        }

        // ── Load all learning entries ────────────────────────────────────────
        public (List<Dictionary<string, object>> Chats, List<Dictionary<string, object>> Scores) LoadAll()
        {
            var chats = new List<Dictionary<string, object>>();
            var scores = new List<Dictionary<string, object>>();

            foreach (var file in Directory.GetFiles(_learnDir, "*.jsonl"))
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var entry = JsonSerializer.Deserialize<Dictionary<string, object>>(line);
                        if (entry == null) continue;
                        if (entry.ContainsKey("quality")) scores.Add(entry);
                        else chats.Add(entry);
                    }
                    catch { }
                }
            }
            return (chats, scores);
        }

        // ── Per-model quality stats ──────────────────────────────────────────
        public Dictionary<string, ModelStats> GetModelStats()
        {
            var (_, scores) = LoadAll();
            var stats = new Dictionary<string, ModelStats>();

            foreach (var s in scores)
            {
                var model = s.GetValueOrDefault("model", "unknown")?.ToString() ?? "unknown";
                if (!stats.ContainsKey(model))
                    stats[model] = new ModelStats { ModelName = model };

                var quality = Convert.ToDouble(s.GetValueOrDefault("quality", 50));
                stats[model].TotalQuality += quality;
                stats[model].Count++;
                stats[model].LastQuality = quality;
                stats[model].LastTimestamp = s.GetValueOrDefault("timestamp", "")?.ToString() ?? "";

                if (s.TryGetValue("has_code", out var hc) && Convert.ToBoolean(hc))
                    stats[model].CodeCount++;
                if (s.TryGetValue("python_valid", out var pv) && Convert.ToBoolean(pv))
                    stats[model].PythonValidCount++;
                if (s.TryGetValue("issues", out var iss) && iss is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.Array)
                        stats[model].TotalIssues += je.GetArrayLength();
                }
            }

            foreach (var m in stats.Values)
            {
                m.AvgQuality = m.Count > 0 ? m.TotalQuality / m.Count : 0;
                m.CodeRatio = m.Count > 0 ? (double)m.CodeCount / m.Count : 0;
            }

            return stats;
        }

        // ── Seeding ──────────────────────────────────────────────────────────

        /// <summary>
        /// Pre-seed broad domain micronauts as base knowledge nodes.
        /// Creates deterministic, tagged micronauts in the standard subject/fold
        /// directory hierarchy. Skips if the micronaut already exists.
        /// </summary>
        public int SeedDomainMicronauts()
        {
            var micronautDir = Path.Combine(_learnDir, "micronauts");
            Directory.CreateDirectory(micronautDir);

            var seeds = new[]
            {
                ("astrophysics",  "Sek", "theoretical", "reasoning",
                 "Black holes are regions of spacetime where gravity is so strong that nothing can escape. They form from collapsing massive stars and have event horizons. Hawking radiation predicts they slowly evaporate. Key topics: Schwarzschild radius, singularity, accretion disks, gravitational waves."),

                ("physics",      "Sek", "theoretical", "reasoning",
                 "Physics studies matter, energy, space, and time. Core domains: classical mechanics (Newton), electromagnetism (Maxwell), thermodynamics, quantum mechanics, relativity (Einstein). The Standard Model describes fundamental particles and forces."),

                ("mathematics",  "Yax", "math",        "logic",
                 "Mathematics is the study of patterns, structures, and relationships using formal logic. Core branches: algebra, geometry, calculus, number theory, topology, statistics. Mathematical proofs establish truth through deductive reasoning from axioms."),

                ("programming",  "Wo",  "code",        "coder",
                 "Programming is designing and building executable code using languages like C#, Python, JavaScript, Rust, and PowerShell. Key concepts: algorithms, data structures, OOP, functional programming, async/await, memory management, and design patterns."),

                ("web-research","Pop",   "search",      "knowledge",
                 "Web research involves locating, evaluating, and synthesizing information from online sources. Key skills: search query formulation, source credibility assessment, cross-referencing, fact-checking, and information synthesis."),

                ("biology",      "Sek", "theoretical", "reasoning",
                 "Biology is the study of living organisms. Core domains: cell biology, genetics (DNA/RNA), evolution (natural selection), ecology, physiology, and neuroscience. The central dogma: DNA to RNA to protein."),

                ("ai-ml",        "Yax", "reasoning",   "reasoning",
                 "Artificial Intelligence and Machine Learning create systems that learn from data. Key concepts: supervised/unsupervised learning, neural networks, transformers, reinforcement learning, gradient descent, embeddings, and LLMs."),

                ("systems",      "Wo",  "engineering", "architecture",
                 "Systems engineering designs complex integrated systems. Key concepts: architecture (monolith vs microservices), networking protocols, databases (SQL/NoSQL), scalability, reliability, observability, and deployment (CI/CD, containers)."),

                ("security",     "Yax", "audit",       "reasoning",
                 "Security protects systems and data from threats. Core domains: cryptography, authentication (OAuth, JWT), authorization, network security, vulnerability assessment, and secure coding practices."),

                ("language",     "Pop", "comprehension","knowledge",
                 "Natural language encompasses human communication through words and grammar. Key concepts: syntax, semantics, pragmatics, discourse analysis, named entity recognition, tokenization, and sentiment analysis.")
            };

            var generated = 0;
            foreach (var (subject, fold, capability, brain, response) in seeds)
            {
                var semanticSignature = string.Join("|", new[]
                {
                    Normalize(subject),
                    Normalize(fold),
                    Normalize("seed"),
                    Normalize(capability),
                    Normalize(brain)
                });

                var id = $"seed_{ComputeHash(semanticSignature).Substring(0, 12)}";
                var subjectDir = SanitizeDirName(subject);
                var foldDir = SanitizeDirName(fold);
                var subDir = Path.Combine(micronautDir, subjectDir, foldDir);
                Directory.CreateDirectory(subDir);
                var path = Path.Combine(subDir, $"{id}.json");

                if (File.Exists(path)) continue;

                var micronaut = new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["subject"] = subject,
                    ["fold"] = fold,
                    ["model"] = "seed",
                    ["quality"] = 100.0,
                    ["verified"] = true,
                    ["admitted"] = true,
                    ["created"] = DateTime.UtcNow.ToString("o"),
                    ["semantic_signature"] = semanticSignature,
                    ["response"] = response,
                    ["provenance"] = new Dictionary<string, object>
                    {
                        ["source"] = "seed",
                        ["capability"] = capability,
                        ["brain"] = brain,
                        ["fold_trace"] = new[] { fold },
                        ["terminal_fold"] = fold,
                        ["interaction_id"] = id,
                        ["timestamp"] = DateTime.UtcNow.ToString("o")
                    },
                    ["authority"] = new Dictionary<string, object>
                    {
                        ["control_mutation"] = false,
                        ["tool_grants"] = Array.Empty<string>(),
                        ["opcode_grants"] = Array.Empty<string>()
                    },
                    ["tags"] = new Dictionary<string, object>
                    {
                        ["source"] = "seed",
                        ["fold"] = fold,
                        ["auto_generated"] = true,
                        ["verified"] = true,
                        ["domain"] = subject
                    }
                };

                File.WriteAllText(path, JsonSerializer.Serialize(micronaut,
                    new JsonSerializerOptions { WriteIndented = true }));
                generated++;
            }

            return generated;
        }

        // ── Controlled mutation boundary ──────────────────────────────────────
        // Mutation occurs only after an interaction has been evaluated/collapsed.
        // It may persist semantic learning, but it may not grant tool/opcode authority.
        public int GenerateMicronauts()
        {
            var (_, scores) = LoadAll();
            var micronautDir = Path.Combine(_learnDir, "micronauts");
            var rejectedDir = Path.Combine(_learnDir, "rejected");

            Directory.CreateDirectory(micronautDir);
            Directory.CreateDirectory(rejectedDir);

            int generated = 0;

            foreach (var entry in scores)
            {
                var quality = GetDouble(entry, "quality", 0);
                var model = GetString(entry, "model", "unknown");
                var verified = GetBool(entry, "verified", false);
                var admitted = GetBool(entry, "admitted", verified);
                var completeness = GetDouble(entry, "completeness", 0);
                var subject = ExtractSubject(entry);
                var fold = DetectFold(entry);
                var capability = GetString(entry, "capability", "");
                var brain = GetString(entry, "brain", "");
                var trace = GetStringArray(entry, "fold_trace");

                // M2: quality is evidence, not authority.
                // Require a high-quality evaluated interaction plus explicit
                // verification/admission before persistent semantic mutation.
                var eligible =
                    quality >= 80 &&
                    model != "unknown" &&
                    verified &&
                    admitted &&
                    completeness >= 0.5;

                if (!eligible)
                {
                    WriteRejectedCandidate(
                        rejectedDir, entry,
                        $"mutation gate rejected: quality={quality:F1}, verified={verified}, admitted={admitted}, completeness={completeness:F2}");
                    continue;
                }

                // M3: learned interactions cannot mint privileged control authority.
                // Tool/capability observations are provenance only. They do not become
                // executable opcodes or tool grants in the generated micronaut.
                var semanticSignature = string.Join("|", new[]
                {
                    Normalize(subject),
                    Normalize(fold),
                    Normalize(model),
                    Normalize(capability),
                    Normalize(brain)
                });

                // M1: deterministic identity makes replay idempotent.
                var id = $"micronaut_{ComputeHash(semanticSignature).Substring(0, 16)}";

                // Organize into subject/fold hierarchy for discoverability and tags.
                var subjectDir = SanitizeDirName(subject ?? "unknown");
                var foldDir = SanitizeDirName(fold ?? "unknown");
                var micronautSubDir = Path.Combine(micronautDir, subjectDir, foldDir);
                Directory.CreateDirectory(micronautSubDir);
                var path = Path.Combine(micronautSubDir, $"{id}.json");

                if (File.Exists(path))
                    continue;

                // M4: retain provenance and the actual runtime fold trace.
                var micronaut = new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["subject"] = subject,
                    ["fold"] = fold,
                    ["model"] = model,
                    ["quality"] = quality,
                    ["verified"] = verified,
                    ["admitted"] = admitted,
                    ["created"] = DateTime.UtcNow.ToString("o"),
                    ["semantic_signature"] = semanticSignature,
                    ["provenance"] = new Dictionary<string, object>
                    {
                        ["source"] = "mutation",
                        ["capability"] = capability,
                        ["brain"] = brain,
                        ["fold_trace"] = trace,
                        ["terminal_fold"] = trace.LastOrDefault() ?? GetString(entry, "terminal_fold", ""),
                        ["interaction_id"] = GetString(entry, "interaction_id", ""),
                        ["timestamp"] = GetString(entry, "timestamp", "")
                    },
                    ["authority"] = new Dictionary<string, object>
                    {
                        ["control_mutation"] = false,
                        ["tool_grants"] = Array.Empty<string>(),
                        ["opcode_grants"] = Array.Empty<string>()
                    },
                    ["tags"] = new Dictionary<string, object>
                    {
                        ["source"] = "mutation",
                        ["fold"] = fold,
                        ["auto_generated"] = true,
                        ["verified"] = true
                    }
                };

                var json = JsonSerializer.Serialize(
                    micronaut,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(path, json);
                generated++;
            }

            return generated;
        }

        // ── Prune old/low quality entries ────────────────────────────────────
        public (int ChatsKept, int ScoresKept) Prune(int maxPerFile = 500)
        {
            int chatKept = 0, scoreKept = 0;
            foreach (var file in Directory.GetFiles(_learnDir, "*.jsonl"))
            {
                var lines = File.ReadAllLines(file).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
                if (lines.Count <= maxPerFile) continue;

                // Keep the most recent entries
                var kept = lines.Skip(lines.Count - maxPerFile).ToList();
                File.WriteAllLines(file, kept);

                if (file.Contains("score")) scoreKept = kept.Count;
                else chatKept = kept.Count;
            }
            return (chatKept, scoreKept);
        }

        // ── Export report ─────────────────────────────────────────────────────
        public string ExportReport()
        {
            var (chats, _) = LoadAll();
            var modelStats = GetModelStats();
            int micronautCount = Directory.Exists(Path.Combine(_learnDir, "micronauts"))
                ? Directory.GetFiles(Path.Combine(_learnDir, "micronauts"), "*.json").Length : 0;

            var report = new System.Text.StringBuilder();
            report.AppendLine("{");
            report.AppendLine($"  \"total_interactions\": {chats.Count},");
            report.AppendLine($"  \"micronauts\": {micronautCount},");
            report.AppendLine($"  \"models_tracked\": {modelStats.Count},");
            report.AppendLine($"  \"models\": [");

            bool first = true;
            foreach (var ms in modelStats.Values.OrderByDescending(m => m.AvgQuality))
            {
                if (!first) report.AppendLine(",");
                first = false;
                report.AppendLine($"    {{ \"name\": \"{ms.ModelName}\", \"avg_quality\": {ms.AvgQuality:F1}, \"count\": {ms.Count}, \"code_ratio\": {ms.CodeRatio:F2}, \"last_quality\": {ms.LastQuality} }}");
            }
            report.AppendLine();
            report.AppendLine("  ]");
            report.AppendLine("}");

            return report.ToString();
        }

        // ── Helpers ──────────────────────────────────────────────────────────
        private string DetectFold(Dictionary<string, object> entry)
        {
            // Prefer runtime truth over heuristic reconstruction.
            var originFold = GetString(entry, "origin_fold", "");
            if (!string.IsNullOrWhiteSpace(originFold))
                return originFold;

            var trace = GetStringArray(entry, "fold_trace");
            if (trace.Length > 0)
            {
                // The terminal Xul is collapse, not necessarily the semantic origin.
                // Prefer the first meaningful fold after Pop when available.
                var meaningful = trace.FirstOrDefault(f =>
                    !string.Equals(f, "Pop", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(f, "Xul", StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(meaningful))
                    return meaningful;

                return trace[0];
            }

            // Compatibility fallback for older learning records.
            var hasCode = GetBool(entry, "has_code", false);
            var rLength = GetInt(entry, "response_length", 0);
            var completeness = GetDouble(entry, "completeness", 0.5);

            if (hasCode) return "Wo";
            if (completeness > 0.8 && rLength > 500) return "Yax";
            if (completeness > 0.5) return "Sek";
            return "Pop";
        }

        private void WriteRejectedCandidate(
            string rejectedDir,
            Dictionary<string, object> entry,
            string reason)
        {
            var signature = JsonSerializer.Serialize(entry);
            var id = ComputeHash(signature).Substring(0, 16);
            var path = Path.Combine(rejectedDir, $"rejected_{id}.json");

            if (File.Exists(path))
                return;

            var rejected = new Dictionary<string, object>
            {
                ["id"] = $"rejected_{id}",
                ["reason"] = reason,
                ["created"] = DateTime.UtcNow.ToString("o"),
                ["candidate"] = entry
            };

            File.WriteAllText(
                path,
                JsonSerializer.Serialize(
                    rejected,
                    new JsonSerializerOptions { WriteIndented = true }));
        }

        private string ComputeHash(string input)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input ?? ""));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        private static string Normalize(string value) =>
            (value ?? "").Trim().ToLowerInvariant();

        /// <summary>Sanitize a string for use as a filesystem directory name.</summary>
        private static string SanitizeDirName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "unknown";
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("-", value.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
            return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized.Trim('-').ToLowerInvariant();
        }

        private static string GetString(
            Dictionary<string, object> entry,
            string key,
            string fallback)
        {
            if (!entry.TryGetValue(key, out var value) || value == null)
                return fallback;

            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.String)
                    return je.GetString() ?? fallback;
                return je.ToString();
            }

            return value.ToString() ?? fallback;
        }

        private static bool GetBool(
            Dictionary<string, object> entry,
            string key,
            bool fallback)
        {
            if (!entry.TryGetValue(key, out var value) || value == null)
                return fallback;

            try
            {
                if (value is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.True) return true;
                    if (je.ValueKind == JsonValueKind.False) return false;
                    if (je.ValueKind == JsonValueKind.String &&
                        bool.TryParse(je.GetString(), out var parsed))
                        return parsed;
                }

                return Convert.ToBoolean(value);
            }
            catch { return fallback; }
        }

        private static double GetDouble(
            Dictionary<string, object> entry,
            string key,
            double fallback)
        {
            if (!entry.TryGetValue(key, out var value) || value == null)
                return fallback;

            try
            {
                if (value is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out var n))
                        return n;
                    if (je.ValueKind == JsonValueKind.String &&
                        double.TryParse(je.GetString(), out var parsed))
                        return parsed;
                }

                return Convert.ToDouble(value);
            }
            catch { return fallback; }
        }

        private static int GetInt(
            Dictionary<string, object> entry,
            string key,
            int fallback)
        {
            if (!entry.TryGetValue(key, out var value) || value == null)
                return fallback;

            try
            {
                if (value is JsonElement je)
                {
                    if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var n))
                        return n;
                    if (je.ValueKind == JsonValueKind.String &&
                        int.TryParse(je.GetString(), out var parsed))
                        return parsed;
                }

                return Convert.ToInt32(value);
            }
            catch { return fallback; }
        }

        private static string[] GetStringArray(
            Dictionary<string, object> entry,
            string key)
        {
            if (!entry.TryGetValue(key, out var value) || value == null)
                return Array.Empty<string>();

            if (value is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                return je.EnumerateArray()
                    .Select(x => x.ValueKind == JsonValueKind.String
                        ? x.GetString()
                        : x.ToString())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray();
            }

            if (value is IEnumerable<string> strings)
                return strings.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

            return Array.Empty<string>();
        }

        private string ExtractSubject(Dictionary<string, object> entry)
        {
            // Try to find a meaningful subject from the response
            var response = entry.GetValueOrDefault("response_preview", "")?.ToString() ?? "";
            if (string.IsNullOrEmpty(response)) return "Auto-generated micronaut";

            // Take the first sentence (up to 80 chars)
            var dot = response.IndexOf('.');
            var subject = dot > 0 ? response.Substring(0, dot) : response.Substring(0, Math.Min(80, response.Length));
            return subject.Length > 80 ? subject.Substring(0, 77) + "..." : subject;
        }

        public class ModelStats
        {
            public string ModelName { get; set; }
            public int Count { get; set; }
            public double TotalQuality { get; set; }
            public double AvgQuality { get; set; }
            public double LastQuality { get; set; }
            public int CodeCount { get; set; }
            public int PythonValidCount { get; set; }
            public int TotalIssues { get; set; }
            public double CodeRatio { get; set; }
            public string LastTimestamp { get; set; }
        }
    }
}
