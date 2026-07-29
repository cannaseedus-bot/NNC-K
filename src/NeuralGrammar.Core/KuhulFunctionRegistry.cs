using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// K'UHUL function registry. Maps effectful function names to sidecar invocations.
    /// Rule: KUHUL calls functions; functions don't call KUHUL.
    /// Authority boundary: functions emit candidate text/structures only; they never
    /// create, update, merge, or promote micronauts. Persistence is owned by MicronautManager.
    /// </summary>
    public class KuhulFunctionRegistry
    {
        private readonly Dictionary<string, Func<JsonArray, JsonNode>> _functions;
        private readonly string _projectRoot;
        private readonly string _binDir;

        public KuhulFunctionRegistry(string projectRoot = null)
        {
            _projectRoot = projectRoot ?? Directory.GetCurrentDirectory();
            _binDir = Path.Combine(_projectRoot, "bin");
            _functions = new Dictionary<string, Func<JsonArray, JsonNode>>(StringComparer.OrdinalIgnoreCase);
            RegisterBuiltins();
        }

        public bool Has(string name) => _functions.ContainsKey(name);

        public JsonNode Call(string name, JsonArray args)
        {
            if (!_functions.TryGetValue(name, out var fn))
                return new JsonObject { ["status"] = "error", ["message"] = $"Unknown K'UHUL function: {name}" };
            try
            {
                return fn(args);
            }
            catch (Exception ex)
            {
                return new JsonObject { ["status"] = "error", ["function"] = name, ["message"] = ex.Message };
            }
        }

        private void RegisterBuiltins()
        {
            _functions["read_file"] = args =>
            {
                string path = ArgString(args, 0);
                if (!File.Exists(path)) return new JsonObject { ["status"] = "error", ["message"] = $"file not found: {path}" };
                return new JsonObject { ["status"] = "success", ["content"] = File.ReadAllText(path, Encoding.UTF8) };
            };

            _functions["write_file"] = args =>
            {
                string path = ArgString(args, 0);
                string content = ArgString(args, 1);
                File.WriteAllText(path, content, Encoding.UTF8);
                return new JsonObject { ["status"] = "success", ["path"] = path, ["bytes"] = content.Length };
            };

            _functions["exec"] = args => RunProcess(ArgString(args, 0), ArgList(args, 1));
            _functions["shell"] = args => RunProcess("cmd.exe", new[] { "/c", ArgString(args, 0) });

            _functions["tool"] = args =>
            {
                string name = ArgString(args, 0);
                var input = ArgMap(args, 1);
                return DispatchTool(name, input);
            };

            _functions["agent"] = args =>
            {
                string name = ArgString(args, 0);
                string prompt = ArgString(args, 1);
                return DispatchAgent(name, prompt);
            };

            _functions["micronaut"] = args =>
            {
                string name = ArgString(args, 0);
                var parameters = ArgMap(args, 1);
                return DispatchMicronautFactory(name, parameters);
            };

            _functions["skill"] = args =>
            {
                string name = ArgString(args, 0);
                JsonNode input = args.Count > 1 ? args[1] : new JsonObject();
                return DispatchSkill(name, input);
            };

            _functions["action"] = args =>
            {
                string name = ArgString(args, 0);
                var parameters = ArgMap(args, 1);
                return new JsonObject { ["status"] = "success", ["action"] = name, ["parameters"] = JsonValue.Create(parameters), ["authority_boundary"] = "candidate_only" };
            };

            _functions["verb"] = args =>
            {
                string verbName = ArgString(args, 0);
                string subject = ArgString(args, 1);
                string obj = ArgString(args, 2);
                return new JsonObject { ["status"] = "success", ["verb"] = verbName, ["subject"] = subject, ["object"] = obj, ["authority_boundary"] = "candidate_only" };
            };

            _functions["bot"] = args =>
            {
                string name = ArgString(args, 0);
                string message = ArgString(args, 1);
                return new JsonObject { ["status"] = "success", ["bot"] = name, ["response"] = $"[{name}] received: {message}", ["authority_boundary"] = "candidate_only" };
            };

            _functions["http"] = args =>
            {
                string method = ArgString(args, 0);
                string url = ArgString(args, 1);
                string body = args.Count > 2 ? ArgString(args, 2) : "";
                return DispatchHttp(method, url, body);
            };
        }

        private static string ArgString(JsonArray args, int index)
        {
            if (index >= args.Count) return string.Empty;
            return args[index]?.ToString() ?? string.Empty;
        }

        private static string[] ArgList(JsonArray args, int index)
        {
            if (index >= args.Count) return Array.Empty<string>();
            if (args[index] is JsonArray arr) return arr.Select(t => t?.ToString() ?? string.Empty).ToArray();
            return new[] { args[index]?.ToString() ?? string.Empty };
        }

        private static Dictionary<string, string> ArgMap(JsonArray args, int index)
        {
            var result = new Dictionary<string, string>();
            if (index >= args.Count) return result;
            if (args[index] is JsonObject obj)
            {
                foreach (var prop in obj)
                    result[prop.Key] = prop.Value?.ToString() ?? string.Empty;
            }
            return result;
        }

        private JsonNode RunProcess(string exe, string[] arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = string.Join(" ", arguments.Select(a => $"\"{a}\"")),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = _projectRoot
            };
            using var proc = Process.Start(psi);
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(60000);
            return new JsonObject { ["status"] = proc.ExitCode == 0 ? "success" : "error", ["exit_code"] = proc.ExitCode, ["stdout"] = stdout, ["stderr"] = stderr };
        }

        private JsonNode DispatchTool(string name, Dictionary<string, string> input)
        {
            string exe = name.ToLowerInvariant() switch
            {
                "hybrid" or "code" => Path.Combine(_binDir, "Quantum", "quantum_hybrid.exe"),
                "grammar" => Path.Combine(_binDir, "Quantum", "quantum_grammar.exe"),
                "research" or "web" => Path.Combine(_binDir, "Quantum", "quantum_trinity.exe"),
                "microagents" => Path.Combine(_binDir, "Quantum", "quantum_microagents.exe"),
                _ => Path.Combine(_binDir, "Quantum", "quantum_hybrid.exe")
            };
            if (!File.Exists(exe)) return new JsonObject { ["status"] = "error", ["message"] = $"tool binary not found: {exe}" };

            var request = new JsonObject();
            foreach (var kv in input) request[kv.Key] = kv.Value;
            request["operation"] = request["operation"]?.ToString() ?? "process";
            return RunJsonRpc(exe, request);
        }

        private JsonNode DispatchAgent(string name, string prompt)
        {
            string exe = Path.Combine(_binDir, "Quantum", "quantum_microagents.exe");
            if (!File.Exists(exe)) return new JsonObject { ["status"] = "error", ["message"] = $"agent binary not found: {exe}" };
            var request = new JsonObject
            {
                ["operation"] = "process",
                ["input"] = prompt,
                ["session_id"] = name,
                ["mode"] = "orchestrated"
            };
            return RunJsonRpc(exe, request);
        }

        private JsonNode DispatchMicronautFactory(string name, Dictionary<string, string> parameters)
        {
            string exe = Path.Combine(_binDir, "micronaut_factory.exe");
            if (!File.Exists(exe)) return new JsonObject { ["status"] = "error", ["message"] = $"factory binary not found: {exe}" };
            var args = new List<string> { "create", name };
            if (parameters.TryGetValue("domain", out var domain) && !string.IsNullOrEmpty(domain)) args.Add(domain);
            return RunProcess(exe, args.ToArray());
        }

        private const long HotSwapMaxBytes = 2L * 1024 * 1024 * 1024;

        private JsonNode DispatchSkill(string name, JsonNode input)
        {
            return name.ToLowerInvariant() switch
            {
                "asx_ram" or "attention" => DispatchAsxRamSkill(input),
                "asx_gemm" or "gemm" => DispatchGemmSkill(input),
                "file_drop" or "file_ingest" => new JsonObject { ["status"] = "success", ["skill"] = name, ["note"] = "use Invoke-FileDropIngest in PowerShell runtime", ["authority_boundary"] = "candidate_only" },
                "semantic_refine" => new JsonObject { ["status"] = "success", ["skill"] = name, ["note"] = "use Refine-Micronaut in PowerShell runtime", ["authority_boundary"] = "candidate_only" },
                _ => new JsonObject { ["status"] = "success", ["skill"] = name, ["input"] = input, ["authority_boundary"] = "candidate_only" }
            };
        }

        private static (bool ok, string lane, long bytes) ClassifyShard(string path)
        {
            if (!File.Exists(path)) return (false, "missing", 0);
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(fs, Encoding.UTF8);
            var magic = reader.ReadBytes(4);
            if (magic[0] != 'X' || magic[1] != 'S' || magic[2] != 'Q' || magic[3] != '2')
                return (false, "bad_magic", 0);
            fs.Position = 36; // dtype offset
            uint dtype = reader.ReadUInt32();
            uint shardClass = reader.ReadUInt32();
            fs.Position = 28; // tile_count offset
            uint tileCount = reader.ReadUInt32();
            uint tileSize = reader.ReadUInt32();
            uint elemBytes = dtype == 0 ? 4u : dtype == 1 ? 2u : 1u;
            long rawTileBytes = (long)tileSize * elemBytes;
            long alignedTileBytes = ((rawTileBytes + 4095) / 4096) * 4096;
            long payloadBytes = (long)tileCount * alignedTileBytes;
            string lane = shardClass switch
            {
                0 => payloadBytes <= HotSwapMaxBytes ? "hot" : "cold_oversized",
                1 => payloadBytes <= HotSwapMaxBytes ? "cold_expert" : "rejected_oversized",
                2 => "cold_load_once",
                _ => "generic"
            };
            return (true, lane, payloadBytes);
        }

        private JsonNode DispatchAsxRamSkill(JsonNode input)
        {
            string q = input?["q_shard"]?.ToString();
            string k = input?["k_shard"]?.ToString();
            string v = input?["v_shard"]?.ToString();
            string cfg = input?["config"]?.ToString() ?? "model_config.json";
            if (string.IsNullOrEmpty(q) || string.IsNullOrEmpty(k) || string.IsNullOrEmpty(v))
                return new JsonObject { ["status"] = "error", ["message"] = "asx_ram skill requires q_shard, k_shard, v_shard" };

            var lanes = new Dictionary<string, string>();
            foreach (var (label, path) in new[] { ("q", q), ("k", k), ("v", v) })
            {
                var (ok, lane, bytes) = ClassifyShard(path);
                if (!ok) return new JsonObject { ["status"] = "error", ["message"] = $"invalid shard {label}: {lane}" };
                if (lane == "rejected_oversized")
                    return new JsonObject { ["status"] = "error", ["message"] = $"{label} shard ({bytes} bytes) exceeds 2GB hot-swap limit", ["lane"] = lane, ["max_bytes"] = HotSwapMaxBytes };
                lanes[label] = lane;
            }

            string exe = Path.Combine(_binDir, "asx_ram_v2.exe");
            if (!File.Exists(exe)) exe = Path.Combine(_binDir, "asx_ram.exe");
            if (!File.Exists(exe)) return new JsonObject { ["status"] = "error", ["message"] = "asx_ram executable not found" };
            var result = RunProcess(exe, new[] { q, k, v, cfg, "1", "--prefetch" });
            var obj = result as JsonObject;
            if (obj != null)
            {
                obj["lanes"] = JsonValue.Create(lanes);
                obj["authority_boundary"] = "compute_only";
            }
            return result;
        }

        private JsonNode DispatchGemmSkill(JsonNode input)
        {
            string shard = input?["shard"]?.ToString();
            string experts = input?["experts"]?.ToString() ?? "0";
            string passes = input?["passes"]?.ToString() ?? "1";
            if (string.IsNullOrEmpty(shard))
                return new JsonObject { ["status"] = "error", ["message"] = "gemm skill requires shard path" };
            var (ok, lane, bytes) = ClassifyShard(shard);
            if (!ok) return new JsonObject { ["status"] = "error", ["message"] = $"invalid shard: {lane}" };
            if (lane == "rejected_oversized")
                return new JsonObject { ["status"] = "error", ["message"] = $"shard ({bytes} bytes) exceeds 2GB hot-swap limit", ["lane"] = lane, ["max_bytes"] = HotSwapMaxBytes };

            string exe = Path.Combine(_binDir, "asx_gemm.exe");
            if (!File.Exists(exe)) exe = Path.Combine(_binDir, "Quantum", "asx_gemm.exe");
            if (!File.Exists(exe)) return new JsonObject { ["status"] = "error", ["message"] = "asx_gemm executable not found" };
            var result = RunProcess(exe, new[] { shard, experts, passes });
            var obj = result as JsonObject;
            if (obj != null)
            {
                obj["lane"] = lane;
                obj["authority_boundary"] = "compute_only";
            }
            return result;
        }

        private JsonNode DispatchHttp(string method, string url, string body)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                HttpResponseMessage resp;
                if (method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    resp = Task.Run(() => client.GetAsync(url)).GetAwaiter().GetResult();
                }
                else
                {
                    var content = new StringContent(body, Encoding.UTF8, "application/json");
                    resp = Task.Run(() => client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = content })).GetAwaiter().GetResult();
                }
                string responseBody = Task.Run(() => resp.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
                return new JsonObject { ["status"] = "success", ["status_code"] = (int)resp.StatusCode, ["body"] = responseBody };
            }
            catch (Exception ex)
            {
                return new JsonObject { ["status"] = "error", ["message"] = ex.Message };
            }
        }

        private JsonNode RunJsonRpc(string exe, JsonObject request)
        {
            string json = request.ToJsonString(new JsonSerializerOptions { PropertyNamingPolicy = null });
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--quiet {json}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = _projectRoot
            };
            using var proc = Process.Start(psi);
            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(60000);
            try
            {
                return JsonNode.Parse(stdout);
            }
            catch
            {
                return new JsonObject { ["status"] = proc.ExitCode == 0 ? "success" : "error", ["exit_code"] = proc.ExitCode, ["stdout"] = stdout, ["stderr"] = stderr };
            }
        }
    }
}
