#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// K'UHUL Program Conformance Test.
    ///
    /// Runs each .kuhul program through every stage of the compiler pipeline
    /// and reports pass/fail per workload.
    ///
    ///   source       → parse
    ///                → validate
    ///                → KAST convert
    ///                → compile
    ///                → closed-loop check
    ///                → SCXQ2 lower
    ///                → schedule
    ///
    /// A program fails if any stage throws or produces an invalid result.
    /// </summary>
    public static class KuhulConformanceTest
    {
        private static readonly string[] RequiredStages =
        {
            "Parse", "Validate", "KAST", "Compile",
            "ClosedLoop", "Lower", "Schedule"
        };

        /// <summary>Discover .kuhul programs — wizard-generated ones auto-included.</summary>
        private static string[] DiscoverPrograms()
        {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "schemas", "programs");
            if (!Directory.Exists(dir)) dir = "schemas/programs";
            return Directory.GetFiles(dir, "*.kuhul", SearchOption.TopDirectoryOnly);
        }

        /// <summary>
        /// Run all conformance tests and return structured results.
        /// </summary>
        public static ConformanceReport RunAll()
        {
            var paths = DiscoverPrograms();
            var report = new ConformanceReport
            {
                Timestamp = DateTime.UtcNow,
                TotalPrograms = paths.Length,
                RequiredStages = RequiredStages.ToList()
            };

            foreach (var path in paths)
            {
                var result = RunProgram(path, expectedId: null);
                report.Results.Add(result);
                if (result.Passed) report.PassedCount++;
            }

            report.AllPassed = report.PassedCount == report.TotalPrograms;
            return report;
        }

        private static ConformanceResult RunProgram(string path, string? expectedId)
        {
            var result = new ConformanceResult
            {
                ProgramFile = path,
                Stages = new List<StageResult>(),
                Errors = new List<string>(),
                Metadata = new Dictionary<string, object?>()
            };

            // ── Stage 1: Parse ──────────────────────────────────────────────
            KuhulProgram? program = null;
            try
            {
                program = KuhulProgram.Build(path);
                result.Stages.Add(Pass("Parse"));
            }
            catch (Exception ex)
            {
                result.Stages.Add(Fail("Parse", ex.Message));
                result.Errors.Add("Parse: " + ex.Message);
                return result;
            }

            // ── Stage 2: Validate ──────────────────────────────────────────
            try
            {
                var validation = KuhulValidator.Validate(program);
                if (validation.IsValid)
                {
                    result.Stages.Add(Pass("Validate", validation.Warnings.Count > 0
                        ? validation.Warnings.Count + " warnings" : null));
                }
                else
                {
                    result.Stages.Add(Fail("Validate",
                        validation.Errors.Count + " error(s): " +
                        string.Join("; ", validation.Errors)));
                    result.Errors.Add("Validate: " +
                        string.Join("; ", validation.Errors));
                }
            }
            catch (Exception ex)
            {
                result.Stages.Add(Fail("Validate", ex.Message));
                result.Errors.Add("Validate: " + ex.Message);
                return result;
            }

            // ── Stage 3: KAST Conversion ──────────────────────────────────
            try
            {
                var kast = program.ToKastDocument();
                if (kast != null && kast.Nodes.Count > 0)
                {
                    result.Stages.Add(Pass("KAST",
                        kast.Nodes.Count + " nodes, " +
                        kast.Edges.Count + " edges"));
                }
                else
                {
                    result.Stages.Add(Fail("KAST", "Empty KAST document"));
                    result.Errors.Add("KAST: empty document");
                }
            }
            catch (Exception ex)
            {
                result.Stages.Add(Fail("KAST", ex.Message));
                result.Errors.Add("KAST: " + ex.Message);
                return result;
            }

            // ── Stage 4: Compile ────────────────────────────────────────────
            KuhulCompiledProgram? compiled = null;
            try
            {
                compiled = KuhulCompiler.Compile(program);
                result.Stages.Add(Pass("Compile",
                    compiled.FoldCount + " folds, " +
                    compiled.Nodes.Count + " nodes"));
            }
            catch (Exception ex)
            {
                result.Stages.Add(Fail("Compile", ex.Message));
                result.Errors.Add("Compile: " + ex.Message);
                return result;
            }

            // ── Stage 5: Closed-Loop Invariant ─────────────────────────────
            try
            {
                if (compiled.IsClosedLoop)
                {
                    result.Stages.Add(Pass("ClosedLoop",
                        "entry=0, Xul.next=0"));
                }
                else
                {
                    result.Stages.Add(Fail("ClosedLoop",
                        "Xul.next != 0 or entry != 0"));
                    result.Errors.Add("ClosedLoop: invariant violated");
                }
            }
            catch (Exception ex)
            {
                result.Stages.Add(Fail("ClosedLoop", ex.Message));
                result.Errors.Add("ClosedLoop: " + ex.Message);
                return result;
            }

            // ── Stage 6: SCXQ2 Lowering ────────────────────────────────────
            LoweringResult? lowered = null;
            try
            {
                lowered = KuhulScxq2Lowering.Lower(compiled);
                if (lowered != null && lowered.LaneCount > 0 &&
                    lowered.IsClosedLoop)
                {
                    result.Stages.Add(Pass("Lower",
                        lowered.LaneCount + " SCXQ2 lanes"));
                }
                else
                {
                    result.Stages.Add(Fail("Lower", "No lanes or not closed-loop"));
                    result.Errors.Add("Lower: invalid result");
                }
            }
            catch (Exception ex)
            {
                result.Stages.Add(Fail("Lower", ex.Message));
                result.Errors.Add("Lower: " + ex.Message);
                return result;
            }

            // ── Stage 7: Schedule Generation ───────────────────────────────
            try
            {
                var schedule = KuhulScxq2Lowering.GenerateSchedule(compiled);
                if (!string.IsNullOrWhiteSpace(schedule))
                {
                    // Verify schedule parses as JSON
                    using var _ = JsonDocument.Parse(schedule);
                    result.Stages.Add(Pass("Schedule", "valid JSON"));
                }
                else
                {
                    result.Stages.Add(Fail("Schedule", "Empty schedule"));
                    result.Errors.Add("Schedule: empty");
                }
            }
            catch (Exception ex)
            {
                result.Stages.Add(Fail("Schedule", ex.Message));
                result.Errors.Add("Schedule: " + ex.Message);
            }

            // ── Program ID check ───────────────────────────────────────────
            if (expectedId != null)
            {
                var actualId = compiled.Id;
                if (actualId == expectedId)
                    result.Metadata["id_match"] = true;
                else
                    result.Metadata["id_match"] = $"expected={expectedId}, actual={actualId}";
            }

            // ── Summary ────────────────────────────────────────────────────
            result.Passed = result.Errors.Count == 0;
            return result;
        }

        private static StageResult Pass(string stage, string? detail = null) => new()
        {
            Stage = stage, Passed = true, Detail = detail ?? "ok"
        };

        private static StageResult Fail(string stage, string detail) => new()
        {
            Stage = stage, Passed = false, Detail = detail
        };
    }

    /// <summary>Full conformance report across all programs.</summary>
    public sealed class ConformanceReport
    {
        public DateTime Timestamp { get; init; }
        public bool AllPassed { get; set; }
        public int TotalPrograms { get; init; }
        public int PassedCount { get; set; }
        public List<string> RequiredStages { get; init; } = new();
        public List<ConformanceResult> Results { get; init; } = new();

        public override string ToString()
        {
            var lines = new List<string>
            {
                "┌──────────────────────────────────────────────────────────┐",
                "│       K'UHUL Program Conformance Test                    │",
                "└──────────────────────────────────────────────────────────┘",
                "",
                $"  {Timestamp:yyyy-MM-dd HH:mm:ss} UTC",
                $"  Required stages: {string.Join(" → ", RequiredStages)}",
                ""
            };

            foreach (var result in Results)
            {
                lines.Add($"  {(result.Passed ? "✅" : "❌")}  {result.ProgramFile}");
                foreach (var stage in result.Stages)
                {
                    lines.Add($"    {(stage.Passed ? "✓" : "✗")}  {stage.Stage,-12} {stage.Detail}");
                }
                if (result.Errors.Count > 0)
                {
                    foreach (var err in result.Errors)
                        lines.Add($"         Error: {err}");
                }
                lines.Add("");
            }

            lines.Add($"  Programs: {PassedCount}/{TotalPrograms} passed " +
                      (AllPassed ? "✅ ALL PASS" : "❌ FAILURES DETECTED"));
            lines.Add("");

            return string.Join("\n", lines);
        }

        public string ToJson() => JsonSerializer.Serialize(new
        {
            timestamp = Timestamp,
            all_passed = AllPassed,
            total_programs = TotalPrograms,
            passed_count = PassedCount,
            results = Results.Select(r => new
            {
                program = r.ProgramFile,
                passed = r.Passed,
                stages = r.Stages.Select(s => new
                {
                    stage = s.Stage,
                    passed = s.Passed,
                    detail = s.Detail
                }),
                errors = r.Errors,
                metadata = r.Metadata
            })
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Result for one program through the pipeline.</summary>
    public sealed class ConformanceResult
    {
        public string ProgramFile { get; init; } = "";
        public bool Passed { get; set; }
        public List<StageResult> Stages { get; init; } = new();
        public List<string> Errors { get; init; } = new();
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    /// <summary>Result for one pipeline stage.</summary>
    public sealed class StageResult
    {
        public string Stage { get; init; } = "";
        public bool Passed { get; init; }
        public string Detail { get; init; } = "";
    }
}
