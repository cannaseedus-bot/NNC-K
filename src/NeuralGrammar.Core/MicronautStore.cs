using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Threading;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// MicronautStore — Thread-safe global state registry for the C# backend.
    /// Provides a central key-value store with typed access, change events,
    /// and optional disk persistence. Accessible from MCP servers, tool calls,
    /// model routing, phase management, and PowerShell interop.
    ///
    /// Patterns:
    ///   MicronautStore.Global.Set("phase.current", "Sek");
    ///   var phase = MicronautStore.Global.GetString("phase.current");
    ///   MicronautStore.Global.OnChange += (k, v) => Console.WriteLine($"{k} = {v}");
    /// </summary>
    public class MicronautStore
    {
        private static readonly Lazy<MicronautStore> _instance = new(() => new MicronautStore());
        public static MicronautStore Global => _instance.Value;

        private readonly ConcurrentDictionary<string, object> _data = new();
        private readonly string _persistPath;
        private Timer _saveTimer;
        private int _changeCount;

        public event Action<string, object> OnChange;
        public event Action<string, object> OnSet;
        public event Action<string> OnDelete;

        public MicronautStore(string persistPath = null)
        {
            _persistPath = persistPath ?? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, ".store", "micronaut_store.json");

            // Auto-load from disk
            Load();
        }

        // ── Auto-persistence ────────────────────────────────────────────────

        public void EnableAutoSave(int intervalMs = 5000)
        {
            _saveTimer?.Dispose();
            _saveTimer = new Timer(_ => Save(), null, intervalMs, intervalMs);
        }

        public void DisableAutoSave()
        {
            _saveTimer?.Dispose();
            _saveTimer = null;
        }

        public string PersistPath => _persistPath;

        // ── Core accessors ──────────────────────────────────────────────────

        public void Set(string key, object value)
        {
            if (value == null)
            {
                _data.TryRemove(key, out _);
                OnDelete?.Invoke(key);
                Interlocked.Increment(ref _changeCount);
                return;
            }

            _data[key] = value;
            Interlocked.Increment(ref _changeCount);
            OnSet?.Invoke(key, value);
            OnChange?.Invoke(key, value);
        }

        public object Get(string key) =>
            _data.TryGetValue(key, out var val) ? val : null;

        public T Get<T>(string key, T fallback = default)
        {
            if (_data.TryGetValue(key, out var val))
            {
                try { return (T)Convert.ChangeType(val, typeof(T)); }
                catch { return fallback; }
            }
            return fallback;
        }

        public string GetString(string key, string fallback = "") =>
            Get(key, fallback)?.ToString() ?? fallback;

        public int GetInt(string key, int fallback = 0) =>
            Get(key, fallback);

        public double GetDouble(string key, double fallback = 0.0) =>
            Get(key, fallback);

        public bool GetBool(string key, bool fallback = false) =>
            Get(key, fallback);

        public bool Contains(string key) => _data.ContainsKey(key);

        public void Delete(string key)
        {
            _data.TryRemove(key, out _);
            OnDelete?.Invoke(key);
            Interlocked.Increment(ref _changeCount);
        }

        public void Clear()
        {
            _data.Clear();
            Interlocked.Increment(ref _changeCount);
        }

        public int Count => _data.Count;
        public int ChangeCount => _changeCount;

        public IReadOnlyCollection<KeyValuePair<string, object>> Entries2
        {
            get { return _data.ToList().AsReadOnly(); }
        }

        // ── Namespace utilities ─────────────────────────────────────────────

        public string[] GetKeys(string prefix = null)
        {
            if (string.IsNullOrEmpty(prefix))
                return _data.Keys.ToArray();

            return _data.Keys.Where(k => k.StartsWith(prefix)).ToArray();
        }

        public Dictionary<string, object> GetNamespace(string prefix)
        {
            return _data
                .Where(kv => kv.Key.StartsWith(prefix))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        // ── Convenience setters for system state ────────────────────────────

        public void SetPhase(string phase) => Set("phase.current", phase);
        public string GetPhase() => GetString("phase.current", "Sek");

        public void SetModel(string model) => Set("model.active", model);
        public string GetModel() => GetString("model.active", "");

        public void SetEndpoint(string endpoint) => Set("endpoint.active", endpoint);

        public void Increment(string key, int delta = 1)
        {
            var val = GetInt(key, 0) + delta;
            Set(key, val);
        }

        // ── Bulk operations ─────────────────────────────────────────────────

        public void SetMany(IEnumerable<KeyValuePair<string, object>> entries)
        {
            foreach (var kv in entries)
                Set(kv.Key, kv.Value);
        }

        // ── Persistence ─────────────────────────────────────────────────────

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_persistPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_persistPath, json);
            }
            catch { }
        }

        public void Load()
        {
            if (File.Exists(_persistPath))
            {
                try
                {
                    var json = File.ReadAllText(_persistPath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    if (data != null)
                    {
                        foreach (var kv in data)
                        {
                            var val = kv.Value.ValueKind switch
                            {
                                JsonValueKind.String => kv.Value.GetString(),
                                JsonValueKind.Number => kv.Value.TryGetInt64(out var l) ? (object)l : kv.Value.GetDouble(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                JsonValueKind.Object => kv.Value.GetRawText(),
                                JsonValueKind.Array => kv.Value.GetRawText(),
                                _ => null
                            };
                            if (val != null)
                                _data[kv.Key] = val;
                        }
                    }
                }
                catch { }
            }
        }

        // ── JSON export for external tools ──────────────────────────────────

        public string ExportJson()
        {
            return JsonSerializer.Serialize(new
            {
                count = _data.Count,
                changes = _changeCount,
                store = _data.ToDictionary(kv => kv.Key, kv => kv.Value)
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
