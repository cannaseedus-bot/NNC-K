using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// Micronaut semantic filesystem curator.
    ///
    /// Responsibilities:
    ///   discover -> inspect -> ask semantic reshaper -> normalize -> validate
    ///   -> hash -> commit -> optionally publish
    ///
    /// The manager does NOT own live routing or execution admission.
    /// OSS-GPT proposes classifications/reshapes; deterministic validation and
    /// this manager own filesystem mutation. XCFE remains runtime authority.
    /// </summary>
    public sealed class MicronautManager
    {
        private readonly string _root;
        private readonly IMicronautSemanticReshaper _reshaper;
        private readonly IMicronautPublisher _publisher;
        private readonly SemaphoreSlim _mutationGate = new(1, 1);
        private readonly List<ReplayEvent> _semanticEvents = new();
        private readonly object _semanticLock = new();
        public MicronautRegister Register { get; set; }
        public MicronautIndex Index { get; private set; } = new();
public MicronautNotebook Notebook { get; } = new();

        /// <summary>Events emitted by the post-admission pipeline (semantic.link, semantic.cluster).</summary>
        public IReadOnlyList<ReplayEvent> SemanticEvents
        {
            get { lock (_semanticLock) return _semanticEvents.ToList(); }
        }

        private static readonly Regex CanonicalName =
            new(@"^[a-z0-9]+(?:-[a-z0-9]+)*\.json$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public MicronautManager(
            string micronautDirectory,
            IMicronautSemanticReshaper reshaper,
            IMicronautPublisher publisher = null)
        {
            Index.BuildFromDirectory(_root);
            if (string.IsNullOrWhiteSpace(micronautDirectory))
                throw new ArgumentException("Micronaut directory required.", nameof(micronautDirectory));

            _root = Path.GetFullPath(micronautDirectory);
            _reshaper = reshaper ?? throw new ArgumentNullException(nameof(reshaper));
            _publisher = publisher;
            Directory.CreateDirectory(_root);
        }

        public string RootDirectory => _root;

        public IReadOnlyList<string> Discover()
        {
            if (!Directory.Exists(_root)) return Array.Empty<string>();

            return Directory
                .EnumerateFiles(_root, "*.json", SearchOption.AllDirectories)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>Load all discovered micronauts into the algebraic register.</summary>
        public int LoadToRegister()
        {
            if (Register == null) return 0;

            var files = Discover();
            int count = 0;

            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var id = root.TryGetProperty("id", out var idProp)
                        ? idProp.GetString() ?? Guid.NewGuid().ToString("N").Substring(0, 12)
                        : Guid.NewGuid().ToString("N").Substring(0, 12);

                    var subject = root.TryGetProperty("intent", out var subj)
                        ? subj.GetString() ?? Path.GetFileNameWithoutExtension(file)
                        : Path.GetFileNameWithoutExtension(file);

                    var capability = root.TryGetProperty("language", out var cap)
                        ? cap.GetString() ?? "general" : "general";

                    Register.Register(new MicronautNode
                    {
                        Id = "mgmt_" + id,
                        Subject = subject,
                        Capability = capability,
                        Brain = "curator",
                        Phase = FoldPhase.Yax,
                        Quality = 100.0,
                        IsSeed = false,
                        Source = "curator"
                    });
                    count++;
                }
                catch { /* skip malformed — non-blocking */ }
            }

            return count;
        }

        /// <summary>
        /// Curate all discovered micronauts through the semantic reshaper.
        /// Skips files that already have a canonical semantic shape.
        /// </summary>
        public async Task<int> CurateAllAsync(
            MicronautContext context = null,
            CancellationToken ct = default)
        {
            var files = Discover();
            int curated = 0;
            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) break;
                var result = await CurateAsync(file, context, ct).ConfigureAwait(false);
                if (result.Ok) curated++;
            }
            return curated;
        }

        /// <summary>
        /// Curates one JSON artifact. No mutation occurs until the proposed
        /// semantic shape passes deterministic validation.
        /// </summary>
        public async Task<MicronautMutationResult> CurateAsync(
            string path,
            MicronautContext context = null,
            CancellationToken ct = default)
        {
            var sourcePath = ResolveInsideRoot(path);
            if (!File.Exists(sourcePath))
                return MicronautMutationResult.Rejected(sourcePath, "source:not_found");

            var sourceJson = await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false);

            JsonDocument sourceDoc;
            try { sourceDoc = JsonDocument.Parse(sourceJson); }
            catch (JsonException ex)
            {
                return MicronautMutationResult.Rejected(
                    sourcePath, "source:invalid_json:" + ex.Message);
            }

            using (sourceDoc)
            {
                var request = new MicronautReshapeRequest
                {
                    SourceFileName = Path.GetFileName(sourcePath),
                    SourceJson = sourceDoc.RootElement.Clone(),
                    Context = context ?? new MicronautContext()
                };

                // Model output is a proposal only.
                var proposal = await _reshaper
                    .ProposeAsync(request, ct)
                    .ConfigureAwait(false);

                var validation = ValidateProposal(proposal);
                if (!validation.Ok)
                    return MicronautMutationResult.Rejected(
                        sourcePath, validation.Errors.ToArray());

                var canonical = Normalize(proposal);
                var destination = ResolveInsideRoot(canonical.CanonicalFileName);

                var canonicalJson = JsonSerializer.Serialize(
                    canonical,
                    JsonOptions.Pretty);

                var hash = Sha256(canonicalJson);
                canonical.ContentHash = "sha256:" + hash;

                // Hash is part of the committed document, so serialize once more.
                canonicalJson = JsonSerializer.Serialize(
                    canonical,
                    JsonOptions.Pretty);

                await _mutationGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await CommitAtomicAsync(
                        sourcePath,
                        destination,
                        canonicalJson,
                        ct).ConfigureAwait(false);
                }
                finally
                {
                    _mutationGate.Release();
                }

                // Update the algebraic register with the curated node.
                if (Register != null)
                    Register.Register(new MicronautNode
                    {
                        Id = "curated_" + Sha256(canonical.Intent ?? "unknown").Substring(0, 12),
                        Subject = canonical.Intent ?? "curated",
                        Capability = canonical.Language ?? "general",
                        Brain = "curator",
                        Phase = FoldPhase.Yax,
                        Quality = 100.0,
                        IsSeed = false,
                        IsDaemon = false,
                        Source = "curator"
                    });

                if (_publisher != null)
                    await _publisher.PublishAsync(canonical, ct).ConfigureAwait(false);

                // Post-admission pipeline: emit semantic events.
                var linkEvents = EmitSemanticLinkEvents(canonical);
                var clusterEvents = EmitSemanticClusterEvents(canonical);
                lock (_semanticLock) _semanticEvents.AddRange(linkEvents);
                lock (_semanticLock) _semanticEvents.AddRange(clusterEvents);

                return MicronautMutationResult.Committed(
                    sourcePath,
                    destination,
                    canonical.ContentHash,
                    canonical.Intent,
                    canonical.Language);
            }
        }

        /// <summary>
        /// Classifies an utterance into composable semantic neighborhoods.
        /// Example: "Hola!" -> greeting + es + conversation.
        /// This returns candidates; XCFE decides what is admitted.
        /// </summary>
        public async Task<MicronautActivationPlan> ResolveAsync(
            string input,
            MicronautContext context = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new MicronautActivationPlan();

            var classification = await _reshaper.ClassifyAsync(
                input,
                context ?? new MicronautContext(),
                ct).ConfigureAwait(false);

            classification ??= new MicronautClassification();

            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(classification.Intent))
                candidates.Add(ToCanonicalFileName(classification.Intent));

            if (!string.IsNullOrWhiteSpace(classification.Language))
            {
                var languageName = LanguageFileName(classification.Language);
                if (!string.IsNullOrWhiteSpace(languageName))
                    candidates.Add(languageName);
            }

            foreach (var semantic in classification.Semantics ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(semantic))
                    candidates.Add(ToCanonicalFileName(semantic));
            }

            // Return only artifacts that actually exist locally.
            var mounted = candidates
                .Select(name => new
                {
                    Name = name,
                    Path = Path.Combine(_root, name)
                })
                .Where(x => File.Exists(x.Path))
                .Select(x => new MicronautCandidate
                {
                    FileName = x.Name,
                    Path = x.Path,
                    Intent = classification.Intent ?? "",
                    Language = classification.Language ?? "",
                    Confidence = Clamp01(classification.Confidence)
                })
                .ToArray();

            return new MicronautActivationPlan
            {
                Input = input,
                Intent = classification.Intent ?? "",
                Language = classification.Language ?? "",
                Semantics = (classification.Semantics ?? Array.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Candidates = mounted
            };
        }

        public MicronautValidationResult ValidateProposal(MicronautReshapeProposal proposal)
        {
            var errors = new List<string>();

            if (proposal == null)
                return MicronautValidationResult.Fail("proposal:null");

            if (string.IsNullOrWhiteSpace(proposal.Intent))
                errors.Add("intent:missing");

            if (string.IsNullOrWhiteSpace(proposal.CanonicalFileName))
                errors.Add("filename:missing");
            else
            {
                var file = Path.GetFileName(proposal.CanonicalFileName);
                if (!string.Equals(file, proposal.CanonicalFileName, StringComparison.Ordinal))
                    errors.Add("filename:path_not_allowed");
                if (!CanonicalName.IsMatch(file))
                    errors.Add("filename:not_canonical");
            }

            if (proposal.Data.ValueKind != JsonValueKind.Object &&
                proposal.Data.ValueKind != JsonValueKind.Array)
                errors.Add("data:object_or_array_required");

            if (proposal.Confidence < 0 || proposal.Confidence > 1)
                errors.Add("confidence:out_of_range");

            return errors.Count == 0
                ? MicronautValidationResult.Pass()
                : MicronautValidationResult.Fail(errors.ToArray());
        }

        private static MicronautArtifactDocument Normalize(
            MicronautReshapeProposal proposal)
        {
            var intent = Slug(proposal.Intent);
            var file = ToCanonicalFileName(
                string.IsNullOrWhiteSpace(proposal.CanonicalFileName)
                    ? intent
                    : Path.GetFileNameWithoutExtension(proposal.CanonicalFileName));

            return new MicronautArtifactDocument
            {
                Protocol = "nnck-micronaut/1",
                CanonicalFileName = file,
                Intent = intent,
                Language = NormalizeLanguage(proposal.Language),
                Aliases = (proposal.Aliases ?? Array.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Semantics = (proposal.Semantics ?? Array.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(Slug)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Lane = Slug(proposal.Lane),
                FoldAffinity = NormalizeFold(proposal.FoldAffinity),
                Capabilities = (proposal.Capabilities ?? Array.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(Slug)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                Confidence = Clamp01(proposal.Confidence),
                Data = proposal.Data.Clone(),
                UpdatedUtc = DateTime.UtcNow
            };
        }

        private async Task CommitAtomicAsync(
            string source,
            string destination,
            string json,
            CancellationToken ct)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? _root);

            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temp, json, new UTF8Encoding(false), ct)
                .ConfigureAwait(false);

            try
            {
                if (File.Exists(destination))
                {
                    // Merge safety: do not silently destroy a canonical artifact.
                    var existing = await File.ReadAllTextAsync(destination, ct)
                        .ConfigureAwait(false);

                    if (!string.Equals(existing, json, StringComparison.Ordinal))
                    {
                        var history = Path.Combine(_root, ".history");
                        Directory.CreateDirectory(history);
                        var backup = Path.Combine(
                            history,
                            Path.GetFileNameWithoutExtension(destination) +
                            "." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") +
                            ".json");
                        File.Copy(destination, backup, overwrite: false);
                    }

                    File.Delete(destination);
                }

                File.Move(temp, destination);

                if (!string.Equals(
                        Path.GetFullPath(source),
                        Path.GetFullPath(destination),
                        StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(source))
                {
                    File.Delete(source);
                }
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private string ResolveInsideRoot(string path)
        {
            var candidate = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(_root, path));

            var rootPrefix = _root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? _root
                : _root + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate, _root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path escapes Micronaut root.");

            return candidate;
        }

        public static string ToCanonicalFileName(string value)
        {
            var slug = Slug(Path.GetFileNameWithoutExtension(value ?? ""));
            if (string.IsNullOrWhiteSpace(slug)) slug = "unclassified";
            return slug + ".json";
        }

        private static string LanguageFileName(string language)
        {
            var lang = NormalizeLanguage(language);
            return lang switch
            {
                "es" => "spanish.json",
                "en" => "english.json",
                "fr" => "french.json",
                "de" => "german.json",
                "it" => "italian.json",
                "pt" => "portuguese.json",
                "ja" => "japanese.json",
                "ko" => "korean.json",
                "zh" => "chinese.json",
                _ => string.IsNullOrWhiteSpace(lang) ? "" : ToCanonicalFileName(lang)
            };
        }

        private static string NormalizeLanguage(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var v = value.Trim().ToLowerInvariant().Replace('_', '-');
            var dash = v.IndexOf('-');
            return dash > 0 ? v.Substring(0, dash) : v;
        }

        private static string NormalizeFold(string fold)
        {
            if (string.IsNullOrWhiteSpace(fold)) return "";
            foreach (var canonical in new[] { "Pop", "Wo", "Yax", "Sek", "Ch'en", "Xul" })
                if (string.Equals(canonical, fold.Trim(), StringComparison.OrdinalIgnoreCase))
                    return canonical;
            return "";
        }

        private static string Slug(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var s = value.Trim().ToLowerInvariant();
            s = Regex.Replace(s, @"[^a-z0-9]+", "-");
            return s.Trim('-');
        }

        private static double Clamp01(double value) =>
            value < 0 ? 0 : value > 1 ? 1 : value;

        private static string Sha256(string text) =>
            Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(text ?? ""))
            ).ToLowerInvariant();

        /// <summary>Emit semantic.link events — connect the committed micronaut to semantically similar nodes.</summary>
        private List<ReplayEvent> EmitSemanticLinkEvents(MicronautArtifactDocument doc)
        {
            var events = new List<ReplayEvent>();
            if (Register == null) return events;

            var docTerms = Slug(doc.Intent ?? "")
                .Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (docTerms.Length == 0) return events;

            foreach (var node in Register.All)
            {
                var nodeTerms = Slug(node.Subject ?? "")
                    .Split('-', StringSplitOptions.RemoveEmptyEntries);
                if (nodeTerms.Length == 0) continue;

                var overlap = docTerms
                    .Intersect(nodeTerms, StringComparer.OrdinalIgnoreCase)
                    .Count();
                if (overlap == 0) continue;

                var score = (double)overlap / Math.Max(docTerms.Length, nodeTerms.Length);
                if (score < 0.25) continue;

                events.Add(new ReplayEvent
                {
                    Type = "semantic.link",
                    SourceId = doc.CanonicalFileName,
                    TargetId = node.Id,
                    Label = node.Subject ?? "",
                    Score = score
                });
                events.Add(new ReplayEvent
                {
                    Type = "semantic.link",
                    SourceId = node.Id,
                    TargetId = doc.CanonicalFileName,
                    Label = doc.Intent ?? "",
                    Score = score
                });
            }
            return events;
        }

        /// <summary>Emit semantic.cluster events — assign the committed micronaut to the dominant intent cluster.</summary>
        private List<ReplayEvent> EmitSemanticClusterEvents(MicronautArtifactDocument doc)
        {
            var events = new List<ReplayEvent>();
            if (Register == null) return events;

            var docTerms = Slug(doc.Intent ?? "")
                .Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (docTerms.Length == 0) return events;

            // Cluster by first intent token (the broadest category).
            var clusterCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in Register.All)
            {
                var terms = Slug(node.Subject ?? "")
                    .Split('-', StringSplitOptions.RemoveEmptyEntries);
                if (terms.Length > 0)
                {
                    var first = terms[0];
                    if (first.Length > 1)
                    {
                        clusterCounts.TryGetValue(first, out var c);
                        clusterCounts[first] = c + 1;
                    }
                }
            }

            var docFirst = docTerms[0];
            var dominant = clusterCounts.Count > 0
                ? clusterCounts.OrderByDescending(kv => kv.Value).First()
                : new KeyValuePair<string, int>(docFirst, 1);

            events.Add(new ReplayEvent
            {
                Type = "semantic.cluster",
                SourceId = doc.CanonicalFileName,
                Label = dominant.Key,
                Score = (double)dominant.Value / Math.Max(Register.Count, 1)
            });

            return events;
        }
    }

    /// <summary>
    /// Adapter boundary for OSS-GPT or another semantic model.
    /// It may propose meaning; it cannot mutate the filesystem.
    /// </summary>
    public interface IMicronautSemanticReshaper
    {
        Task<MicronautReshapeProposal> ProposeAsync(
            MicronautReshapeRequest request,
            CancellationToken ct = default);

        Task<MicronautClassification> ClassifyAsync(
            string input,
            MicronautContext context,
            CancellationToken ct = default);
    }

    public sealed class DefaultSemanticReshaper : IMicronautSemanticReshaper
    {
        public Task<MicronautReshapeProposal> ProposeAsync(
            MicronautReshapeRequest request,
            CancellationToken ct = default)
        {
            var proposal = new MicronautReshapeProposal
            {
                Intent = request?.SourceFileName ?? "general",
                Language = "en",
                Confidence = 0.5,
                Data = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()
            };
            return Task.FromResult(proposal);
        }

        public Task<MicronautClassification> ClassifyAsync(
            string input, MicronautContext context,
            CancellationToken ct = default)
        {
            return Task.FromResult(new MicronautClassification
            {
                Intent = "general", Language = "en",
                Confidence = 0.5,
                Semantics = new[] { "general" }
            });
        }
    }

    /// <summary>
    /// Optional deployment boundary, e.g. Supernaut -> GAS -> Drive cache.
    /// </summary>
    public interface IMicronautPublisher
    {
        Task PublishAsync(
            MicronautArtifactDocument micronaut,
            CancellationToken ct = default);
    }

    public sealed class MicronautReshapeRequest
    {
        public string SourceFileName { get; init; } = "";
        public JsonElement SourceJson { get; init; }
        public MicronautContext Context { get; init; } = new();
    }

    public sealed class MicronautReshapeProposal
    {
        public string CanonicalFileName { get; init; } = "";
        public string Intent { get; init; } = "";
        public string Language { get; init; } = "";
        public string[] Aliases { get; init; } = Array.Empty<string>();
        public string[] Semantics { get; init; } = Array.Empty<string>();
        public string Lane { get; init; } = "";
        public string FoldAffinity { get; init; } = "";
        public string[] Capabilities { get; init; } = Array.Empty<string>();
        public double Confidence { get; init; }
        public JsonElement Data { get; init; }
    }

    public sealed class MicronautClassification
    {
        public string Intent { get; init; } = "";
        public string Language { get; init; } = "";
        public string[] Semantics { get; init; } = Array.Empty<string>();
        public double Confidence { get; init; }
    }

    public sealed class MicronautContext
    {
        public string UserId { get; init; } = "";
        public string PreferredLanguage { get; init; } = "";
        public IReadOnlyDictionary<string, string> UserPreferences { get; init; }
            = new Dictionary<string, string>();
    }

    public sealed class MicronautArtifactDocument
    {
        public string Protocol { get; set; } = "nnck-micronaut/1";
        public string CanonicalFileName { get; set; } = "";
        public string Intent { get; set; } = "";
        public string Language { get; set; } = "";
        public string[] Aliases { get; set; } = Array.Empty<string>();
        public string[] Semantics { get; set; } = Array.Empty<string>();
        public string Lane { get; set; } = "";
        public string FoldAffinity { get; set; } = "";
        public string[] Capabilities { get; set; } = Array.Empty<string>();
        public double Confidence { get; set; }
        public JsonElement Data { get; set; }
        public string ContentHash { get; set; } = "";
        public DateTime UpdatedUtc { get; set; }
    }

    public sealed class MicronautCandidate
    {
        public string FileName { get; init; } = "";
        public string Path { get; init; } = "";
        public string Intent { get; init; } = "";
        public string Language { get; init; } = "";
        public double Confidence { get; init; }
    }

    public sealed class MicronautActivationPlan
    {
        public string Input { get; init; } = "";
        public string Intent { get; init; } = "";
        public string Language { get; init; } = "";
        public string[] Semantics { get; init; } = Array.Empty<string>();
        public IReadOnlyList<MicronautCandidate> Candidates { get; init; }
            = Array.Empty<MicronautCandidate>();
    }

    public sealed class MicronautMutationResult
    {
        public bool Ok { get; init; }
        public string SourcePath { get; init; } = "";
        public string DestinationPath { get; init; } = "";
        public string ContentHash { get; init; } = "";
        public string Intent { get; init; } = "";
        public string Language { get; init; } = "";
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

        public static MicronautMutationResult Committed(
            string source, string destination, string hash, string intent, string language) =>
            new()
            {
                Ok = true,
                SourcePath = source,
                DestinationPath = destination,
                ContentHash = hash,
                Intent = intent,
                Language = language
            };

        public static MicronautMutationResult Rejected(
            string source, params string[] errors) =>
            new()
            {
                Ok = false,
                SourcePath = source,
                Errors = errors ?? Array.Empty<string>()
            };
    }

    public sealed class MicronautValidationResult
    {
        public bool Ok { get; init; }
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

        public static MicronautValidationResult Pass() => new() { Ok = true };

        public static MicronautValidationResult Fail(params string[] errors) =>
            new()
            {
                Ok = false,
                Errors = errors?.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray()
                    ?? Array.Empty<string>()
            };
    }

    // ── Notation / Research Note System ──────────────────────────────────

    /// <summary>
    /// A notation is a model-generated annotation attached to a micronaut
    /// subject, topic, or research question. It carries the model's analysis,
    /// improvement suggestions, cross-references, and confidence.
    ///
    /// Notations are persisted alongside micronauts and survive restarts.
    /// They are searchable via HybridSearch.
    /// </summary>
    public sealed class MicronautNotation
    {
        public string Id { get; init; } = "";
        public string Subject { get; init; } = "";          // linked topic/subject
        public string Type { get; init; } = "note";         // note | improvement | research | correction
        public string Content { get; init; } = "";          // free-text note
        public double Confidence { get; init; } = 0.8;
        public string Source { get; init; } = "model";      // model | user | curator
        public string[] Tags { get; init; } = Array.Empty<string>();
        public Dictionary<string, string> Metadata { get; init; } = new();
        public DateTime Created { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// A notebook collects notations and provides subject-based querying.
    /// Each MicronautManager has one notebook, shared across curation cycles.
    /// </summary>
    public sealed class MicronautNotebook
    {
        private readonly List<MicronautNotation> _notes = new();
        private readonly object _lock = new();

        /// <summary>All notations in the notebook.</summary>
        public IReadOnlyList<MicronautNotation> All
        {
            get { lock (_lock) return _notes.ToList(); }
        }

        /// <summary>Add a notation. Generates a deterministic id if empty.</summary>
        public void Add(MicronautNotation note)
        {
            if (note == null) return;
            lock (_lock)
            {
                _notes.Add(string.IsNullOrWhiteSpace(note.Id)
                    ? new MicronautNotation
                    {
                        Id = "note_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                        Subject = note.Subject,
                        Type = note.Type,
                        Content = note.Content,
                        Confidence = note.Confidence,
                        Source = note.Source,
                        Tags = note.Tags,
                        Metadata = note.Metadata,
                        Created = note.Created
                    }
                    : note);
            }
        }

        /// <summary>Get all notations for a subject.</summary>
        public List<MicronautNotation> GetBySubject(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject))
                return new List<MicronautNotation>();
            var norm = subject.Trim().ToLowerInvariant();
            lock (_lock)
            {
                return _notes
                    .Where(n => n.Subject?.Equals(norm, StringComparison.OrdinalIgnoreCase) == true)
                    .OrderByDescending(n => n.Created)
                    .ToList();
            }
        }

        /// <summary>Get notations by type (note, improvement, research, correction).</summary>
        public List<MicronautNotation> GetByType(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return new List<MicronautNotation>();
            var norm = type.Trim().ToLowerInvariant();
            lock (_lock)
            {
                return _notes
                    .Where(n => n.Type?.Equals(norm, StringComparison.OrdinalIgnoreCase) == true)
                    .OrderByDescending(n => n.Confidence)
                    .ToList();
            }
        }

        /// <summary>Search notations by keyword in content or subject.</summary>
        public List<MicronautNotation> Search(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new List<MicronautNotation>();
            var kw = keyword.Trim().ToLowerInvariant();
            lock (_lock)
            {
                return _notes
                    .Where(n => (n.Content?.Contains(kw, StringComparison.OrdinalIgnoreCase) == true)
                             || (n.Subject?.Contains(kw, StringComparison.OrdinalIgnoreCase) == true)
                             || (n.Tags?.Any(t => t.Contains(kw, StringComparison.OrdinalIgnoreCase)) == true))
                    .OrderByDescending(n => n.Confidence)
                    .ThenByDescending(n => n.Created)
                    .ToList();
            }
        }

        /// <summary>Total number of notations.</summary>
        public int Count
        {
            get { lock (_lock) return _notes.Count; }
        }

        /// <summary>Shortcut: add a research note with metadata.</summary>
        public MicronautNotation AddResearchNote(
            string subject,
            string content,
            double confidence = 0.8,
            string[] tags = null)
        {
            var note = new MicronautNotation
            {
                Id = "note_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Subject = subject?.Trim().ToLowerInvariant() ?? "",
                Type = "research",
                Content = content ?? "",
                Confidence = Math.Max(0, Math.Min(1, confidence)),
                Source = "model",
                Tags = tags ?? Array.Empty<string>(),
                Created = DateTime.UtcNow
            };
            Add(note);
            return note;
        }

        /// <summary>Shortcut: add an improvement annotation for a micronaut.</summary>
        public MicronautNotation AddImprovement(
            string subject,
            string suggestion,
            double confidence = 0.7)
        {
            var note = new MicronautNotation
            {
                Id = "note_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Subject = subject?.Trim().ToLowerInvariant() ?? "",
                Type = "improvement",
                Content = suggestion ?? "",
                Confidence = Math.Max(0, Math.Min(1, confidence)),
                Source = "model",
                Created = DateTime.UtcNow
            };
            Add(note);
            return note;
        }

        /// <summary>Shortcut: add a correction (when a micronaut's info is wrong).</summary>
        public MicronautNotation AddCorrection(
            string subject,
            string correction,
            double confidence = 0.9)
        {
            var note = new MicronautNotation
            {
                Id = "note_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Subject = subject?.Trim().ToLowerInvariant() ?? "",
                Type = "correction",
                Content = correction ?? "",
                Confidence = Math.Max(0, Math.Min(1, confidence)),
                Source = "model",
                Created = DateTime.UtcNow
            };
            Add(note);
            return note;
        }
    }

    // ── JSON serialization options ─────────────────────────────────────

    public static class JsonOptions
    {
        public static readonly JsonSerializerOptions Pretty = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
