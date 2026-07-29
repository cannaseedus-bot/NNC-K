#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// K'UHUL -> SCXQ2 Lowering.
    ///
    /// Converts a compiled K'UHUL program (.kprog) into a sequence of SCXQ2
    /// lanes. Each fold becomes one lane; the fold graph's next pointers become
    /// the execution schedule. Xul's body hash feeds Pop's input hash (closed
    /// loop invariant).
    ///
    ///   .kprog  ->  KuhulScxq2Lowering.Lower()  ->  SCXQ2 lanes
    ///                                                  |
    ///                                          GenerateSchedule() -> JSON
    ///                                                  |
    ///                                          XCFE / backends execute
    /// </summary>
    public static class KuhulScxq2Lowering
    {
        /// <summary>
        /// Lower a compiled K'UHUL program into SCXQ2 lanes.
        /// Each fold produces one lane, ordered by fold id.
        /// </summary>
        public static LoweringResult Lower(KuhulCompiledProgram program)
        {
            if (program == null)
                throw new InvalidOperationException("Program is null");

            if (program.Folds.Count == 0)
                throw new InvalidOperationException("Program has no folds");

            if (!program.IsClosedLoop)
                throw new InvalidOperationException(
                    "Program is not closed-loop: Xul.next must equal Pop.id (0)");

            var lanes = new List<Scxq2LaneLowered>();
            byte[] previousHash = new byte[32];

            for (int i = 0; i < program.Folds.Count; i++)
            {
                var fold = program.Folds[i];
                var body = new Dictionary<string, object?>
                {
                    ["fold_id"] = fold.Id,
                    ["phase"] = fold.Phase,
                    ["next"] = fold.Next,
                    ["op"] = fold.Op ?? "noop",
                };

                if (fold.Target != null) body["target"] = fold.Target;
                if (fold.Value != null) body["value"] = fold.Value;
                if (fold.Args != null && fold.Args.Count > 0)
                    body["args"] = fold.Args;
                if (fold.Decision != null) body["decision"] = fold.Decision;

                var nodes = program.Nodes
                    .Where(n => fold.NodeIds.Contains(n.Id))
                    .Select(n => new Dictionary<string, object?>
                    {
                        ["id"] = n.Id,
                        ["kind"] = n.Kind ?? "unknown",
                        ["op"] = n.Op,
                        ["target"] = n.Target,
                        ["value"] = n.Value,
                        ["operands"] = n.Operands.Select(o =>
                            new Dictionary<string, string?>
                            {
                                ["name"] = o.Name,
                                ["kind"] = o.Kind,
                                ["value"] = o.Value
                            }).ToList()
                    }).ToList();

                if (nodes.Count > 0) body["nodes"] = nodes;

                var bodyJson = JsonSerializer.Serialize(body);
                var bodyBytes = Encoding.UTF8.GetBytes(bodyJson);
                var bodyHash = SHA256.HashData(bodyBytes);

                lanes.Add(new Scxq2LaneLowered
                {
                    Tick = (ulong)i,
                    FoldId = fold.Id,
                    Phase = fold.Phase,
                    NextFold = fold.Next,
                    InputHash = previousHash,
                    BodyHash = bodyHash,
                    BodyJson = bodyJson,
                    BodyBytes = bodyBytes
                });

                previousHash = (i == program.Folds.Count - 1) ? bodyHash : new byte[32];
            }

            return new LoweringResult
            {
                ProgramId = program.Id,
                LaneCount = lanes.Count,
                Lanes = lanes,
                IsClosedLoop = true,
                EntryLaneId = 0
            };
        }

        /// <summary>
        /// Generate the SCXQ2 dispatch schedule as a readable JSON artifact.
        /// </summary>
        public static string GenerateSchedule(KuhulCompiledProgram program)
        {
            var result = Lower(program);
            var schedule = new Dictionary<string, object?>
            {
                ["program"] = result.ProgramId,
                ["entry"] = result.EntryLaneId,
                ["closed_loop"] = result.IsClosedLoop,
                ["lanes"] = result.Lanes.Select((l, i) =>
                    new Dictionary<string, object?>
                    {
                        ["index"] = i,
                        ["tick"] = l.Tick,
                        ["fold_id"] = l.FoldId,
                        ["phase"] = l.Phase,
                        ["next_fold"] = l.NextFold,
                        ["input_hash"] = Convert.ToHexString(l.InputHash).ToLowerInvariant(),
                        ["body_hash"] = Convert.ToHexString(l.BodyHash).ToLowerInvariant(),
                        ["body"] = l.BodyJson
                    }).ToList()
            };

            return JsonSerializer.Serialize(
                schedule,
                new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>Result of lowering a compiled program to SCXQ2 lanes.</summary>
    public sealed class LoweringResult
    {
        public string ProgramId { get; init; } = "";
        public int LaneCount { get; init; }
        public List<Scxq2LaneLowered> Lanes { get; init; } = new();
        public bool IsClosedLoop { get; init; }
        public int EntryLaneId { get; init; }
    }

    /// <summary>One SCXQ2 lane produced by lowering a single fold.</summary>
    public sealed class Scxq2LaneLowered
    {
        public ulong Tick { get; init; }
        public int FoldId { get; init; }
        public string Phase { get; init; } = "";
        public int NextFold { get; init; }
        public byte[] InputHash { get; init; } = new byte[32];
        public byte[] BodyHash { get; init; } = new byte[32];
        public string BodyJson { get; init; } = "";
        public byte[] BodyBytes { get; init; } = Array.Empty<byte>();
    }
}
