using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// CodeAnalyzer — wraps dotnet SDK tools for compilation, formatting, analysis,
    /// and code generation. Routes code tasks to micronaut_coder.exe or Roslyn.
    /// Supports micro-agent delegation: model reasons → tool call → coder generates.
    /// </summary>
    public class CodeAnalyzer
    {
        // ---- Tool paths (auto-detected) ----
        public static class DotnetPaths
        {
            public static string DotnetExe { get; private set; } = FindExe("dotnet.exe", @"C:\Program Files\dotnet\dotnet.exe");
            public static string CscExe { get; private set; } = FindExe("csc.exe", @"C:\Program Files\dotnet\sdk\10.0.300\Roslyn\bincore\csc.exe");
            public static string VbcExe { get; private set; } = FindExe("vbc.exe", @"C:\Program Files\dotnet\sdk\10.0.300\Roslyn\bincore\vbc.exe");
            public static string VbcsCompiler { get; private set; } = @"C:\Program Files\dotnet\sdk\10.0.300\Roslyn\bincore\VBCSCompiler.exe";
            public static string DotnetFormat { get; private set; } = @"C:\Program Files\dotnet\sdk\10.0.300\DotnetTools\dotnet-format\dotnet-format.exe";
            public static string RoslynBuildHost { get; private set; } = @"C:\Program Files\dotnet\sdk\10.0.300\DotnetTools\dotnet-format\BuildHost-net472\Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.exe";
            public static string SdkManifests { get; private set; } = @"C:\Program Files\dotnet\sdk-manifests\10.0.100";
            public static string MicronautCoder { get; private set; } = FindExe("micronaut_coder.exe",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "micronaut_coder.exe"),
                Path.Combine(Directory.GetCurrentDirectory(), "bin", "micronaut_coder.exe"));

            private static string FindExe(string name, params string[] candidates)
            {
                foreach (var c in candidates)
                    if (File.Exists(c)) return c;
                // Try PATH
                try
                {
                    var psi = new ProcessStartInfo("where", name) { RedirectStandardOutput = true, CreateNoWindow = true };
                    using var p = Process.Start(psi);
                    if (p != null)
                    {
                        var output = p.StandardOutput.ReadToEnd().Trim();
                        p.WaitForExit(1000);
                        if (!string.IsNullOrEmpty(output) && File.Exists(output.Split('\n')[0].Trim()))
                            return output.Split('\n')[0].Trim();
                    }
                }
                catch { }
                return null;
            }
        }

        // ---- Result types ----
        public class CodeResult
        {
            public bool Success { get; set; }
            public string Output { get; set; }
            public string Errors { get; set; }
            public long ElapsedMs { get; set; }
            public string Tool { get; set; }
            public string Capability { get; set; }
            public bool Admitted { get; set; }
        }

        public class AstInfo
        {
            public string Language { get; set; }
            public int Lines { get; set; }
            public int Classes { get; set; }
            public int Methods { get; set; }
            public int Usings { get; set; }
            public List<string> Namespaces { get; set; } = new();
            public List<string> Diagnostics { get; set; } = new();
            public string Summary { get; set; }
        }

        public class AgentRoute
        {
            public string Intent { get; set; }
            public string Model { get; set; }
            public string Tool { get; set; }
            public string Description { get; set; }
            public string Capability { get; set; }
            public bool RequiresAdmission { get; set; } = true;
        }

        // ---- Agent routing table ----
        public static readonly AgentRoute[] AgentRoutes = new[]
        {
            new AgentRoute { Intent = "code_gen", Tool = "coder",  Capability = "code.generate", Description = "Code generation delegated to micronaut_coder.exe" },
            new AgentRoute { Intent = "compile",  Tool = "csc",    Capability = "code.compile",  Description = "C# compilation through the installed .NET SDK" },
            new AgentRoute { Intent = "format",   Tool = "format", Capability = "code.format",   Description = "Code formatting via dotnet-format" },
            new AgentRoute { Intent = "analysis", Tool = "kast",   Capability = "code.analyze",  Description = "KAST structural code analysis" },
            new AgentRoute { Intent = "inspect",  Tool = "sdk",    Capability = "code.inspect",  Description = "Read-only SDK manifest inspection" }
        };

        // XCFE/K'UHUL is the routing/admission authority. This lookup only resolves
        // a capability that has already been selected by the control plane.
        public AgentRoute ResolveCapability(string intent)
        {
            if (string.IsNullOrWhiteSpace(intent)) return null;
            return AgentRoutes.FirstOrDefault(r =>
                string.Equals(r.Intent, intent, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Capability, intent, StringComparison.OrdinalIgnoreCase));
        }

        // ---- Compile C# code ----
        public CodeResult CompileCSharp(
            string sourceCode,
            string outputPath = null,
            bool admitted = false)
        {
            var sw = Stopwatch.StartNew();

            if (!admitted)
                return Denied("code.compile", "Compilation requires XCFE admission", sw);

            if (string.IsNullOrWhiteSpace(sourceCode))
                return Failed("code.compile", "Source code is empty", sw, "dotnet");

            if (string.IsNullOrWhiteSpace(DotnetPaths.DotnetExe) ||
                !File.Exists(DotnetPaths.DotnetExe))
                return Failed("code.compile", "dotnet.exe not found", sw, "dotnet");

            var projDir = Path.Combine(Path.GetTempPath(), $"nnck_compile_{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(projDir);

                var sourcePath = Path.Combine(projDir, "Source.cs");
                var projectPath = Path.Combine(projDir, "Compile.csproj");
                var buildDir = Path.Combine(projDir, "out");

                File.WriteAllText(sourcePath, sourceCode);
                File.WriteAllText(projectPath,
                    "<Project Sdk=\"Microsoft.NET.Sdk\">" +
                    "<PropertyGroup>" +
                    "<TargetFramework>net10.0</TargetFramework>" +
                    "<OutputType>Library</OutputType>" +
                    "<ImplicitUsings>enable</ImplicitUsings>" +
                    "<Nullable>disable</Nullable>" +
                    "<Deterministic>true</Deterministic>" +
                    "</PropertyGroup>" +
                    "</Project>");

                var result = RunProcess(
                    DotnetPaths.DotnetExe,
                    $"build \"{projectPath}\" -c Release -o \"{buildDir}\" --nologo",
                    30000);

                if (result.ExitCode != 0)
                    return Failed("code.compile", result.Stderr + result.Stdout, sw, "dotnet build");

                var built = Directory.GetFiles(buildDir, "*.dll")
                    .FirstOrDefault(f => !Path.GetFileName(f).StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrWhiteSpace(built))
                    return Failed("code.compile", "Build succeeded but no output assembly was found", sw, "dotnet build");

                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    var dir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                    File.Copy(built, outputPath, true);
                    built = outputPath;
                }

                return new CodeResult
                {
                    Success = true,
                    Output = $"Compiled to: {built}",
                    Errors = result.Stderr,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Tool = "dotnet build",
                    Capability = "code.compile",
                    Admitted = true
                };
            }
            catch (Exception ex)
            {
                return Failed("code.compile", ex.Message, sw, "dotnet build");
            }
            finally
            {
                TryDeleteDirectory(projDir);
            }
        }

        // ---- Format code ----
        public CodeResult FormatCode(string sourceCode, string language = "csharp", bool admitted = false)
        {
            var sw = Stopwatch.StartNew();
            if (!admitted)
                return Denied("code.format", "Formatting requires XCFE admission", sw);
            try
            {
                if (File.Exists(DotnetPaths.DotnetFormat))
                {
                    var tmpFile = Path.Combine(Path.GetTempPath(), $"nnck_fmt_{Guid.NewGuid():N}.cs");
                    File.WriteAllText(tmpFile, sourceCode);

                    var result = RunProcess(DotnetPaths.DotnetFormat, $"\"{tmpFile}\" --severity info", 10000);
                    if (result.ExitCode == 0)
                        sourceCode = File.ReadAllText(tmpFile);

                    return new CodeResult
                    {
                        Success = true,
                        Output = sourceCode,
                        Errors = result.Stderr,
                        ElapsedMs = sw.ElapsedMilliseconds,
                        Tool = "dotnet-format", Capability = "code.format", Admitted = true
                    };
                }

                // Built-in simple formatter
                var formatted = SimpleFormat(sourceCode);
                return new CodeResult
                {
                    Success = true,
                    Output = formatted,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Tool = "simple", Capability = "code.format", Admitted = true
                };
            }
            catch (Exception ex)
            {
                return new CodeResult { Success = false, Errors = ex.Message, ElapsedMs = sw.ElapsedMilliseconds };
            }
        }

        // ---- KAST structural analysis ----
        // Read-only structural projection. This is intentionally not presented as
        // a full Microsoft.CodeAnalysis semantic model unless Roslyn assemblies are loaded.
        public AstInfo AnalyzeCode(string sourceCode, string language = "csharp")
        {
            var info = new AstInfo { Language = language, Lines = sourceCode.Split('\n').Length };

            if (language == "csharp")
            {
                info.Classes = Regex.Matches(sourceCode, @"\b(class|struct|record|interface)\s+\w+").Count;
                info.Methods = Regex.Matches(
                    sourceCode,
                    @"\b(?:public|private|protected|internal|static|virtual|override|async|sealed|new|partial|extern|unsafe|\s)+\s*[\w<>,\[\]?.]+\s+\w+\s*\(").Count;
                info.Usings = Regex.Matches(sourceCode, @"^using\s+[\w.]+;", RegexOptions.Multiline).Count;

                foreach (Match m in Regex.Matches(sourceCode, @"^using\s+([\w.]+);", RegexOptions.Multiline))
                    info.Namespaces.Add(m.Groups[1].Value);

                info.Diagnostics.Add($"Classes: {info.Classes}, Methods: {info.Methods}, Usings: {info.Usings}");

                // Basic diagnostics
                if (info.Methods > 20) info.Diagnostics.Add("High method count — consider refactoring");
                if (sourceCode.Length > 50000) info.Diagnostics.Add("Large file — >50KB");
                if (!sourceCode.Contains("using System;")) info.Diagnostics.Add("Missing 'using System;'");
                if (Regex.IsMatch(sourceCode, @".{120,}")) info.Diagnostics.Add("Lines exceed 120 chars — needs formatting");

                var nameMatch = Regex.Match(sourceCode, @"\bclass\s+(\w+)");
                info.Diagnostics.Add("Analyzer: KAST structural projection (syntax-oriented, no semantic compilation)");
                info.Summary = nameMatch.Success
                    ? $"KAST C# projection '{nameMatch.Groups[1].Value}' — {info.Lines} lines, {info.Methods} methods"
                    : $"KAST C# projection — {info.Lines} lines";
            }

            return info;
        }

        // ---- Generate code via micronaut_coder.exe ----
        public CodeResult GenerateCode(
            string prompt,
            string language = "csharp",
            bool admitted = false)
        {
            var sw = Stopwatch.StartNew();

            if (!admitted)
                return Denied("code.generate", "Code generation requires XCFE admission", sw);

            try
            {
                if (!string.IsNullOrWhiteSpace(DotnetPaths.MicronautCoder) &&
                    File.Exists(DotnetPaths.MicronautCoder))
                {
                    var safePrompt = (prompt ?? "").Replace("\"", "\\\"");
                    var result = RunProcess(
                        DotnetPaths.MicronautCoder,
                        $"\"{safePrompt}\" --language {language}",
                        60000);

                    return new CodeResult
                    {
                        Success = result.ExitCode == 0,
                        Output = result.Stdout,
                        Errors = result.Stderr,
                        ElapsedMs = sw.ElapsedMilliseconds,
                        Tool = "micronaut_coder.exe",
                        Capability = "code.generate",
                        Admitted = true
                    };
                }

                return Failed(
                    "code.generate",
                    "micronaut_coder.exe is unavailable; no fabricated template fallback was emitted",
                    sw,
                    "micronaut_coder.exe");
            }
            catch (Exception ex)
            {
                return Failed("code.generate", ex.Message, sw, "micronaut_coder.exe");
            }
        }

        // ---- Run dotnet commands ----
        public CodeResult RunDotnetCommand(
            string args,
            int timeoutMs = 30000,
            bool admitted = false)
        {
            var sw = Stopwatch.StartNew();

            if (!admitted)
                return Denied("code.dotnet", "dotnet command execution requires XCFE admission", sw);

            if (string.IsNullOrWhiteSpace(args))
                return Failed("code.dotnet", "dotnet arguments are empty", sw, "dotnet");

            // CodeAnalyzer is a build/analyze capability, not a general shell.
            var verb = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.ToLowerInvariant();

            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "build", "test", "format", "--info", "--list-sdks", "--list-runtimes"
            };

            if (string.IsNullOrWhiteSpace(verb) || !allowed.Contains(verb))
                return Denied("code.dotnet", $"dotnet verb '{verb}' is not admitted by CodeAnalyzer", sw);

            try
            {
                if (string.IsNullOrWhiteSpace(DotnetPaths.DotnetExe) ||
                    !File.Exists(DotnetPaths.DotnetExe))
                    return Failed("code.dotnet", "dotnet.exe not found", sw, "dotnet");

                var result = RunProcess(DotnetPaths.DotnetExe, args, timeoutMs);
                return new CodeResult
                {
                    Success = result.ExitCode == 0,
                    Output = result.Stdout,
                    Errors = result.Stderr,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Tool = "dotnet",
                    Capability = "code.dotnet",
                    Admitted = true
                };
            }
            catch (Exception ex)
            {
                return Failed("code.dotnet", ex.Message, sw, "dotnet");
            }
        }

        // ---- Micro-agent routing ----
        [Obsolete("XCFE/K'UHUL owns semantic routing. Use ResolveCapability after admission.")]
        public AgentRoute RouteIntent(string intent)
        {
            return ResolveCapability(intent);
        }

        // ---- Helpers ----

        private static CodeResult Denied(string capability, string error, Stopwatch sw)
        {
            return new CodeResult
            {
                Success = false,
                Errors = error,
                ElapsedMs = sw.ElapsedMilliseconds,
                Tool = "XCFE",
                Capability = capability,
                Admitted = false
            };
        }

        private static CodeResult Failed(
            string capability,
            string error,
            Stopwatch sw,
            string tool)
        {
            return new CodeResult
            {
                Success = false,
                Errors = error ?? "",
                ElapsedMs = sw.ElapsedMilliseconds,
                Tool = tool,
                Capability = capability,
                Admitted = true
            };
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch { }
        }

        private static ProcessResult RunProcess(string exe, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var p = new Process { StartInfo = psi };
                p.Start();

                var stdout = new StringBuilder();
                var stderr = new StringBuilder();

                p.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                if (p.WaitForExit(timeoutMs))
                {
                    p.WaitForExit(); // drain buffers
                    return new ProcessResult
                    {
                        ExitCode = p.ExitCode,
                        Stdout = stdout.ToString(),
                        Stderr = stderr.ToString()
                    };
                }

                p.Kill();
                return new ProcessResult { ExitCode = -1, Stderr = $"Process timed out after {timeoutMs}ms" };
            }
            catch (Exception ex)
            {
                return new ProcessResult { ExitCode = -1, Stderr = ex.Message };
            }
        }

        private class ProcessResult
        {
            public int ExitCode { get; set; }
            public string Stdout { get; set; } = "";
            public string Stderr { get; set; } = "";
        }

        private static string SimpleFormat(string code)
        {
            var lines = code.Split('\n');
            var result = new StringBuilder();
            int indent = 0;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("}")) indent = Math.Max(0, indent - 1);
                if (trimmed.Length > 0)
                    result.AppendLine(new string(' ', indent * 4) + trimmed);
                else
                    result.AppendLine();
                if (trimmed.EndsWith("{")) indent++;
            }
            return result.ToString();
        }

        private static string GenerateCSharpTemplate(string prompt)
        {
            var className = "GeneratedClass";
            var nameMatch = Regex.Match(prompt, @"\b(class|struct|interface)\s+(\w+)");
            if (nameMatch.Success) className = nameMatch.Groups[2].Value;

            return $$"""
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Generated
{
    /// <summary>
    /// Auto-generated by micronaut_coder: {{prompt}}
    /// </summary>
    public class {{className}}
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Generated by NNC-K Code Analyzer");
            {{(prompt.Contains("async", StringComparison.OrdinalIgnoreCase) ? "await RunAsync(args);" : "Run(args);")}}
        }

        {{(prompt.Contains("async", StringComparison.OrdinalIgnoreCase) ? "private static async Task RunAsync(string[] args)\n        {" : "private static void Run(string[] args)\n        {")}}
            // TODO: Implement {{className}} logic
            Console.WriteLine("Hello from {{className}}!");
        }
    }
}
""";
        }

        private static string GeneratePowerShellTemplate(string prompt)
        {
            return $@"# Auto-generated by micronaut_coder: {prompt}
# ============================================================================
function Invoke-Task {{
    param([string[]]$Args)
    Write-Host ""Hello from generated PowerShell function""
}}

# Main
Invoke-Task -Args $args
";
        }

        // ---- SDK manifest inspection ----
        public string[] GetSdkManifests()
        {
            if (Directory.Exists(DotnetPaths.SdkManifests))
                return Directory.GetFiles(DotnetPaths.SdkManifests, "*.json", SearchOption.AllDirectories);
            return Array.Empty<string>();
        }

        public string InspectSdkManifest(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return "File not found";

            try
            {
                var root = Path.GetFullPath(DotnetPaths.SdkManifests)
                    .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var candidate = Path.GetFullPath(path);

                if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return "Denied: path is outside the SDK manifest root";

                return File.ReadAllText(candidate);
            }
            catch
            {
                return "Could not read";
            }
        }
    }
}
