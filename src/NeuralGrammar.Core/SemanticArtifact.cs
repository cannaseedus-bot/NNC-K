#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuralGrammar.Core
{
    // ── Artifact Types ──────────────────────────────────────────────────

    public enum ArtifactKind
    {
        Notation,        // semantic shorthand / compression glyph
        ResearchNote,    // observation with evidence[]
        Evidence,        // sourced finding with provenance
        Hypothesis,      // unverified relation
        Correction,      // supersedes bad state
        Improvement      // proposed behavior change
    }

    public enum AdmissionStatus
    {
        Pending,         // proposed, not yet evaluated
        Admitted,        // passed threshold -> queryable
        Rejected,        // below threshold / contradictory
        Superseded       // replaced by a newer version
    }

    /// <summary>Lifecycle state for semantic notations.</summary>
    public enum NotationStatus
    {
        Proposed,
        Observed,
        Supported,
        Admitted,
        Canonical,
        Superseded,
        Disproven
    }

    /// <summary>
    /// A semantic artifact is any model-generated or curated piece of
    /// information that lives in the information plane rather than the
    /// behavior plane.  It is NOT a micronaut — it is information
    /// available to micronauts.
    ///
    /// Artifacts are subject to admission policy.  A proposal by the
    /// model does not automatically become established knowledge.
    /// </summary>
    public sealed record SemanticArtifact
    {
        public string Id { get; init; } = "";
        public ArtifactKind Kind { get; init; } = ArtifactKind.Notation;
        public AdmissionStatus Status { get; set; } = AdmissionStatus.Pending;
        public string Subject { get; init; } = "";
        public string Content { get; init; } = "";
        public string Glyph { get; init; } = "";          // optional compression symbol
        public double Confidence { get; init; } = 0.0;
        public string Source { get; init; } = "model";
        public List<string> Tags { get; init; } = new();
        public List<string> Evidence { get; init; } = new();    // sourced references
        public List<string> Contradictions { get; init; } = new();
        public List<string> Relations { get; init; } = new();   // edge IDs
        public string? Supersedes { get; init; }                // ID of artifact this replaces
        public Dictionary<string, string> Metadata { get; init; } = new();
        public DateTime Created { get; init; } = DateTime.UtcNow;
        public DateTime? AdmittedAt { get; set; }
    }

    /// <summary>
    /// A directed edge between two semantic artifacts in the information
    /// plane.  Edges are what make the artifact set a graph rather than
    /// a flat list.
    /// </summary>
    public sealed record SemanticRelation
    {
        public string Id { get; init; } = "";
        public string FromId { get; init; } = "";
        public string ToId { get; init; } = "";
        public string Kind { get; init; } = "related";  // uses, supports, contradicts, improves, invokes
        public double Confidence { get; init; } = 1.0;
        public string Source { get; init; } = "model";
        public DateTime Created { get; init; } = DateTime.UtcNow;
    }

    // ── Artifact Store ─────────────────────────────────────────────────

    /// <summary>
    /// Thread-safe store for semantic artifacts and their relations.
    /// All artifacts enter as Pending; admission policy determines which
    /// become queryable via Admitted().
    ///
    /// Information plane — NOT behavior plane.  Artifacts are available
    /// to micronauts and K'UHUL programs, not executed as programs.
    /// </summary>
    public sealed class SemanticArtifactStore
    {
        private readonly List<SemanticArtifact> _artifacts = new();
        private readonly List<SemanticRelation> _relations = new();
        private readonly object _lock = new();

        // ── Artifact CRUD ─────────────────────────────────────────────

        /// <summary>Propose a new artifact (starts as Pending).</summary>
        public SemanticArtifact Propose(SemanticArtifact artifact)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));

            var admitted = artifact with
            {
                Id = string.IsNullOrWhiteSpace(artifact.Id)
                    ? Guid.NewGuid().ToString("N").Substring(0, 16)
                    : artifact.Id,
                Status = AdmissionStatus.Pending,
                Created = artifact.Created == default ? DateTime.UtcNow : artifact.Created
            };

            lock (_lock) _artifacts.Add(admitted);
            return admitted;
        }

        /// <summary>Admit a pending artifact (makes it queryable).</summary>
        public bool Admit(string id)
        {
            lock (_lock)
            {
                var a = _artifacts.Find(x => x.Id == id);
                if (a == null || a.Status != AdmissionStatus.Pending) return false;
                a.Status = AdmissionStatus.Admitted;
                a.AdmittedAt = DateTime.UtcNow;
                return true;
            }
        }

        /// <summary>Reject a pending artifact.</summary>
        public bool Reject(string id, string? reason = null)
        {
            lock (_lock)
            {
                var a = _artifacts.Find(x => x.Id == id);
                if (a == null || a.Status != AdmissionStatus.Pending) return false;
                a.Status = AdmissionStatus.Rejected;
                if (reason != null) a.Metadata["rejected_reason"] = reason;
                return true;
            }
        }

        /// <summary>Supersede an admitted artifact — mark it superseded and point to the replacement.</summary>
        public bool Supersede(string id, string replacementId)
        {
            lock (_lock)
            {
                var a = _artifacts.Find(x => x.Id == id);
                if (a == null || a.Status == AdmissionStatus.Pending) return false;
                a.Status = AdmissionStatus.Superseded;
                a.Metadata["superseded_by"] = replacementId;
                return true;
            }
        }

        // ── Lifecycle Operations ──────────────────────────────────────

        /// <summary>Observe an artifact — record that this was encountered.</summary>
        public SemanticArtifact Observe(string subject, string content, double confidence = 0.6)
        {
            if (string.IsNullOrWhiteSpace(subject))
                throw new ArgumentNullException(nameof(subject));

            var artifact = new SemanticArtifact
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 16),
                Subject = subject.Trim().ToLowerInvariant(),
                Content = content ?? "",
                Kind = ArtifactKind.Notation,
                Confidence = Math.Max(0, Math.Min(1, confidence)),
                Source = "model",
                Metadata = new Dictionary<string, string>
                {
                    ["status"] = NotationStatus.Observed.ToString()
                }
            };

            lock (_lock) _artifacts.Add(artifact);
            return artifact;
        }

        /// <summary>Support an artifact with evidence — create a supporting relation.</summary>
        public SemanticRelation Support(string artifactId, string evidenceId)
        {
            if (string.IsNullOrWhiteSpace(artifactId))
                throw new ArgumentNullException(nameof(artifactId));

            var relation = new SemanticRelation
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 16),
                FromId = evidenceId,
                ToId = artifactId,
                Kind = "supports",
                Confidence = 1.0,
                Source = "model",
                Created = DateTime.UtcNow
            };

            lock (_lock)
            {
                _relations.Add(relation);
                var a = _artifacts.Find(x => x.Id == artifactId);
                if (a != null && a.Status == AdmissionStatus.Admitted)
                {
                    if (!a.Metadata.ContainsKey("status"))
                        a.Metadata["status"] = NotationStatus.Supported.ToString();
                }
            }

            return relation;
        }

        /// <summary>Contradict an artifact — create a contradicting relation.</summary>
        public SemanticRelation Contradict(string artifactId, string contradictingId)
        {
            var relation = new SemanticRelation
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 16),
                FromId = contradictingId,
                ToId = artifactId,
                Kind = "contradicts",
                Confidence = 1.0,
                Source = "model",
                Created = DateTime.UtcNow
            };

            lock (_lock)
            {
                _relations.Add(relation);
                var a = _artifacts.Find(x => x.Id == artifactId);
                if (a != null)
                    a.Metadata["status"] = NotationStatus.Disproven.ToString();
            }

            return relation;
        }

        /// <summary>Promote an artifact to canonical — marks any existing canonical for this subject as superseded.</summary>
        public SemanticArtifact PromoteToCanonical(string artifactId)
        {
            lock (_lock)
            {
                var a = _artifacts.Find(x => x.Id == artifactId);
                if (a == null) throw new ArgumentException("Artifact not found: " + artifactId);
                if (a.Status < AdmissionStatus.Admitted)
                    a.Status = AdmissionStatus.Admitted;

                // Supersede any existing canonical for this subject
                foreach (var existing in _artifacts.Where(x =>
                    x.Subject == a.Subject &&
                    x.Id != a.Id &&
                    x.Metadata.TryGetValue("status", out var s) && s == NotationStatus.Canonical.ToString()))
                {
                    existing.Status = AdmissionStatus.Superseded;
                    existing.Metadata["superseded_by"] = a.Id;
                }

                a.Metadata["status"] = NotationStatus.Canonical.ToString();
                a.AdmittedAt = DateTime.UtcNow;
                return a;
            }
        }

        /// <summary>Correct an artifact — supresede it and create a replacement with a correction relation.</summary>
        public SemanticArtifact Correct(string artifactId, string newContent, double confidence = 0.9)
        {
            SemanticArtifact? original;
            lock (_lock) original = _artifacts.Find(x => x.Id == artifactId);
            if (original == null) throw new ArgumentException("Artifact not found: " + artifactId);

            var correction = new SemanticArtifact
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 16),
                Subject = original.Subject,
                Content = newContent,
                Kind = ArtifactKind.Correction,
                Status = AdmissionStatus.Admitted,
                Confidence = confidence,
                Source = "model",
                Supersedes = artifactId,
                Metadata = new Dictionary<string, string>
                {
                    ["status"] = NotationStatus.Canonical.ToString(),
                    ["corrects"] = artifactId
                },
                Created = DateTime.UtcNow
            };

            lock (_lock)
            {
                _artifacts.Add(correction);
                var orig = _artifacts.Find(x => x.Id == artifactId);
                if (orig != null)
                {
                    orig.Status = AdmissionStatus.Superseded;
                    orig.Metadata["superseded_by"] = correction.Id;
                    orig.Metadata["status"] = NotationStatus.Superseded.ToString();
                }

                _relations.Add(new SemanticRelation
                {
                    Id = Guid.NewGuid().ToString("N").Substring(0, 16),
                    FromId = correction.Id,
                    ToId = artifactId,
                    Kind = "corrects",
                    Confidence = 1.0,
                    Source = "model",
                    Created = DateTime.UtcNow
                });
            }

            return correction;
        }

        // ── Query ─────────────────────────────────────────────────────

        /// <summary>All artifacts matching a predicate, optionally filtered by status.</summary>
        public List<SemanticArtifact> Query(
            Func<SemanticArtifact, bool>? predicate = null,
            AdmissionStatus? minStatus = AdmissionStatus.Pending)
        {
            lock (_lock)
            {
                var q = _artifacts.AsEnumerable();
                if (minStatus.HasValue)
                    q = q.Where(a => a.Status >= minStatus.Value);
                if (predicate != null)
                    q = q.Where(predicate);
                return q.OrderByDescending(a => a.Confidence)
                        .ThenByDescending(a => a.Created)
                        .ToList();
            }
        }

        /// <summary>Admitted artifacts only, optionally filtered.</summary>
        public List<SemanticArtifact> Admitted(Func<SemanticArtifact, bool>? predicate = null)
            => Query(predicate, AdmissionStatus.Admitted);

        /// <summary>Get artifacts by subject.</summary>
        public List<SemanticArtifact> GetBySubject(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject))
                return new List<SemanticArtifact>();
            var norm = subject.Trim().ToLowerInvariant();
            return Admitted(a => a.Subject?.Equals(norm, StringComparison.OrdinalIgnoreCase) == true);
        }

        /// <summary>Get artifacts by kind.</summary>
        public List<SemanticArtifact> GetByKind(ArtifactKind kind)
            => Admitted(a => a.Kind == kind);

        /// <summary>Search admitted artifacts by keyword in content or subject.</summary>
        public List<SemanticArtifact> Search(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
                return new List<SemanticArtifact>();
            var kw = keyword.Trim().ToLowerInvariant();
            return Admitted(a =>
                (a.Content?.Contains(kw, StringComparison.OrdinalIgnoreCase) == true) ||
                (a.Subject?.Contains(kw, StringComparison.OrdinalIgnoreCase) == true) ||
                (a.Glyph?.Contains(kw, StringComparison.OrdinalIgnoreCase) == true) ||
                (a.Tags?.Any(t => t.Contains(kw, StringComparison.OrdinalIgnoreCase)) == true));
        }

        /// <summary>Apply admission policy: auto-admit artifacts with confidence >= threshold.</summary>
        public int RunAdmissionPolicy(double threshold = 0.8)
        {
            int count = 0;
            lock (_lock)
            {
                foreach (var a in _artifacts.Where(x =>
                    x.Status == AdmissionStatus.Pending &&
                    x.Confidence >= threshold))
                {
                    a.Status = AdmissionStatus.Admitted;
                    a.AdmittedAt = DateTime.UtcNow;
                    count++;
                }
            }
            return count;
        }

        public int TotalCount
        {
            get { lock (_lock) return _artifacts.Count; }
        }

        public int AdmittedCount
        {
            get { lock (_lock) return _artifacts.Count(a => a.Status == AdmissionStatus.Admitted); }
        }

        // ── Relations ─────────────────────────────────────────────────

        public void AddRelation(SemanticRelation relation)
        {
            if (relation == null) return;
            lock (_lock) _relations.Add(relation with
            {
                Id = string.IsNullOrWhiteSpace(relation.Id)
                    ? "rel_" + Guid.NewGuid().ToString("N").Substring(0, 12)
                    : relation.Id
            });
        }

        public List<SemanticRelation> GetRelations(string artifactId)
        {
            lock (_lock)
            {
                return _relations
                    .Where(r => r.FromId == artifactId || r.ToId == artifactId)
                    .OrderByDescending(r => r.Confidence)
                    .ToList();
            }
        }

        public List<SemanticRelation> AllRelations
        {
            get { lock (_lock) return _relations.ToList(); }
        }

        // ── Convenience factories ─────────────────────────────────────

        public SemanticArtifact AddNote(
            string subject, string content,
            double confidence = 0.7, string[]? tags = null)
            => Propose(new SemanticArtifact
            {
                Id = "art_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Kind = ArtifactKind.Notation,
                Subject = subject.Trim().ToLowerInvariant(),
                Content = content,
                Confidence = Math.Max(0, Math.Min(1, confidence)),
                Source = "model",
                Tags = tags?.ToList() ?? new()
            });

        public SemanticArtifact AddResearch(
            string subject, string content,
            double confidence = 0.8,
            string[]? evidence = null, string[]? tags = null)
            => Propose(new SemanticArtifact
            {
                Id = "art_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Kind = ArtifactKind.ResearchNote,
                Subject = subject.Trim().ToLowerInvariant(),
                Content = content,
                Confidence = Math.Max(0, Math.Min(1, confidence)),
                Source = "model",
                Evidence = evidence?.ToList() ?? new(),
                Tags = tags?.ToList() ?? new()
            });

        public SemanticArtifact AddHypothesis(
            string subject, string content,
            double confidence = 0.5,
            string[]? evidence = null, string[]? contradictions = null)
            => Propose(new SemanticArtifact
            {
                Id = "art_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Kind = ArtifactKind.Hypothesis,
                Subject = subject.Trim().ToLowerInvariant(),
                Content = content,
                Confidence = Math.Max(0, Math.Min(1, confidence)),
                Source = "model",
                Evidence = evidence?.ToList() ?? new(),
                Contradictions = contradictions?.ToList() ?? new()
            });

        public SemanticArtifact AddCorrection(
            string subject, string correction, string? supersedes = null,
            double confidence = 0.9, string[]? evidence = null)
            => Propose(new SemanticArtifact
            {
                Id = "art_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Kind = ArtifactKind.Correction,
                Subject = subject.Trim().ToLowerInvariant(),
                Content = correction,
                Confidence = Math.Max(0, Math.Min(1, confidence)),
                Source = "model",
                Supersedes = supersedes,
                Evidence = evidence?.ToList() ?? new()
            });

        public SemanticArtifact AddImprovement(
            string subject, string suggestion,
            double confidence = 0.7)
            => Propose(new SemanticArtifact
            {
                Id = "art_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                Kind = ArtifactKind.Improvement,
                Subject = subject.Trim().ToLowerInvariant(),
                Content = suggestion,
                Confidence = Math.Max(0, Math.Min(1, confidence)),
                Source = "model"
            });
    }
}
