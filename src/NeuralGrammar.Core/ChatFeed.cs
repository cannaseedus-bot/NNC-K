using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// ChatFeed — serializes conversation history as XSD-validated XML.
    /// Consumable by XSDParser, HybridSearch, LoRAAdapter, and CascadeRouter.
    /// </summary>
    public class ChatFeed
    {
        private readonly string _feedPath;
        private readonly List<ChatSession> _sessions = new();
        private XDocument _doc;

        public ChatFeed(string feedPath = null)
        {
            _feedPath = feedPath ?? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, ".holotape", "feed",
                $"chat-feed-{DateTime.UtcNow:yyyy-MM-dd}.xml");
        }

        // ---- Data models ----

        public class ChatSession
        {
            public string Id { get; set; }
            public string Topic { get; set; }
            public string Phase { get; set; } = "Pop";
            public DateTime Started { get; set; }
            public DateTime? Ended { get; set; }
            public List<ChatTurn> Turns { get; set; } = new();
            public List<string> Tags { get; set; } = new();
            public List<string> Tokens { get; set; } = new();
            public List<string> GravityWells { get; set; } = new();
            public string AdapterId { get; set; }
            public string LoraHash { get; set; }
        }

        public class ChatTurn
        {
            public int Seq { get; set; }
            public string Phase { get; set; } = "Sek";
            public DateTime Timestamp { get; set; }
            public string UserMessage { get; set; }
            public string SystemResponse { get; set; }
            public string Intent { get; set; }
            public string Target { get; set; }
            public double Confidence { get; set; }
            public string RouteTier { get; set; }
            public string MathML { get; set; }
            public string NNC { get; set; }
            public TurnMetrics Metrics { get; set; }
        }

        public class TurnMetrics
        {
            public int LatencyMs { get; set; }
            public int TokensIn { get; set; }
            public int TokensOut { get; set; }
            public bool CacheHit { get; set; }
            public bool LoraApplied { get; set; }
            public string Hash { get; set; }
        }

        // ---- Session management ----

        public ChatSession StartSession(string topic, string id = null)
        {
            var session = new ChatSession
            {
                Id = id ?? $"session-{Guid.NewGuid():N}",
                Topic = topic,
                Phase = "Pop",
                Started = DateTime.UtcNow
            };
            _sessions.Add(session);
            return session;
        }

        public void EndSession(ChatSession session, string phase = "Xul")
        {
            session.Phase = phase;
            session.Ended = DateTime.UtcNow;
            Save();
        }

        public ChatTurn AddTurn(ChatSession session, int seq, string userMsg, string systemResp,
            string intent = null, string target = null, double confidence = 0,
            string routeTier = null, string mathML = null, string nnc = null,
            int latencyMs = 0)
        {
            var turn = new ChatTurn
            {
                Seq = seq,
                Phase = "Sek",
                Timestamp = DateTime.UtcNow,
                UserMessage = userMsg,
                SystemResponse = systemResp,
                Intent = intent,
                Target = target,
                Confidence = confidence,
                RouteTier = routeTier,
                MathML = mathML,
                NNC = nnc,
                Metrics = new TurnMetrics
                {
                    LatencyMs = latencyMs,
                    TokensIn = userMsg?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length ?? 0,
                    TokensOut = systemResp?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length ?? 0,
                    CacheHit = routeTier == "Tier1_Exact",
                    LoraApplied = routeTier == "Tier2_LoRA",
                    Hash = ComputeHash(userMsg + systemResp)
                }
            };
            session.Turns.Add(turn);
            return turn;
        }

        public void TagSession(ChatSession session, params string[] tags)
        {
            session.Tags.AddRange(tags.Where(t => !session.Tags.Contains(t)));
        }

        public void AddSessionTokens(ChatSession session, params string[] tokens)
        {
            session.Tokens.AddRange(tokens.Where(t => !session.Tokens.Contains(t)));
        }

        // ---- LoRA integration ----

        /// <summary>Train a LoRA adapter from a session's content</summary>
        public void TrainLoRA(ChatSession session, XCFE.LoRAAdapter adapter)
        {
            var text = string.Join("\n", session.Turns.SelectMany(t =>
                new[] { t.UserMessage, t.SystemResponse }));
            adapter.TrainFromText(text);
            session.AdapterId = adapter.Topic;
            session.LoraHash = ComputeHash(text);
        }

        /// <summary>
        /// Register the session against a canonical XCFE fold gravity well.
        /// FoldAlgebra is the authoritative six-fold geometry:
        /// Pop -> Wo -> Yax -> Sek -> Ch'en -> Xul.
        /// </summary>
        public void RegisterGravityWell(ChatSession session, FoldAlgebra algebra, string targetFold)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (algebra == null)
                throw new ArgumentNullException(nameof(algebra));
            if (string.IsNullOrWhiteSpace(targetFold))
                throw new ArgumentException("Target fold is required.", nameof(targetFold));

            var fold = FoldAlgebra.GetFold(targetFold);
            var well = FoldAlgebra.GetGravityWell(targetFold);

            if (fold == null || well == null)
                throw new ArgumentException(
                    $"Unknown canonical fold '{targetFold}'.",
                    nameof(targetFold));

            // Score through the live algebra so the feed records the actual
            // fold geometry/posture rather than a hard-coded 0.85 value.
            var score = algebra.ScoreFold(targetFold);

            var descriptor =
                $"{fold.Name}#{score.Score:0.000}" +
                $"|mass={score.Mass:0.000}" +
                $"|phase={fold.Phase}" +
                $"|radians={fold.Radians:0.######}";

            if (!session.GravityWells.Contains(descriptor))
                session.GravityWells.Add(descriptor);

            session.Phase = fold.Name;
        }

        // ---- Serialization ----

        /// <summary>Save feed to XML file</summary>
        public void Save()
        {
            var dir = Path.GetDirectoryName(_feedPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _doc = BuildDocument();
            _doc.Save(_feedPath);
        }

        /// <summary>Load feed from XML file</summary>
        public static ChatFeed Load(string path)
        {
            var feed = new ChatFeed(path);
            var doc = XDocument.Load(path);
            var ns = XNamespace.Get("http://kuhul.io/chat-feed/v1");

            foreach (var sessionEl in doc.Descendants(ns + "ChatSession"))
            {
                var session = new ChatSession
                {
                    Id = (string)sessionEl.Attribute("id"),
                    Topic = (string)sessionEl.Attribute("topic"),
                    Phase = (string)sessionEl.Attribute("phase") ?? "Pop",
                    Started = (DateTime?)sessionEl.Attribute("started") ?? DateTime.MinValue,
                    Ended = (DateTime?)sessionEl.Attribute("ended")
                };

                foreach (var turnEl in sessionEl.Descendants(ns + "ChatTurn"))
                {
                    var turn = new ChatTurn
                    {
                        Seq = (int)turnEl.Attribute("seq"),
                        Phase = (string)turnEl.Attribute("phase") ?? "Sek",
                        Timestamp = (DateTime?)turnEl.Attribute("timestamp") ?? DateTime.MinValue,
                        UserMessage = (string)turnEl.Element(ns + "UserMessage"),
                        SystemResponse = (string)turnEl.Element(ns + "SystemResponse"),
                        Intent = (string)turnEl.Element(ns + "UserMessage")?.Attribute("intent"),
                        Target = (string)turnEl.Element(ns + "SystemResponse")?.Attribute("target"),
                        Confidence = (double?)turnEl.Element(ns + "SystemResponse")?.Attribute("confidence") ?? 0,
                        RouteTier = (string)turnEl.Attribute("routeTier"),
                        MathML = (string)turnEl.Element(ns + "MathMLBlock"),
                        NNC = (string)turnEl.Element(ns + "NNCBlock")
                    };

                    var metricsEl = turnEl.Element(ns + "TurnMetrics");
                    if (metricsEl != null)
                    {
                        turn.Metrics = new TurnMetrics
                        {
                            LatencyMs = (int?)metricsEl.Attribute("latencyMs") ?? 0,
                            TokensIn = (int?)metricsEl.Attribute("tokensIn") ?? 0,
                            TokensOut = (int?)metricsEl.Attribute("tokensOut") ?? 0,
                            CacheHit = (bool?)metricsEl.Attribute("cacheHit") ?? false,
                            LoraApplied = (bool?)metricsEl.Attribute("loraApplied") ?? false,
                            Hash = (string)metricsEl.Attribute("hash")
                        };
                    }

                    session.Turns.Add(turn);
                }

                var metaEl = sessionEl.Element(ns + "SessionMeta");
                if (metaEl != null)
                {
                    session.Tags = metaEl.Elements("Tag").Select(e => (string)e).ToList();
                    session.Tokens = metaEl.Elements("Token").Select(e => (string)e).ToList();
                    session.GravityWells = metaEl.Elements("GravityWell").Select(e => (string)e).ToList();
                    session.AdapterId = (string)metaEl.Attribute("adapterId");
                    session.LoraHash = (string)metaEl.Attribute("loraHash");
                }

                feed._sessions.Add(session);
            }

            feed._doc = doc;
            return feed;
        }

        /// <summary>Index all feed content into a HybridSearch engine</summary>
        public void IndexInto(HybridSearch engine)
        {
            foreach (var session in _sessions)
            {
                foreach (var turn in session.Turns)
                {
                    var docId = $"{session.Id}/turn-{turn.Seq}";
                    var content = $"Q: {turn.UserMessage}\nA: {turn.SystemResponse}";
                    engine.IndexDocument(docId, content);
                }
            }
        }

        /// <summary>Validate feed XML against chat-feed.xsd</summary>
        public bool Validate(XSDParser parser, string schemaPath)
        {
            if (_doc == null) return false;
            var tempPath = Path.GetTempFileName() + ".xml";
            try
            {
                _doc.Save(tempPath);
                return parser.Validate(tempPath, schemaPath);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        // ---- Query ----

        /// <summary>Search turns by intent or keyword</summary>
        public List<ChatTurn> QueryTurns(string keyword)
        {
            keyword = keyword.ToLowerInvariant();
            return _sessions.SelectMany(s => s.Turns)
                .Where(t => (t.UserMessage?.ToLowerInvariant().Contains(keyword) ?? false) ||
                            (t.SystemResponse?.ToLowerInvariant().Contains(keyword) ?? false))
                .ToList();
        }

        /// <summary>Get sessions by tag</summary>
        public List<ChatSession> GetSessionsByTag(string tag) =>
            _sessions.Where(s => s.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList();

        /// <summary>Get sessions by topic keyword</summary>
        public List<ChatSession> GetSessionsByTopic(string keyword) =>
            _sessions.Where(s => s.Topic?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

        /// <summary>All sessions</summary>
        public IReadOnlyList<ChatSession> Sessions => _sessions;

        /// <summary>Total turns across all sessions</summary>
        public int TotalTurns => _sessions.Sum(s => s.Turns.Count);

        /// <summary>Feed file path</summary>
        public string FeedPath => _feedPath;

        // ---- Private ----

        private XDocument BuildDocument()
        {
            var ns = XNamespace.Get("http://kuhul.io/chat-feed/v1");
            var k = XNamespace.Get("http://kuhul.io/schema/v3");
            var xsi = XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance");

            var doc = new XDocument(
                new XElement(ns + "ChatFeed",
                    new XAttribute(XNamespace.Xmlns + "cf", ns.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "k", k.NamespaceName),
                    new XAttribute(xsi + "schemaLocation",
                        $"{ns.NamespaceName} chat-feed.xsd"),
                    new XAttribute("version", "1.0"),
                    new XAttribute("feedId", $"feed-{DateTime.UtcNow:yyyy-MM-dd}"),
                    new XAttribute("created", DateTime.UtcNow.ToString("O")),
                    new XAttribute("totalTurns", TotalTurns),

                    _sessions.Select(BuildSessionElement)
                )
            );

            return doc;
        }

        private XElement BuildSessionElement(ChatSession session)
        {
            var ns = XNamespace.Get("http://kuhul.io/chat-feed/v1");

            var el = new XElement(ns + "ChatSession",
                new XAttribute("id", session.Id ?? Guid.NewGuid().ToString("N")),
                new XAttribute("topic", session.Topic ?? ""),
                new XAttribute("phase", session.Phase ?? "Pop"),
                new XAttribute("started", session.Started.ToString("O")),
                new XAttribute("turnCount", session.Turns.Count),

                session.Turns.Select(t => BuildTurnElement(t, ns))
            );

            if (session.Ended.HasValue)
                el.Add(new XAttribute("ended", session.Ended.Value.ToString("O")));

            // SessionMeta
            if (session.Tags.Count > 0 || session.Tokens.Count > 0 || session.AdapterId != null)
            {
                var meta = new XElement(ns + "SessionMeta");
                foreach (var tag in session.Tags)
                    meta.Add(new XElement("Tag", tag));
                foreach (var token in session.Tokens)
                    meta.Add(new XElement("Token", token));
                foreach (var well in session.GravityWells)
                    meta.Add(new XElement("GravityWell", well));
                if (session.AdapterId != null)
                    meta.Add(new XAttribute("adapterId", session.AdapterId));
                if (session.LoraHash != null)
                    meta.Add(new XAttribute("loraHash", session.LoraHash));
                el.Add(meta);
            }

            return el;
        }

        private XElement BuildTurnElement(ChatTurn turn, XNamespace ns)
        {
            var el = new XElement(ns + "ChatTurn",
                new XAttribute("seq", turn.Seq),
                new XAttribute("phase", turn.Phase ?? "Sek"),
                new XAttribute("timestamp", turn.Timestamp.ToString("O")),

                new XElement(ns + "UserMessage",
                    new XAttribute("role", "user"),
                    new XAttribute("tokenCount", turn.Metrics?.TokensIn ?? 0),
                    new XAttribute("intent", turn.Intent ?? ""),
                    turn.UserMessage ?? ""
                ),

                new XElement(ns + "SystemResponse",
                    new XAttribute("role", "system"),
                    new XAttribute("target", turn.Target ?? ""),
                    new XAttribute("fold", ResolveFold(turn.Target)),
                    new XAttribute("confidence", turn.Confidence),
                    turn.SystemResponse ?? ""
                )
            );

            if (turn.RouteTier != null)
                el.Add(new XAttribute("routeTier", turn.RouteTier));
            if (turn.Confidence > 0)
                el.Add(new XAttribute("cascadeConfidence", turn.Confidence));

            if (!string.IsNullOrEmpty(turn.MathML))
                el.Add(new XElement(ns + "MathMLBlock",
                    new XAttribute("engine", "kuhul"), turn.MathML));

            if (!string.IsNullOrEmpty(turn.NNC))
                el.Add(new XElement(ns + "NNCBlock",
                    new XAttribute("schema", "asx-nnc-v1"), turn.NNC));

            if (turn.Metrics != null)
            {
                el.Add(new XElement(ns + "TurnMetrics",
                    new XAttribute("latencyMs", turn.Metrics.LatencyMs),
                    new XAttribute("tokensIn", turn.Metrics.TokensIn),
                    new XAttribute("tokensOut", turn.Metrics.TokensOut),
                    new XAttribute("cacheHit", turn.Metrics.CacheHit),
                    new XAttribute("loraApplied", turn.Metrics.LoraApplied),
                    new XAttribute("hash", turn.Metrics.Hash ?? "")
                ));
            }

            return el;
        }

        private static string ResolveFold(string target) => target switch
        {
            "CM-1" => "CONTROL_FOLD", "PM-1" => "DATA_FOLD", "TM-1" => "TIME_FOLD",
            "HM-1" => "STATE_FOLD", "SM-1" => "STORAGE_FOLD", "MM-1" => "COMPUTE_FOLD",
            "XM-1" => "PATTERN_FOLD", "VM-1" => "UI_FOLD", "VM-2" => "META_FOLD",
            _ => "POP_FOLD"
        };

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? ""));
            return "sha256:" + BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }
}