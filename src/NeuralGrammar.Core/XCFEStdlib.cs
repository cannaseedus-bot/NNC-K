using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuralGrammar.Core.XCFE
{
    /// <summary>
    /// XCFE Standard Library — registry of all @-verbs by category.
    /// Matches the stdlib contract from asx-xcfe-authority.manifest.json
    /// </summary>
    public static class XCFEStdlib
    {
        private static readonly Dictionary<string, VerbDef> _verbs = new(StringComparer.OrdinalIgnoreCase);

        static XCFEStdlib()
        {
            RegisterVerbs();
        }

        public static IReadOnlyDictionary<string, VerbDef> All =>
            _verbs.ToDictionary(kv => kv.Key, kv => Clone(kv.Value), StringComparer.OrdinalIgnoreCase);

        public static bool IsKnown(string verb) => _verbs.ContainsKey(verb);

        public static VerbDef Get(string verb) =>
            _verbs.TryGetValue(verb, out var def) ? def : null;

        public static string CategoryOf(string verb) =>
            _verbs.TryGetValue(verb, out var def) ? def.Category : null;

        public static Capability RequiredCapability(string verb) =>
            _verbs.TryGetValue(verb, out var def) ? def.RequiredCap : Capability.None;

        public static bool RequiresCapability(string verb, Capability cap) =>
            _verbs.TryGetValue(verb, out var def) &&
            def.RequiredCap != Capability.None &&
            (def.RequiredCap & cap) == def.RequiredCap;

        public static VerbAdmission Admit(
            string verb,
            FoldAlgebra algebra,
            Capability grantedCapabilities,
            string lane = null)
        {
            if (string.IsNullOrWhiteSpace(verb) || !_verbs.TryGetValue(verb, out var def))
                return VerbAdmission.Deny(verb, "Unknown XCFE verb");

            var currentFold = algebra?.CurrentFold;
            if (algebra == null)
                return VerbAdmission.Deny(verb, "FoldAlgebra is required for execution admission");

            if (!def.LegalFolds.Contains(currentFold, StringComparer.OrdinalIgnoreCase))
                return VerbAdmission.Deny(
                    verb,
                    $"Verb '{verb}' is not legal in fold {currentFold}; legal folds: {string.Join(", ", def.LegalFolds)}",
                    def,
                    currentFold);

            var selectedLane = string.IsNullOrWhiteSpace(lane)
                ? def.LegalLanes.FirstOrDefault()
                : lane;

            if (string.IsNullOrWhiteSpace(selectedLane) ||
                !def.LegalLanes.Contains(selectedLane, StringComparer.OrdinalIgnoreCase))
                return VerbAdmission.Deny(
                    verb,
                    $"Lane '{lane}' is not legal for {verb}; legal lanes: {string.Join(", ", def.LegalLanes)}",
                    def,
                    currentFold);

            if (def.RequiredCap != Capability.None &&
                (grantedCapabilities & def.RequiredCap) != def.RequiredCap)
                return VerbAdmission.Deny(
                    verb,
                    $"Missing capability {def.RequiredCap}",
                    def,
                    currentFold);

            return new VerbAdmission
            {
                Admitted = true,
                Reason = "admitted",
                Verb = def.Name,
                Category = def.Category,
                Fold = currentFold,
                Lane = selectedLane,
                RequiredCapability = def.RequiredCap,
                Effect = def.Effect,
                Replay = def.Replay,
                Deterministic = def.Deterministic
            };
        }

        public static bool IsPure(string verb) =>
            _verbs.TryGetValue(verb, out var def) && def.Effect == EffectClass.Pure;

        public static bool IsMutation(string verb) =>
            _verbs.TryGetValue(verb, out var def) &&
            (def.Effect == EffectClass.StateMutation ||
             def.Effect == EffectClass.ExternalMutation);

        private static void RegisterVerbs()
        {
            // --- Core control flow ---
            Register("core", "@seq",   Capability.None,     "Sequential execution of ordered children");
            Register("core", "@par",   Capability.None,     "Parallel execution of children");
            Register("core", "@if",    Capability.None,     "Conditional branch");
            Register("core", "@switch",Capability.None,     "Pattern-matched switch");
            Register("core", "@for",   Capability.None,     "Bounded iteration");
            Register("core", "@while", Capability.None,     "Conditional loop");
            Register("core", "@try",   Capability.None,     "Try/catch error handling");
            Register("core", "@catch", Capability.None,     "Catch handler");
            Register("core", "@finally",Capability.None,    "Finally handler");
            Register("core", "@throw",  Capability.None,    "Throw error");
            Register("core", "@halt",   Capability.None,    "Halt execution");

            // --- Async ---
            Register("async", "@await", Capability.None,    "Await async operation");
            Register("async", "@spawn", Capability.Process, "Spawn concurrent task");
            Register("async", "@join",  Capability.None,    "Join concurrent tasks");
            Register("async", "@sleep", Capability.None,    "Sleep for duration");
            Register("async", "@cancel",Capability.Process, "Cancel a task");

            // --- State ---
            Register("state", "@var",   Capability.None,    "Declare mutable variable");
            Register("state", "@const", Capability.None,    "Declare constant");
            Register("state", "@get",   Capability.None,    "Get value from state");
            Register("state", "@set",   Capability.None,    "Set value in state");
            Register("state", "@unset", Capability.None,    "Unset/delete value");
            Register("state", "@push",  Capability.None,    "Push to array state");
            Register("state", "@pop",   Capability.None,    "Pop from array state");
            Register("state", "@merge", Capability.None,    "Merge object state");

            // --- Type ---
            Register("type", "@array",  Capability.None,    "Create array");
            Register("type", "@map",    Capability.None,    "Create map");
            Register("type", "@object", Capability.None,    "Create object");
            Register("type", "@class",  Capability.None,    "Declare class");
            Register("type", "@new",    Capability.None,    "Instantiate class");
            Register("type", "@typeof", Capability.None,    "Get type of value");

            // --- Math ---
            Register("math", "@calc",   Capability.None,    "Evaluate math expression");
            Register("math", "@hash",   Capability.None,    "Hash a value");
            Register("math", "@encode", Capability.None,    "Encode data");
            Register("math", "@decode", Capability.None,    "Decode data");
            Register("math", "@rand",   Capability.None,    "Secure random (requires seed for determinism)");

            // --- Log ---
            Register("log",  "@log",    Capability.None,    "Log message");
            Register("log",  "@trace",  Capability.None,    "Trace execution");
            Register("log",  "@metric", Capability.None,    "Emit metric");

            // --- Import / Capabilities ---
            Register("import", "@import",      Capability.None,    "Import pack");
            Register("import", "@cap.list",    Capability.None,    "List granted capabilities");
            Register("import", "@cap.require", Capability.None,    "Require capability");
            Register("import", "@cap.revoke",  Capability.None,    "Revoke capability");

            // --- Network ---
            Register("net", "@http.get",     Capability.Network, "HTTP GET request");
            Register("net", "@http.post",    Capability.Network, "HTTP POST request");
            Register("net", "@http.request", Capability.Network, "HTTP request (any method)");
            Register("net", "@ipc.pipe",     Capability.IPC,     "Open IPC pipe");
            Register("net", "@ipc.write",    Capability.IPC,     "Write to IPC pipe");
            Register("net", "@ipc.read",     Capability.IPC,     "Read from IPC pipe");
            Register("net", "@ws.connect",   Capability.Network, "WebSocket connect");
            Register("net", "@ws.send",      Capability.Network, "WebSocket send");
            Register("net", "@ws.close",     Capability.Network, "WebSocket close");
            Register("net", "@on",           Capability.Network, "Event listener registration");

            // --- I/O ---
            Register("io",  "@file.read",  Capability.Filesystem, "Read file");
            Register("io",  "@file.write", Capability.Filesystem, "Write file");
            Register("io",  "@dir.list",   Capability.Filesystem, "List directory");
            Register("io",  "@kv.get",     Capability.Filesystem, "KV store get");
            Register("io",  "@kv.set",     Capability.Filesystem, "KV store set");
            Register("io",  "@idb.query",  Capability.Filesystem, "IndexedDB query");

            // --- UI ---
            Register("ui",  "@window",     Capability.UI,    "Create/manage window");
            Register("ui",  "@dom.set",    Capability.UI,    "Set DOM content");
            Register("ui",  "@css.var.set",Capability.UI,    "Set CSS variable");
            Register("ui",  "@ui.emit",    Capability.UI,    "Emit UI event");

            // --- GPU ---
            Register("gpu", "@gpu.dispatch",    Capability.GPU, "Dispatch GPU compute");
            Register("gpu", "@gpu.buffer.write",Capability.GPU, "Write GPU buffer");
            Register("gpu", "@gpu.buffer.read", Capability.GPU, "Read GPU buffer");

            // --- AI ---
            Register("ai",  "@ai.infer",     Capability.None, "AI inference");
            Register("ai",  "@ai.embed",     Capability.None, "AI embedding");
            Register("ai",  "@ai.image.infer",Capability.GPU, "AI image inference");

            // --- Crypto (optional pack) ---
            Register("crypto", "@crypto.session.bind", Capability.Crypto, "Bind crypto session");
            Register("crypto", "@crypto.kdf.derive",   Capability.Crypto, "KDF key derivation");
            Register("crypto", "@crypto.sign",          Capability.Crypto, "Sign data");
            Register("crypto", "@crypto.verify",        Capability.Crypto, "Verify signature");
            Register("crypto", "@crypto.encrypt",       Capability.Crypto, "Encrypt data");
            Register("crypto", "@crypto.decrypt",       Capability.Crypto, "Decrypt data");
            Register("crypto", "@crypto.hash",          Capability.Crypto, "Hash data");

            // --- SCX Chain ---
            Register("scx",  "@scx.chain.init",  Capability.Crypto, "Initialize SCX chain");
            Register("scx",  "@scx.chain.append",Capability.Crypto, "Append to SCX chain");
            Register("scx",  "@scx.chain.prove", Capability.Crypto, "Prove chain entry");
            Register("scx",  "@scx.chain.verify",Capability.Crypto, "Verify chain");

            // --- Securolink / OAuth ---
            Register("auth", "@securolink.env.load", Capability.Network, "Load Securolink env");
            Register("auth", "@oauth.session.assert",Capability.Network, "Assert OAuth session");

            // Mutations replay as recorded mutations rather than being silently re-executed.
            foreach (var def in _verbs.Values.Where(v =>
                v.Effect == EffectClass.StateMutation ||
                v.Effect == EffectClass.ExternalMutation))
            {
                def.Replay = ReplayLaw.RecordMutation;
            }
        }

        private static void Register(string category, string verb, Capability cap, string description)
        {
            var contract = InferContract(category, verb);

            _verbs[verb] = new VerbDef
            {
                Name = verb,
                Category = category,
                RequiredCap = cap,
                Description = description,
                LegalFolds = contract.Folds,
                LegalLanes = contract.Lanes,
                Effect = contract.Effect,
                Replay = contract.Replay,
                Deterministic = contract.Deterministic
            };
        }

        private static VerbContract InferContract(string category, string verb)
        {
            switch (category)
            {
                case "core":
                    return C(
                        new[] { "Yax", "Sek", "Xul" },
                        new[] { "phase" },
                        EffectClass.Control,
                        ReplayLaw.Deterministic,
                        true);

                case "async":
                    return C(
                        new[] { "Sek" },
                        new[] { "event", "agent" },
                        verb == "@sleep" || verb == "@await"
                            ? EffectClass.Control
                            : EffectClass.Process,
                        ReplayLaw.RecordResult,
                        false);

                case "state":
                    return C(
                        new[] { "Wo", "Sek", "Xul" },
                        new[] { "memory" },
                        verb == "@get" ? EffectClass.Pure : EffectClass.StateMutation,
                        ReplayLaw.Deterministic,
                        true);

                case "type":
                    return C(
                        new[] { "Wo", "Sek" },
                        new[] { "memory", "tensor" },
                        EffectClass.Pure,
                        ReplayLaw.Deterministic,
                        true);

                case "math":
                    return C(
                        new[] { "Sek" },
                        new[] { "tensor" },
                        EffectClass.Pure,
                        verb == "@rand" ? ReplayLaw.RecordResult : ReplayLaw.Deterministic,
                        verb != "@rand");

                case "log":
                    return C(
                        new[] { "Ch'en", "Xul" },
                        new[] { "event" },
                        EffectClass.Observation,
                        ReplayLaw.RecordResult,
                        true);

                case "import":
                    return C(
                        new[] { "Pop", "Yax" },
                        new[] { "manifest", "tool" },
                        verb == "@cap.revoke"
                            ? EffectClass.StateMutation
                            : EffectClass.Control,
                        ReplayLaw.RecordResult,
                        true);

                case "net":
                    return C(
                        new[] { "Sek" },
                        new[] { "network", "web" },
                        verb == "@http.get" || verb == "@ipc.read"
                            ? EffectClass.ExternalRead
                            : EffectClass.ExternalMutation,
                        ReplayLaw.RecordResult,
                        false);

                case "io":
                    return C(
                        new[] { "Pop", "Sek" },
                        new[] { "file", "memory" },
                        verb == "@file.read" || verb == "@dir.list" ||
                        verb == "@kv.get" || verb == "@idb.query"
                            ? EffectClass.ExternalRead
                            : EffectClass.ExternalMutation,
                        ReplayLaw.RecordResult,
                        false);

                case "ui":
                    return C(
                        new[] { "Ch'en" },
                        new[] { "event" },
                        EffectClass.ExternalMutation,
                        ReplayLaw.RecordResult,
                        false);

                case "gpu":
                    return C(
                        new[] { "Sek" },
                        new[] { "gpu", "tensor" },
                        verb == "@gpu.buffer.read"
                            ? EffectClass.ExternalRead
                            : EffectClass.Compute,
                        ReplayLaw.RecordResult,
                        false);

                case "ai":
                    return C(
                        new[] { "Sek" },
                        verb == "@ai.embed"
                            ? new[] { "model", "tensor" }
                            : new[] { "model" },
                        EffectClass.Inference,
                        ReplayLaw.RecordResult,
                        false);

                case "crypto":
                    return C(
                        new[] { "Sek", "Ch'en" },
                        new[] { "tool", "memory" },
                        verb == "@crypto.verify" || verb == "@crypto.hash"
                            ? EffectClass.Pure
                            : EffectClass.Compute,
                        ReplayLaw.RecordResult,
                        verb == "@crypto.verify" || verb == "@crypto.hash");

                case "scx":
                    return C(
                        new[] { "Sek", "Ch'en", "Xul" },
                        new[] { "memory", "event" },
                        verb == "@scx.chain.verify" || verb == "@scx.chain.prove"
                            ? EffectClass.Observation
                            : EffectClass.StateMutation,
                        ReplayLaw.Deterministic,
                        true);

                case "auth":
                    return C(
                        new[] { "Pop", "Yax" },
                        new[] { "network", "manifest" },
                        EffectClass.ExternalRead,
                        ReplayLaw.RecordResult,
                        false);

                default:
                    return C(
                        new[] { "Sek" },
                        new[] { "tool" },
                        EffectClass.Control,
                        ReplayLaw.RecordResult,
                        false);
            }
        }

        private static VerbContract C(
            string[] folds,
            string[] lanes,
            EffectClass effect,
            ReplayLaw replay,
            bool deterministic) =>
            new VerbContract
            {
                Folds = folds,
                Lanes = lanes,
                Effect = effect,
                Replay = replay,
                Deterministic = deterministic
            };

        private static VerbDef Clone(VerbDef source) =>
            new VerbDef
            {
                Name = source.Name,
                Category = source.Category,
                RequiredCap = source.RequiredCap,
                Description = source.Description,
                LegalFolds = source.LegalFolds?.ToArray() ?? Array.Empty<string>(),
                LegalLanes = source.LegalLanes?.ToArray() ?? Array.Empty<string>(),
                Effect = source.Effect,
                Replay = source.Replay,
                Deterministic = source.Deterministic
            };

        private sealed class VerbContract
        {
            public string[] Folds { get; set; }
            public string[] Lanes { get; set; }
            public EffectClass Effect { get; set; }
            public ReplayLaw Replay { get; set; }
            public bool Deterministic { get; set; }
        }
    }

    public class VerbDef
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public Capability RequiredCap { get; set; }
        public string Description { get; set; }
        public string[] LegalFolds { get; set; } = Array.Empty<string>();
        public string[] LegalLanes { get; set; } = Array.Empty<string>();
        public EffectClass Effect { get; set; }
        public ReplayLaw Replay { get; set; }
        public bool Deterministic { get; set; }
    }

    public class VerbAdmission
    {
        public bool Admitted { get; set; }
        public string Reason { get; set; }
        public string Verb { get; set; }
        public string Category { get; set; }
        public string Fold { get; set; }
        public string Lane { get; set; }
        public Capability RequiredCapability { get; set; }
        public EffectClass Effect { get; set; }
        public ReplayLaw Replay { get; set; }
        public bool Deterministic { get; set; }

        public static VerbAdmission Deny(
            string verb,
            string reason,
            VerbDef def = null,
            string fold = null) =>
            new VerbAdmission
            {
                Admitted = false,
                Reason = reason,
                Verb = verb,
                Category = def?.Category,
                Fold = fold,
                RequiredCapability = def?.RequiredCap ?? Capability.None,
                Effect = def?.Effect ?? EffectClass.Control,
                Replay = def?.Replay ?? ReplayLaw.RecordResult,
                Deterministic = def?.Deterministic ?? false
            };
    }

    public enum EffectClass
    {
        Pure,
        Control,
        StateMutation,
        ExternalRead,
        ExternalMutation,
        Observation,
        Compute,
        Process,
        Inference
    }

    public enum ReplayLaw
    {
        Deterministic,
        RecordResult,
        RecordMutation,
        NonReplayable
    }

    [Flags]
    public enum Capability
    {
        None       = 0,
        Network    = 1 << 0,
        Filesystem = 1 << 1,
        GPU        = 1 << 2,
        Process    = 1 << 3,
        Crypto     = 1 << 4,
        UI         = 1 << 5,
        IPC        = 1 << 6,
        Audio      = 1 << 7,
        Video      = 1 << 8,
        Eval       = 1 << 9,
        Dom        = 1 << 10,
        All        = (1 << 11) - 1
    }
}
