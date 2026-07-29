using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuralGrammar.Core.XCFE
{
    /// <summary>
    /// BrainRouter — n-gram intent matching for 9 micronaut profiles.
    /// Matches asx-micronaut-brains.manifest.json
    /// </summary>
    public class BrainRouter
    {
        private readonly Dictionary<string, MicronautProfile> _profiles = new();
        private readonly Dictionary<string, IntentDef> _intents = new();
        private readonly List<IntentItem> _intentItems = new();

        public BrainRouter()
        {
            RegisterProfiles();
            RegisterIntents();
        }

        // ---- Profile model ----

        public class MicronautProfile
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Role { get; set; }
            public string Fold { get; set; }
            public string[] Tools { get; set; }
            public string AcceptResponse { get; set; }
            public string RejectResponse { get; set; }
        }

        public class IntentDef
        {
            public string Name { get; set; }
            public string Target { get; set; }
            public string Fold { get; set; }
            public int Priority { get; set; }
            public string[] Triggers { get; set; }
        }

        public class RouteResult
        {
            public bool Routed { get; set; }
            public string Intent { get; set; }
            public string Target { get; set; }
            public string Fold { get; set; }
            public string[] Tools { get; set; }
            public double Confidence { get; set; }
            public string[] MatchedNgrams { get; set; }
            public string FallbackReason { get; set; }
        }

        private class IntentItem
        {
            public string IntentName { get; set; }
            public string Target { get; set; }
            public string Fold { get; set; }
            public int Priority { get; set; }
            public List<string> Bigrams { get; set; } = new();
            public List<string> Trigrams { get; set; } = new();
            public string[] RawTriggers { get; set; }
        }

        // ---- Profile access ----

        public MicronautProfile GetProfile(string id) =>
            _profiles.TryGetValue(id, out var p) ? p : null;

        public IReadOnlyDictionary<string, MicronautProfile> AllProfiles => _profiles;

        public IntentDef GetIntent(string name) =>
            _intents.TryGetValue(name, out var i) ? i : null;

        // ---- N-gram routing ----

        /// <summary>Route input text to the best-matching micronaut profile</summary>
        public RouteResult Route(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Fallback("Empty input");

            var inputBigrams = ExtractBigrams(text);
            var inputTrigrams = ExtractTrigrams(text);

            var scores = new List<(IntentItem Item, double Score, List<string> Matched)>();

            foreach (var item in _intentItems)
            {
                double score = 0;
                var matchedNgrams = new List<string>();

                // Score bigrams
                foreach (var bg in item.Bigrams)
                {
                    if (inputBigrams.Any(ib => ib.Equals(bg, StringComparison.OrdinalIgnoreCase)))
                    {
                        score += 1.0;
                        matchedNgrams.Add(bg);
                    }
                }

                // Score trigrams (higher weight)
                foreach (var tg in item.Trigrams)
                {
                    if (inputTrigrams.Any(it => it.Equals(tg, StringComparison.OrdinalIgnoreCase)))
                    {
                        score += 1.7;
                        matchedNgrams.Add(tg);
                    }
                }

                // Also check raw trigger phrases as substring matches
                foreach (var trigger in item.RawTriggers)
                {
                    if (text.Contains(trigger, StringComparison.OrdinalIgnoreCase))
                    {
                        // Give partial credit for phrase-level matches
                        int wordCount = trigger.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                        score += wordCount >= 3 ? 2.5 : wordCount >= 2 ? 1.5 : 1.0;
                        matchedNgrams.Add(trigger);
                    }
                }

                if (score > 0)
                    scores.Add((item, score, matchedNgrams));
            }

            if (scores.Count == 0)
                return Fallback("No intent matched — ExtrapolationMicronaut handles unclassified input via narrative expansion");

            // Sort: highest score first, then highest priority first (lower number = higher priority)
            var best = scores
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.Item.Priority)
                .First();

            double maxPossible = ComputeMaxPossible(text);
            double confidence = maxPossible > 0 ? Math.Min(best.Score / maxPossible, 1.0) : 0;

            if (confidence < 0.3)
                return Fallback($"Low confidence ({confidence:F2}) — ExtrapolationMicronaut handles unclassified input");

            var profile = GetProfile(best.Item.Target);
            return new RouteResult
            {
                Routed = true,
                Intent = best.Item.IntentName,
                Target = best.Item.Target,
                Fold = best.Item.Fold,
                Tools = profile?.Tools ?? Array.Empty<string>(),
                Confidence = confidence,
                MatchedNgrams = best.Matched.Distinct().ToArray()
            };
        }

        /// <summary>Get tools available for a specific profile</summary>
        public string[] GetTools(string profileId) =>
            _profiles.TryGetValue(profileId, out var p) ? p.Tools : Array.Empty<string>();

        /// <summary>Get all tools across all profiles with their owning profile</summary>
        public Dictionary<string, string> AllTools
        {
            get
            {
                var tools = new Dictionary<string, string>();
                foreach (var p in _profiles.Values)
                    foreach (var t in p.Tools)
                        tools[t] = p.Id;
                return tools;
            }
        }

        /// <summary>Get all tool names</summary>
        public string[] AllToolNames => AllTools.Keys.ToArray();

        // ---- Private ----

        private RouteResult Fallback(string reason)
        {
            // Fallback returns NONE — no micronaut is granted authority by default.
            return new RouteResult
            {
                Routed = false,
                Intent = "unresolved",
                Target = "NONE",
                Fold = "",
                Tools = Array.Empty<string>(),
                Confidence = 0,
                FallbackReason = reason
            };
        }


        private double ComputeMaxPossible(string text)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int bigramCount = Math.Max(0, words.Length - 1);
            int trigramCount = Math.Max(0, words.Length - 2);
            return bigramCount * 1.0 + trigramCount * 1.7;
        }

        private static List<string> ExtractBigrams(string text)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var bigrams = new List<string>();
            for (int i = 0; i < words.Length - 1; i++)
                bigrams.Add($"{words[i]} {words[i + 1]}");
            return bigrams;
        }

        private static List<string> ExtractTrigrams(string text)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var trigrams = new List<string>();
            for (int i = 0; i < words.Length - 2; i++)
                trigrams.Add($"{words[i]} {words[i + 1]} {words[i + 2]}");
            return trigrams;
        }

        // ---- Registration ----

        private void RegisterProfiles()
        {
            RegisterProfile("CM-1", "ControlMicronaut",     "phase_geometry",       "CONTROL_FOLD",
                new[] { "mark_boundary", "signal_phase_shift", "segment_stream", "gate_scope", "annotate_zone" },
                "ACCEPT", "REJECT");

            RegisterProfile("PM-1", "PerceptionMicronaut",  "field_selection",      "DATA_FOLD",
                new[] { "select_field", "filter_noise", "route_curvature", "measure_salience", "focus_attention" },
                "FIELD_ACCEPT", "FIELD_REJECT");

            RegisterProfile("TM-1", "TemporalMicronaut",    "collapse_timing",      "TIME_FOLD",
                new[] { "schedule_collapse", "gate_replay", "align_transition", "tick_clock", "decay_window" },
                "TIME_ACCEPT", "TIME_REJECT");

            RegisterProfile("HM-1", "HostMicronaut",        "host_abstraction",     "STATE_FOLD",
                new[] { "detect_capabilities", "normalize_io", "expose_reality", "probe_platform", "flatten_interface" },
                "HOST_ACCEPT", "HOST_REJECT");

            RegisterProfile("SM-1", "StorageMicronaut",     "inert_persistence",    "STORAGE_FOLD",
                new[] { "store_object", "retrieve_object", "seal_snapshot", "verify_identity", "compute_delta" },
                "STORE_OK", "STORE_DENY");

            RegisterProfile("MM-1", "ModelMicronaut",       "token_signal_generator","COMPUTE_FOLD",
                new[] { "emit_token", "stream_tokens", "voice_model", "score_logits", "sample_distribution" },
                "MODEL_ON", "MODEL_OFF");

            RegisterProfile("XM-1", "ExtrapolationMicronaut","narrative_expansion", "PATTERN_FOLD",
                new[] { "expand_explanation", "generate_metaphor", "provide_analogy", "continue_narrative", "cluster_patterns" },
                "EXPAND", "HALT");

            RegisterProfile("VM-1", "VisualizationMicronaut","rendering_projection", "UI_FOLD",
                new[] { "render_svg", "render_css", "render_dom", "render_terminal", "emit_frame" },
                "RENDER_ON", "RENDER_OFF");

            RegisterProfile("VM-2", "VerificationMicronaut", "proof_generation",    "META_FOLD",
                new[] { "verify_replay", "verify_projection", "verify_boundary", "attest_hash", "audit_trace" },
                "PROOF_OK", "PROOF_FAIL");
        }

        private void RegisterProfile(string id, string name, string role, string fold,
            string[] tools, string accept, string reject)
        {
            _profiles[id] = new MicronautProfile
            {
                Id = id, Name = name, Role = role, Fold = fold,
                Tools = tools, AcceptResponse = accept, RejectResponse = reject
            };
        }

        private void RegisterIntents()
        {
            RegisterIntent("control",     "CM-1", "CONTROL_FOLD",  1,
                new[] { "phase boundary", "scope gate", "control signal", "mark phase boundary", "signal phase shift" });
            RegisterIntent("perceive",    "PM-1", "DATA_FOLD",     2,
                new[] { "input field", "noise filter", "select input field", "focus attention" });
            RegisterIntent("schedule",    "TM-1", "TIME_FOLD",     3,
                new[] { "collapse schedule", "replay gate", "phase align", "clock tick" });
            RegisterIntent("detect_host", "HM-1", "STATE_FOLD",    4,
                new[] { "host capability", "io normalize", "probe host platform", "flatten interface" });
            RegisterIntent("store",       "SM-1", "STORAGE_FOLD",  5,
                new[] { "store object", "retrieve object", "byte identity", "seal snapshot" });
            RegisterIntent("infer",       "MM-1", "COMPUTE_FOLD",  6,
                new[] { "token signal", "model voice", "emit token", "score logits" });
            RegisterIntent("expand",      "XM-1", "PATTERN_FOLD",  7,
                new[] { "expand narrative", "generate metaphor", "provide analogy", "continue narrative" });
            RegisterIntent("render",      "VM-1", "UI_FOLD",       8,
                new[] { "render projection", "render svg", "render css", "emit render frame" });
            RegisterIntent("verify",      "VM-2", "META_FOLD",     9,
                new[] { "proof check", "verify replay", "verify boundary", "attest hash" });
        }

        private void RegisterIntent(string name, string target, string fold, int priority, string[] triggers)
        {
            _intents[name] = new IntentDef
            {
                Name = name, Target = target, Fold = fold,
                Priority = priority, Triggers = triggers
            };

            var item = new IntentItem
            {
                IntentName = name, Target = target, Fold = fold,
                Priority = priority, RawTriggers = triggers
            };

            // Precompute n-grams from triggers
            foreach (var trigger in triggers)
            {
                var words = trigger.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < words.Length - 1; i++)
                    item.Bigrams.Add($"{words[i]} {words[i + 1]}".ToLowerInvariant());
                for (int i = 0; i < words.Length - 2; i++)
                    item.Trigrams.Add($"{words[i]} {words[i + 1]} {words[i + 2]}".ToLowerInvariant());
            }

            _intentItems.Add(item);
        }
    }
}
