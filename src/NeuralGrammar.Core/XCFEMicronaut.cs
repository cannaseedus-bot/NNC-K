#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NeuralGrammar.Core.XCFE
{
    /// <summary>
    /// Micronaut Runtime — factory pipeline, worker host dispatch, route binding.
    /// Matches asx-runtime-micronaut.manifest.json
    /// </summary>
    public class MicronautRuntime
    {
        private readonly string _dataRoot;
        private readonly string _factoryPath;
        private readonly string _workerPath;
        private readonly string _httpWorkerPath;
        private readonly Dictionary<string, MicronautRoute> _routes = new();
        private readonly Dictionary<string, MicronautInstance> _installed = new();
        public MicronautRegister Register { get; set; }

        public MicronautRuntime(string dataRoot = "")
        {
            // Default to a project-relative, writable directory. PowerShell's
            // AppDomain base directory is the install folder (not writable),
            // so never rely on it for runtime data.
            _dataRoot = string.IsNullOrWhiteSpace(dataRoot)
                ? Path.Combine(Directory.GetCurrentDirectory(), ".learning", "micronauts")
                : dataRoot;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Prefer published / flat layouts, then fall back to subproject build outputs.
            _factoryPath = ResolveWorkerPath(baseDir, "micronaut_factory.exe",
                Path.Combine(Directory.GetCurrentDirectory(), "bin", "micronaut-factory", "Release", "micronaut_factory.exe"));

            _workerPath = ResolveWorkerPath(baseDir, "Micronaut.Worker.Host.exe",
                Path.Combine(Directory.GetCurrentDirectory(), "bin", "dotnet-workers", "Workers", "Micronaut.Worker.Host.exe"));

            _httpWorkerPath = ResolveWorkerPath(baseDir, "Micronaut.Worker.Host.Http.exe",
                Path.Combine(Directory.GetCurrentDirectory(), "bin", "dotnet-workers", "HttpWorker", "bin", "Release", "net9.0", "Micronaut.Worker.Host.Http.exe"));

            // Register default routes
            RegisterRoute("micronaut_factory",             "POST", "/api/micronaut/factory",             "proxy_contract");
            RegisterRoute("micronaut_worker_run",           "POST", "/api/micronaut/worker/run",          "proxy_contract");
            RegisterRoute("micronaut_http_run",             "POST", "/api/micronaut/http/run",            "proxy_contract");
            RegisterRoute("micronaut_factory_pack_scxq2",   "POST", "/api/micronaut/factory/pack-scxq2",  "requires_policy");
            RegisterRoute("micronaut_factory_bind_bson",    "POST", "/api/micronaut/factory/bind-bson",    "requires_policy");

            EnsureDataRoot();
        }

        private static string ResolveWorkerPath(string baseDir, string fileName, string fallback)
        {
            var cwd = Directory.GetCurrentDirectory();
            var roots = new[] { cwd, baseDir };
            var candidates = new List<string>();

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                var dir = root;
                for (int i = 0; i < 6; i++)
                {
                    candidates.Add(Path.Combine(dir, "bin", fileName));
                    candidates.Add(Path.Combine(dir, fileName));
                    candidates.Add(Path.Combine(dir, "bin", "dotnet-workers", "Workers", fileName));
                    candidates.Add(Path.Combine(dir, "bin", "dotnet-workers", "HttpWorker", "bin", "Release", "net9.0", fileName));
                    var parent = Directory.GetParent(dir)?.FullName;
                    if (string.IsNullOrWhiteSpace(parent) || parent == dir) break;
                    dir = parent;
                }
            }

            candidates.Add(fallback);
            return candidates.FirstOrDefault(File.Exists) ?? fallback;
        }

        // ---- Routes ----

        public class MicronautRoute
        {
            public string Name { get; set; }
            public string Method { get; set; }
            public string Path { get; set; }
            public string Status { get; set; }
            public string[] Body { get; set; }
        }

        public void RegisterRoute(string name, string method, string path, string status)
        {
            _routes[name] = new MicronautRoute
            {
                Name = name,
                Method = method,
                Path = path,
                Status = status
            };
        }

        public MicronautRoute GetRoute(string name) =>
            _routes.TryGetValue(name, out var r) ? r : null;

        public IReadOnlyDictionary<string, MicronautRoute> Routes => _routes;

        // ---- Factory Pipeline ----

        public class FactoryResult
        {
            public bool Success { get; set; }
            public string OutputPath { get; set; }
            public string ManifestPath { get; set; }
            public string Error { get; set; }
            public List<FactoryStep> Steps { get; set; } = new();
        }

        public class FactoryStep
        {
            public string Id { get; set; }
            public string Phase { get; set; }
            public string Status { get; set; } // "completed", "failed", "skipped"
            public string Output { get; set; }
        }

        /// <summary>Run the full factory pipeline: create -> validate -> pack -> bind</summary>
        public FactoryResult FactoryRun(string manifestJson, string glyph = null, string lane = null)
        {
            var result = new FactoryResult();

            try
            {
                // Step 1: Factory create (Sek/generate)
                var step1 = new FactoryStep { Id = "factory_create", Phase = "Sek" };
                var micronaut = CreateFromManifest(manifestJson);
                var micronautPath = Path.Combine(_dataRoot, $"{micronaut.Id}.micronaut.json");
                File.WriteAllText(micronautPath, JsonSerializer.Serialize(micronaut, new JsonSerializerOptions { WriteIndented = true }));
                step1.Status = "completed";
                step1.Output = micronautPath;
                result.Steps.Add(step1);

                // Step 2: Validate manifest (Yax/validate)
                var step2 = new FactoryStep { Id = "validate_manifest", Phase = "Yax" };
                var validation = ValidateManifest(micronaut);
                if (!validation.IsValid)
                {
                    step2.Status = "failed";
                    step2.Output = validation.Error;
                    result.Error = validation.Error;
                    result.Steps.Add(step2);
                    return result;
                }
                step2.Status = "completed";
                step2.Output = "admitted";
                result.Steps.Add(step2);

                // Step 3: SCXQ2 pack (Xul/storage)
                var step3 = new FactoryStep { Id = "scxq2_pack", Phase = "Xul" };
                var scxq2 = PackSCXQ2(micronaut, glyph ?? micronaut.Id, lane ?? "default");
                var scxq2Path = Path.Combine(_dataRoot, $"{micronaut.Id}.scxq2.json");
                File.WriteAllText(scxq2Path, JsonSerializer.Serialize(scxq2, new JsonSerializerOptions { WriteIndented = true }));
                step3.Status = "completed";
                step3.Output = scxq2Path;
                result.Steps.Add(step3);

                // Step 4: BSON bind (Xul/storage)
                var step4 = new FactoryStep { Id = "bson_bind", Phase = "Xul" };
                var bson = BindBSON(scxq2, glyph ?? micronaut.Id, lane ?? "default");
                var bsonPath = Path.Combine(_dataRoot, $"{micronaut.Id}.bson.micronaut.json");
                File.WriteAllText(bsonPath, JsonSerializer.Serialize(bson, new JsonSerializerOptions { WriteIndented = true }));
                step4.Status = "completed";
                step4.Output = bsonPath;
                result.Steps.Add(step4);

                result.Success = true;
                result.OutputPath = bsonPath;
                result.ManifestPath = micronautPath;

                _installed[micronaut.Id] = new MicronautInstance
                {
                    Id = micronaut.Id,
                    ManifestPath = micronautPath,
                    BSONPath = bsonPath,
                    Installed = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>Create factory output from a manifest spec</summary>
        public MicronautSpec CreateFromManifest(string manifestJson)
        {
            var spec = JsonSerializer.Deserialize<MicronautSpec>(manifestJson);
            spec.CreatedAt = DateTime.UtcNow;
            spec.Hash = ComputeHash(manifestJson);
            return spec;
        }

        /// <summary>Validate a micronaut manifest</summary>
        public ValidationResult ValidateManifest(MicronautSpec spec)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(spec.Id))
                errors.Add("Micronaut ID is required");
            if (string.IsNullOrWhiteSpace(spec.Role))
                errors.Add("Micronaut role is required");
            if (spec.Opcodes == null || spec.Opcodes.Count == 0)
                errors.Add("At least one opcode is required");

            // Validate opcodes have known verbs
            if (spec.Opcodes != null)
            {
                foreach (var op in spec.Opcodes)
                {
                    if (!XCFEStdlib.IsKnown(op.Value.Verb))
                        errors.Add($"Unknown verb '{op.Value.Verb}' in opcode '{op.Key}'");
                }
            }

            return new ValidationResult
            {
                IsValid = errors.Count == 0,
                Error = errors.Count > 0 ? string.Join("; ", errors) : null
            };
        }

        /// <summary>Pack into SCXQ2 envelope</summary>
        public SCXQ2Envelope PackSCXQ2(MicronautSpec spec, string glyph, string lane)
        {
            var runtimeBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(spec.Opcodes));
            var semanticKey = $"{glyph}+{lane}+{Convert.ToBase64String(runtimeBytes)}";

            return new SCXQ2Envelope
            {
                Schema = "scxq2://envelope/v1",
                Glyph = glyph,
                Lane = lane,
                MicronautId = spec.Id,
                SemanticKey = semanticKey,
                BodyHash = ComputeHash(semanticKey),
                Timestamp = DateTime.UtcNow,
                Body = spec
            };
        }

        /// <summary>Bind SCXQ2 envelope into BSON format</summary>
        public BSONMicronaut BindBSON(SCXQ2Envelope envelope, string glyph, string lane)
        {
            var checksum = ComputeChecksum(JsonSerializer.Serialize(envelope));

            return new BSONMicronaut
            {
                Schema = "bson://micronaut/v1",
                Glyph = glyph,
                Lane = lane,
                SemanticKey = envelope.SemanticKey,
                Checksum = checksum,
                Timestamp = DateTime.UtcNow,
                Envelope = envelope
            };
        }

        // ---- Worker Dispatch ----

        public class WorkerResult
        {
            public bool Success { get; set; }
            public string Output { get; set; }
            public string Error { get; set; }
            public string Transport { get; set; }
        }

        /// <summary>Dispatch a job to the real console worker over stdin/stdout.</summary>
        public WorkerResult RunWorker(string jobJson, string manifestJson)
        {
            if (!WorkerAvailable)
                return new WorkerResult { Transport = "stdio", Success = false, Error = $"Worker binary not found: {_workerPath}" };

            try
            {
                var spec = JsonSerializer.Deserialize<MicronautSpec>(manifestJson);
                if (spec == null)
                    return new WorkerResult { Transport = "stdio", Success = false, Error = "Invalid micronaut manifest" };

                var envelope = JsonSerializer.Serialize(new { job = JsonDocument.Parse(jobJson).RootElement.Clone(), micronaut = spec });
                var psi = new ProcessStartInfo
                {
                    FileName = _workerPath,
                    WorkingDirectory = Path.GetDirectoryName(_workerPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process == null) throw new InvalidOperationException("Failed to start micronaut worker");
                process.StandardInput.WriteLine(envelope);
                process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return new WorkerResult { Transport = "stdio", Success = process.ExitCode == 0, Output = output, Error = process.ExitCode == 0 ? null : error };
            }
            catch (Exception ex)
            {
                return new WorkerResult { Transport = "stdio", Success = false, Error = ex.Message };
            }
        }

        /// <summary>Dispatch a payload to a real HTTP capability endpoint.</summary>
        public WorkerResult RunHttpWorker(string route, string payload)
        {
            try
            {
                if (!Uri.TryCreate(route, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    return new WorkerResult { Transport = "http", Success = false, Error = "Route must be an absolute http/https capability URL" };

                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                using var content = new StringContent(payload ?? "{}", Encoding.UTF8, "application/json");
                using var response = client.PostAsync(uri, content).GetAwaiter().GetResult();
                var output = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                return new WorkerResult
                {
                    Transport = "http",
                    Success = response.IsSuccessStatusCode,
                    Output = output,
                    Error = response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}"
                };
            }
            catch (Exception ex)
            {
                return new WorkerResult { Transport = "http", Success = false, Error = ex.Message };
            }
        }

        /// <summary>Dispatch a job to the best available worker transport.</summary>
        /// <param name="job">Job object; will be serialized to JSON.</param>
        /// <param name="manifest">Optional micronaut manifest; required for stdio transport.</param>
        /// <param name="httpUrl">Optional absolute HTTP endpoint; if provided and the HTTP worker is available, it is tried first.</param>
        public WorkerResult DispatchJob(object job)
        {
            return DispatchJob(job, null, "");
        }

        /// <summary>Dispatch a job to the best available worker transport.</summary>
        /// <param name="job">Job object; will be serialized to JSON.</param>
        /// <param name="manifest">Optional micronaut manifest; required for stdio transport.</param>
        /// <param name="httpUrl">Optional absolute HTTP endpoint; if provided and the HTTP worker is available, it is tried first.</param>
        public WorkerResult DispatchJob(object job, MicronautSpec manifest, string httpUrl)
        {
            var jobJson = job is string s ? s : JsonSerializer.Serialize(job);

            if (!string.IsNullOrWhiteSpace(httpUrl) && HttpWorkerAvailable)
            {
                var httpResult = RunHttpWorker(httpUrl, jobJson);
                if (httpResult.Success) return httpResult;
            }

            if (WorkerAvailable)
            {
                var manifestJson = manifest != null
                    ? JsonSerializer.Serialize(manifest)
                    : JsonSerializer.Serialize(new MicronautSpec { Id = "anonymous", Role = "dispatch" });
                return RunWorker(jobJson, manifestJson);
            }

            return new WorkerResult
            {
                Transport = "none",
                Success = false,
                Error = "No worker transport available (stdio or http)"
            };
        }

        /// <summary>Check if factory binary is available</summary>
        public bool FactoryAvailable => File.Exists(_factoryPath);

        /// <summary>Check if worker binaries are available</summary>
        public bool WorkerAvailable => File.Exists(_workerPath);
        public bool HttpWorkerAvailable => File.Exists(_httpWorkerPath);

        /// <summary>Report which worker transports are available.</summary>
        public WorkerAvailability GetAvailability()
        {
            return new WorkerAvailability
            {
                Factory = FactoryAvailable,
                FactoryPath = _factoryPath,
                StdioWorker = WorkerAvailable,
                StdioWorkerPath = _workerPath,
                HttpWorker = HttpWorkerAvailable,
                HttpWorkerPath = _httpWorkerPath
            };
        }

        // ---- Helpers ----

        private void EnsureDataRoot()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_dataRoot) && !Directory.Exists(_dataRoot))
                    Directory.CreateDirectory(_dataRoot);
            }
            catch
            {
                // Runtime data root is best-effort; callers that need persistence
                // should supply an explicit writable dataRoot.
            }
        }

        private string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return "sha256:" + BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        private string ComputeChecksum(string input)
        {
            // ADLER-32 style checksum for BSON binding
            uint a = 1, b = 0;
            foreach (byte c in Encoding.UTF8.GetBytes(input))
            {
                a = (a + c) % 65521;
                b = (b + a) % 65521;
            }
            return $"adler32:{((b << 16) | a):x8}";
        }
    }

    // ---- Data types ----

    public class MicronautSpec
    {
        public string Id { get; set; }
        public string Role { get; set; }
        public string Phase { get; set; }
        public string Persona { get; set; }
        public Dictionary<string, MicronautOpcode> Opcodes { get; set; } = new();
        public Dictionary<string, string> Skills { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public string Hash { get; set; }
    }

    public class MicronautOpcode
    {
        public string Verb { get; set; }
        public string Description { get; set; }
        public Dictionary<string, string> Params { get; set; } = new();
    }

    public class MicronautInstance
    {
        public string Id { get; set; }
        public string ManifestPath { get; set; }
        public string BSONPath { get; set; }
        public DateTime Installed { get; set; }
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Error { get; set; }
    }

    public class SCXQ2Envelope
    {
        public string Schema { get; set; }
        public string Glyph { get; set; }
        public string Lane { get; set; }
        public string MicronautId { get; set; }
        public string SemanticKey { get; set; }
        public string BodyHash { get; set; }
        public DateTime Timestamp { get; set; }
        public object Body { get; set; }
    }

    public class BSONMicronaut
    {
        public string Schema { get; set; }
        public string Glyph { get; set; }
        public string Lane { get; set; }
        public string SemanticKey { get; set; }
        public string Checksum { get; set; }
        public DateTime Timestamp { get; set; }
        public SCXQ2Envelope Envelope { get; set; }
    }

    public class WorkerAvailability
    {
        public bool Factory { get; set; }
        public string FactoryPath { get; set; }
        public bool StdioWorker { get; set; }
        public string StdioWorkerPath { get; set; }
        public bool HttpWorker { get; set; }
        public string HttpWorkerPath { get; set; }
    }

    internal static class StringExtensions
    {
        public static string Truncate(this string s, int max)
            => s.Length <= max ? s : s.Substring(0, max) + "...";
    }
}
