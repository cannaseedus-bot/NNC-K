#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// MicronautWizard — UI-facing builder for compiling .kuhul personality
    /// programs into .kprog executable graphs.
    ///
    /// Provides: template listing, property configuration, compilation,
    /// and installation into the active MicronautRegister.
    /// </summary>
    public static class MicronautWizard
    {
        private static readonly string[] Templates =
        {
            "MathTeacher",
            "Researcher",
            "Coder",
            "SocraticTutor",
            "CreativeWriter",
            "Custom"
        };

        private static readonly string[] DefaultFolds =
            { "Pop", "Wo", "Yax", "Sek", "Ch'en", "Xul" };

        private static readonly Dictionary<string, string> TemplateDescriptions = new()
        {
            ["MathTeacher"]    = "Adaptive math teacher with REASON-u/PLAN-u/MATH-u/MEM-u/CREATE-u/VERIFY-u mixture. Adjusts personality from student failure rate.",
            ["Researcher"]     = "Evidence-driven research agent. Decomposes claims, gathers sources, cross-references, detects contradictions, and refolds until agreement >= threshold.",
            ["Coder"]          = "Code generation and review personality. Plans, implements, tests, and reviews code across languages with VERIFY-u gates.",
            ["SocraticTutor"]  = "Socratic dialogue teacher. Poses questions instead of answers, guides student through reasoning chains, adapts difficulty to responses.",
            ["CreativeWriter"] = "Creative writing assistant with tone/style mixture. Balances imagination, structure, vocabulary, and critique micronauts.",
            ["Custom"]         = "Empty template with six-fold skeleton. Define your own personality state, mixture weights, and contracts."
        };

        /// <summary>Available program templates.</summary>
        public static string[] GetTemplates() => Templates;

        /// <summary>Description for a template.</summary>
        public static string Describe(string template) =>
            TemplateDescriptions.TryGetValue(template, out var d) ? d : "Unknown template";

        /// <summary>
        /// Create a .kuhul source file from a template with the given name and properties.
        /// Properties are template-specific key/value pairs.
        /// </summary>
        public static string Scaffold(
            string outputDir,
            string template,
            string programName,
            Dictionary<string, string>? properties = null)
        {
            if (string.IsNullOrWhiteSpace(outputDir))
                throw new ArgumentException("Output directory is required");
            if (string.IsNullOrWhiteSpace(programName))
                throw new ArgumentException("Program name is required");

            Directory.CreateDirectory(outputDir);
            var shamanProps = properties ?? new Dictionary<string, string>();
            var name = Sanitize(programName);
            var path = Path.Combine(outputDir, name + ".kuhul");

            if (template == "Custom")
                File.WriteAllText(path, GenerateSkeleton(name, shamanProps));
            else if (template == "MathTeacher")
                File.WriteAllText(path, GenerateMathTeacher(name, shamanProps));
            else if (template == "Researcher")
                File.WriteAllText(path, GenerateResearcher(name, shamanProps));
            else if (template == "Coder")
                File.WriteAllText(path, GenerateCoder(name, shamanProps));
            else if (template == "SocraticTutor")
                File.WriteAllText(path, GenerateSocratic(name, shamanProps));
            else if (template == "CreativeWriter")
                File.WriteAllText(path, GenerateWriter(name, shamanProps));
            else
                File.WriteAllText(path, GenerateSkeleton(name, shamanProps));

            return path;
        }

        /// <summary>
        /// Compile a .kuhul source into a .kprog and optionally install
        /// it into a MicronautRegister.
        /// </summary>
        public static WizardResult Compile(string sourcePath, MicronautRegister? register = null)
        {
            var result = new WizardResult { SourcePath = sourcePath };

            try
            {
                // 1. Build: parse + validate
                var program = KuhulProgram.Build(sourcePath);
                result.ParsePassed = true;

                // 2. Validate
                var validation = KuhulValidator.Validate(program);
                result.Validated = validation.IsValid;
                result.ValidationErrors = validation.Errors;

                if (!validation.IsValid)
                {
                    result.Error = "Validation failed: " +
                        string.Join("; ", validation.Errors);
                    return result;
                }

                // 3. Compile to .kprog
                var outputPath = Path.ChangeExtension(sourcePath, ".kprog");
                result.CompiledProgram = KuhulCompiler.Build(sourcePath, outputPath);
                result.CompilePassed = true;
                result.KprogPath = outputPath;

                // 4. Closed-loop check
                result.IsClosedLoop = result.CompiledProgram.IsClosedLoop;

                // 5. Optionally install into register
                if (register != null)
                {
                    int count = InstallIntoRegister(result.CompiledProgram, register);
                    result.InstalledNodeCount = count;
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>Install a compiled program's folds as MicronautNodes.</summary>
        public static int InstallIntoRegister(
            KuhulCompiledProgram program,
            MicronautRegister register)
        {
            if (register == null) return 0;

            var phaseMap = new Dictionary<string, FoldPhase>(StringComparer.OrdinalIgnoreCase)
            {
                ["Pop"]   = FoldPhase.Pop,
                ["Wo"]    = FoldPhase.Wo,
                ["Yax"]   = FoldPhase.Yax,
                ["Sek"]   = FoldPhase.Sek,
                ["Chen"]  = FoldPhase.Chen,
                ["Ch'en"] = FoldPhase.Chen,
                ["Xul"]   = FoldPhase.Xul
            };

            int count = 0;
            foreach (var fold in program.Folds)
            {
                var phase = phaseMap.TryGetValue(fold.Phase, out var p)
                    ? p : FoldPhase.Pop;

                // Determine capability from the fold's first node.
                var capability = fold.Op ?? "orchestrate";
                var brain = program.Id + "_" + fold.Phase;

                var node = new MicronautNode
                {
                    Id = $"{Sanitize(program.Id)}_{fold.Phase}_{fold.Id}",
                    Subject = program.Id,
                    Capability = capability,
                    Brain = brain,
                    Phase = phase,
                    Quality = 100.0,
                    IsSeed = true,
                    IsDaemon = fold.Phase == "Xul" || fold.Phase == "Pop",
                    Source = "wizard"
                };

                register.Register(node);
                count++;
            }

            return count;
        }

        // ── Template generators ──────────────────────────────────────

        private static string GenerateSkeleton(string name, Dictionary<string, string> props)
        {
            var role = props.GetValueOrDefault("role", "assistant");
            return
                "{\n" +
                "  \"kuhul\": \"1.0\",\n" +
                "  \"type\": \"program\",\n" +
                "  \"folds\": [\n" +
                "    { \"type\": \"fold\", \"name\": \"Pop\", \"nodes\": [" +
                    "{ \"type\": \"literal\", \"value\": \"" + name + "\" }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Wo\", \"nodes\": [\n" +
                    "      { \"type\": \"assign\", \"target\": \"role\", " +
                        "\"value\": { \"type\": \"literal\", \"value\": \"" + role + "\" } },\n" +
                    "      { \"type\": \"assign\", \"target\": \"state\", " +
                        "\"value\": { \"type\": \"literal\", \"value\": {} } }\n" +
                "    ] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Yax\", \"nodes\": [" +
                    "{ \"type\": \"ref\", \"name\": \"input\" }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Sek\", \"nodes\": [" +
                    "{ \"type\": \"call\", \"name\": \"invoke\", " +
                        "\"args\": [{ \"type\": \"ref\", \"name\": \"role\" }, " +
                        "{ \"type\": \"ref\", \"name\": \"input\" }] }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Ch'en\", \"nodes\": [" +
                    "{ \"type\": \"emit\", \"value\": { \"type\": \"ref\", \"name\": \"result\" } }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Xul\", \"nodes\": [\n" +
                "      { \"type\": \"if\", \"test\": " +
                        "{ \"type\": \"op\", \"op\": \"==\", " +
                        "\"args\": [{ \"type\": \"ref\", \"name\": \"done\" }, " +
                        "{ \"type\": \"literal\", \"value\": true }] },\n" +
                "        \"then\": { \"type\": \"block\", \"nodes\": [" +
                        "{ \"type\": \"call\", \"name\": \"collapse\", " +
                        "\"args\": [{ \"type\": \"literal\", \"value\": \"" + name + "\" }] }] } } ] }\n" +
                "  ]\n" +
                "}\n";
        }

        private static string GenerateMathTeacher(string name, Dictionary<string, string> props)
        {
            var tone = props.GetValueOrDefault("tone", "clear");
            var patience = props.GetValueOrDefault("patience", "0.95");
            var rigor = props.GetValueOrDefault("rigor", "0.92");
            var mixtureReason = props.GetValueOrDefault("mixture_reason", "0.30");
            var mixturePlan = props.GetValueOrDefault("mixture_plan", "0.15");
            var mixtureMath = props.GetValueOrDefault("mixture_math", "0.30");
            var mixtureVerify = props.GetValueOrDefault("mixture_verify", "0.10");

            return
                "{\n" +
                "  \"kuhul\": \"1.0\", \"type\": \"program\",\n" +
                "  \"folds\": [\n" +
                "    { \"type\": \"fold\", \"name\": \"Pop\", \"nodes\": [" +
                    "{ \"type\": \"literal\", \"value\": \"" + name + "\" }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Wo\", \"nodes\": [\n" +
                "      { \"type\": \"assign\", \"target\": \"personality\", " +
                    "\"value\": { \"type\": \"literal\", \"value\": " +
                    "{ \"role\": \"math_teacher\", \"tone\": \"" + tone + "\", " +
                    "\"patience\": " + patience + ", \"rigor\": " + rigor + " } } },\n" +
                "      { \"type\": \"assign\", \"target\": \"mixture\", " +
                    "\"value\": { \"type\": \"literal\", \"value\": " +
                    "{ \"REASON-u\": " + mixtureReason + ", " +
                    "\"PLAN-u\": " + mixturePlan + ", \"MATH-u\": " + mixtureMath + ", " +
                    "\"VERIFY-u\": " + mixtureVerify + " } } } ] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Yax\", \"nodes\": [" +
                    "{ \"type\": \"ref\", \"name\": \"input.question\" }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Sek\", \"nodes\": [" +
                    "{ \"type\": \"call\", \"name\": \"invoke\", " +
                    "\"args\": [{ \"type\": \"ref\", \"name\": \"MATH-u\" }, " +
                    "{ \"type\": \"ref\", \"name\": \"input.question\" }] }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Ch'en\", \"nodes\": [" +
                    "{ \"type\": \"emit\", \"value\": { \"type\": \"ref\", \"name\": \"result\" } }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Xul\", \"nodes\": [" +
                    "{ \"type\": \"call\", \"name\": \"fold\", " +
                    "\"args\": [{ \"type\": \"literal\", \"value\": \"" + name + "\" }] }] }\n" +
                "  ]\n" +
                "}\n";
        }

        private static string GenerateResearcher(string name, Dictionary<string, string> props)
        {
            var sources = props.GetValueOrDefault("sources", "8");
            var threshold = props.GetValueOrDefault("agreement_threshold", "0.85");

            return
                "{\n" +
                "  \"kuhul\": \"1.0\", \"type\": \"program\",\n" +
                "  \"folds\": [\n" +
                "    { \"type\": \"fold\", \"name\": \"Pop\", \"nodes\": [" +
                    "{ \"type\": \"literal\", \"value\": \"" + name + "\" }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Wo\", \"nodes\": [" +
                    "{ \"type\": \"assign\", \"target\": \"params\", " +
                    "\"value\": { \"type\": \"literal\", \"value\": " +
                    "{ \"max_sources\": " + sources + ", " +
                    "\"threshold\": " + threshold + " } } }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Yax\", \"nodes\": [" +
                    "{ \"type\": \"call\", \"name\": \"decompose\", " +
                    "\"args\": [{ \"type\": \"ref\", \"name\": \"input.claim\" }] }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Sek\", \"nodes\": [\n" +
                "      { \"type\": \"call\", \"name\": \"invoke\", " +
                    "\"args\": [{ \"type\": \"ref\", \"name\": \"NET-u\" }, " +
                    "{ \"type\": \"ref\", \"name\": \"questions\" }] },\n" +
                "      { \"type\": \"call\", \"name\": \"classify\", " +
                    "\"args\": [{ \"type\": \"ref\", \"name\": \"evidence\" }] } ] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Ch'en\", \"nodes\": [" +
                    "{ \"type\": \"emit\", \"value\": { \"type\": \"ref\", \"name\": \"conclusion\" } }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Xul\", \"nodes\": [" +
                    "{ \"type\": \"if\", \"test\": " +
                    "{ \"type\": \"op\", \"op\": \">=\", " +
                    "\"args\": [{ \"type\": \"ref\", \"name\": \"agreement\" }, " +
                    "{ \"type\": \"literal\", \"value\": " + threshold + " }] },\n" +
                "      \"then\": { \"type\": \"block\", \"nodes\": [" +
                    "{ \"type\": \"call\", \"name\": \"collapse\", " +
                    "\"args\": [{ \"type\": \"literal\", \"value\": \"" + name + "\" }] }] },\n" +
                "      \"else\": { \"type\": \"block\", \"nodes\": [" +
                    "{ \"type\": \"call\", \"name\": \"fold\", " +
                    "\"args\": [{ \"type\": \"literal\", \"value\": \"" + name + "\" }] }] } } ] }\n" +
                "  ]\n" +
                "}\n";
        }

        private static string GenerateCoder(string name, Dictionary<string, string> props)
        {
            var lang = props.GetValueOrDefault("language", "python");

            return
                "{\n" +
                "  \"kuhul\": \"1.0\", \"type\": \"program\",\n" +
                "  \"folds\": [\n" +
                "    { \"type\": \"fold\", \"name\": \"Pop\", \"nodes\": [" +
                    "{ \"type\": \"literal\", \"value\": \"" + name + "\" }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Wo\", \"nodes\": [" +
                    "{ \"type\": \"assign\", \"target\": \"language\", " +
                    "\"value\": { \"type\": \"literal\", \"value\": \"" + lang + "\" } }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Yax\", \"nodes\": [" +
                    "{ \"type\": \"call\", \"name\": \"plan\", " +
                    "\"args\": [{ \"type\": \"ref\", \"name\": \"input.spec\" }] }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Sek\", \"nodes\": [\n" +
                "      { \"type\": \"call\", \"name\": \"implement\", " +
                    "\"args\": [{ \"type\": \"ref\", \"name\": \"plan\" }] },\n" +
                "      { \"type\": \"call\", \"name\": \"test\", " +
                    "\"args\": [{ \"type\": \"ref\", \"name\": \"code\" }] } ] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Ch'en\", \"nodes\": [" +
                    "{ \"type\": \"emit\", \"value\": { \"type\": \"ref\", \"name\": \"code\" } }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Xul\", \"nodes\": [" +
                    "{ \"type\": \"call\", \"name\": \"collapse\", " +
                    "\"args\": [{ \"type\": \"literal\", \"value\": \"" + name + "\" }] }] }\n" +
                "  ]\n" +
                "}\n";
        }

        private static string GenerateSocratic(string name, Dictionary<string, string> props)
        {
            return
                "{\n" +
                "  \"kuhul\": \"1.0\", \"type\": \"program\",\n" +
                "  \"folds\": [\n" +
                "    { \"type\": \"fold\", \"name\": \"Pop\", \"nodes\": [" +
                    "{ \"type\": \"literal\", \"value\": \"" + name + "\" }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Wo\", \"nodes\": [" +
                    "{ \"type\": \"assign\", \"target\": \"method\", " +
                    "\"value\": { \"type\": \"literal\", \"value\": \"socratic\" } }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Yax\", \"nodes\": [" +
                    "{ \"type\": \"call\", \"name\": \"analyze\", " +
                    "\"args\": [{ \"type\": \"ref\", \"name\": \"input\" }] }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Sek\", \"nodes\": [" +
                    "{ \"type\": \"call\", \"name\": \"pose_question\", " +
                    "\"args\": [{ \"type\": \"ref\", \"name\": \"analysis\" }] }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Ch'en\", \"nodes\": [" +
                    "{ \"type\": \"emit\", \"value\": { \"type\": \"ref\", \"name\": \"question\" } }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Xul\", \"nodes\": [" +
                    "{ \"type\": \"if\", \"test\": " +
                    "{ \"type\": \"op\", \"op\": \"==\", " +
                    "\"args\": [{ \"type\": \"ref\", \"name\": \"answered\" }, " +
                    "{ \"type\": \"literal\", \"value\": false }] },\n" +
                "      \"then\": { \"type\": \"block\", \"nodes\": [" +
                    "{ \"type\": \"call\", \"name\": \"fold\", " +
                    "\"args\": [{ \"type\": \"literal\", \"value\": \"" + name + "\" }] }] } } ] }\n" +
                "  ]\n" +
                "}\n";
        }

        private static string GenerateWriter(string name, Dictionary<string, string> props)
        {
            var style = props.GetValueOrDefault("style", "creative");
            var vocab = props.GetValueOrDefault("vocabulary", "0.7");
            var structure = props.GetValueOrDefault("structure", "0.5");

            return
                "{\n" +
                "  \"kuhul\": \"1.0\", \"type\": \"program\",\n" +
                "  \"folds\": [\n" +
                "    { \"type\": \"fold\", \"name\": \"Pop\", \"nodes\": [" +
                    "{ \"type\": \"literal\", \"value\": \"" + name + "\" }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Wo\", \"nodes\": [" +
                    "{ \"type\": \"assign\", \"target\": \"style\", " +
                    "\"value\": { \"type\": \"literal\", \"value\": \"" + style + "\" } }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Yax\", \"nodes\": [" +
                    "{ \"type\": \"ref\", \"name\": \"input.prompt\" }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Sek\", \"nodes\": [" +
                    "{ \"type\": \"call\", \"name\": \"compose\", " +
                    "\"args\": [{ \"type\": \"ref\", \"name\": \"input.prompt\" }] }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Ch'en\", \"nodes\": [" +
                    "{ \"type\": \"emit\", \"value\": { \"type\": \"ref\", \"name\": \"draft\" } }] },\n" +
                "    { \"type\": \"fold\", \"name\": \"Xul\", \"nodes\": [" +
                    "{ \"type\": \"call\", \"name\": \"fold\", " +
                    "\"args\": [{ \"type\": \"literal\", \"value\": \"" + name + "\" }] }] }\n" +
                "  ]\n" +
                "}\n";
        }

        private static string Sanitize(string value) =>
            string.IsNullOrWhiteSpace(value) ? "program" :
                string.Join("", value.Split(Path.GetInvalidFileNameChars()));

        // props passed through Scaffold parameters
    }

    public sealed class WizardResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string SourcePath { get; init; } = "";
        public string? KprogPath { get; set; }
        public bool ParsePassed { get; set; }
        public bool Validated { get; set; }
        public List<string> ValidationErrors { get; set; } = new();
        public bool CompilePassed { get; set; }
        public bool IsClosedLoop { get; set; }
        public KuhulCompiledProgram? CompiledProgram { get; set; }
        public int InstalledNodeCount { get; set; }
    }
}
