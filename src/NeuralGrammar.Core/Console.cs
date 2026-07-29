using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// Micronaut Console — high-performance register buffer for system messages,
    /// tool calls, phase changes, and model events. Replaces the PowerShell ArrayList
    /// with a managed ring buffer that auto-prunes, supports categories,
    /// filtering, timestamps, and JSON export.
    /// </summary>
    public class MicronautConsole
    {
        public enum EntryCategory
        {
            System,       // Startup, shutdown, generic
            ToolCall,     // Tool invocation
            ToolResult,   // Tool output
            Context,      // Context loading
            Phase,        // Phase transitions
            Model,        // Model switching
            Error,        // Errors
            Debug,        // Debug messages
            Effect,       // Monad effect trace (IO/network call)
            Recovery,     // Fallback/recovery mechanism
            User,         // User actions
            Assistant,    // Assistant responses
        }

        public class ConsoleEntry
        {
            public int Seq { get; set; }
            public DateTime Timestamp { get; set; } = DateTime.Now;
            public string Message { get; set; }
            public EntryCategory Category { get; set; } = EntryCategory.System;
            public string Source { get; set; }  // e.g. "web_search", "phase-bridge"
            public Dictionary<string, object> Metadata { get; set; } = new();

            public string Formatted => $"[{Timestamp:HH:mm:ss}] [{Category}] {Message}";
            public string ShortForm => Category switch
            {
                EntryCategory.ToolCall => $"  {Message}",
                EntryCategory.ToolResult => $"    -> {Message}",
                EntryCategory.Phase => $"  {Message}",
                EntryCategory.Error => $"  {Message}",
                _ => $"  {Message}"
            };
        }

        private readonly List<ConsoleEntry> _entries = new();
        private readonly int _maxEntries;
        private int _seq;

        public MicronautRegister Register { get; set; }

        public MicronautConsole(int maxEntries = 500)
        {
            _maxEntries = maxEntries;
        }

        public int Count => _entries.Count;
        public IReadOnlyList<ConsoleEntry> Entries => _entries.AsReadOnly();

        // ---- Write methods ----

        public ConsoleEntry Write(string message, EntryCategory category = EntryCategory.System, string source = null)
        {
            var entry = new ConsoleEntry
            {
                Seq = _seq++,
                Message = message,
                Category = category,
                Source = source,
            };

            lock (_entries)
            {
                _entries.Add(entry);
                if (_entries.Count > _maxEntries)
                    _entries.RemoveRange(0, _entries.Count - _maxEntries);
            }

            return entry;
        }

        public ConsoleEntry WriteTool(string toolName, string args = null)
        {
            var msg = args != null ? $"{toolName}({args})" : toolName;
            return Write(msg, EntryCategory.ToolCall, toolName);
        }

        public ConsoleEntry WriteToolResult(string toolName, string result)
        {
            var preview = result?.Length > 80 ? result.Substring(0, 80) + "..." : result ?? "(empty)";
            return Write(preview, EntryCategory.ToolResult, toolName);
        }

        public ConsoleEntry WritePhase(string phase)
        {
            return Write($"Phase -> {phase}", EntryCategory.Phase, "phase-engine");
        }

        public ConsoleEntry WriteError(string message, string source = null)
        {
            return Write(message, EntryCategory.Error, source);
        }

        public ConsoleEntry WriteModel(string modelName, string action = "loaded")
        {
            return Write($"Model {action}: {modelName}", EntryCategory.Model, "model-router");
        }

        public ConsoleEntry WriteContext(string contextSource, int matches)
        {
            return Write($"Context: {matches} from {contextSource}", EntryCategory.Context, "micronaut-router");
        }

        public ConsoleEntry WriteRegister()
        {
            if (Register == null)
                return Write("Register: unavailable", EntryCategory.System, "console");

            var seeds = Register.Seeds.Count();
            var daemons = Register.Daemons.Count();
            var total = Register.Count;
            var byPhase = string.Join(", ",
                new[] { "Pop", "Wo", "Yax", "Sek", "Chen", "Xul" }
                    .Select((p, i) => $"{p}={Register.GetByPhase((FoldPhase)i).Count}"));

            return Write($"Register: {total} nodes ({seeds} seeds, {daemons} daemons) [{byPhase}]",
                EntryCategory.System, "console");
        }

        public ConsoleEntry WritePhaseWithRegister(string phase)
        {
            var entry = WritePhase(phase);
            if (Register != null) WriteRegister();
            return entry;
        }

        public ConsoleEntry WriteRouteResult(string intent, string brain, double confidence, string fold)
        {
            var msg = $"route: intent={intent} brain={brain} confidence={confidence:F2} fold={fold}";
            return Write(msg, EntryCategory.Phase, "xrfe-router");
        }

        public ConsoleEntry WriteMemory(int count, string source)
        {
            return Write($"memory: {count} from {source}", EntryCategory.Context, "memory-router");
        }

        public ConsoleEntry WriteMutation(string subject, int count)
        {
            return Write($"mutation: {count} micronauts seeded for '{subject}'",
                EntryCategory.System, "mutation-engine");
        }

        public ConsoleEntry WriteNotation(string type, string subject, double confidence)
        {
            return Write($"notation: {type} '{subject}' confidence={confidence:F2}",
                EntryCategory.System, "notation-engine");
        }

        public ConsoleEntry WriteFoldTrace(List<string> trace)
        {
            if (trace == null || trace.Count == 0)
                return Write("fold: (none)", EntryCategory.Phase, "fold-engine");

            var joined = string.Join(" -> ", trace);
            return Write($"fold: {joined}", EntryCategory.Phase, "fold-engine");
        }

        public ConsoleEntry WriteEffect(string operation, string status, string detail = null)
        {
            var msg = $"effect: {operation} -> {status}" +
                      (detail != null ? $" ({detail})" : "");
            var entry = Write(msg, EntryCategory.Effect, "trace");
            if (detail != null)
                entry.Metadata["detail"] = detail;
            entry.Metadata["operation"] = operation;
            entry.Metadata["status"] = status;
            entry.Metadata["timestamp"] = DateTime.UtcNow.ToString("o");
            return entry;
        }

        public ConsoleEntry WriteRecovery(string operation, string fallback, string reason = null)
        {
            var msg = $"recovery: {operation} -> {fallback}" +
                      (reason != null ? $" because: {reason}" : "");
            var entry = Write(msg, EntryCategory.Recovery, "trace");
            entry.Metadata["operation"] = operation;
            entry.Metadata["fallback"] = fallback;
            entry.Metadata["reason"] = reason ?? "exception";
            return entry;
        }

        public ConsoleEntry WriteDebug(string message, string source = null)
        {
            return Write(message, EntryCategory.Debug, source);
        }

        // ---- Read methods ----

        public List<ConsoleEntry> GetRecent(int count = 20)
        {
            lock (_entries)
            {
                return _entries.Skip(Math.Max(0, _entries.Count - count)).ToList();
            }
        }

        public List<ConsoleEntry> GetByCategory(EntryCategory category, int count = 20)
        {
            lock (_entries)
            {
                return _entries
                    .Where(e => e.Category == category)
                    .Reverse()
                    .Take(count)
                    .Reverse()
                    .ToList();
            }
        }

        public List<ConsoleEntry> Search(string term, int count = 20)
        {
            var t = term.ToLowerInvariant();
            lock (_entries)
            {
                return _entries
                    .Where(e => e.Message.ToLowerInvariant().Contains(t) ||
                                e.Source?.ToLowerInvariant().Contains(t) == true)
                    .Reverse()
                    .Take(count)
                    .Reverse()
                    .ToList();
            }
        }

        // ---- Management ----

        public void Clear()
        {
            lock (_entries)
            {
                _entries.Clear();
                _seq = 0;
            }
        }

        public void ClearCategory(EntryCategory category)
        {
            lock (_entries)
            {
                _entries.RemoveAll(e => e.Category == category);
            }
        }

        public int Prune(int max)
        {
            lock (_entries)
            {
                if (_entries.Count <= max) return 0;
                var remove = _entries.Count - max;
                _entries.RemoveRange(0, remove);
                return remove;
            }
        }

        // ---- Stats ----

        public Dictionary<EntryCategory, int> GetStats()
        {
            lock (_entries)
            {
                return _entries
                    .GroupBy(e => e.Category)
                    .ToDictionary(g => g.Key, g => g.Count());
            }
        }

        public int TotalEntries => _seq;

        // ---- Export ----

        public string ExportToJson(int maxEntries = 0)
        {
            lock (_entries)
            {
                var export = maxEntries > 0
                    ? _entries.Skip(Math.Max(0, _entries.Count - maxEntries)).ToList()
                    : _entries;

                return JsonSerializer.Serialize(new
                {
                    total = _seq,
                    exported = export.Count,
                    entries = export.Select(e => new
                    {
                        seq = e.Seq,
                        time = e.Timestamp.ToString("HH:mm:ss"),
                        category = e.Category.ToString(),
                        source = e.Source,
                        message = e.Message,
                    })
                }, new JsonSerializerOptions { WriteIndented = true });
            }
        }

        // ---- Batch write (for PowerShell interop) ----

        public string[] GetRecentStrings(int count = 30)
        {
            return GetRecent(count).Select(e => e.Formatted).ToArray();
        }
    }
}
