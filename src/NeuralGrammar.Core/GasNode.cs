using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// GAS Node — Google Apps Script Network Client
    /// Connects the local C# runtime to the global micronaut network via a GAS web app.
    /// The GAS endpoint acts as a rendezvous/registry — it lets instances discover
    /// each other, advertise capabilities, lease micronauts, and sync artifacts.
    ///
    /// GAS deployment: https://script.google.com/macros/s/AKfycbx5VIUH_7p_z90VMyXyZqLpiT1qIZNSPwWqwkC_pU6SykjDkvNqhZdZd5HZMYp7oeLH/exec
    /// Apps Script source: src/MicronautNetworkNode.gs
    ///
    /// Protocol wire format: POST JSON body, GET query params.
    /// All endpoints return { ok: true/false, ... }.
    /// </summary>
    public class GasNode : IDisposable
    {
        private readonly HttpClient _http = new();
        private readonly string _endpoint;
        private readonly string _nodeId;
        private readonly string _publicKey;
        private bool _registered;
        private Timer _heartbeatTimer;
        private int _activeJobs = 0;
        private int _capacity = 10;

        // ---- Event for when remote micronauts become available ----
        public event Action<List<RemoteMicronaut>> OnMicronautsDiscovered;

        public class RemoteMicronaut
        {
            public string NodeId { get; set; }
            public string MicronautId { get; set; }
            public List<string> Capabilities { get; set; } = new();
            public Dictionary<string, double> Scores { get; set; } = new();
            public int ActiveJobs { get; set; }
            public int Capacity { get; set; }
            public string Endpoint { get; set; }
            public string ProofHead { get; set; }
            public string LastSeenUtc { get; set; }
        }

        public class GasResponse
        {
            public bool Ok { get; set; }
            public string Error { get; set; }
            public JsonElement? Node { get; set; }
            public JsonElement? Nodes { get; set; }
            public JsonElement? Micronauts { get; set; }
            public JsonElement? Candidates { get; set; }
            public JsonElement? Lease { get; set; }
            public JsonElement? Receipt { get; set; }
            public long? ExpiresAt { get; set; }
        }

        public GasNode(string endpoint = null, string nodeId = null, string publicKey = null)
        {
            _endpoint = (endpoint ?? "https://script.google.com/macros/s/AKfycbx5VIUH_7p_z90VMyXyZqLpiT1qIZNSPwWqwkC_pU6SykjDkvNqhZdZd5HZMYp7oeLH/exec")
                .TrimEnd('/');
            _nodeId = nodeId ?? $"nnck-{Guid.NewGuid():N}".Substring(0, 24);
            _publicKey = publicKey ?? $"pk-{Guid.NewGuid():N}";
            _http.Timeout = TimeSpan.FromSeconds(15);
        }

        public string NodeId => _nodeId;
        public bool Registered => _registered;
        public string Endpoint => _endpoint;

        // ---- Register with GAS rendezvous server ----
        public async Task<GasResponse> RegisterAsync(List<MicronautAd> micronauts = null)
        {
            var body = new Dictionary<string, object>
            {
                ["node_id"] = _nodeId,
                ["public_key"] = _publicKey,
                ["endpoint"] = "https://nnc-k.local",
                ["runtime_version"] = "nnck-csharp-1.0",
                ["protocol_version"] = "micronaut-network/1.0",
                ["capacity"] = _capacity,
                ["active_jobs"] = _activeJobs,
                ["micronauts"] = micronauts ?? new List<MicronautAd>(),
            };
            var resp = await PostAsync("register", body);
            if (resp?.Ok == true) _registered = true;
            return resp;
        }

        // ---- Heartbeat (keeps registration alive, call every ~60s) ----
        public async Task<GasResponse> HeartbeatAsync(List<MicronautAd> micronauts = null)
        {
            if (!_registered) return new GasResponse { Ok = false, Error = "not_registered" };

            var body = new Dictionary<string, object>
            {
                ["node_id"] = _nodeId,
                ["active_jobs"] = _activeJobs,
                ["capacity"] = _capacity,
            };
            if (micronauts != null) body["micronauts"] = micronauts;
            return await PostAsync("heartbeat", body);
        }

        // ---- Unregister ----
        public async Task<GasResponse> UnregisterAsync()
        {
            if (!_registered) return new GasResponse { Ok = false };
            var resp = await PostAsync("unregister", new { node_id = _nodeId });
            _registered = false;
            StopHeartbeat();
            return resp;
        }

        // ---- List all active nodes ----
        public async Task<List<RemoteNode>> GetNodesAsync()
        {
            try
            {
                var json = await _http.GetStringAsync($"{_endpoint}?pathInfo=nodes");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.GetProperty("ok").GetBoolean()) return new List<RemoteNode>();

                var nodes = new List<RemoteNode>();
                foreach (var n in root.GetProperty("nodes").EnumerateArray())
                {
                    nodes.Add(new RemoteNode
                    {
                        NodeId = n.GetProperty("node_id").GetString(),
                        Capacity = n.GetProperty("capacity").GetInt32(),
                        ActiveJobs = n.TryGetProperty("active_jobs", out var aj) ? aj.GetInt32() : 0,
                        LastSeenUtc = n.GetProperty("last_seen_utc").GetString(),
                    });
                }
                return nodes;
            }
            catch { return new List<RemoteNode>(); }
        }

        // ---- List all advertised micronauts across the network ----
        public async Task<List<RemoteMicronaut>> GetMicronautsAsync()
        {
            try
            {
                var json = await _http.GetStringAsync($"{_endpoint}?pathInfo=micronauts");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.GetProperty("ok").GetBoolean()) return new List<RemoteMicronaut>();

                var list = new List<RemoteMicronaut>();
                foreach (var m in root.GetProperty("micronauts").EnumerateArray())
                {
                    var rn = new RemoteMicronaut
                    {
                        NodeId = m.GetProperty("node_id").GetString(),
                        MicronautId = m.GetProperty("micronaut_id").GetString(),
                        ActiveJobs = m.TryGetProperty("active_jobs", out var aj) ? aj.GetInt32() : 0,
                        Capacity = m.TryGetProperty("capacity", out var cap) ? cap.GetInt32() : 1,
                        Endpoint = m.TryGetProperty("endpoint", out var ep) ? ep.GetString() : "",
                        ProofHead = m.TryGetProperty("proof_head", out var ph) ? ph.GetString() : "",
                        LastSeenUtc = m.TryGetProperty("last_seen_utc", out var ls) ? ls.GetString() : "",
                    };
                    if (m.TryGetProperty("capabilities", out var caps))
                        foreach (var c in caps.EnumerateArray()) rn.Capabilities.Add(c.GetString());
                    if (m.TryGetProperty("scores", out var sc))
                        foreach (var kv in sc.EnumerateObject()) rn.Scores[kv.Name] = kv.Value.GetDouble();
                    list.Add(rn);
                }
                OnMicronautsDiscovered?.Invoke(list);
                return list;
            }
            catch { return new List<RemoteMicronaut>(); }
        }

        // ---- Find nodes with a specific capability ----
        public async Task<List<RemoteMicronaut>> GetCandidatesAsync(string capability, double minScore = 0.0)
        {
            try
            {
                var json = await _http.GetStringAsync(
                    $"{_endpoint}?pathInfo=capabilities&capability={Uri.EscapeDataString(capability)}&min_score={minScore:F1}");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.GetProperty("ok").GetBoolean()) return new List<RemoteMicronaut>();

                var list = new List<RemoteMicronaut>();
                foreach (var c in root.GetProperty("candidates").EnumerateArray())
                {
                    var rn = new RemoteMicronaut
                    {
                        NodeId = c.TryGetProperty("node_id", out var nid) ? nid.GetString() : "",
                        MicronautId = c.TryGetProperty("micronaut_id", out var mid) ? mid.GetString() : "",
                        ActiveJobs = c.TryGetProperty("active_jobs", out var aj) ? aj.GetInt32() : 0,
                        Capacity = c.TryGetProperty("capacity", out var cap) ? cap.GetInt32() : 1,
                    };
                    if (c.TryGetProperty("capabilities", out var caps))
                        foreach (var c_item in caps.EnumerateArray()) rn.Capabilities.Add(c_item.GetString());
                    list.Add(rn);
                }
                return list;
            }
            catch { return new List<RemoteMicronaut>(); }
        }

        // ---- Lease a micronaut on a remote node ----
        public async Task<GasResponse> LeaseAsync(string targetNodeId, string micronautId, string capability)
        {
            return await PostAsync("lease", new
            {
                requester_node_id = _nodeId,
                target_node_id = targetNodeId,
                micronaut_id = micronautId,
                capability = capability,
            });
        }

        // ---- Release a lease ----
        public async Task<GasResponse> ReleaseAsync(string leaseId)
        {
            return await PostAsync("release", new { lease_id = leaseId });
        }

        // ---- Submit a receipt (creates a hash chain for audit) ----
        public async Task<GasResponse> SubmitReceiptAsync(string leaseId, string status, string resultHash = null)
        {
            return await PostAsync("receipt", new
            {
                lease_id = leaseId,
                status = status,
                result_hash = resultHash ?? "",
            });
        }

        // ---- Cache a micronaut artifact to Google Drive ----
        public async Task<GasResponse> CacheArtifactAsync(string micronautId, object manifest,
            List<string> capabilities = null, Dictionary<string, double> scores = null, int version = 1)
        {
            return await PostAsync("micronauts/cache", new
            {
                micronaut_id = micronautId,
                manifest = manifest,
                capabilities = capabilities ?? new List<string>(),
                scores = scores ?? new Dictionary<string, double>(),
                version = version,
            });
        }

        // ---- Get Drive cache index ----
        public async Task<JsonElement?> GetCacheIndexAsync()
        {
            try
            {
                var json = await _http.GetStringAsync($"{_endpoint}?pathInfo=micronauts%2Fcache");
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch { return null; }
        }

        // ---- Get specific artifact from Drive ----
        public async Task<JsonElement?> GetArtifactAsync(string micronautId)
        {
            try
            {
                var json = await _http.GetStringAsync(
                    $"{_endpoint}?pathInfo=micronauts%2Fartifact%2F{Uri.EscapeDataString(micronautId)}");
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch { return null; }
        }

        // ---- Auto-heartbeat management ----
        public void StartHeartbeat(int intervalMs = 60000)
        {
            StopHeartbeat();
            _heartbeatTimer = new Timer(async _ =>
            {
                try { await HeartbeatAsync(); }
                catch { }
            }, null, intervalMs, intervalMs);
        }

        public void StopHeartbeat()
        {
            _heartbeatTimer?.Dispose();
            _heartbeatTimer = null;
        }

        // ---- Health check ----
        public async Task<bool> IsReachableAsync()
        {
            try
            {
                var resp = await _http.GetAsync($"{_endpoint}?pathInfo=health");
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        // ---- Helpers ----

        private async Task<GasResponse> PostAsync(string path, object body)
        {
            try
            {
                var json = JsonSerializer.Serialize(body);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                // GAS requires POST to the base URL with pathInfo in the body
                var resp = await _http.PostAsync($"{_endpoint}?pathInfo={Uri.EscapeDataString(path)}", content);
                var respBody = await resp.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<GasResponse>(respBody);
            }
            catch (Exception ex)
            {
                return new GasResponse { Ok = false, Error = ex.Message };
            }
        }

        public void Dispose()
        {
            StopHeartbeat();
            if (_registered) try { UnregisterAsync().GetAwaiter().GetResult(); } catch { }
            _http.Dispose();
        }

        // ---- Public types ----

        public class RemoteNode
        {
            public string NodeId { get; set; }
            public int Capacity { get; set; }
            public int ActiveJobs { get; set; }
            public string LastSeenUtc { get; set; }
        }

        public class MicronautAd
        {
            public string MicronautId { get; set; }
            public List<string> Capabilities { get; set; } = new();
            public Dictionary<string, double> Scores { get; set; } = new();
        }
    }
}
