#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// Bounded session cache for the Pop fold working set.
    ///
    /// Stores recent retrievals, notation references, note IDs, and
    /// artifact handles. Evicts by LRU when the limit is reached.
    /// The Pop fold queries this before hitting the permanent stores.
    ///
    /// This is NOT a permanent store — it's the active working memory
    /// that gets cycled per session. ArtifactStore, Notebook, and
    /// HybridSearch are the authorities.
    /// </summary>
    public sealed class SessionCache
    {
        private readonly int _limit;
        private readonly LinkedList<CacheEntry> _entries = new();
        private readonly Dictionary<string, LinkedListNode<CacheEntry>> _index = new();

        /// <summary>Called when an entry is evicted. Source = "design" entries can be persisted here.</summary>
        public Action<CacheEntry>? OnEvicted { get; set; }

        public SessionCache(int limit = 50)
        {
            _limit = Math.Max(1, limit);
        }

        public int Count => _entries.Count;
        public int Limit => _limit;
        public IReadOnlyList<CacheEntry> Entries
        {
            get
            {
                lock (_entries)
                {
                    return _entries.ToList().AsReadOnly();
                }
            }
        }

        /// <summary>Get an entry by key, promoting it to most-recently-used.</summary>
        public CacheEntry? Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            lock (_entries)
            {
                if (_index.TryGetValue(key, out var node))
                {
                    _entries.Remove(node);
                    _entries.AddFirst(node);
                    return node.Value;
                }
            }
            return null;
        }

        /// <summary>Store or update an entry. Evicts oldest if at limit.</summary>
        public void Set(string key, object value, string? source = null, double priority = 0.5)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            lock (_entries)
            {
                // Update existing
                if (_index.TryGetValue(key, out var node))
                {
                    node.Value.Value = value;
                    node.Value.Source = source ?? node.Value.Source;
                    node.Value.LastAccess = DateTime.UtcNow;
                    node.Value.AccessCount++;
                    _entries.Remove(node);
                    _entries.AddFirst(node);
                    return;
                }

                // Evict if at limit
                while (_entries.Count >= _limit)
                {
                    var last = _entries.Last;
                    if (last != null)
                    {
                        _index.Remove(last.Value.Key);
                        _entries.RemoveLast();
                        OnEvicted?.Invoke(last.Value);
                    }
                    else break;
                }

                // Add new
                var entry = new CacheEntry
                {
                    Key = key,
                    Value = value,
                    Source = source ?? "Pop",
                    Priority = priority,
                    Created = DateTime.UtcNow,
                    LastAccess = DateTime.UtcNow,
                    AccessCount = 1
                };
                var newNode = _entries.AddFirst(entry);
                _index[key] = newNode;
            }
        }

        /// <summary>Remove an entry.</summary>
        public bool Remove(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            lock (_entries)
            {
                if (_index.TryGetValue(key, out var node))
                {
                    _index.Remove(key);
                    _entries.Remove(node);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Clear all entries.</summary>
        public void Clear()
        {
            lock (_entries)
            {
                _entries.Clear();
                _index.Clear();
            }
        }

        /// <summary>Get entries by source.</summary>
        public List<CacheEntry> GetBySource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return new List<CacheEntry>();

            lock (_entries)
            {
                return _entries
                    .Where(e => string.Equals(e.Source, source, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        /// <summary>Get all keys currently in cache.</summary>
        public string[] GetKeys()
        {
            lock (_entries)
            {
                return _entries.Select(e => e.Key).ToArray();
            }
        }

        /// <summary>Check if a key exists without promoting it.</summary>
        public bool Contains(string key)
        {
            lock (_entries)
            {
                return _index.ContainsKey(key);
            }
        }

        /// <summary>Touch a key (promote to MRU) without returning value.</summary>
        public bool Touch(string key)
        {
            lock (_entries)
            {
                if (_index.TryGetValue(key, out var node))
                {
                    _entries.Remove(node);
                    _entries.AddFirst(node);
                    node.Value.LastAccess = DateTime.UtcNow;
                    return true;
                }
            }
            return false;
        }
    }

    public sealed class CacheEntry
    {
        public string Key { get; init; } = "";
        public object? Value { get; set; }
        public string Source { get; set; } = "Pop";
        public double Priority { get; init; } = 0.5;
        public DateTime Created { get; init; }
        public DateTime LastAccess { get; set; }
        public int AccessCount { get; set; }
        public string? TypeName => Value?.GetType().Name;

        public override string ToString() =>
            $"{Key} [{Source}] (acc={AccessCount}, priority={Priority:F2})";
    }

    /// <summary>Helpers for wiring memory Micronauts to the cache.</summary>
    public static class SessionCacheExtensions
    {
        /// <summary>
        /// Register an eviction handler that persists "design" source entries
        /// to the UI_DESIGN_REFERENCE.txt file so design work survives cache cycling.
        /// </summary>
        public static void RegisterDesignAutoSave(this SessionCache cache, string? refFilePath = null)
        {
            cache.OnEvicted += entry =>
            {
                if (entry?.Source != "design") return;
                if (entry.Value == null) return;

                try
                {
                    var path = refFilePath ?? System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "UI_DESIGN_REFERENCE.txt");

                    var dir = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrWhiteSpace(dir) && !System.IO.Directory.Exists(dir))
                        System.IO.Directory.CreateDirectory(dir);

                    var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    var content = entry.Value.ToString() ?? "";
                    var line = $"\n// Auto-saved from cache eviction [{timestamp}]: {entry.Key}\n{content}\n";

                    System.IO.File.AppendAllText(path, line);
                }
                catch
                {
                    // Swallow file I/O errors — cache eviction should never throw.
                }
            };
        }
    }
}
