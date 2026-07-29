#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// MicronautIndex — typed identity index for O(1) micronaut resolution.
    /// Owned by MicronautManager. Maps every micronaut identity (name, engine,
    /// capability, program path) to a MicronautDescriptor. Prevents unresolved
    /// strings from leaking into PowerShell command resolution.
    /// </summary>
    public sealed record MicronautDescriptor(
        string Id,
        string Name,
        string Engine,
        string Capability,
        string Program,
        string Source,
        string Status,
        string[] Tags,
        string? ParentId,
        string? CreatedBy
    );

    public sealed class MicronautIndex
    {
        private readonly Dictionary<string, MicronautDescriptor> _byId = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<MicronautDescriptor>> _byEngine = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<MicronautDescriptor>> _byCapability = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MicronautDescriptor> _byName = new(StringComparer.OrdinalIgnoreCase);

        public int Count => _byId.Count;

        /// <summary>Registered engine names present in this index.</summary>
        public IReadOnlyCollection<string> Engines => _byEngine.Keys;

        /// <summary>Registered capability strings present in this index.</summary>
        public IReadOnlyCollection<string> Capabilities => _byCapability.Keys;

        /// <summary>All descriptors in this index.</summary>
        public IReadOnlyCollection<MicronautDescriptor> All => _byId.Values;

        /// <summary>Register or update a micronaut descriptor.</summary>
        public void Register(MicronautDescriptor descriptor)
        {
            if (descriptor == null) return;

            _byId[descriptor.Id] = descriptor;
            _byName[descriptor.Name] = descriptor;

            if (!string.IsNullOrWhiteSpace(descriptor.Engine))
            {
                if (!_byEngine.ContainsKey(descriptor.Engine))
                    _byEngine[descriptor.Engine] = new List<MicronautDescriptor>();
                var list = _byEngine[descriptor.Engine];
                list.RemoveAll(d => d.Id == descriptor.Id);
                list.Add(descriptor);
            }

            if (!string.IsNullOrWhiteSpace(descriptor.Capability))
            {
                if (!_byCapability.ContainsKey(descriptor.Capability))
                    _byCapability[descriptor.Capability] = new List<MicronautDescriptor>();
                var list = _byCapability[descriptor.Capability];
                list.RemoveAll(d => d.Id == descriptor.Id);
                list.Add(descriptor);
            }
        }

        /// <summary>O(1) lookup by id.</summary>
        public MicronautDescriptor? ResolveById(string id) =>
            _byId.TryGetValue(id, out var d) ? d : null;

        /// <summary>O(1) lookup by name.</summary>
        public MicronautDescriptor? ResolveByName(string name) =>
            _byName.TryGetValue(name, out var d) ? d : null;

        /// <summary>Lookup by engine type.</summary>
        public IReadOnlyList<MicronautDescriptor> ResolveByEngine(string engine) =>
            _byEngine.TryGetValue(engine, out var list) ? list.AsReadOnly() : Array.Empty<MicronautDescriptor>();

        /// <summary>Lookup by capability.</summary>
        public IReadOnlyList<MicronautDescriptor> ResolveByCapability(string capability) =>
            _byCapability.TryGetValue(capability, out var list) ? list.AsReadOnly() : Array.Empty<MicronautDescriptor>();

        /// <summary>Remove a micronaut from the index.</summary>
        public bool Remove(string id)
        {
            if (!_byId.TryGetValue(id, out var d)) return false;

            _byId.Remove(id);
            _byName.Remove(d.Name);

            if (!string.IsNullOrWhiteSpace(d.Engine) && _byEngine.TryGetValue(d.Engine, out var elist))
                elist.RemoveAll(x => x.Id == id);

            if (!string.IsNullOrWhiteSpace(d.Capability) && _byCapability.TryGetValue(d.Capability, out var clist))
                clist.RemoveAll(x => x.Id == id);

            return true;
        }

        /// <summary>Build the index from a directory tree of micronaut files.</summary>
        public int BuildFromDirectory(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                return 0;

            var patterns = new[] { "*.json", "*.kuhul", "*.kprog" };
            var files = patterns.SelectMany(p =>
                Directory.EnumerateFiles(rootPath, p, SearchOption.AllDirectories));

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

                    var name = root.TryGetProperty("subject", out var s)
                        ? s.GetString() ?? id : id;

                    var capability = root.TryGetProperty("capability", out var cap)
                        ? cap.GetString() ?? "" : "";

                    var engine = root.TryGetProperty("fold", out var f)
                        ? f.GetString() ?? "" : "";

                    var kind = root.TryGetProperty("type", out var t)
                        ? t.GetString() ?? "" : "";

                    var descriptor = new MicronautDescriptor(
                        Id: id,
                        Name: name,
                        Engine: engine,
                        Capability: capability,
                        Program: file,
                        Source: kind,
                        Status: "indexed",
                        Tags: Array.Empty<string>(),
                        ParentId: null,
                        CreatedBy: null
                    );

                    Register(descriptor);
                    count++;
                }
                catch { }
            }

            return count;
        }
    }
}
