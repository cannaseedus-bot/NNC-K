using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NeuralGrammar.Core
{
#pragma warning disable CS1998
    /// <summary>
    /// JsonRuntime — Unified runtime that reads all manifests (batches, threads, rpc, server, api)
    /// and orchestrates batch scheduling, thread pools, RPC handling, and fold-phase execution.
    /// Integrates with MCP server, model backends, search, math engine, and gravity well planner.
    /// </summary>
    public class JsonRuntime : IDisposable
    {
        // ---- Manifest data ----
        public class ManifestSet
        {
            public JsonElement Batches { get; set; }
            public JsonElement Threads { get; set; }
            public JsonElement Rpc { get; set; }
            public JsonElement Server { get; set; }
        }

        // ---- Batch system ----
        public class BatchJob
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 12);
            public string Pipeline { get; set; }
            public Dictionary<string, object> Inputs { get; set; } = new();
            public int Priority { get; set; } = 5;
            public string Status { get; set; } = "queued";
            public string Stage { get; set; }
            public double Progress { get; set; }
            public string Error { get; set; }
            public DateTime Created { get; set; } = DateTime.UtcNow;
            public List<BatchStageResult> StageResults { get; set; } = new();
        }

        public class BatchStageResult
        {
            public string StageId { get; set; }
            public string Phase { get; set; }
            public string Status { get; set; }
            public long ElapsedMs { get; set; }
            public string Output { get; set; }
        }

        // ---- Thread pool ----
        public class ThreadPoolStats
        {
            public int Active { get; set; }
            public int Idle { get; set; }
            public int Queued { get; set; }
            public long Completed { get; set; }
            public int PoolSize { get; set; }
        }

        // ---- RPC ----
        public class RpcRequest
        {
            public string Method { get; set; }
            public string Id { get; set; }
            public JsonElement? Params { get; set; }
        }

        public class RpcResponse
        {
            public string Id { get; set; }
            public object Result { get; set; }
            public RpcError Error { get; set; }
        }

        public class RpcError
        {
            public int Code { get; set; }
            public string Message { get; set; }
        }

        // ---- Backend references ----
        private readonly ModelBackend _model;
        private readonly HybridSearch _search;
        private readonly MCPServer _mcp;
        private readonly Kuhul.KuhulMathEngine _math;
        private readonly GravityWellPlanner _planner;

        // ---- Resident K'UHUL / XCFE kernel ----
        // One runtime instance survives across RPC calls so fold state, replay,
        // micronauts, and paged semantic memory are not recreated per prompt.
        private readonly XCFERuntime _xcfe;
        private readonly string _runtimeRoot;
        private readonly string _learningRoot;
        private readonly string _micronautMemoryRoot;
        private readonly object _memoryGate = new();
        private int _mountedMemoryCount;

        // ---- State ----
        private readonly ManifestSet _manifests;
        private readonly ConcurrentQueue<BatchJob> _batchQueue = new();
        private readonly ConcurrentBag<BatchJob> _completedBatches = new();
        private readonly ConcurrentDictionary<string, BatchJob> _batchIndex = new();
        private readonly Dictionary<string, Func<RpcRequest, Task<object>>> _rpcHandlers = new();
        private readonly Dictionary<string, Func<string, CancellationToken, Task<object>>> _methodCache = new();

        private HttpListener _rpcListener;
        private CancellationTokenSource _cts;
        private int _rpcPort = 24682;
        private long _completedTasks;
        private bool _running;

        public JsonRuntime(ModelBackend model, HybridSearch search, MCPServer mcp,
            Kuhul.KuhulMathEngine math, GravityWellPlanner planner)
        {
            _model = model;
            _search = search;
            _mcp = mcp;
            _math = math;
            _planner = planner;

            _runtimeRoot = ResolveRuntimeRoot();
            _learningRoot = Path.Combine(_runtimeRoot, ".learning");
            _micronautMemoryRoot = Path.Combine(_learningRoot, "micronauts");

            _xcfe = new XCFERuntime();

            _manifests = LoadManifests();
            MountPersistentMemory();
            RegisterRpcHandlers();
        }

        public ManifestSet Manifests => _manifests;
        public bool IsRunning => _running;
        public int RpcPort => _rpcPort;
        public int MountedMemoryCount => _mountedMemoryCount;
        public string RuntimeRoot => _runtimeRoot;
        public ThreadPoolStats PoolStats => new()
        {
            Active = _batchQueue.Count,
            Idle = Math.Max(0, 4 - _batchQueue.Count),
            Queued = _batchQueue.Count,
            Completed = Interlocked.Read(ref _completedTasks),
            PoolSize = 8
        };

        // ---- Persistent semantic memory ---------------------------------------
        private static string ResolveRuntimeRoot()
        {
            // Prefer the working directory used to launch the runtime. Fall back
            // to the assembly base directory when hosted elsewhere.
            var cwd = Directory.GetCurrentDirectory();
            if (Directory.Exists(Path.Combine(cwd, "src")) ||
                Directory.Exists(Path.Combine(cwd, ".learning")))
                return cwd;

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        /// <summary>
        /// Mount durable semantic objects into XCFE's resident micronaut node.
        /// Disk remains the persistent plane; XCFE receives the active index.
        /// </summary>
        public int MountPersistentMemory()
        {
            lock (_memoryGate)
            {
                if (!Directory.Exists(_micronautMemoryRoot))
                {
                    _mountedMemoryCount = 0;
                    return 0;
                }

                var mounted = 0;
                var known = new HashSet<string>(
                    _xcfe.NodeQuery("micronauts")
                        .Select(x => x.TryGetValue("_source_path", out var p) ? p?.ToString() : null)
                        .Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var path in Directory.EnumerateFiles(
                    _micronautMemoryRoot, "*.json", SearchOption.TopDirectoryOnly))
                {
                    if (known.Contains(path))
                        continue;

                    try
                    {
                        var json = File.ReadAllText(path);
                        using var doc = JsonDocument.Parse(json);

                        var entry = JsonElementToDictionary(doc.RootElement);
                        entry["_source_path"] = path;
                        entry["_memory_kind"] = "micronaut";
                        entry["_mounted_at"] = DateTimeOffset.UtcNow.ToString("O");

                        if (!entry.ContainsKey("id"))
                            entry["id"] = Path.GetFileNameWithoutExtension(path);

                        _xcfe.NodeInsert("micronauts", entry);
                        known.Add(path);
                        mounted++;
                    }
                    catch
                    {
                        // A malformed memory object must not prevent the resident
                        // kernel from mounting the rest of the memory plane.
                    }
                }

                _mountedMemoryCount = _xcfe.NodeQuery("micronauts").Count;
                return mounted;
            }
        }

        /// <summary>
        /// Persist an admitted semantic object atomically, then mount it into the
        /// resident XCFE working index. Xul is the intended caller/admission edge.
        /// </summary>
        public string CommitPersistentMemory(
            string id,
            Dictionary<string, object> memory)
        {
            if (string.IsNullOrWhiteSpace(id))
                id = Guid.NewGuid().ToString("N").Substring(0, 12);

            Directory.CreateDirectory(_micronautMemoryRoot);

            var safeId = new string(id
                .ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-')
                .ToArray())
                .Trim('-');

            if (string.IsNullOrWhiteSpace(safeId))
                safeId = Guid.NewGuid().ToString("N").Substring(0, 12);

            memory ??= new Dictionary<string, object>();
            memory["id"] = safeId;
            memory["updated"] = DateTimeOffset.UtcNow.ToString("O");

            var finalPath = Path.Combine(_micronautMemoryRoot, safeId + ".json");
            var tempPath = finalPath + ".tmp";

            var json = JsonSerializer.Serialize(memory, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(tempPath, json, Encoding.UTF8);
            File.Move(tempPath, finalPath, true);

            var mounted = new Dictionary<string, object>(memory)
            {
                ["_source_path"] = finalPath,
                ["_memory_kind"] = "micronaut",
                ["_mounted_at"] = DateTimeOffset.UtcNow.ToString("O")
            };

            _xcfe.NodeInsert("micronauts", mounted);
            _mountedMemoryCount = _xcfe.NodeQuery("micronauts").Count;

            return finalPath;
        }

        private static Dictionary<string, object> JsonElementToDictionary(JsonElement element)
        {
            var result = new Dictionary<string, object>(
                StringComparer.OrdinalIgnoreCase);

            if (element.ValueKind != JsonValueKind.Object)
            {
                result["value"] = JsonElementToObject(element);
                return result;
            }

            foreach (var property in element.EnumerateObject())
                result[property.Name] = JsonElementToObject(property.Value);

            return result;
        }

        private static object JsonElementToObject(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.Object => element.EnumerateObject()
                    .ToDictionary(
                        p => p.Name,
                        p => JsonElementToObject(p.Value),
                        StringComparer.OrdinalIgnoreCase),
                JsonValueKind.Array => element.EnumerateArray()
                    .Select(JsonElementToObject)
                    .ToList(),
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number when element.TryGetInt64(out var i) => i,
                JsonValueKind.Number when element.TryGetDouble(out var d) => d,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.ToString()
            };
        }

        // ---- Manifest loading ----
        private static ManifestSet LoadManifests()
        {
            var ms = new ManifestSet();
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            foreach (var kv in new[] {
                ("batches", "batches.manifest.json"),
                ("threads", "threads.manifest.json"),
                ("rpc", "rpc.manifest.json"),
                ("server", "server.manifest.json")
            })
            {
                var path = Path.Combine(baseDir, kv.Item2);
                if (!File.Exists(path))
                    path = Path.Combine(Directory.GetCurrentDirectory(), kv.Item2);
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(text);
                    var prop = kv.Item1;
                    if (doc.RootElement.TryGetProperty(prop, out var el))
                    {
                        typeof(ManifestSet).GetProperty(char.ToUpper(prop[0]) + prop.Substring(1))
                            ?.SetValue(ms, el);
                    }
                }
            }
            return ms;
        }

        // ---- Start runtime ----
        public void Start(int rpcPort = 24682)
        {
            _rpcPort = rpcPort;
            _cts = new CancellationTokenSource();
            _running = true;

            // Start RPC HTTP listener
            _rpcListener = new HttpListener();
            _rpcListener.Prefixes.Add($"http://127.0.0.1:{_rpcPort}/rpc/");
            _rpcListener.Start();

            Task.Run(() => RpcListenLoop(_cts.Token));
            Task.Run(() => BatchProcessorLoop(_cts.Token));
        }

        public void Stop()
        {
            _running = false;
            _cts?.Cancel();
            _rpcListener?.Stop();
        }

        // ---- RPC HTTP listener ----
        private async Task RpcListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _rpcListener.GetContextAsync();
                    _ = Task.Run(() => HandleRpcRequest(ctx));
                }
                catch { break; }
            }
        }

        private async Task HandleRpcRequest(HttpListenerContext ctx)
        {
            try
            {
                using var reader = new StreamReader(ctx.Request.InputStream);
                var body = await reader.ReadToEndAsync();
                var req = JsonSerializer.Deserialize<RpcRequest>(body);
                var resp = await ExecuteRpc(req);

                var json = JsonSerializer.Serialize(resp);
                var buf = Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = buf.Length;
                await ctx.Response.OutputStream.WriteAsync(buf, 0, buf.Length);
            }
            catch (Exception ex)
            {
                var err = JsonSerializer.Serialize(new RpcResponse { Error = new RpcError { Code = -32603, Message = ex.Message } });
                var buf = Encoding.UTF8.GetBytes(err);
                ctx.Response.ContentType = "application/json";
                ctx.Response.StatusCode = 500;
                await ctx.Response.OutputStream.WriteAsync(buf, 0, buf.Length);
            }
            finally { ctx.Response.OutputStream.Close(); }
        }

        // ---- RPC handler registry ----
        private void RegisterRpcHandlers()
        {
            _rpcHandlers["system.ping"] = async (req) => new { pong = true, timestamp = DateTime.UtcNow.ToString("o") };
            _rpcHandlers["system.status"] = async (req) => new
            {
                status = "running",
                uptime = 0,
                models = 0,
                threads = PoolStats.PoolSize,
                queued = PoolStats.Queued,
                persistent_memory = _mountedMemoryCount,
                runtime_root = _runtimeRoot
            };
            _rpcHandlers["system.shutdown"] = async (req) => { _ = Task.Run(() => { Thread.Sleep(500); Stop(); }); return new { status = "shutting_down" }; };
            _rpcHandlers["model.chat"] = async (req) => await HandleModelChat(req);
            _rpcHandlers["model.configure"] = async (req) => HandleModelConfigure(req);
            _rpcHandlers["model.list"] = async (req) => new { models = new[] { new { name = "auto", type = "auto" } } };
            _rpcHandlers["search.execute"] = async (req) => HandleSearch(req);
            _rpcHandlers["math.evaluate"] = async (req) => HandleMath(req);
            _rpcHandlers["planner.create"] = async (req) => HandlePlannerCreate(req);

            // Native semantic route. Python/model layers consume this contract;
            // they do not own K'UHUL fold selection.
            _rpcHandlers["xcfe.route"] = async (req) => HandleXcfeRoute(req);
            _rpcHandlers["memory.status"] = async (req) => new
            {
                mounted = _mountedMemoryCount,
                root = _micronautMemoryRoot
            };
            _rpcHandlers["memory.reload"] = async (req) => new
            {
                added = MountPersistentMemory(),
                mounted = _mountedMemoryCount
            };

            _rpcHandlers["planner.status"] = async (req) => new
            {
                plan_id = "",
                goal = "",
                current_fold = "Pop",
                completed = 0,
                total = 0,
                memory_mounted = _mountedMemoryCount
            };
            _rpcHandlers["batch.submit"] = async (req) => HandleBatchSubmit(req);
            _rpcHandlers["batch.status"] = async (req) => HandleBatchStatus(req);
            _rpcHandlers["mcp.start"] = async (req) => { _mcp.Start(24681); return new { success = true, port = 24681 }; };
            _rpcHandlers["mcp.stop"] = async (req) => { _mcp.Stop(); return new { success = true }; };
            _rpcHandlers["mcp.call"] = async (req) => await HandleMcpCall(req);
            _rpcHandlers["thread.status"] = async (req) => PoolStats;
        }

        private async Task<RpcResponse> ExecuteRpc(RpcRequest req)
        {
            if (req == null)
                return new RpcResponse { Error = new RpcError { Code = -32700, Message = "Parse error" } };

            if (string.IsNullOrEmpty(req.Method))
                return new RpcResponse { Id = req.Id, Error = new RpcError { Code = -32600, Message = "No method" } };

            if (!_rpcHandlers.ContainsKey(req.Method))
                return new RpcResponse { Id = req.Id, Error = new RpcError { Code = -32601, Message = $"Method not found: {req.Method}" } };

            try
            {
                var result = await _rpcHandlers[req.Method](req);
                return new RpcResponse { Id = req.Id, Result = result };
            }
            catch (Exception ex)
            {
                return new RpcResponse { Id = req.Id, Error = new RpcError { Code = -32603, Message = ex.Message } };
            }
        }

        // ---- RPC method implementations ----

        private object HandleXcfeRoute(RpcRequest req)
        {
            var text = GetParam(req, "text", "");
            var memoryLimit = GetParam(req, "memory_limit", 4);

            if (string.IsNullOrWhiteSpace(text))
                return new { success = false, error = "Empty text" };

            // Pick up memory files created by prior turns/processes without
            // reconstructing the XCFE runtime.
            MountPersistentMemory();

            var result = _xcfe.RouteTurn(text, memoryLimit);

            return new
            {
                success = result.Success,
                routed = result.Routed,
                error = result.Error,
                intent = result.Intent,
                brain = result.Brain,
                fold = result.Fold,
                confidence = result.Confidence,
                fallback = result.Fallback,
                fallback_reason = result.FallbackReason,
                matched_ngrams = result.MatchedNgrams,
                tools = result.Tools,
                memories = result.Memories,
                requirements = result.Requirements,
                fold_trace = result.FoldTrace,
                persistent_memory_mounted = _mountedMemoryCount
            };
        }

        private async Task<object> HandleModelChat(RpcRequest req)
        {
            var text = GetParam(req, "text", "");
            if (string.IsNullOrEmpty(text)) return new { success = false, error = "Empty text" };

            var chatReq = new ModelBackend.ChatRequest();
            chatReq.Messages.Add(new ModelBackend.ChatMessage { Role = "user", Content = text });
            chatReq.MaxTokens = 256;

            var result = await _model.ChatAsync(chatReq);
            if (result.Success)
                return new { success = true, response = result.Content, backend = result.Backend.ToString(), tokens = result.Tokens };
            return new { success = false, error = result.Error };
        }

        private object HandleModelConfigure(RpcRequest req)
        {
            var key = GetParam(req, "deepseek_key", "");
            var ep = GetParam(req, "llama_endpoint", "");
            var mode = GetParam(req, "model_mode", "");

            if (!string.IsNullOrEmpty(key) && key != "sk-...") _model.SetDeepSeekKey(key);
            if (!string.IsNullOrEmpty(ep)) _model.SetLlamaEndpoint(ep);

            return new { success = true, mode = string.IsNullOrEmpty(mode) ? "auto" : mode };
        }

        private object HandleSearch(RpcRequest req)
        {
            var query = GetParam(req, "query", "");
            var maxResults = GetParam(req, "max_results", 5);

            var config = new HybridSearchConfig { MaxResults = maxResults };
            var result = _search.Search(query, config);

            return new
            {
                query,
                total = result.TotalMatches,
                results = result.Results.Select(r => new { doc_id = r.DocId, score = r.Score, preview = r.Explanation?.Preview ?? "" })
            };
        }

        private object HandleMath(RpcRequest req)
        {
            var expr = GetParam(req, "expression", "");
            var result = _math.Execute(expr);
            if (result.Success)
                return new { success = true, value = result.Value, js = result.JsExpression };
            return new { success = false, error = result.Error };
        }

        private object HandlePlannerCreate(RpcRequest req)
        {
            var goal = GetParam(req, "goal", "default");
            var plan = _planner.AutoPlan(goal, new[] { "auto" });
            return new
            {
                plan_id = plan.Id,
                goal = plan.Goal,
                current_fold = plan.CurrentFold,
                tasks = plan.Tasks.Select(t => new { id = t.Id, description = t.Description, fold = t.Fold, priority = t.Priority, completed = t.Completed })
            };
        }

        private object HandleBatchSubmit(RpcRequest req)
        {
            var pipeline = GetParam(req, "pipeline", "default");
            var priority = GetParam(req, "priority", 5);

            var job = new BatchJob { Pipeline = pipeline, Priority = priority };
            var text = GetParam(req, "text", "");
            if (!string.IsNullOrWhiteSpace(text))
                job.Inputs["text"] = text;
            _batchQueue.Enqueue(job);
            _batchIndex[job.Id] = job;

            return new { batch_id = job.Id, status = "queued", queued = true };
        }

        private object HandleBatchStatus(RpcRequest req)
        {
            var batchId = GetParam(req, "batch_id", "");
            if (string.IsNullOrEmpty(batchId) || !_batchIndex.TryGetValue(batchId, out var job))
                return new { error = "Batch not found" };

            return new
            {
                batch_id = job.Id,
                pipeline = job.Pipeline,
                stage = job.Stage ?? "queued",
                progress = job.Progress,
                status = job.Status,
                error = job.Error ?? ""
            };
        }

        private async Task<object> HandleMcpCall(RpcRequest req)
        {
            var tool = GetParam(req, "tool", "");
            if (string.IsNullOrEmpty(tool) || !_mcp.Tools.ContainsKey(tool))
                return new { success = false, error = $"Tool not found: {tool}" };

            var mcpReq = new MCPServer.MCPRequest
            {
                Method = "tools/call",
                Params = JsonSerializer.Deserialize<JsonElement>($"{{\"name\":\"{tool}\",\"arguments\":{{\"tool\":\"{tool}\"}}}}")
            };
            var result = await _mcp.HandleMCPRequest(mcpReq);
            return new { success = true, output = result };
        }

        // ---- Helpers ----
        private string GetParam(RpcRequest req, string key, string def)
        {
            if (req.Params == null) return def;
            try { return req.Params.Value.GetProperty(key).GetString() ?? def; }
            catch { return def; }
        }

        private int GetParam(RpcRequest req, string key, int def)
        {
            if (req.Params == null) return def;
            try { return req.Params.Value.GetProperty(key).GetInt32(); }
            catch { return def; }
        }

        // ---- Batch processor ----
        private async Task BatchProcessorLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_batchQueue.TryDequeue(out var job))
                    {
                        job.Status = "processing";
                        job.Stage = "Pop";

                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        var prompt = job.Inputs.TryGetValue("text", out var inputText)
                            ? inputText?.ToString()
                            : job.Pipeline;

                        if (string.IsNullOrWhiteSpace(prompt))
                            prompt = "batch " + job.Id;

                        MountPersistentMemory();
                        var turn = _xcfe.RouteTurn(prompt, 4);

                        sw.Stop();

                        if (!turn.Success)
                        {
                            job.Status = "failed";
                            job.Error = turn.Error ?? "XCFE turn failed";
                            job.Progress = 1.0;
                        }
                        else
                        {
                            var perFold = turn.FoldTrace.Count > 0
                                ? Math.Max(1L, sw.ElapsedMilliseconds / turn.FoldTrace.Count)
                                : sw.ElapsedMilliseconds;

                            foreach (var fold in turn.FoldTrace)
                            {
                                job.Stage = fold;
                                job.StageResults.Add(new BatchStageResult
                                {
                                    StageId = fold.ToLowerInvariant().Replace("'", ""),
                                    Phase = fold,
                                    Status = "completed",
                                    ElapsedMs = perFold,
                                    Output = fold == "Sek"
                                        ? JsonSerializer.Serialize(turn.Requirements)
                                        : null
                                });
                            }

                            job.Progress = 1.0;
                            job.Status = "completed";
                        }
                        _completedBatches.Add(job);
                        Interlocked.Increment(ref _completedTasks);
                    }
                    else
                    {
                        await Task.Delay(500, ct);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }

        // ---- Load manifests at runtime ----
        public T GetManifestSection<T>(string section, T fallback = default)
        {
            try
            {
                var prop = typeof(ManifestSet).GetProperty(section);
                var elem = (JsonElement?)prop?.GetValue(_manifests);
                if (elem.HasValue) return JsonSerializer.Deserialize<T>(elem.Value.GetRawText());
            }
            catch { }
            return fallback;
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
            _rpcListener?.Close();
        }
    }
#pragma warning restore CS1998
}
