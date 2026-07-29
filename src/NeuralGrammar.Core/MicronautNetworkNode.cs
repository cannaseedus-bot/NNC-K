using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NeuralGrammar.Core
{
    // Network presence/discovery only. XCFE owns admission; FoldAlgebra owns folds.
    public sealed class MicronautNetworkNode : IDisposable
    {
        readonly HttpClient _http;
        readonly bool _ownsHttp;
        readonly string _baseUrl;
        readonly Dictionary<string, MicronautAdvertisement> _local = new(StringComparer.OrdinalIgnoreCase);
        readonly object _sync = new();

        public string NodeId { get; }
        public string PublicKey { get; }
        public string RuntimeVersion { get; set; } = "NNC-K";
        public string ProtocolVersion { get; set; } = "micronaut-network/1.0";
        public string Endpoint { get; set; } = "";
        public string ProofHead { get; set; } = "";
        public int Capacity { get; set; } = 1;
        public int ActiveJobs { get; set; }

        public MicronautNetworkNode(string baseUrl, string nodeId, string publicKey, HttpClient http = null)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentException("Base URL required", nameof(baseUrl));
            if (string.IsNullOrWhiteSpace(nodeId)) throw new ArgumentException("Node id required", nameof(nodeId));
            if (string.IsNullOrWhiteSpace(publicKey)) throw new ArgumentException("Public key required", nameof(publicKey));
            _baseUrl = baseUrl.TrimEnd('/');
            NodeId = nodeId; PublicKey = publicKey;
            _ownsHttp = http == null;
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        }

        public void Advertise(string micronautId, IEnumerable<string> capabilities, IReadOnlyDictionary<string,double> scores)
        {
            if (string.IsNullOrWhiteSpace(micronautId)) throw new ArgumentException("Micronaut id required", nameof(micronautId));
            var m = new MicronautAdvertisement {
                MicronautId = micronautId,
                Capabilities = (capabilities ?? Array.Empty<string>()).Where(x=>!string.IsNullOrWhiteSpace(x))
                    .Select(x=>x.Trim().ToLowerInvariant()).Distinct().ToArray()
            };
            if (scores != null) foreach (var kv in scores)
                m.Scores[kv.Key.ToLowerInvariant()] = Clamp01(kv.Value);
            lock (_sync) _local[micronautId] = m;
        }

        public Task<NetworkResponse<NodeRegistration>> RegisterAsync(CancellationToken ct=default) =>
            PostAsync<NodeRegistration>("register", Presence(), "node", ct);

        public Task<NetworkResponse<HeartbeatResult>> HeartbeatAsync(CancellationToken ct=default) =>
            PostAsync<HeartbeatResult>("heartbeat", Presence(), null, ct);

        public Task<NetworkResponse<UnregisterResult>> UnregisterAsync(CancellationToken ct=default) =>
            PostAsync<UnregisterResult>("unregister", new { node_id=NodeId }, null, ct);

        public async Task<IReadOnlyList<NetworkCandidate>> DiscoverAsync(string capability, double minimumScore=0, CancellationToken ct=default)
        {
            if (string.IsNullOrWhiteSpace(capability)) throw new ArgumentException("Capability required", nameof(capability));
            var uri = UriFor("capabilities?capability=" + Uri.EscapeDataString(capability.ToLowerInvariant()) +
                "&min_score=" + Clamp01(minimumScore).ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var response = await _http.GetAsync(uri, ct).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var e = JsonSerializer.Deserialize<CandidateEnvelope>(json, Options());
            if (e?.Candidates is { } candidates)
                return candidates;

            return Array.Empty<NetworkCandidate>();
        }

        // Lease is post-admission. Network discovery cannot authorize execution.
        public Task<NetworkResponse<Lease>> RequestLeaseAsync(NetworkCandidate candidate, string capability, bool admitted, CancellationToken ct=default)
        {
            if (!admitted) throw new InvalidOperationException("XCFE/K'UHUL admission required before network lease.");
            if (candidate == null) throw new ArgumentNullException(nameof(candidate));
            return PostAsync<Lease>("lease", new {
                requester_node_id=NodeId, target_node_id=candidate.NodeId,
                micronaut_id=candidate.MicronautId, capability=capability
            }, "lease", ct);
        }

        public Task<NetworkResponse<ReleaseResult>> ReleaseLeaseAsync(string leaseId, CancellationToken ct=default) =>
            PostAsync<ReleaseResult>("release", new { lease_id=leaseId }, null, ct);

        public Task<NetworkResponse<NetworkReceipt>> SubmitReceiptAsync(string leaseId, string status, string resultHash, CancellationToken ct=default) =>
            PostAsync<NetworkReceipt>("receipt", new { lease_id=leaseId, status=status, result_hash=resultHash ?? "" }, "receipt", ct);

        object Presence()
        {
            MicronautAdvertisement[] m;
            lock (_sync) m = _local.Values.ToArray();
            return new {
                node_id=NodeId, public_key=PublicKey, endpoint=Endpoint, runtime_version=RuntimeVersion,
                protocol_version=ProtocolVersion, proof_head=ProofHead, capacity=Math.Max(0,Capacity),
                active_jobs=Math.Max(0,ActiveJobs),
                micronauts=m.Select(x=>new { micronaut_id=x.MicronautId, capabilities=x.Capabilities, scores=x.Scores }).ToArray()
            };
        }

        async Task<NetworkResponse<T>> PostAsync<T>(string path, object payload, string valueProperty, CancellationToken ct)
        {
            using var content = new StringContent(JsonSerializer.Serialize(payload, Options()), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(UriFor(path), content, ct).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(json);
            var root=doc.RootElement;
            var r=new NetworkResponse<T> {
                Ok=root.TryGetProperty("ok",out var ok)&&ok.GetBoolean(),
                Error=root.TryGetProperty("error",out var er)?er.GetString():null
            };
            if (r.Ok) {
                var v = valueProperty!=null && root.TryGetProperty(valueProperty,out var p) ? p : root;
                r.Value=v.Deserialize<T>(Options());
            }
            return r;
        }

        Uri UriFor(string path)=>new Uri(_baseUrl+"/"+path.TrimStart('/'));
        static JsonSerializerOptions Options()=>new JsonSerializerOptions {
            PropertyNameCaseInsensitive=true, PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower
        };
        static double Clamp01(double v)=>double.IsNaN(v)?0:Math.Max(0,Math.Min(1,v));
        public void Dispose(){ if(_ownsHttp)_http.Dispose(); }
    }

    public sealed class MicronautAdvertisement {
        public string MicronautId {get;set;}
        public string[] Capabilities {get;set;}=Array.Empty<string>();
        public Dictionary<string,double> Scores {get;set;}=new(StringComparer.OrdinalIgnoreCase);
    }
    public sealed class NetworkCandidate {
        public string NodeId {get;set;} public string MicronautId {get;set;}
        public string[] Capabilities {get;set;}=Array.Empty<string>();
        public Dictionary<string,double> Scores {get;set;}=new();
        public string Capability {get;set;} public double CapabilityScore {get;set;}
        public double Load {get;set;} public double RankScore {get;set;}
        public int ActiveJobs {get;set;} public int Capacity {get;set;}
        public string Endpoint {get;set;} public string ProofHead {get;set;} public string LastSeenUtc {get;set;}
    }
    public sealed class CandidateEnvelope { public bool Ok {get;set;} public List<NetworkCandidate> Candidates {get;set;}=new(); }
    public sealed class NetworkResponse<T> { public bool Ok {get;set;} public string Error {get;set;} public T Value {get;set;} }
    public sealed class NodeRegistration {
        public string NodeId {get;set;} public string PublicKey {get;set;} public string Endpoint {get;set;}
        public string RuntimeVersion {get;set;} public string ProtocolVersion {get;set;} public string ProofHead {get;set;}
        public int Capacity {get;set;} public int ActiveJobs {get;set;} public List<MicronautAdvertisement> Micronauts {get;set;}=new();
        public string RegisteredUtc {get;set;} public string LastSeenUtc {get;set;} public long ExpiresAt {get;set;}
    }
    public sealed class HeartbeatResult { public bool Ok {get;set;} public string NodeId {get;set;} public long ExpiresAt {get;set;} }
    public sealed class UnregisterResult { public bool Ok {get;set;} public string NodeId {get;set;} }
    public sealed class Lease {
        public string LeaseId {get;set;} public string RequesterNodeId {get;set;} public string TargetNodeId {get;set;}
        public string MicronautId {get;set;} public string Capability {get;set;} public string CreatedUtc {get;set;}
        public long ExpiresAt {get;set;} public string Status {get;set;}
    }
    public sealed class ReleaseResult { public bool Ok {get;set;} public string LeaseId {get;set;} }
    public sealed class NetworkReceipt {
        public string ReceiptId {get;set;} public string LeaseId {get;set;} public string Status {get;set;}
        public string ResultHash {get;set;} public string PreviousReceiptHash {get;set;} public string ReceiptHash {get;set;}
        public string CreatedUtc {get;set;}
    }
}
