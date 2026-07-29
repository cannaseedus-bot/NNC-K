using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

#nullable enable
using NeuralGrammar.Core;

namespace Kuhul.Pi;

/// <summary>
/// K'UHUL π host kernel.
///
/// Closed semantic cycle:
///     Pop -> Wo -> Yax -> Sek -> Ch'en -> Xul -> Pop
///
/// Pop() is the only public semantic entry point.
/// XCFE owns control/arbitration outside this class.
/// SCXQ2 is the deterministic IR emitted by Ch'en.
/// Backends consume SCXQ2; π never targets GPU APIs directly.
///
/// This file is the deterministic reference kernel. The closed-fold law and
/// boundary contracts are authoritative; the default Sek transform is only a
/// reference transform until a frozen π algebra is supplied.
/// </summary>
public sealed class KuhulPi
{
    public const string Version = "π-1.0.0";
    public const string Invariant = "collapse_only";

    private readonly IKuhulSource _source;
    private readonly IXcfeGate _xcfe;
    private readonly IScxq2Emitter _scxq2;
    private readonly IArtifactSink _artifacts;
    private readonly IKuhulPiAlgebra _algebra;

    private ulong _tick;
    private byte[] _previousTraceHash = new byte[32];
    public MicronautRegister? MicronautRegister { get; set; }

    public KuhulPi(
        IKuhulSource source,
        IXcfeGate xcfe,
        IScxq2Emitter scxq2,
        IArtifactSink artifacts,
        IKuhulPiAlgebra? algebra = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _xcfe = xcfe ?? throw new ArgumentNullException(nameof(xcfe));
        _scxq2 = scxq2 ?? throw new ArgumentNullException(nameof(scxq2));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _algebra = algebra ?? ReferenceKuhulPiAlgebra.Instance;
    }

    /// <summary>
    /// The ONLY public semantic phase entry.
    /// One call executes exactly one complete fold.
    /// Xul closes this fold; XCFE may admit another Pop.
    /// </summary>
    public FoldResult Pop()
    {
        var tick = checked(++_tick);

        // XCFE is control algebra. It may admit/reject a fold,
        // but it cannot mutate π semantics.
        var admission = _xcfe.Admit(new PopRequest(tick, _previousTraceHash));
        if (!admission.Admitted)
            return FoldResult.Rejected(tick, admission.Reason);

        var frame = new FoldFrame(tick, admission.Input);

        Wo(frame);       // establish immutable world
        Yax(frame);      // resolve topology/bindings
        Sek(frame);      // deterministic semantic transform
        Chen(frame);     // collapse + emit SCXQ2 artifact
        Xul(frame);      // seal proof + fold

        _previousTraceHash = frame.TraceHash.ToArray();

        var artifact = frame.Artifact
            ?? throw new KuhulPiViolation(
                "Ch'en completed without emitting an SCXQ2 artifact.");

        var result = new FoldResult(
            Tick: tick,
            Status: FoldStatus.Collapsed,
            Artifact: artifact,
            TraceHash: frame.TraceHash,
            Reason: null);

        _artifacts.Commit(artifact);
        _xcfe.Observe(result); // observation only; no π mutation

      return result;
    }

    // -----------------------------------------------------------------
    // Wo — immutable world/state declaration.
    // -----------------------------------------------------------------
    /// <summary>
    /// SHA-256 identity of a collapsed SCXQ2 semantic artifact.
    /// Same artifact bytes => same semantic hash, independent of backend.
    /// </summary>
    public static string Hash(Scxq2Artifact artifact)
    {
        if (artifact is null)
            throw new ArgumentNullException(nameof(artifact));

        return Convert.ToHexString(
            SHA256.HashData(artifact.Bytes.Span)
        ).ToLowerInvariant();
    }

    private static void Wo(FoldFrame f)
    {
        f.Advance(Phase.Pop, Phase.Wo);

        f.World = new WorldState(
            Version: Version,
            Invariant: Invariant,
            Tick: f.Tick,
            InputHash: Hash(f.Input.Span),
            Constants: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["pi"] = "3.14159265358979323846264338327950288419716939937510",
                ["fold"] = "Pop>Wo>Yax>Sek>Ch'en>Xul",
                ["entry"] = "Pop",
                ["ir"] = "SCXQ2"
            });
    }

    // -----------------------------------------------------------------
    // Yax — resolve references/topology into a canonical semantic field.
    // No branching authority is introduced here.
    // -----------------------------------------------------------------
    private void Yax(FoldFrame f)
    {
        f.Advance(Phase.Wo, Phase.Yax);
        f.Resolved = _source.Resolve(f.World!, f.Input);
        RequireCanonical(f.Resolved);
    }

    // -----------------------------------------------------------------
    // Sek — deterministic transform.
    // π does not schedule, retry, branch, or choose a backend.
    // -----------------------------------------------------------------
    private void Sek(FoldFrame f)
    {
        f.Advance(Phase.Yax, Phase.Sek);

        // Sek is law-gated through a deterministic algebra contract.
        // The kernel owns when transformation occurs; the algebra owns only
        // canonical-field -> canonical-signal semantics.
        var signal = _algebra.Transform(f.World!, f.Resolved!);

        if (signal.Bytes.IsEmpty)
            throw new KuhulPiViolation("Sek produced an empty semantic signal.");

        f.Signal = signal;
    }

    // -----------------------------------------------------------------
    // Ch'en — collapse to one artifact and emit SCXQ2.
    // -----------------------------------------------------------------
    private void Chen(FoldFrame f)
    {
        f.Advance(Phase.Sek, Phase.Chen);

        var signalHash = Hash(f.Signal!.Bytes.Span);

        var lane = new Scxq2Lane(
            Tick: f.Tick,
            InputHash: f.World!.InputHash,
            SignalHash: signalHash,
            Payload: f.Signal.Bytes);

        f.Artifact = _scxq2.Emit(lane);
    }

    // -----------------------------------------------------------------
    // Xul — seal the fold. Xul never directly calls Pop().
    // Returning control to XCFE avoids recursion and preserves the
    // architectural boundary: Xul -> [XCFE arbitration] -> Pop.
    // -----------------------------------------------------------------
    private static void Xul(FoldFrame f)
    {
        f.Advance(Phase.Chen, Phase.Xul);

        using var ms = new MemoryStream();
        WriteU64(ms, f.Tick);
        ms.Write(f.World!.InputHash);
        ms.Write(f.Signal!.Bytes.Span);
        ms.Write(f.Artifact!.Hash);

        f.TraceHash = Hash(ms.ToArray());
        f.Sealed = true;
    }

    private static void RequireCanonical(ResolvedField field)
    {
        if (field.CanonicalBytes.IsEmpty)
            throw new KuhulPiViolation("Yax produced an empty canonical field.");

        if (!field.IsCanonical)
            throw new KuhulPiViolation("Yax output violates canonical-form law.");
    }

    private static byte[] Hash(ReadOnlySpan<byte> data)
    {
        return SHA256.HashData(data);
    }

    private static void WriteU64(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    // =================================================================
    // Fold state
    // =================================================================

    private sealed class FoldFrame
    {
        public FoldFrame(ulong tick, ReadOnlyMemory<byte> input)
        {
            Tick = tick;
            Input = input;
            Phase = Phase.Pop;
        }

        public ulong Tick { get; }
        public ReadOnlyMemory<byte> Input { get; }
        public Phase Phase { get; private set; }

        public WorldState? World { get; set; }
        public ResolvedField? Resolved { get; set; }
        public SemanticSignal? Signal { get; set; }
        public Scxq2Artifact? Artifact { get; set; }
        public byte[] TraceHash { get; set; } = new byte[32];
        public bool Sealed { get; set; }

        public void Advance(Phase expected, Phase next)
        {
            if (Phase != expected)
                throw new KuhulPiViolation(
                    $"Illegal phase transition: expected {expected}, found {Phase}, requested {next}.");

            if (!PhaseLaw.IsLegal(expected, next))
                throw new KuhulPiViolation($"Illegal π transition {expected} -> {next}.");

            Phase = next;
        }
    }
}

// =====================================================================
// Closed-loop phase law
// =====================================================================

public enum Phase : byte
{
    Pop = 0,
    Wo = 1,
    Yax = 2,
    Sek = 3,
    Chen = 4,
    Xul = 5
}

public static class PhaseLaw
{
    public static bool IsLegal(Phase from, Phase to) =>
        (from, to) switch
        {
            (Phase.Pop, Phase.Wo) => true,
            (Phase.Wo, Phase.Yax) => true,
            (Phase.Yax, Phase.Sek) => true,
            (Phase.Sek, Phase.Chen) => true,
            (Phase.Chen, Phase.Xul) => true,
            _ => false
        };

    /// <summary>
    /// The architectural closure edge.
    /// It crosses the π/XCFE boundary rather than being an internal call.
    /// </summary>
    public const string Closure = "Xul -> XCFE -> Pop";
}

// =====================================================================
// Contracts
// =====================================================================

public interface IKuhulSource
{
    ResolvedField Resolve(WorldState world, ReadOnlyMemory<byte> input);
}

/// <summary>
/// Pure deterministic Sek algebra. Implementations must not schedule work,
/// perform network I/O, select backends, mutate XCFE, or depend on wall-clock
/// time/randomness. Given identical WorldState + ResolvedField, output must
/// be byte-identical.
/// </summary>
public interface IKuhulPiAlgebra
{
    SemanticSignal Transform(WorldState world, ResolvedField field);
}

public interface IXcfeGate
{
    PopAdmission Admit(PopRequest request);
    void Observe(FoldResult result);
}

public interface IScxq2Emitter
{
    Scxq2Artifact Emit(Scxq2Lane lane);
}

public interface IArtifactSink
{
    void Commit(Scxq2Artifact artifact);
}

public readonly record struct PopRequest(
    ulong Tick,
    ReadOnlyMemory<byte> PreviousTraceHash);

public readonly record struct PopAdmission(
    bool Admitted,
    ReadOnlyMemory<byte> Input,
    string? Reason)
{
    public static PopAdmission Accept(ReadOnlyMemory<byte> input) =>
        new(true, input, null);

    public static PopAdmission Reject(string reason) =>
        new(false, ReadOnlyMemory<byte>.Empty, reason);
}

public sealed record WorldState(
    string Version,
    string Invariant,
    ulong Tick,
    byte[] InputHash,
    IReadOnlyDictionary<string, string> Constants);

public sealed record ResolvedField(
    ReadOnlyMemory<byte> CanonicalBytes,
    bool IsCanonical);

public sealed record SemanticSignal(
    ReadOnlyMemory<byte> Bytes);

public sealed record Scxq2Lane(
    ulong Tick,
    byte[] InputHash,
    byte[] SignalHash,
    ReadOnlyMemory<byte> Payload);

public sealed record Scxq2Artifact(
    string Id,
    ReadOnlyMemory<byte> Bytes,
    byte[] Hash);

public enum FoldStatus
{
    Collapsed,
    Rejected
}

public sealed record FoldResult(
    ulong Tick,
    FoldStatus Status,
    Scxq2Artifact? Artifact,
    byte[] TraceHash,
    string? Reason)
{
    /// <summary>
    /// Content identity of the emitted SCXQ2 artifact.
    /// Null for rejected folds that emitted no artifact.
    /// </summary>
    public string? SemanticHash =>
        Artifact is null ? null : KuhulPi.Hash(Artifact);

    public static FoldResult Rejected(ulong tick, string? reason) =>
        new(tick, FoldStatus.Rejected, null, Array.Empty<byte>(), reason);
}

public sealed class KuhulPiViolation : InvalidOperationException
{
    public KuhulPiViolation(string message) : base(message) { }
}

// =====================================================================
// Minimal deterministic reference adapters
// =====================================================================

public sealed class Utf8KuhulSource : IKuhulSource
{
    public ResolvedField Resolve(WorldState world, ReadOnlyMemory<byte> input)
    {
        // Reference canonicalization: strict UTF-8 bytes are preserved.
        // A production Yax resolver can canonicalize KXML/XJSON/SVG-3D
        // semantic sources before producing this field.
        _ = new UTF8Encoding(false, true).GetString(input.Span);
        return new ResolvedField(input.ToArray(), true);
    }
}

/// <summary>
/// Deterministic reference Sek transform used until a frozen π algebra is
/// injected. It proves lifecycle/replay behavior; it is not the canonical
/// production π algebra.
/// </summary>
public sealed class ReferenceKuhulPiAlgebra : IKuhulPiAlgebra
{
    public static ReferenceKuhulPiAlgebra Instance { get; } = new();

    private ReferenceKuhulPiAlgebra() { }

    public SemanticSignal Transform(WorldState world, ResolvedField field)
    {
        _ = world;
        var src = field.CanonicalBytes.Span;
        var signal = new byte[src.Length];

        for (int i = 0; i < src.Length; i++)
            signal[i] = (byte)(src[i] ^ (byte)((i * 31 + 0x50) & 0xFF));

        return new SemanticSignal(signal);
    }
}

public sealed class Scxq2ReferenceEmitter : IScxq2Emitter
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SCXQ2PI\0");

    public Scxq2Artifact Emit(Scxq2Lane lane)
    {
        using var ms = new MemoryStream();

        ms.Write(Magic);
        WriteU64(ms, lane.Tick);
        ms.Write(lane.InputHash);
        ms.Write(lane.SignalHash);

        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, checked((uint)lane.Payload.Length));
        ms.Write(length);
        ms.Write(lane.Payload.Span);

        var bytes = ms.ToArray();
        var hash = SHA256.HashData(bytes);
        var id = Convert.ToHexString(hash).ToLowerInvariant();

        return new Scxq2Artifact(id, bytes, hash);
    }

    private static void WriteU64(Stream stream, ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}

public sealed class DirectoryArtifactSink : IArtifactSink
{
    private readonly string _directory;

    public DirectoryArtifactSink(string directory)
    {
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
    }

    public void Commit(Scxq2Artifact artifact)
    {
        var path = Path.Combine(_directory, $"{artifact.Id}.scxq2");
        File.WriteAllBytes(path, artifact.Bytes.ToArray());
    }
}

/// <summary>
/// Minimal XCFE adapter for testing. Production XCFE can arbitrate queues,
/// shards, recovery, scheduling, and admission while preserving π isolation.
/// </summary>
public sealed class QueueXcfeGate : IXcfeGate
{
    private readonly Queue<byte[]> _queue = new();

    public void Enqueue(string utf8) => _queue.Enqueue(Encoding.UTF8.GetBytes(utf8));
    public void Enqueue(byte[] bytes) => _queue.Enqueue(bytes.ToArray());

    public PopAdmission Admit(PopRequest request)
    {
        return _queue.Count == 0
            ? PopAdmission.Reject("XCFE has no admitted fold.")
            : PopAdmission.Accept(_queue.Dequeue());
    }

    public void Observe(FoldResult result)
    {
        // Intentionally read-only from π's perspective.
    }
}
