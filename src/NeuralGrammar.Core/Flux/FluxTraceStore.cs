#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NeuralGrammar.Core.Flux
{
    /// <summary>
    /// Durable @flux lineage store. Saves per-tick execution traces under
    /// <c>{dataRoot}/flux/{sessionId}/{tick}.json</c> and supports replay recovery.
    /// Authority remains with XCFE/K'UHUL; this store is only a time-travel carrier.
    /// </summary>
    public sealed class FluxTraceStore
    {
        private readonly string _root;

        public FluxTraceStore(string? dataRoot = null)
        {
            _root = dataRoot ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".learning");
            Directory.CreateDirectory(_root);
        }

        /// <summary>Logical session identifier. Defaults to "default".</summary>
        public string SessionId { get; set; } = "default";

        public string SessionDirectory => Path.Combine(_root, "flux", SessionId);

        /// <summary>Persist a complete trace JSON string (PowerShell interop friendly).</summary>
        public void Save(int tick, string traceJson)
        {
            if (string.IsNullOrWhiteSpace(traceJson))
                throw new ArgumentException("Trace JSON required", nameof(traceJson));

            var dir = SessionDirectory;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{tick}.json");
            var temp = path + ".tmp";
            File.WriteAllText(temp, traceJson);
            File.Move(temp, path, overwrite: true);
        }

        /// <summary>Persist a typed <see cref="FluxTrace"/>.</summary>
        public void Save(FluxTrace trace)
        {
            if (trace == null) throw new ArgumentNullException(nameof(trace));
            Save(trace.Tick, JsonSerializer.Serialize(trace, JsonOptions.Pretty));
        }

        /// <summary>Load a single tick from disk.</summary>
        public FluxTrace? Load(int tick)
        {
            var path = Path.Combine(SessionDirectory, $"{tick}.json");
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<FluxTrace>(json, JsonOptions.Pretty);
        }

        /// <summary>Load a single tick as raw JSON (PowerShell interop friendly).</summary>
        public string? LoadRawJson(int tick)
        {
            var path = Path.Combine(SessionDirectory, $"{tick}.json");
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path);
        }

        /// <summary>Return all persisted ticks as raw JSON strings.</summary>
        public IReadOnlyDictionary<int, string> LoadSessionRawJson()
        {
            var dir = SessionDirectory;
            var result = new SortedDictionary<int, string>();
            if (!Directory.Exists(dir)) return result;

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!int.TryParse(name, out var tick)) continue;
                try { result[tick] = File.ReadAllText(file); }
                catch { }
            }
            return result;
        }

        /// <summary>Return all persisted ticks for the current session.</summary>
        public IReadOnlyDictionary<int, FluxTrace> LoadSession()
        {
            var dir = SessionDirectory;
            var result = new SortedDictionary<int, FluxTrace>();
            if (!Directory.Exists(dir)) return result;

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!int.TryParse(name, out var tick)) continue;
                try
                {
                    var json = File.ReadAllText(file);
                    var trace = JsonSerializer.Deserialize<FluxTrace>(json, JsonOptions.Pretty);
                    if (trace != null) result[tick] = trace;
                }
                catch { /* tolerate corrupt trace files */ }
            }
            return result;
        }

        /// <summary>List known session identifiers.</summary>
        public IReadOnlyList<string> ListSessions()
        {
            var dir = Path.Combine(_root, "flux");
            if (!Directory.Exists(dir)) return Array.Empty<string>();
            return Directory.GetDirectories(dir)
                .Select(Path.GetFileName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Cast<string>()
                .ToList();
        }

        /// <summary>Export the entire current session as a single JSON document.</summary>
        public string ExportSession()
        {
            var traces = LoadSession();
            var envelope = new
            {
                schema = "nnc-k/flux-session/v1",
                session_id = SessionId,
                exported_at = DateTime.UtcNow,
                ticks = traces.Keys.ToList(),
                traces = traces.Values.ToList()
            };
            return JsonSerializer.Serialize(envelope, JsonOptions.Pretty);
        }

        /// <summary>Delete every trace for the current session.</summary>
        public void ClearSession()
        {
            var dir = SessionDirectory;
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*.json"))
                try { File.Delete(file); } catch { }
        }
    }
}
