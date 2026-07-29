using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NeuralGrammar.Core
{
#pragma warning disable CS1998, CS4014
    /// <summary>
    /// MCP (Model Context Protocol) Server — hosts tools, resources, prompts
    /// via stdio and HTTP/SSE transports. Connects to local and remote MCP servers.
    /// https://modelcontextprotocol.io/
    /// </summary>
    public class MCPServer : IDisposable
    {
        // ---- MCP Protocol Types ----
        public class MCPRequest
        {
            public string Jsonrpc { get; set; } = "2.0";
            public string Id { get; set; }
            public string Method { get; set; }
            public JsonElement? Params { get; set; }
        }

        public class MCPResponse
        {
            public string Jsonrpc { get; set; } = "2.0";
            public string Id { get; set; }
            public object Result { get; set; }
            public MCPError Error { get; set; }
        }

        public class MCPError
        {
            public int Code { get; set; }
            public string Message { get; set; }
        }

        public class MCPTool
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public object InputSchema { get; set; } = new { type = "object", properties = new Dictionary<string, object>(), required = Array.Empty<string>() };
            public Func<string, Task<string>> Handler { get; set; }
        }

        public class MCPResource
        {
            public string Uri { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string MimeType { get; set; } = "text/plain";
            public Func<Task<string>> Reader { get; set; }
        }

        public class MCPPrompt
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public object Arguments { get; set; }
        }

        // ---- Free / well-known MCP servers ----
        public static readonly Dictionary<string, string> FreeServers = new()
        {
            ["github"] = "https://api.github.com/mcp",           // GitHub MCP
            ["filesystem"] = "local://filesystem",                // Local fs tool
            ["search"] = "local://search",                        // Hybrid search
            ["math"] = "local://math",                            // K'UHUL math engine
            ["tensor"] = "local://tensor",                        // Tensor ops
            ["deepseek"] = "https://api.deepseek.com/mcp",        // DeepSeek MCP
            ["brave-search"] = "https://api.search.brave.com/mcp" // Brave search MCP
        };

        private readonly Dictionary<string, MCPTool> _tools = new();
        private readonly Dictionary<string, MCPResource> _resources = new();
        private readonly Dictionary<string, MCPPrompt> _prompts = new();
        private readonly Dictionary<string, string> _remoteServers = new();
        private readonly Dictionary<string, string> _localEndpoints = new();

        private HttpListener _httpListener;
        private CancellationTokenSource _cts;
        private int _port = 24681;
        private bool _running;

        public MCPServer()
        {
            RegisterDefaultMCPTools();
            RegisterDefaultResources();
            RegisterDefaultPrompts();
            RegisterFreeRemoteServers();
        }

        public int Port => _port;
        public bool IsRunning => _running;
        public IReadOnlyDictionary<string, MCPTool> Tools => _tools;
        public IReadOnlyDictionary<string, MCPResource> Resources => _resources;

        // ---- Registration ----

        public void RegisterTool(MCPTool tool)
        {
            _tools[tool.Name] = tool;
        }

        public void RegisterResource(MCPResource resource)
        {
            _resources[resource.Uri] = resource;
        }

        public void RegisterPrompt(MCPPrompt prompt)
        {
            _prompts[prompt.Name] = prompt;
        }

        public void RegisterRemoteServer(string name, string url)
        {
            _remoteServers[name] = url;
        }

        public void SetLocalEndpoint(string name, string endpoint)
        {
            _localEndpoints[name] = endpoint;
        }

        // ---- Default tools (wire to existing C# backends) ----

        private void RegisterDefaultMCPTools()
        {
            RegisterTool(new MCPTool
            {
                Name = "read_file",
                Description = "Read a file from disk (up to 10KB)",
                InputSchema = new { type = "object", properties = new { path = new { type = "string" } }, required = new[] { "path" } },
                Handler = async (args) =>
                {
                    var p = JsonSerializer.Deserialize<Dictionary<string, string>>(args);
                    var path = p?.GetValueOrDefault("path", "");
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "Error: file not found";
                    var text = await File.ReadAllTextAsync(path);
                    return text.Length > 10000 ? text.Substring(0, 10000) + "\n... (truncated)" : text;
                }
            });

            RegisterTool(new MCPTool
            {
                Name = "write_file",
                Description = "Write content to a file",
                InputSchema = new { type = "object", properties = new { path = new { type = "string" }, content = new { type = "string" } }, required = new[] { "path", "content" } },
                Handler = async (args) =>
                {
                    var p = JsonSerializer.Deserialize<Dictionary<string, string>>(args);
                    if (p == null || !p.ContainsKey("path") || !p.ContainsKey("content")) return "Error: missing path or content";
                    await File.WriteAllTextAsync(p["path"], p["content"]);
                    return $"Written to {p["path"]}";
                }
            });

            RegisterTool(new MCPTool
            {
                Name = "list_dir",
                Description = "List files in a directory",
                InputSchema = new { type = "object", properties = new { path = new { type = "string" } } },
                Handler = async (args) =>
                {
                    var p = JsonSerializer.Deserialize<Dictionary<string, string>>(args);
                    var path = p?.GetValueOrDefault("path", ".");
                    if (!Directory.Exists(path)) return "Error: directory not found";
                    var entries = Directory.GetFileSystemEntries(path).Take(50).ToList();
                    return string.Join("\n", entries);
                }
            });

            RegisterTool(new MCPTool
            {
                Name = "calculate",
                Description = "Evaluate a math expression using K'UHUL Math Engine",
                InputSchema = new { type = "object", properties = new { expression = new { type = "string" } }, required = new[] { "expression" } },
                Handler = async (args) =>
                {
                    try
                    {
                        var p = JsonSerializer.Deserialize<Dictionary<string, string>>(args);
                        var expr = p?.GetValueOrDefault("expression", "");
                        var engine = new Kuhul.KuhulMathEngine();
                        var result = engine.Execute(expr);
                        return result.Success
                            ? $"{expr} = {result.Value:F6}"
                            : $"Error: {result.Error}";
                    }
                    catch (Exception ex) { return $"Error: {ex.Message}"; }
                }
            });

            RegisterTool(new MCPTool
            {
                Name = "search",
                Description = "Search indexed documents using hybrid search",
                InputSchema = new { type = "object", properties = new { query = new { type = "string" }, max_results = new { type = "number" } }, required = new[] { "query" } },
                Handler = async (args) =>
                {
                    try
                    {
                        var p = JsonSerializer.Deserialize<Dictionary<string, string>>(args);
                        var query = p?.GetValueOrDefault("query", "");
                        var engine = new HybridSearch();
                        var config = new HybridSearchConfig { MaxResults = 5 };
                        var result = engine.Search(query, config);
                        var sb = new StringBuilder();
                        sb.AppendLine($"Found {result.TotalMatches} matches for '{query}':");
                        foreach (var r in result.Results)
                            sb.AppendLine($"  [{r.DocId}] Score: {r.Score:F3} - {r.Explanation?.Preview?.Substring(0, Math.Min(80, r.Explanation?.Preview?.Length ?? 0))}");
                        return sb.ToString();
                    }
                    catch (Exception ex) { return $"Error: {ex.Message}"; }
                }
            });

            RegisterTool(new MCPTool
            {
                Name = "code_completion",
                Description = "Get code completion suggestions (delegates to model backend)",
                InputSchema = new { type = "object", properties = new { code = new { type = "string" }, language = new { type = "string" } }, required = new[] { "code" } },
                Handler = (args) => Task.FromResult("Tool: code_completion would delegate to local/cloud model")
            });

            RegisterTool(new MCPTool
            {
                Name = "create_plan",
                Description = "Create a gravity well task plan across fold phases",
                InputSchema = new { type = "object", properties = new { goal = new { type = "string" } }, required = new[] { "goal" } },
                Handler = async (args) =>
                {
                    try
                    {
                        var p = JsonSerializer.Deserialize<Dictionary<string, string>>(args);
                        var goal = p?.GetValueOrDefault("goal", "default goal");
                        var planner = new GravityWellPlanner();
                        var plan = planner.AutoPlan(goal, new[] { "mcp", "tool" });
                        var sb = new StringBuilder();
                        sb.AppendLine($"Plan '{goal}' created: {plan.Id}");
                        sb.AppendLine($"Phases: {plan.CurrentFold}");
                        foreach (var t in plan.Tasks)
                            sb.AppendLine($"  [{t.Fold}] {t.Description} (priority: {t.Priority})");
                        return sb.ToString();
                    }
                    catch (Exception ex) { return $"Error: {ex.Message}"; }
                }
            });

            RegisterTool(new MCPTool
            {
                Name = "generate_code",
                Description = "Generate code through the admitted micronaut_coder capability",
                InputSchema = new { type = "object", properties = new { prompt = new { type = "string" }, language = new { type = "string" } }, required = new[] { "prompt" } },
                Handler = async (args) =>
                {
                    var p = JsonSerializer.Deserialize<Dictionary<string, string>>(args);
                    var analyzer = new CodeAnalyzer();
                    var result = analyzer.GenerateCode(
                        p?.GetValueOrDefault("prompt", "") ?? "",
                        p?.GetValueOrDefault("language", "csharp") ?? "csharp",
                        admitted: true);
                    return result.Success ? result.Output : $"Error: {result.Errors}";
                }
            });

            RegisterTool(new MCPTool
            {
                Name = "compile_code",
                Description = "Compile C# source code through the admitted .NET SDK build capability",
                InputSchema = new { type = "object", properties = new { code = new { type = "string" } }, required = new[] { "code" } },
                Handler = async (args) =>
                {
                    var p = JsonSerializer.Deserialize<Dictionary<string, string>>(args);
                    var analyzer = new CodeAnalyzer();
                    var result = analyzer.CompileCSharp(
                        p?.GetValueOrDefault("code", "") ?? "",
                        admitted: true);
                    return result.Success ? result.Output : $"Error: {result.Errors}";
                }
            });

            RegisterTool(new MCPTool
            {
                Name = "format_code",
                Description = "Format C# source code using dotnet-format or built-in formatter",
                InputSchema = new { type = "object", properties = new { code = new { type = "string" } }, required = new[] { "code" } },
                Handler = async (args) =>
                {
                    var p = JsonSerializer.Deserialize<Dictionary<string, string>>(args);
                    var analyzer = new CodeAnalyzer();
                    var result = analyzer.FormatCode(
                        p?.GetValueOrDefault("code", "") ?? "",
                        admitted: true);
                    return result.Success ? result.Output : $"Error: {result.Errors}";
                }
            });

            RegisterTool(new MCPTool
            {
                Name = "analyze_code",
                Description = "Analyze C# source code structure — classes, methods, usings, diagnostics",
                InputSchema = new { type = "object", properties = new { code = new { type = "string" } }, required = new[] { "code" } },
                Handler = async (args) =>
                {
                    var p = JsonSerializer.Deserialize<Dictionary<string, string>>(args);
                    var analyzer = new CodeAnalyzer();
                    var info = analyzer.AnalyzeCode(p?.GetValueOrDefault("code", "") ?? "");
                    return JsonSerializer.Serialize(info);
                }
            });

            RegisterTool(new MCPTool
            {
                Name = "run_dotnet",
                Description = "Run an admitted dotnet CLI operation (build, test, format, SDK/runtime info)",
                InputSchema = new { type = "object", properties = new { args = new { type = "string" } }, required = new[] { "args" } },
                Handler = async (args) =>
                {
                    var p = JsonSerializer.Deserialize<Dictionary<string, string>>(args);
                    var analyzer = new CodeAnalyzer();
                    var result = analyzer.RunDotnetCommand(
                        p?.GetValueOrDefault("args", "") ?? "",
                        admitted: true);
                    return result.Success ? result.Output : $"Error: {result.Errors}";
                }
            });

            RegisterTool(new MCPTool
            {
                Name = "route_intent",
                Description = "Resolve an already-selected code intent to a CodeAnalyzer capability; XCFE/K'UHUL remains the semantic router",
                InputSchema = new { type = "object", properties = new { intent = new { type = "string" } }, required = new[] { "intent" } },
                Handler = async (args) =>
                {
                    var p = JsonSerializer.Deserialize<Dictionary<string, string>>(args);
                    var analyzer = new CodeAnalyzer();
                    var intent = p?.GetValueOrDefault("intent", "") ?? "";
                    var route = analyzer.ResolveCapability(intent);
                    return route != null
                        ? JsonSerializer.Serialize(route)
                        : JsonSerializer.Serialize(new
                        {
                            admitted = false,
                            intent,
                            reason = "No admitted code capability resolved. Semantic routing belongs to XCFE/K'UHUL."
                        });
                }
            });

            RegisterTool(new MCPTool
            {
                Name = "get_server_info",
                Description = "Get information about the MCP server and available tools",
                InputSchema = new { type = "object", properties = new Dictionary<string, object>() },
                Handler = async (args) =>
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"MCP Server v1.0 (port {_port})");
                    sb.AppendLine($"Tools: {string.Join(", ", _tools.Keys)}");
                    sb.AppendLine($"Resources: {string.Join(", ", _resources.Keys)}");
                    sb.AppendLine($"Remote servers: {string.Join(", ", _remoteServers.Keys)}");
                    return sb.ToString();
                }
            });
        }

        private void RegisterDefaultResources()
        {
            RegisterResource(new MCPResource
            {
                Uri = "file://system/schemas",
                Name = "Schema Files",
                Description = "List of available XSD and JSON schemas",
                Reader = () =>
                {
                    var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "schemas");
                    if (!Directory.Exists(path)) return Task.FromResult("No schemas directory");
                    var files = Directory.GetFiles(path, "*.xsd").Concat(Directory.GetFiles(path, "*.json"));
                    return Task.FromResult(string.Join("\n", files.Select(f => Path.GetFileName(f))));
                }
            });
        }

        private void RegisterDefaultPrompts()
        {
            RegisterPrompt(new MCPPrompt
            {
                Name = "analyze",
                Description = "Analyze a file or code snippet using available tools",
                Arguments = new { type = "object", properties = new { target = new { type = "string" }, type = new { type = "string", @enum = new[] { "code", "data", "text" } } } }
            });
            RegisterPrompt(new MCPPrompt
            {
                Name = "plan",
                Description = "Create an execution plan across fold phases",
                Arguments = new { type = "object", properties = new { goal = new { type = "string" } } }
            });
        }

        private void RegisterFreeRemoteServers()
        {
            foreach (var kv in FreeServers)
                _remoteServers[kv.Key] = kv.Value;
        }

        // ---- HTTP Server (transport) ----

        public void Start(int port = 24681)
        {
            _port = port;
            _cts = new CancellationTokenSource();
            _httpListener = new HttpListener();
            // _httpListener.Prefixes.Add($"http://+:{_port}/mcp/");
            _httpListener.Prefixes.Add($"http://127.0.0.1:{_port}/mcp/");
            _httpListener.Start();
            _running = true;

            Task.Run(() => ListenLoop(_cts.Token));
        }

        public void Stop()
        {
            _running = false;
            _cts?.Cancel();
            _httpListener?.Stop();
        }

        private async Task ListenLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _httpListener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(ctx));
                }
                catch { break; }
            }
        }

        private async Task HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                var request = ctx.Request;
                var response = ctx.Response;

                if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/mcp/health")
                {
                    await WriteJson(response, new { status = "ok", tools = _tools.Count, resources = _resources.Count, remote = _remoteServers.Count });
                    return;
                }

                if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/mcp/tools")
                {
                    var toolList = _tools.Values.Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        inputSchema = t.InputSchema
                    }).ToList();
                    await WriteJson(response, new { tools = toolList });
                    return;
                }

                if (request.HttpMethod == "POST" && request.Url.AbsolutePath == "/mcp/call")
                {
                    using var reader = new StreamReader(request.InputStream);
                    var body = await reader.ReadToEndAsync();
                    var mcpReq = JsonSerializer.Deserialize<MCPRequest>(body);

                    var result = await HandleMCPRequest(mcpReq);
                    var respJson = JsonSerializer.Serialize(result);
                    var buffer = Encoding.UTF8.GetBytes(respJson);
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    return;
                }

                // SSE endpoint for streaming
                if (request.HttpMethod == "GET" && request.Url.AbsolutePath == "/mcp/sse")
                {
                    response.ContentType = "text/event-stream";
                    response.Headers.Add("Cache-Control", "no-cache");
                    response.Headers.Add("Connection", "keep-alive");

                    var initData = Encoding.UTF8.GetBytes($"data: {JsonSerializer.Serialize(new { type = "connected", tools = _tools.Keys })}\n\n");
                    await response.OutputStream.WriteAsync(initData, 0, initData.Length);
                    await response.OutputStream.FlushAsync();

                    // Keep connection open
                    await Task.Delay(-1, _cts.Token);
                    return;
                }

                // Default: list
                var list = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                {
                    name = "NNC-K MCP Server",
                    version = "1.0",
                    endpoints = new { tools = "/mcp/tools", call = "/mcp/call (POST)", health = "/mcp/health", sse = "/mcp/sse" }
                }));
                response.ContentType = "application/json";
                response.ContentLength64 = list.Length;
                await response.OutputStream.WriteAsync(list, 0, list.Length);
            }
            catch (Exception ex)
            {
                try
                {
                    var err = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = ex.Message }));
                    ctx.Response.ContentType = "application/json";
                    ctx.Response.StatusCode = 500;
                    await ctx.Response.OutputStream.WriteAsync(err, 0, err.Length);
                }
                catch { }
            }
            finally
            {
                try { ctx.Response.OutputStream.Close(); } catch { }
            }
        }

        // ---- JSON-RPC handler ----

        public async Task<string> HandleMCPRequest(MCPRequest req)
        {
            if (req == null)
                return JsonSerializer.Serialize(new MCPResponse { Error = new MCPError { Code = -32700, Message = "Parse error" } });

            try
            {
                switch (req.Method)
                {
                    case "tools/list":
                        var toolList = _tools.Values.Select(t => new { name = t.Name, description = t.Description, inputSchema = t.InputSchema });
                        return JsonSerializer.Serialize(new MCPResponse { Id = req.Id, Result = new { tools = toolList } });

                    case "tools/call":
                        var toolName = req.Params?.GetProperty("name").GetString();
                        var arguments = req.Params?.GetProperty("arguments").GetRawText();
                        if (string.IsNullOrEmpty(toolName) || !_tools.ContainsKey(toolName))
                            return JsonSerializer.Serialize(new MCPResponse { Id = req.Id, Error = new MCPError { Code = -32601, Message = $"Tool not found: {toolName}" } });
                        var output = await _tools[toolName].Handler(arguments);
                        return JsonSerializer.Serialize(new MCPResponse { Id = req.Id, Result = new { content = new[] { new { type = "text", text = output } } } });

                    case "resources/list":
                        var resourceList = _resources.Values.Select(r => new { uri = r.Uri, name = r.Name, description = r.Description, mimeType = r.MimeType });
                        return JsonSerializer.Serialize(new MCPResponse { Id = req.Id, Result = new { resources = resourceList } });

                    case "resources/read":
                        var uri = req.Params?.GetProperty("uri").GetString();
                        if (string.IsNullOrEmpty(uri) || !_resources.ContainsKey(uri))
                            return JsonSerializer.Serialize(new MCPResponse { Id = req.Id, Error = new MCPError { Code = -32601, Message = $"Resource not found: {uri}" } });
                        var text = await _resources[uri].Reader();
                        return JsonSerializer.Serialize(new MCPResponse { Id = req.Id, Result = new { contents = new[] { new { uri = uri, mimeType = _resources[uri].MimeType, text = text } } } });

                    case "prompts/list":
                        var promptList = _prompts.Values.Select(p => new { name = p.Name, description = p.Description, arguments = p.Arguments });
                        return JsonSerializer.Serialize(new MCPResponse { Id = req.Id, Result = new { prompts = promptList } });

                    case "server/info":
                        return JsonSerializer.Serialize(new MCPResponse
                        {
                            Id = req.Id,
                            Result = new
                            {
                                name = "NNC-K MCP Server",
                                version = "1.0",
                                tools = _tools.Count,
                                resources = _resources.Count,
                                remoteServers = _remoteServers.Keys
                            }
                        });

                    default:
                        return JsonSerializer.Serialize(new MCPResponse { Id = req.Id, Error = new MCPError { Code = -32601, Message = $"Method not found: {req.Method}" } });
                }
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new MCPResponse { Id = req.Id, Error = new MCPError { Code = -32603, Message = $"Internal error: {ex.Message}" } });
            }
        }

        // ---- Remote MCP server proxy ----

        public async Task<MCPResponse> CallRemoteServer(string serverName, MCPRequest req)
        {
            if (!_remoteServers.TryGetValue(serverName, out var url))
                return new MCPResponse { Error = new MCPError { Code = -32601, Message = $"Remote server not found: {serverName}" } };

            try
            {
                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                var json = JsonSerializer.Serialize(req);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var resp = await client.PostAsync(url.TrimEnd('/') + "/mcp/call", content);
                if (!resp.IsSuccessStatusCode) return new MCPResponse { Error = new MCPError { Code = (int)resp.StatusCode, Message = $"Remote server error: {resp.StatusCode}" } };
                var body = await resp.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<MCPResponse>(body);
            }
            catch (Exception ex)
            {
                return new MCPResponse { Error = new MCPError { Code = -31000, Message = $"Remote call failed: {ex.Message}" } };
            }
        }

        // ---- Helpers ----

        private async Task WriteJson(HttpListenerResponse response, object obj)
        {
            var json = JsonSerializer.Serialize(obj);
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }

        public void Dispose()
        {
            Stop();
            _httpListener?.Close();
            _cts?.Dispose();
        }
    }
#pragma warning restore CS1998
}
