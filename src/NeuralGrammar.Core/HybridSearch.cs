using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// Model Backend — DeepSeek cloud API + llama.cpp GGUF local + tool calling
    /// Provides unified inference, embeddings, and chat completion across backends.
    /// </summary>
    public class ModelBackend
    {
        public enum BackendType { DeepSeek, LlamaCpp, Ollama, Auto }
        public string OllamaEndpoint { get; set; } = "http://127.0.0.1:11434";
        public enum ModelCapability { Chat, Completion, Embedding, Tools, Code }
        public enum ModelTier { Tiny, Light, Base, Boss }

        private BackendType _activeBackend = BackendType.Auto;
        private readonly HttpClient _http = new();
        private string _deepseekKey;
        // Local model selection/inference is owned by XCFE, not by a direct llama.cpp stub.
        private string _llamaEndpoint = "http://127.0.0.1:1235";
        private readonly Dictionary<string, ModelInfo> _models = new();
        private readonly HybridSearch _hybridSearch = new();
        private TaskPlanner _taskPlanner;
        private MicronautNotebook _notebook;
        private SemanticArtifactStore _artifactStore;

        public TaskPlanner TaskPlanner
        {
            get => _taskPlanner;
            set { _taskPlanner = value; }
        }

        public MicronautNotebook Notebook
        {
            get => _notebook;
            set { _notebook = value; }
        }

        public SemanticArtifactStore ArtifactStore
        {
            get => _artifactStore;
            set { _artifactStore = value; }
        }

        public class ModelInfo
        {
            public string Id { get; set; }
            public BackendType Backend { get; set; }
            public ModelTier Tier { get; set; } = ModelTier.Base;
            public ModelCapability[] Capabilities { get; set; }
            public string Path { get; set; }
            public int ContextLength { get; set; } = 4096;
        }

        public int GetModelCount() => _models.Count;
        public string[] GetModelNames() => _models.Keys.ToArray();

        public void RegisterLocalModels()
        {
            _models["lfm-2.5-1.2b"] = new ModelInfo
            {
                Id = "lfm-2.5-1.2b", Backend = BackendType.LlamaCpp,
                Tier = ModelTier.Base,
                Capabilities = new[] { ModelCapability.Chat, ModelCapability.Tools, ModelCapability.Code },
                Path = @"C:\Users\canna\.lmstudio\models\lmstudio-community\LFM2.5-1.2B-Instruct-GGUF\LFM2.5-1.2B-Instruct-Q8_0.gguf",
                ContextLength = 8192
            };
            _models["gpt-oss-20b"] = new ModelInfo
            {
                Id = "gpt-oss-20b", Backend = BackendType.LlamaCpp,
                Tier = ModelTier.Boss,
                Capabilities = new[] { ModelCapability.Chat, ModelCapability.Tools, ModelCapability.Completion },
                Path = @"C:\Users\canna\.lmstudio\models\lmstudio-community\gpt-oss-20b-GGUF\gpt-oss-20b-MXFP4.gguf",
                ContextLength = 16384
            };
            _models["qwen-0.5b"] = new ModelInfo
            {
                Id = "qwen-0.5b", Backend = BackendType.LlamaCpp,
                Tier = ModelTier.Light,
                Capabilities = new[] { ModelCapability.Chat, ModelCapability.Tools },
                Path = @"C:\Users\canna\.lmstudio\models\qwen2.5-0.5b-instruct-q8_0.gguf",
                ContextLength = 32768
            };
            _models["gpt2"] = new ModelInfo
            {
                Id = "gpt2", Backend = BackendType.LlamaCpp,
                Tier = ModelTier.Tiny,
                Capabilities = new[] { ModelCapability.Chat },
                Path = @"C:\Users\canna\.lmstudio\models\gpt2.Q8_0.gguf",
                ContextLength = 1024
            };

            // Ollama cloud models — accessible via ollama serve or ollama cloud proxy.
            // These use the OpenAI-compatible Ollama API at _ollamaEndpoint.
            _models["gemma4:cloud"] = new ModelInfo
            {
                Id = "gemma4:cloud", Backend = BackendType.Ollama,
                Tier = ModelTier.Boss,
                Capabilities = new[] { ModelCapability.Chat, ModelCapability.Tools, ModelCapability.Code },
                ContextLength = 32768
            };
            _models["deepseek-v4-pro:cloud"] = new ModelInfo
            {
                Id = "deepseek-v4-pro:cloud", Backend = BackendType.Ollama,
                Tier = ModelTier.Boss,
                Capabilities = new[] { ModelCapability.Chat, ModelCapability.Tools, ModelCapability.Code },
                ContextLength = 128000
            };
        }

        public class ChatRequest
        {
            public string Model { get; set; }
            public List<ChatMessage> Messages { get; set; } = new();
            public List<ToolDef> Tools { get; set; } = new();
            public double Temperature { get; set; } = 0.7;
            public int MaxTokens { get; set; } = 2048;
            public bool Stream { get; set; }
        }

        public class ChatMessage
        {
            public string Role { get; set; } // "system", "user", "assistant", "tool"
            public string Content { get; set; }
            public List<ToolCall> ToolCalls { get; set; }
            public string ToolCallId { get; set; }
        }

        public class ToolDef
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public object Parameters { get; set; }
        }

        public class ToolCall
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Arguments { get; set; }
        }

        public class ChatResult
        {
            public bool Success { get; set; }
            public string Content { get; set; }
            public List<ToolCall> ToolCalls { get; set; }
            public string Error { get; set; }
            public BackendType Backend { get; set; }
            public long Tokens { get; set; }
            public double Confidence { get; set; } = 0.5;
            public ChatResult EscalatedFrom { get; set; }
            public string ModelUsed { get; set; }
        }

        public class EmbeddingResult
        {
            public bool Success { get; set; }
            public float[] Vector { get; set; }
            public string Error { get; set; }
        }

        public ModelBackend()
        {
            RegisterLocalModels();
        }

        public void SetDeepSeekKey(string key) => _deepseekKey = key;
        // Compatibility name: callers may still use SetLlamaEndpoint, but this is
        // now the XCFE router endpoint.
        public void SetLlamaEndpoint(string url) => _llamaEndpoint = url;
        public void SetXCFEEndpoint(string url) => _llamaEndpoint = url;

        public void RegisterModel(string id, BackendType backend, ModelCapability[] caps, string path = null)
        {
            _models[id] = new ModelInfo { Id = id, Backend = backend, Capabilities = caps, Path = path };
        }

        public BackendType SelectBackend(string modelId = null)
        {
            if (modelId != null && _models.TryGetValue(modelId, out var m)) return m.Backend;
            if (_activeBackend == BackendType.Auto)
            {
                // Prefer DeepSeek if key is set, else try llama
                if (!string.IsNullOrEmpty(_deepseekKey)) return BackendType.DeepSeek;
                return BackendType.LlamaCpp;
            }
            return _activeBackend;
        }

        // ---- Chat Completion ----

        public async Task<ChatResult> ChatAsync(ChatRequest req)
        {
            var backend = SelectBackend(req.Model);
            return backend switch
            {
                BackendType.DeepSeek => await DeepSeekChatAsync(req),
                BackendType.LlamaCpp => await LlamaChatAsync(req),
                BackendType.Ollama => await OllamaChatAsync(req),
                _ => new ChatResult { Error = "No backend available" }
            };
        }

        /// <summary>
        /// Chat with model escalation: BASE (LFM) first, escalate to BOSS (GPT-OSS)
        /// if confidence is below threshold.
        /// </summary>
        public async Task<ChatResult> ChatWithEscalationAsync(
            ChatRequest req,
            double confidenceThreshold = 0.6,
            string baseModel = "lfm-2.5-1.2b",
            string bossModel = "gpt-oss-20b")
        {
            // Step 1: Run BASE model
            req.Model = baseModel;
            var baseResult = await ChatAsync(req);

            if (!baseResult.Success || string.IsNullOrWhiteSpace(baseResult.Content))
                return baseResult;

            // Step 2: Rough confidence estimation
            double confidence = EstimateConfidence(baseResult.Content);
            baseResult.Confidence = confidence;

            // Step 3: If confidence below threshold, escalate to BOSS for review
            if (confidence < confidenceThreshold)
            {
                var reviewReq = new ChatRequest
                {
                    Model = bossModel,
                    Temperature = 0.5,
                    MaxTokens = req.MaxTokens + 512
                };

                // Context: BASE's raw output
                reviewReq.Messages.Add(new ChatMessage
                {
                    Role = "system",
                    Content = "You are a BOSS review model. Review the output below " +
                              "from a BASE worker model. Improve, correct, or expand it " +
                              "as needed. Return ONLY the final improved version, " +
                              "no meta-commentary."
                });
                reviewReq.Messages.Add(new ChatMessage
                {
                    Role = "user",
                    Content = "BASE model output:\n\n" + baseResult.Content
                });

                var bossResult = await DeepSeekChatAsync(reviewReq);
                // Fallback to LlamaCpp if DeepSeek not available
                if (!bossResult.Success)
                    bossResult = await LlamaChatAsync(reviewReq);

                if (bossResult.Success && !string.IsNullOrWhiteSpace(bossResult.Content))
                {
                    bossResult.EscalatedFrom = baseResult;
                    bossResult.Confidence = Math.Min(1.0, confidence + 0.2);
                    return bossResult;
                }
            }

            return baseResult;
        }

        /// <summary>Rough confidence estimate based on output characteristics.</summary>
        private static double EstimateConfidence(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return 0.0;

            double score = 0.5; // default

            // Longer responses tend to be more substantive
            if (content.Length > 500) score += 0.1;
            if (content.Length > 2000) score += 0.05;

            // Presence of hedging reduces confidence
            var hedging = new[] { "i think", "i'm not sure", "maybe", "perhaps", "not sure", "uncertain" };
            int hedgeCount = 0;
            var lower = content.ToLowerInvariant();
            foreach (var h in hedging)
            {
                if (lower.Contains(h)) hedgeCount++;
            }
            score -= hedgeCount * 0.1;

            // Presence of structure increases confidence
            if (content.Contains('\n') && (content.Contains("1.") || content.Contains("- ")))
                score += 0.1;

            return Math.Max(0.1, Math.Min(1.0, score));
        }

        /// <summary>
        /// Chat with automatic multi-turn tool calling.
        /// The model can make tool calls; results are fed back until a text response is produced.
        /// </summary>
        public async Task<ChatResult> ChatWithToolsAsync(ChatRequest req, int maxTurns = 5)
        {
            // Seed tool definitions if not already set.
            if (req.Tools.Count == 0 && req.Model == "deepseek-chat")
            {
                req.Tools.AddRange(DefaultTools);
            }

            var backend = SelectBackend(req.Model);
            var totalTokens = 0L;
            var allToolCalls = new List<ToolCall>();

            for (int turn = 0; turn < maxTurns; turn++)
            {
                ChatResult result = backend switch
                {
                    BackendType.DeepSeek => await DeepSeekChatAsync(req),
                    BackendType.LlamaCpp => await LlamaChatAsync(req),
                    BackendType.Ollama => await OllamaChatAsync(req),
                    _ => new ChatResult { Error = "No backend available" }
                };

                if (!result.Success)
                    return result;

                totalTokens += result.Tokens;

                // Model produced a text response — done.
                if (result.ToolCalls == null || result.ToolCalls.Count == 0)
                {
                    result.Tokens = totalTokens;
                    return result;
                }

                // Model wants to call tools — execute them.
                foreach (var tc in result.ToolCalls)
                {
                    var toolResult = ExecuteTool(tc);
                    allToolCalls.Add(tc);

                    // Append tool result as a new message.
                    req.Messages.Add(new ChatMessage
                    {
                        Role = "tool",
                        Content = toolResult,
                        ToolCallId = tc.Id
                    });
                }

                // Append the assistant message with tool_calls so the model sees context.
                req.Messages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = null,
                    ToolCalls = new List<ToolCall>(result.ToolCalls)
                });
            }

            return new ChatResult
            {
                Success = false,
                Backend = backend,
                Tokens = totalTokens,
                ToolCalls = allToolCalls,
                Error = $"Exceeded max tool-call turns ({maxTurns})"
            };
        }

        private async Task<ChatResult> DeepSeekChatAsync(ChatRequest req)
        {
            try
            {
                var body = new
                {
                    model = req.Model ?? "deepseek-chat",
                    messages = req.Messages.Select(m => new {
                        role = m.Role,
                        content = m.Content,
                        tool_calls = m.ToolCalls?.Select(tc => new {
                            id = tc.Id, type = "function",
                            function = new { name = tc.Name, arguments = tc.Arguments }
                        }),
                        tool_call_id = m.ToolCallId
                    }),
                    tools = req.Tools.Select(t => new {
                        type = "function",
                        function = new { name = t.Name, description = t.Description, parameters = t.Parameters }
                    }),
                    temperature = req.Temperature,
                    max_tokens = req.MaxTokens,
                    stream = false
                };

                var json = JsonSerializer.Serialize(body);
                var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/v1/chat/completions");
                httpReq.Headers.Add("Authorization", $"Bearer {_deepseekKey}");
                httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var resp = await _http.SendAsync(httpReq);
                var respBody = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return new ChatResult { Error = $"DeepSeek API error: {resp.StatusCode} {respBody.Substring(0, Math.Min(200, respBody.Length))}" };

                using var doc = JsonDocument.Parse(respBody);
                var root = doc.RootElement;
                var choice = root.GetProperty("choices")[0];
                var msg = choice.GetProperty("message");
                var content = msg.TryGetProperty("content", out var c) ? c.GetString() : "";

                var toolCalls = new List<ToolCall>();
                if (msg.TryGetProperty("tool_calls", out var tcs))
                {
                    foreach (var tc in tcs.EnumerateArray())
                    {
                        toolCalls.Add(new ToolCall
                        {
                            Id = tc.GetProperty("id").GetString(),
                            Name = tc.GetProperty("function").GetProperty("name").GetString(),
                            Arguments = tc.GetProperty("function").GetProperty("arguments").GetString()
                        });
                    }
                }

                return new ChatResult
                {
                    Success = true,
                    Content = content,
                    ToolCalls = toolCalls,
                    Backend = BackendType.DeepSeek,
                    Tokens = root.GetProperty("usage").GetProperty("total_tokens").GetInt64()
                };
            }
            catch (Exception ex)
            {
                return new ChatResult { Error = $"DeepSeek error: {ex.Message}" };
            }
        }

        // ---- llama.cpp / GGUF Local ----

        public bool IsLlamaRunning()
        {
            try
            {
                var resp = _http.GetAsync($"{_llamaEndpoint}/v1/capabilities").Result;
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        private async Task<ChatResult> LlamaChatAsync(ChatRequest req)
            => await ForwardChatAsync(req, _llamaEndpoint);

        private async Task<ChatResult> OllamaChatAsync(ChatRequest req)
            => await ForwardChatAsync(req, OllamaEndpoint);

        private async Task<ChatResult> ForwardChatAsync(ChatRequest req, string endpoint)
        {
            try
            {
                var body = new
                {
                    model = req.Model,
                    messages = req.Messages.Select(m => new {
                        role = m.Role,
                        content = m.Content,
                        tool_calls = m.ToolCalls?.Select(tc => new {
                            id = tc.Id,
                            type = "function",
                            function = new { name = tc.Name, arguments = tc.Arguments }
                        }),
                        tool_call_id = m.ToolCallId
                    }),
                    tools = req.Tools.Select(t => new {
                        type = "function",
                        function = new {
                            name = t.Name,
                            description = t.Description,
                            parameters = t.Parameters
                        }
                    }),
                    temperature = req.Temperature,
                    max_tokens = req.MaxTokens,
                    stream = false,
                    cache_prompt = true
                };

                var json = JsonSerializer.Serialize(body);
                var httpReq = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/v1/chat/completions");
                httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var resp = await _http.SendAsync(httpReq);
                var respBody = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                    return new ChatResult { Error = $"XCFE error: {resp.StatusCode}" };

                using var doc = JsonDocument.Parse(respBody);
                var root = doc.RootElement;
                var msg = root.GetProperty("choices")[0].GetProperty("message");
                var content = msg.TryGetProperty("content", out var c) ? c.GetString() : "";

                var toolCalls = new List<ToolCall>();
                if (msg.TryGetProperty("tool_calls", out var tcs) &&
                    tcs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tc in tcs.EnumerateArray())
                    {
                        var fn = tc.GetProperty("function");
                        toolCalls.Add(new ToolCall
                        {
                            Id = tc.TryGetProperty("id", out var id) ? id.GetString() : "",
                            Name = fn.GetProperty("name").GetString(),
                            Arguments = fn.TryGetProperty("arguments", out var a) ? a.GetString() : "{}"
                        });
                    }
                }

                return new ChatResult
                {
                    Success = true,
                    Content = content,
                    ToolCalls = toolCalls,
                    Backend = endpoint == OllamaEndpoint ? BackendType.Ollama : BackendType.LlamaCpp,
                    Tokens = root.TryGetProperty("usage", out var u) &&
                             u.TryGetProperty("total_tokens", out var t)
                        ? t.GetInt64() : 0
                };
            }
            catch (Exception ex)
            {
                return new ChatResult { Error = $"XCFE error: {ex.Message}" };
            }
        }

        // ---- Embeddings ----

        public async Task<EmbeddingResult> EmbedAsync(string text, string modelId = null)
        {
            var backend = SelectBackend(modelId);
            try
            {
                if (backend == BackendType.DeepSeek && !string.IsNullOrEmpty(_deepseekKey))
                {
                    var body = new { model = "deepseek-embedding", input = text };
                    var json = JsonSerializer.Serialize(body);
                    var httpReq = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/v1/embeddings");
                    httpReq.Headers.Add("Authorization", $"Bearer {_deepseekKey}");
                    httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    var resp = await _http.SendAsync(httpReq);
                    var respBody = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(respBody);
                    var vec = doc.RootElement.GetProperty("data")[0].GetProperty("embedding");
                    var arr = new List<float>();
                    foreach (var v in vec.EnumerateArray()) arr.Add(v.GetSingle());
                    return new EmbeddingResult { Success = true, Vector = arr.ToArray() };
                }

                // llama.cpp embeddings
                var lBody = new { content = text };
                var lJson = JsonSerializer.Serialize(lBody);
                var lReq = new HttpRequestMessage(HttpMethod.Post, $"{_llamaEndpoint}/v1/embeddings");
                lReq.Content = new StringContent(lJson, Encoding.UTF8, "application/json");
                var lResp = await _http.SendAsync(lReq);
                var lRespBody = await lResp.Content.ReadAsStringAsync();
                using var lDoc = JsonDocument.Parse(lRespBody);
                var lVec = lDoc.RootElement.GetProperty("data")[0].GetProperty("embedding");
                var lArr = new List<float>();
                foreach (var v in lVec.EnumerateArray()) lArr.Add(v.GetSingle());
                return new EmbeddingResult { Success = true, Vector = lArr.ToArray() };
            }
            catch (Exception ex)
            {
                return new EmbeddingResult { Error = ex.Message };
            }
        }

        // ---- Tool Execution ----

        public string ExecuteTool(ToolCall call)
        {
            try
            {
                var args = JsonSerializer.Deserialize<Dictionary<string, string>>(call.Arguments);
                return call.Name switch
                {
                    "read_file" => System.IO.File.Exists(args.GetValueOrDefault("path", ""))
                        ? System.IO.File.ReadAllText(args["path"])
                        : "File not found",
                    "write_file" => TryWriteFile(args),
                    "list_dir" => TryListDir(args),
                    "run_code" => "Tool execution requires an admitted XCFE capability",
                    "search" => ExecuteHybridSearch(args),
                    "plan_task" => ExecutePlanTask(args),
                    "write_note" => ExecuteWriteNote(args),
                    "write_artifact" => ExecuteWriteArtifact(args),
                    _ => $"Unknown tool: {call.Name}"
                };
            }
            catch (Exception ex)
            {
                return $"Tool error: {ex.Message}";
            }
        }

        public void IndexSearchDocument(string docId, string content, NDArray embedding = null)
        {
            _hybridSearch.IndexDocument(docId, content, embedding);
        }

        private string ExecuteHybridSearch(Dictionary<string, string> args)
        {
            var query = args.GetValueOrDefault("query", "");
            if (string.IsNullOrWhiteSpace(query)) return "Missing query";

            var result = _hybridSearch.Search(query);
            return JsonSerializer.Serialize(result);
        }

        private string ExecuteWriteNote(Dictionary<string, string> args)
        {
            var subject = args.GetValueOrDefault("subject", "");
            if (string.IsNullOrWhiteSpace(subject))
                return JsonSerializer.Serialize(new { error = "Missing subject" });

            var content = args.GetValueOrDefault("content", "");
            if (string.IsNullOrWhiteSpace(content))
                return JsonSerializer.Serialize(new { error = "Missing content" });

            var type = args.GetValueOrDefault("type", "note");
            var confStr = args.GetValueOrDefault("confidence", "0.8");
            double.TryParse(confStr, out var confidence);

            if (_notebook == null)
                return JsonSerializer.Serialize(new { error = "Notebook not available" });

            MicronautNotation note;
            switch (type.ToLowerInvariant())
            {
                case "correction":
                    note = _notebook.AddCorrection(subject, content, confidence);
                    break;
                case "improvement":
                    note = _notebook.AddImprovement(subject, content, confidence);
                    break;
                case "research":
                    note = _notebook.AddResearchNote(subject, content, confidence);
                    break;
                default:
                    _notebook.Add(new MicronautNotation
                    {
                        Id = "note_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                        Subject = subject.Trim().ToLowerInvariant(),
                        Type = "note",
                        Content = content,
                        Confidence = Math.Max(0, Math.Min(1, confidence)),
                        Source = "model"
                    });
                    note = _notebook.All.LastOrDefault();
                    break;
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                note_id = note?.Id ?? "",
                subject = subject,
                type = type,
                confidence = confidence,
                timestamp = DateTime.UtcNow
            });
        }

        private string ExecuteWriteArtifact(Dictionary<string, string> args)
        {
            var subject = args.GetValueOrDefault("subject", "");
            if (string.IsNullOrWhiteSpace(subject))
                return JsonSerializer.Serialize(new { error = "Missing subject" });

            var content = args.GetValueOrDefault("content", "");
            if (string.IsNullOrWhiteSpace(content))
                return JsonSerializer.Serialize(new { error = "Missing content" });

            var kindStr = args.GetValueOrDefault("kind", "notation");
            var confStr = args.GetValueOrDefault("confidence", "0.7");
            double.TryParse(confStr, out var confidence);

            if (_artifactStore == null)
                return JsonSerializer.Serialize(new { error = "ArtifactStore not available" });

            var kindMap = new Dictionary<string, ArtifactKind>(StringComparer.OrdinalIgnoreCase)
            {
                ["notation"] = ArtifactKind.Notation,
                ["research"] = ArtifactKind.ResearchNote,
                ["evidence"] = ArtifactKind.Evidence,
                ["hypothesis"] = ArtifactKind.Hypothesis,
                ["correction"] = ArtifactKind.Correction,
                ["improvement"] = ArtifactKind.Improvement
            };

            if (!kindMap.TryGetValue(kindStr, out var kind))
                return JsonSerializer.Serialize(new { error = "Unknown kind: " + kindStr });

            SemanticArtifact artifact;
            switch (kind)
            {
                case ArtifactKind.Hypothesis:
                    artifact = _artifactStore.AddHypothesis(subject, content, confidence);
                    break;
                case ArtifactKind.Correction:
                    artifact = _artifactStore.AddCorrection(subject, content, null, confidence);
                    break;
                case ArtifactKind.Improvement:
                    artifact = _artifactStore.AddImprovement(subject, content, confidence);
                    break;
                case ArtifactKind.ResearchNote:
                    artifact = _artifactStore.AddResearch(subject, content, confidence);
                    break;
                default:
                    artifact = _artifactStore.AddNote(subject, content, confidence);
                    break;
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                artifact_id = artifact.Id,
                subject = subject,
                kind = kindStr,
                status = artifact.Status.ToString(),
                confidence = confidence,
                timestamp = DateTime.UtcNow
            });
        }

        private string ExecutePlanTask(Dictionary<string, string> args)
        {
            var objective = args.GetValueOrDefault("objective", "");
            if (string.IsNullOrWhiteSpace(objective))
                return JsonSerializer.Serialize(new { error = "Missing objective" });

            if (_taskPlanner == null)
                return JsonSerializer.Serialize(new { error = "TaskPlanner not available" });

            var planResult = _taskPlanner.Plan(objective);
            return JsonSerializer.Serialize(new
            {
                status = planResult.Status.ToString(),
                confidence = planResult.Confidence,
                task_count = planResult.Plan?.Tasks?.Count ?? 0,
                tasks = planResult.Plan?.Tasks?.Select(t => new
                {
                    id = t.Id,
                    title = t.Title,
                    phase = t.Phase,
                    priority = t.Priority.ToString(),
                    dependencies = t.Dependencies,
                    effort = t.EstimatedEffort
                }).ToArray(),
                recommendations = planResult.NextRecommendations.Select(r => new
                {
                    skill = r.Skill,
                    rationale = r.Rationale
                }).ToArray(),
                metadata = new
                {
                    task_count = planResult.Metadata?.TaskCount ?? 0,
                    dependency_count = planResult.Metadata?.DependencyCount ?? 0,
                    confidence = planResult.Metadata?.Confidence ?? 0
                }
            });
        }

        private string TryWriteFile(Dictionary<string, string> args)
        {
            try
            {
                if (args.ContainsKey("path") && args.ContainsKey("content"))
                {
                    System.IO.File.WriteAllText(args["path"], args["content"]);
                    return $"Written to {args["path"]}";
                }
                return "Missing path or content";
            }
            catch (Exception ex) { return $"Write error: {ex.Message}"; }
        }

        private string TryListDir(Dictionary<string, string> args)
        {
            var path = args.GetValueOrDefault("path", ".");
            if (!System.IO.Directory.Exists(path)) return "Directory not found";
            var entries = System.IO.Directory.GetFileSystemEntries(path);
            return string.Join("\n", entries.Take(20));
        }

        public static ToolDef[] DefaultTools => new[]
        {
            new ToolDef { Name = "read_file", Description = "Read a file from disk", Parameters = new { type = "object", properties = new { path = new { type = "string" } }, required = new[] { "path" } } },
            new ToolDef { Name = "write_file", Description = "Write content to a file", Parameters = new { type = "object", properties = new { path = new { type = "string" }, content = new { type = "string" } }, required = new[] { "path", "content" } } },
            new ToolDef { Name = "list_dir", Description = "List directory contents", Parameters = new { type = "object", properties = new { path = new { type = "string" } } } },
            new ToolDef { Name = "search", Description = "Search indexed documents", Parameters = new { type = "object", properties = new { query = new { type = "string" } }, required = new[] { "query" } } },
            new ToolDef { Name = "plan_task", Description = "Decompose an objective into a task graph with phases and dependencies. Use this before executing complex multi-step work.", Parameters = new { type = "object", properties = new { objective = new { type = "string" } }, required = new[] { "objective" } } },
            new ToolDef { Name = "write_note", Description = "Write a research note or improvement annotation for a subject. Notes are stored in the MicronautNotebook and are queryable.", Parameters = new { type = "object", properties = new { subject = new { type = "string" }, content = new { type = "string" }, type = new { type = "string", @enum = new[] { "research", "improvement", "correction", "note" } }, confidence = new { type = "number" } }, required = new[] { "subject", "content" } } },
            new ToolDef { Name = "write_artifact", Description = "Propose a semantic artifact (notation, hypothesis, evidence) with admission gating. Artifacts start as Pending and become Admitted when confidence >= threshold.", Parameters = new { type = "object", properties = new { subject = new { type = "string" }, content = new { type = "string" }, kind = new { type = "string", @enum = new[] { "notation", "hypothesis", "evidence", "correction", "improvement", "research" } }, confidence = new { type = "number" }, evidence = new { type = "array", items = new { type = "string" } } }, required = new[] { "subject", "content" } } },
            new ToolDef { Name = "run_code", Description = "Execute Python or JavaScript code", Parameters = new { type = "object", properties = new { code = new { type = "string" }, language = new { type = "string", @enum = new[] { "python" } } }, required = new[] { "code" } } }
        };
    }

    /// <summary>
    /// Gravity Well Task Planner — orchestrates tasks across fold phases
    /// </summary>
    public class GravityWellPlanner
    {
        public class Task
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
            public string Description { get; set; }
            public string Fold { get; set; }
            public double Priority { get; set; } = 0.5;
            public bool Completed { get; set; }
            public string Result { get; set; }
            public DateTime Created { get; set; } = DateTime.UtcNow;
            public List<string> DependsOn { get; set; } = new();
        }

        public class Plan
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
            public string Goal { get; set; }
            public List<Task> Tasks { get; set; } = new();
            public string CurrentFold { get; set; } = "Pop";
            public DateTime Created { get; set; } = DateTime.UtcNow;
        }

        private readonly Dictionary<string, Plan> _plans = new();
        private readonly Dictionary<string, List<Task>> _todoByFold = new();

        public Plan CreatePlan(string goal)
        {
            var plan = new Plan { Goal = goal };
            _plans[plan.Id] = plan;
            return plan;
        }

        public Plan GetPlan(string id) => _plans.GetValueOrDefault(id);

        public Task AddTask(string planId, string description, string fold, double priority = 0.5, string[] dependsOn = null)
        {
            var plan = _plans.GetValueOrDefault(planId);
            if (plan == null) return null;

            var task = new Task
            {
                Description = description,
                Fold = fold,
                Priority = priority,
                DependsOn = dependsOn?.ToList() ?? new List<string>()
            };
            plan.Tasks.Add(task);

            if (!_todoByFold.ContainsKey(fold)) _todoByFold[fold] = new List<Task>();
            _todoByFold[fold].Add(task);

            return task;
        }

        public Task[] GetTasksForFold(string fold)
        {
            return _todoByFold.GetValueOrDefault(fold, new List<Task>())
                .Where(t => !t.Completed)
                .OrderByDescending(t => t.Priority)
                .ToArray();
        }

        public Plan AutoPlan(string goal, string[] context)
        {
            var plan = CreatePlan(goal);

            // Auto-generate tasks based on gravity well phases
            AddTask(plan.Id, $"Load context: {string.Join(", ", context)}", "Pop", 0.9);
            AddTask(plan.Id, "Bind resources and capabilities", "Wo", 0.85);
            AddTask(plan.Id, "Plan execution path and schedule", "Yax", 0.8);
            AddTask(plan.Id, $"Execute: {goal}", "Sek", 1.0, new[] { plan.Tasks.Count > 0 ? plan.Tasks[0].Id : "" });
            AddTask(plan.Id, "Project results and emit artifacts", "Ch'en", 0.75);
            AddTask(plan.Id, "Consolidate, record replay, close", "Xul", 0.7);

            return plan;
        }

        public void CompleteTask(string planId, string taskId, string result)
        {
            var plan = _plans.GetValueOrDefault(planId);
            var task = plan?.Tasks.Find(t => t.Id == taskId);
            if (task != null)
            {
                task.Completed = true;
                task.Result = result;
            }
        }

        public Plan[] GetAllPlans() => _plans.Values.ToArray();
    
    }
    /// <summary>
    /// Hybrid Search Engine — lexical BM25-style retrieval + NDArray semantic geometry.
    /// External acquisition (web/news/files) remains an XCFE capability; this class
    /// ranks admitted/indexed evidence and persistent memory.
    /// </summary>
    public class HybridSearch
    {
        private readonly InvertedIndex _invertedIndex = new();
        private readonly Dictionary<string, string> _documents = new();
        private readonly Dictionary<string, NDArray> _embeddings = new();
        private Func<string, NDArray> _queryEmbedder;

        public void SetQueryEmbedder(Func<string, NDArray> embedder)
        {
            _queryEmbedder = embedder;
        }

        public void IndexDocument(string docId, string content, bool computeEmbedding = true)
        {
            IndexDocument(docId, content, null);
        }

        public void IndexDocument(string docId, string content, NDArray embedding)
        {
            if (string.IsNullOrWhiteSpace(docId))
                throw new ArgumentException("docId is required", nameof(docId));

            content ??= "";
            _documents[docId] = content;
            _invertedIndex.AddDocument(docId, Tokenize(content));

            if (embedding != null)
                _embeddings[docId] = NormalizeVector(embedding);
        }

        public void SetEmbedding(string docId, NDArray embedding)
        {
            if (!_documents.ContainsKey(docId))
                throw new ArgumentException($"Document not indexed: {docId}", nameof(docId));
            if (embedding == null) throw new ArgumentNullException(nameof(embedding));

            _embeddings[docId] = NormalizeVector(embedding);
        }

        /// <summary>
        /// Index all micronauts from a directory tree (subject/fold/id.json hierarchy)
        /// into the search index. Each micronaut's subject, fold, capability, and brain
        /// fields become searchable tokens. Idempotent — re-indexing replaces the
        /// existing entry for the same docId.
        /// </summary>
        /// <param name="micronautRoot">Root path of the micronaut directory tree.</param>
        /// <returns>Number of micronauts indexed.</returns>
        public int IndexMicronautsFromDirectory(string micronautRoot)
        {
            if (string.IsNullOrWhiteSpace(micronautRoot) || !Directory.Exists(micronautRoot))
                return 0;

            var jsonFiles = Directory.EnumerateFiles(micronautRoot, "*.json", SearchOption.AllDirectories);
            var kuhulFiles = Directory.EnumerateFiles(micronautRoot, "*.kuhul", SearchOption.AllDirectories);
            var kprogFiles = Directory.EnumerateFiles(micronautRoot, "*.kprog", SearchOption.AllDirectories);
            var files = jsonFiles.Concat(kuhulFiles).Concat(kprogFiles);

            int count = 0;
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var id = root.TryGetProperty("id", out var idProp)
                        ? idProp.GetString() ?? Path.GetFileNameWithoutExtension(file)
                        : Path.GetFileNameWithoutExtension(file);

                    var subject = root.TryGetProperty("subject", out var s)
                        ? s.GetString() ?? "" : "";
                    var fold = root.TryGetProperty("fold", out var f)
                        ? f.GetString() ?? "" : "";

                    string capability = "";
                    string brain = "";
                    if (root.TryGetProperty("provenance", out var prov))
                    {
                        capability = prov.TryGetProperty("capability", out var cap)
                            ? cap.GetString() ?? "" : "";
                        brain = prov.TryGetProperty("brain", out var br)
                            ? br.GetString() ?? "" : "";
                    }

                    // Build searchable content from all metadata fields.
                    var content = string.Join(" ", new[]
                    {
                        subject,
                        fold,
                        capability,
                        brain,
                        root.TryGetProperty("semantic_signature", out var sig)
                            ? sig.GetString() ?? "" : "",
                        root.TryGetProperty("model", out var m)
                            ? m.GetString() ?? "" : ""
                    }.Where(x => !string.IsNullOrWhiteSpace(x)));

                    // DocId encodes the directory path for traceability.
                    var docId = $"{SanitizeForIndex(subject)}/{SanitizeForIndex(fold)}/{id}";
                    IndexDocument(docId, content, embedding: null);
                    count++;
                }
                catch
                {
                    // Skip malformed files — don't block indexing on one bad entry.
                }
            }

            return count;
        }

        private static string SanitizeForIndex(string value) =>
            string.IsNullOrWhiteSpace(value) ? "_" : value.Trim().ToLowerInvariant();

        public SearchResult Search(string query, HybridSearchConfig config = null)
        {
            NDArray queryEmbedding = null;
            if (_queryEmbedder != null)
            {
                try { queryEmbedding = _queryEmbedder(query); }
                catch { queryEmbedding = null; }
            }

            return Search(query, queryEmbedding, config);
        }

        public SearchResult Search(
            string query,
            NDArray queryEmbedding,
            HybridSearchConfig config = null)
        {
            config ??= new HybridSearchConfig();
            ValidateWeights(config);

            var tokens = Tokenize(query ?? "");
            var textResults = _invertedIndex.Search(tokens);
            var rawTextScores = ScoreDocuments(textResults, tokens);

            // Normalize lexical values into [0,1] before fusion.
            var textScores = NormalizeScores(rawTextScores);

            var semanticScores = new Dictionary<string, double>();
            if (queryEmbedding != null && _embeddings.Count > 0)
            {
                var q = NormalizeVector(queryEmbedding);
                foreach (var kv in _embeddings)
                {
                    if (!_documents.ContainsKey(kv.Key)) continue;
                    semanticScores[kv.Key] = CosineSimilarity(q, kv.Value);
                }
            }

            // Union allows a semantic-only match to surface even with zero token overlap.
            var ids = new HashSet<string>(textScores.Keys);
            foreach (var id in semanticScores.Keys) ids.Add(id);

            var results = new List<SearchResultItem>();
            foreach (var docId in ids)
            {
                var textScore = textScores.GetValueOrDefault(docId, 0);
                var semanticScore = semanticScores.GetValueOrDefault(docId, 0);
                var fused =
                    config.TextWeight * textScore +
                    config.SemanticWeight * semanticScore;

                results.Add(new SearchResultItem
                {
                    DocId = docId,
                    Score = fused,
                    Explanation = config.IncludeExplanations
                        ? new Explanation
                        {
                            DocId = docId,
                            TextScore = textScore,
                            SemanticScore = semanticScore,
                            MatchedTokens = textResults.ContainsKey(docId)
                                ? textResults[docId]
                                : new List<string>(),
                            Preview = GetPreview(_documents.GetValueOrDefault(docId, ""))
                        }
                        : null,
                    Content = _documents.GetValueOrDefault(docId, "")
                });
            }

            return new SearchResult
            {
                Query = query,
                Results = results
                    .OrderByDescending(r => r.Score)
                    .ThenBy(r => r.DocId, StringComparer.Ordinal)
                    .Take(config.MaxResults)
                    .ToList(),
                TotalMatches = results.Count,
                Tokens = tokens,
                Timestamp = DateTime.UtcNow
            };
        }

        private static void ValidateWeights(HybridSearchConfig config)
        {
            if (config.MaxResults <= 0)
                throw new ArgumentOutOfRangeException(nameof(config.MaxResults));
            if (config.TextWeight < 0 || config.SemanticWeight < 0)
                throw new ArgumentException("Search weights cannot be negative");
            if (config.TextWeight == 0 && config.SemanticWeight == 0)
                throw new ArgumentException("At least one search weight must be positive");

            var total = config.TextWeight + config.SemanticWeight;
            config.TextWeight /= total;
            config.SemanticWeight /= total;
        }

        private static NDArray NormalizeVector(NDArray vector)
        {
            if (vector == null) throw new ArgumentNullException(nameof(vector));
            var flat = vector.Flatten();
            var norm = flat.Norm();
            return norm > 0 ? flat.Div(norm) : flat;
        }

        private static double CosineSimilarity(NDArray a, NDArray b)
        {
            var av = a.Flatten();
            var bv = b.Flatten();
            if (av.Size != bv.Size) return 0;

            var an = av.Norm();
            var bn = bv.Norm();
            if (an == 0 || bn == 0) return 0;

            var dot = av.Dot(bv).ToArray()[0];
            // Convert cosine [-1,1] to a stable ranking field [0,1].
            return Math.Max(0, Math.Min(1, (dot / (an * bn) + 1.0) / 2.0));
        }

        private static Dictionary<string, double> NormalizeScores(
            Dictionary<string, double> scores)
        {
            if (scores.Count == 0) return new Dictionary<string, double>();

            var max = scores.Values.Max();
            if (max <= 0)
                return scores.ToDictionary(kv => kv.Key, _ => 0.0);

            return scores.ToDictionary(kv => kv.Key, kv => kv.Value / max);
        }

        private List<string> Tokenize(string text)
        {
            var clean = Regex.Replace((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}\s]", " ");
            return clean
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .Distinct()
                .ToList();
        }

        private Dictionary<string, double> ScoreDocuments(
            Dictionary<string, List<string>> matches,
            List<string> queryTokens)
        {
            var scores = new Dictionary<string, double>();
            foreach (var match in matches)
            {
                double score = 0;
                var docTokens = _invertedIndex.GetDocumentTokens(match.Key);
                var docLength = docTokens.Count;
                var avgDocLength = _invertedIndex.AverageDocumentLength;

                foreach (var token in queryTokens)
                {
                    var tf = docTokens.Count(t => t == token);
                    if (tf == 0) continue;

                    var idf = _invertedIndex.GetIDF(token);
                    const double k1 = 1.2;
                    const double b = 0.75;
                    var numerator = tf * (k1 + 1);
                    var denominator =
                        tf + k1 * (1 - b + b * (docLength / Math.Max(avgDocLength, 1)));
                    score += idf * (numerator / Math.Max(denominator, 0.001));
                }
                scores[match.Key] = score;
            }
            return scores;
        }

        private string GetPreview(string content, int maxLength = 200)
        {
            if (string.IsNullOrEmpty(content)) return "";
            return content.Length <= maxLength
                ? content
                : content.Substring(0, maxLength) + "...";
        }
    }

    public class InvertedIndex
    {
        private Dictionary<string, HashSet<string>> _index = new();
        private Dictionary<string, List<string>> _documents = new();
        private Dictionary<string, int> _documentFrequency = new();
        private int _totalDocuments = 0;
        public double AverageDocumentLength { get; private set; } = 0;

        public void AddDocument(string docId, List<string> tokens)
        {
            tokens ??= new List<string>();

            // Replacement is idempotent: remove the previous posting/DF contribution.
            if (_documents.TryGetValue(docId, out var previous))
            {
                foreach (var token in previous.Distinct())
                {
                    if (_index.TryGetValue(token, out var posting))
                    {
                        posting.Remove(docId);
                        if (posting.Count == 0) _index.Remove(token);
                    }

                    if (_documentFrequency.ContainsKey(token))
                    {
                        _documentFrequency[token]--;
                        if (_documentFrequency[token] <= 0)
                            _documentFrequency.Remove(token);
                    }
                }
            }
            else
            {
                _totalDocuments++;
            }

            _documents[docId] = tokens;

            foreach (var token in tokens.Distinct())
            {
                if (!_index.ContainsKey(token))
                    _index[token] = new HashSet<string>();

                _index[token].Add(docId);
                _documentFrequency[token] =
                    _documentFrequency.GetValueOrDefault(token, 0) + 1;
            }

            var totalLength = _documents.Values.Sum(t => t.Count);
            AverageDocumentLength =
                totalLength / (double)Math.Max(_documents.Count, 1);
        }

        public Dictionary<string, List<string>> Search(List<string> queryTokens)
        {
            var results = new Dictionary<string, List<string>>();
            foreach (var token in queryTokens)
            {
                if (_index.ContainsKey(token))
                {
                    foreach (var docId in _index[token])
                    {
                        if (!results.ContainsKey(docId)) results[docId] = new List<string>();
                        results[docId].Add(token);
                    }
                }
            }
            return results;
        }

        public List<string> GetDocumentTokens(string docId) =>
            _documents.ContainsKey(docId) ? _documents[docId] : new List<string>();

        public double GetIDF(string token) =>
            _documentFrequency.ContainsKey(token)
                ? Math.Log(1.0 + ((_totalDocuments - _documentFrequency[token] + 0.5) /
                                  (_documentFrequency[token] + 0.5)))
                : 0;
    }

    public class HybridSearchConfig
    {
        public double TextWeight { get; set; } = 0.6;
        public double SemanticWeight { get; set; } = 0.4;
        public int MaxResults { get; set; } = 20;
        public bool IncludeExplanations { get; set; } = true;
    }

    public class SearchResult
    {
        public string Query { get; set; }
        public List<SearchResultItem> Results { get; set; }
        public int TotalMatches { get; set; }
        public List<string> Tokens { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class SearchResultItem
    {
        public string DocId { get; set; }
        public double Score { get; set; }
        public Explanation Explanation { get; set; }
        public string Content { get; set; }
    }

    public class Explanation
    {
        public string DocId { get; set; }
        public double TextScore { get; set; }
        public double SemanticScore { get; set; }
        public List<string> MatchedTokens { get; set; }
        public string Preview { get; set; }
    }

}
