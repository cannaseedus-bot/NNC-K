using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NeuralGrammar.Core.XCFE;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// Unified IR Engine — KIMD tensor compute, SVG geometry, D3D11 projection, replay log.
    /// Matches asx-unified-ir-vector-surface.manifest.json (IR-1.0)
    /// </summary>
    public class UnifiedIREngine
    {
        private readonly Dictionary<string, IRTensor> _tensors = new();
        private readonly Dictionary<string, IRSvgPath> _paths = new();
        private readonly Dictionary<string, IRSurface> _surfaces = new();
        private readonly Dictionary<string, IRCluster> _clusters = new();
        private readonly ReplayLog _log = new();
        private readonly GPUProviderRegistry _gpu;
        private readonly GlyphRegistry _glyphs;
        private long _seq;

        public UnifiedIREngine(GPUProviderRegistry gpu = null, GlyphRegistry glyphs = null)
        {
            _gpu = gpu ?? new GPUProviderRegistry();
            _glyphs = glyphs ?? new GlyphRegistry();
        }

        public ReplayLog Log => _log;

        // ---- Core types ----

        public class IRTensor
        {
            public string Id { get; set; }
            public double[] Data { get; set; }
            public int[] Shape { get; set; }
            public double Phase { get; set; } // in pi units

            public int Rows => Shape != null && Shape.Length >= 1 ? Shape[0] : 0;
            public int Cols => Shape != null && Shape.Length >= 2 ? Shape[1] : 1;
            public int Size => Data?.Length ?? 0;
            public int Rank => Shape?.Length ?? 0;
        }

        public class IRSvgPath
        {
            public string Id { get; set; }
            public string D { get; set; } // SVG path d string
            public IRTensor SourceTensor { get; set; }
        }

        public class IRSurface
        {
            public string Id { get; set; }
            public List<IRTensor> Tensors { get; set; } = new();
            public List<IRSvgPath> Paths { get; set; } = new();
        }

        public class IRCluster
        {
            public string Id { get; set; }
            public List<IRTensor> Tensors { get; set; } = new();
            public double[] Center { get; set; }
            public string Plane { get; set; }
        }

        // ---- Replay log ----

        public class ReplayLog
        {
            private readonly List<ReplayEntry> _entries = new();

            public class ReplayEntry
            {
                public long Seq { get; set; }
                public string Op { get; set; }
                public object Inputs { get; set; }
                public object Outputs { get; set; }
                public string HashIn { get; set; }
                public string HashOut { get; set; }
                public string Kernel { get; set; }
                public string PrevHash { get; set; }
                public string EntryHash { get; set; }
                public DateTime Timestamp { get; set; }
                public string Version { get; set; } = "IR-1.1";
            }

            public void Append(long seq, string op, object inputs, object outputs, string kernel)
            {
                var hashIn = ComputeHash(JsonSerializer.Serialize(inputs));
                var hashOut = ComputeHash(JsonSerializer.Serialize(outputs));
                var prev = _entries.Count == 0 ? "GENESIS" : _entries[^1].EntryHash;

                var entry = new ReplayEntry
                {
                    Seq = seq,
                    Op = op,
                    Inputs = inputs,
                    Outputs = outputs,
                    HashIn = hashIn,
                    HashOut = hashOut,
                    Kernel = kernel,
                    PrevHash = prev,
                    Timestamp = DateTime.UtcNow
                };

                entry.EntryHash = ComputeHash(
                    $"{entry.Seq}|{entry.Op}|{entry.HashIn}|{entry.HashOut}|{entry.Kernel}|{entry.PrevHash}|{entry.Version}");

                _entries.Add(entry);
            }

            public bool Verify(out string error)
            {
                string prev = "GENESIS";
                long expectedSeq = _entries.Count == 0 ? 0 : _entries[0].Seq;

                foreach (var entry in _entries)
                {
                    if (entry.Seq != expectedSeq)
                    {
                        error = $"Replay sequence discontinuity at {entry.Seq}; expected {expectedSeq}";
                        return false;
                    }

                    var hashIn = ComputeHash(JsonSerializer.Serialize(entry.Inputs));
                    var hashOut = ComputeHash(JsonSerializer.Serialize(entry.Outputs));
                    if (hashIn != entry.HashIn || hashOut != entry.HashOut)
                    {
                        error = $"Replay payload hash mismatch at seq {entry.Seq}";
                        return false;
                    }

                    if (entry.PrevHash != prev)
                    {
                        error = $"Replay chain mismatch at seq {entry.Seq}";
                        return false;
                    }

                    var expected = ComputeHash(
                        $"{entry.Seq}|{entry.Op}|{entry.HashIn}|{entry.HashOut}|{entry.Kernel}|{entry.PrevHash}|{entry.Version}");
                    if (entry.EntryHash != expected)
                    {
                        error = $"Replay entry hash mismatch at seq {entry.Seq}";
                        return false;
                    }

                    prev = entry.EntryHash;
                    expectedSeq++;
                }

                error = null;
                return true;
            }

            public IReadOnlyList<ReplayEntry> Entries => _entries;
            public int Count => _entries.Count;

            public void Save(string path)
            {
                var lines = _entries.Select(e => JsonSerializer.Serialize(e));
                File.WriteAllLines(path, lines);
            }

            public static ReplayLog Load(string path)
            {
                var log = new ReplayLog();
                if (File.Exists(path))
                {
                    var lines = File.ReadAllLines(path);
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var entry = JsonSerializer.Deserialize<ReplayEntry>(line);
                            if (entry != null) log._entries.Add(entry);
                        }
                    }
                }
                return log;
            }

            private static string ComputeHash(string data)
            {
                using var sha = SHA256.Create();
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(data));
                return "sha256:" + BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }

        // ---- Program model ----

        public class IRProgram
        {
            public List<string> Streams { get; set; } = new();
            public List<string> Ops { get; set; } = new();
        }

        public class IRResult
        {
            public bool Success { get; set; }
            public string Error { get; set; }
            public IRTensor LastTensor { get; set; }
            public IRSurface LastSurface { get; set; }
            public List<string> LogMessages { get; set; } = new();
            public int OpsExecuted { get; set; }
        }

        // ---- Parse IR program text (simplified EBNF surface) ----

        private static readonly Regex _programRegex = new(
            @"streams\s*:\s*\[(.*?)\]\s*ops\s*:\s*\[(.*?)\]",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        private static readonly Regex _opRegex = new(
            @"(\w[\w_]*)\s*\((.*?)\)",
            RegexOptions.Singleline);

        public IRProgram ParseProgram(string text)
        {
            var program = new IRProgram();
            text = text.Trim();

            var m = _programRegex.Match(text);
            if (!m.Success) return program;

            // Parse streams
            var streamsPart = m.Groups[1].Value;
            var streamMatches = Regex.Matches(streamsPart, @"(json|svg)\((""[^""]*"")?\)");
            foreach (Match sm in streamMatches)
                program.Streams.Add(sm.Value);

            // Parse ops
            var opsPart = m.Groups[2].Value;
            var opMatches = _opRegex.Matches(opsPart);
            foreach (Match om in opMatches)
                program.Ops.Add(om.Value);

            return program;
        }

        // ---- Execute IR program ----

        public IRResult Execute(IRProgram program)
        {
            var result = new IRResult();

            try
            {
                foreach (var opText in program.Ops)
                {
                    var parsed = ParseOpText(opText);
                    if (parsed == null)
                        throw new InvalidOperationException($"Unparseable IR op: {opText}");

                    DispatchOp(parsed, result);
                    result.OpsExecuted++;
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }

        // ---- Op dispatch ----

        private class ParsedOp
        {
            public string Name { get; set; }
            public string[] Args { get; set; }
        }

        private ParsedOp ParseOpText(string text)
        {
            var m = Regex.Match(text, @"^(\w[\w_]*)\s*\((.*)\)$");
            if (!m.Success) return null;
            var argsText = m.Groups[2].Value;

            // Split args by comma, respecting arrow notation
            var args = new List<string>();
            int depth = 0;
            var current = new StringBuilder();
            foreach (char c in argsText)
            {
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (c == ',' && depth == 0)
                {
                    args.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }
                current.Append(c);
            }
            if (current.Length > 0)
                args.Add(current.ToString().Trim());

            return new ParsedOp { Name = m.Groups[1].Value, Args = args.ToArray() };
        }

        private void DispatchOp(ParsedOp op, IRResult result)
        {
            var kernel = _gpu.IsAvailable("d3d11_1") ? "D3D11_1" : "CPU";

            switch (op.Name)
            {
                // --- Linalg ---
                case "mul_mat_vec": OpMulMatVec(op, result, kernel); break;
                case "mul_mat_mat": OpMulMatMat(op, result, kernel); break;
                case "add_vec":     OpAddVec(op, result, kernel); break;

                // --- Geometry ---
                case "rotate":      OpRotate(op, result, kernel); break;
                case "scale":       OpScale(op, result, kernel); break;
                case "shear":       OpShear(op, result, kernel); break;
                case "relate":      OpRelate(op, result, kernel); break;
                case "geom_product": OpGeomProduct(op, result, kernel); break;

                // --- SVG ---
                case "path_from_tensor": OpPathFromTensor(op, result, kernel); break;
                case "surface_compose": OpSurfaceCompose(op, result, kernel); break;
                case "render":           OpRender(op, result, kernel); break;

                default:
                    throw new InvalidOperationException($"Unknown IR op: {op.Name}");
            }
        }

        // ---- Op implementations ----

        private void OpMulMatVec(ParsedOp op, IRResult result, string kernel)
        {
            // mul_mat_vec(mA, vB -> vR)
            var call = ParseArrowCall(op, 2);
            var mA = RequireTensor(call.Inputs[0]);
            var vB = RequireTensor(call.Inputs[1]);

            RequireMatrix(mA, "mul_mat_vec left operand");
            RequireVector(vB, "mul_mat_vec right operand");

            if (mA.Cols != vB.Size)
                throw new InvalidOperationException(
                    $"mul_mat_vec shape mismatch: [{mA.Rows},{mA.Cols}] x [{vB.Size}]");

            var resultData = new double[mA.Rows];
            for (int i = 0; i < mA.Rows; i++)
            {
                double sum = 0;
                for (int k = 0; k < mA.Cols; k++)
                    sum += mA.Data[i * mA.Cols + k] * vB.Data[k];
                resultData[i] = sum;
            }

            var t = NewTensor(call.Output, resultData, new[] { mA.Rows });
            StoreTensor(t, result);
            LogAppend(op.Name,
                new { a = TensorRef(mA), b = TensorRef(vB) },
                TensorRef(t), kernel);
        }

        private void OpMulMatMat(ParsedOp op, IRResult result, string kernel)
        {
            var call = ParseArrowCall(op, 2);
            var mA = RequireTensor(call.Inputs[0]);
            var mB = RequireTensor(call.Inputs[1]);

            RequireMatrix(mA, "mul_mat_mat left operand");
            RequireMatrix(mB, "mul_mat_mat right operand");

            if (mA.Cols != mB.Rows)
                throw new InvalidOperationException(
                    $"mul_mat_mat shape mismatch: [{mA.Rows},{mA.Cols}] x [{mB.Rows},{mB.Cols}]");

            int rows = mA.Rows, cols = mB.Cols, inner = mA.Cols;
            var resultData = new double[rows * cols];

            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < inner; k++)
                        sum += mA.Data[i * inner + k] * mB.Data[k * cols + j];
                    resultData[i * cols + j] = sum;
                }

            var t = NewTensor(call.Output, resultData, new[] { rows, cols });
            StoreTensor(t, result);
            LogAppend(op.Name,
                new { a = TensorRef(mA), b = TensorRef(mB) },
                TensorRef(t), kernel);
        }

        private void OpAddVec(ParsedOp op, IRResult result, string kernel)
        {
            var call = ParseArrowCall(op, 2);
            var vA = RequireTensor(call.Inputs[0]);
            var vB = RequireTensor(call.Inputs[1]);

            RequireVector(vA, "add_vec left operand");
            RequireVector(vB, "add_vec right operand");

            if (vA.Size != vB.Size)
                throw new InvalidOperationException(
                    $"add_vec shape mismatch: [{vA.Size}] + [{vB.Size}]");

            var resultData = new double[vA.Size];
            for (int i = 0; i < resultData.Length; i++)
                resultData[i] = vA.Data[i] + vB.Data[i];

            var t = NewTensor(call.Output, resultData, new[] { resultData.Length });
            StoreTensor(t, result);
            LogAppend(op.Name,
                new { a = TensorRef(vA), b = TensorRef(vB) },
                TensorRef(t), kernel);
        }

        private void OpRotate(ParsedOp op, IRResult result, string kernel)
        {
            var parts = op.Args[0].Split(new[] { "->" }, StringSplitOptions.None);
            if (parts.Length < 2) return;
            var inputs = parts[0].Split(',');
            var t = GetTensor(inputs[0].Trim());
            var anglePi = double.TryParse(Regex.Replace(inputs.Length > 1 ? inputs[1] : "0", "pi", "").Trim(), out var a) ? a : 0;
            var outId = parts[1].Trim();

            if (t == null) throw new KeyNotFoundException("rotate source tensor not found");
            ValidateTensor(t);
            if ((t.Data.Length % 2) != 0) throw new InvalidOperationException("rotate requires coordinate pairs");

            double cosA = Math.Cos(anglePi * Math.PI);
            double sinA = Math.Sin(anglePi * Math.PI);
            var resultData = new double[t.Data.Length];
            for (int i = 0; i < t.Data.Length; i += 2)
            {
                double x = t.Data[i];
                double y = (i + 1 < t.Data.Length) ? t.Data[i + 1] : 0;
                resultData[i] = x * cosA - y * sinA;
                if (i + 1 < t.Data.Length) resultData[i + 1] = x * sinA + y * cosA;
            }

            var rt = new IRTensor { Id = outId, Data = resultData, Shape = t.Shape.ToArray(), Phase = anglePi };
            _tensors[outId] = rt;
            result.LastTensor = rt;
            LogAppend(op.Name, t.Id + ", " + anglePi, outId, kernel);
        }

        private void OpScale(ParsedOp op, IRResult result, string kernel)
        {
            var parts = op.Args[0].Split(new[] { "->" }, StringSplitOptions.None);
            if (parts.Length < 2) return;
            var inputs = parts[0].Split(',');
            var t = GetTensor(inputs[0].Trim());
            var factor = double.TryParse(inputs.Length > 1 ? inputs[1] : "1", out var f) ? f : 1;
            var outId = parts[1].Trim();

            if (t == null) return;

            var resultData = t.Data.Select(d => d * factor).ToArray();
            var st = new IRTensor { Id = outId, Data = resultData, Shape = t.Shape.ToArray() };
            _tensors[outId] = st;
            result.LastTensor = st;
            LogAppend(op.Name, t.Id + ", " + factor, outId, kernel);
        }

        private void OpShear(ParsedOp op, IRResult result, string kernel)
        {
            var parts = op.Args[0].Split(new[] { "->" }, StringSplitOptions.None);
            if (parts.Length < 2) return;
            var inputs = parts[0].Split(',');
            var t = GetTensor(inputs[0].Trim());
            var dir = inputs.Length > 1 ? inputs[1].Trim() : "x";
            var amt = double.TryParse(inputs.Length > 2 ? inputs[2] : "0", out var a) ? a : 0;
            var outId = parts[1].Trim();

            if (t == null) return;

            var resultData = new double[t.Data.Length];
            for (int i = 0; i < t.Data.Length; i += 2)
            {
                double x = t.Data[i];
                double y = (i + 1 < t.Data.Length) ? t.Data[i + 1] : 0;
                if (dir == "x") { resultData[i] = x + amt * y; if (i + 1 < t.Data.Length) resultData[i + 1] = y; }
                else if (dir == "y") { resultData[i] = x; if (i + 1 < t.Data.Length) resultData[i + 1] = y + amt * x; }
                else { resultData[i] = x; if (i + 1 < t.Data.Length) resultData[i + 1] = y; }
            }

            var ht = new IRTensor { Id = outId, Data = resultData, Shape = t.Shape.ToArray() };
            _tensors[outId] = ht;
            result.LastTensor = ht;
            LogAppend(op.Name, t.Id + ", " + dir + ", " + amt, outId, kernel);
        }

        private void OpRelate(ParsedOp op, IRResult result, string kernel)
        {
            var parts = op.Args[0].Split(new[] { "->" }, StringSplitOptions.None);
            if (parts.Length < 2) return;
            var inputs = parts[0].Split(',');
            var tA = GetTensor(inputs[0].Trim());
            var tB = GetTensor(inputs.Length > 1 ? inputs[1].Trim() : "");
            var relation = inputs.Length > 2 ? inputs[2].Trim() : "similar";
            var outId = parts[1].Trim();

            if (tA == null || tB == null) return;

            // Compute similarity score (dot product / magnitudes)
            double dot = 0, magA = 0, magB = 0;
            int n = Math.Min(tA.Data.Length, tB.Data.Length);
            for (int i = 0; i < n; i++)
            {
                dot += tA.Data[i] * tB.Data[i];
                magA += tA.Data[i] * tA.Data[i];
                magB += tB.Data[i] * tB.Data[i];
            }
            double score = (Math.Sqrt(magA) * Math.Sqrt(magB)) > 0
                ? dot / (Math.Sqrt(magA) * Math.Sqrt(magB))
                : 0;

            var st = new IRTensor { Id = outId, Data = new[] { score }, Shape = new[] { 1 } };
            _tensors[outId] = st;
            result.LastTensor = st;
            LogAppend(op.Name, tA.Id + ", " + tB.Id + ", " + relation, outId, kernel);
        }

        private void OpGeomProduct(ParsedOp op, IRResult result, string kernel)
        {
            var parts = op.Args[0].Split(new[] { "->" }, StringSplitOptions.None);
            if (parts.Length < 2) return;
            var inputs = parts[0].Split(',');
            var v = GetTensor(inputs[0].Trim());
            var phasePi = double.TryParse(Regex.Replace(inputs.Length > 1 ? inputs[1] : "0", "pi", "").Trim(), out var p) ? p : 0;
            var outId = parts[1].Trim();

            if (v == null) return;

            // Geometric product: multiply each component by e^(i*theta)
            double cosA = Math.Cos(phasePi * Math.PI);
            double sinA = Math.Sin(phasePi * Math.PI);
            var resultData = new double[v.Data.Length];
            for (int i = 0; i < v.Data.Length; i++)
                resultData[i] = v.Data[i] * cosA - (i + 1 < v.Data.Length ? v.Data[i + 1] * sinA : 0);

            var gt = new IRTensor { Id = outId, Data = resultData, Shape = v.Shape, Phase = phasePi };
            _tensors[outId] = gt;
            result.LastTensor = gt;
            LogAppend(op.Name, v.Id + ", " + phasePi, outId, kernel);
        }

        private void OpPathFromTensor(ParsedOp op, IRResult result, string kernel)
        {
            var parts = op.Args[0].Split(new[] { "->" }, StringSplitOptions.None);
            if (parts.Length < 2) return;
            var t = GetTensor(parts[0].Trim());
            var pathId = parts[1].Trim();

            if (t == null) throw new KeyNotFoundException("path_from_tensor source tensor not found");
            ValidateTensor(t);

            // Build SVG path d string from tensor data (pairs of coordinates)
            var sb = new StringBuilder();
            if (t.Data.Length < 2 || (t.Data.Length % 2) != 0)
                throw new InvalidOperationException(
                    $"path_from_tensor requires an even coordinate tensor; '{t.Id}' has {t.Data.Length} values");

            for (int i = 0; i < t.Data.Length; i += 2)
            {
                var cmd = (i == 0) ? "M" : "L";
                sb.Append($"{cmd} {t.Data[i]:F4},{t.Data[i + 1]:F4} ");
            }
            if (t.Data.Length > 2) sb.Append("Z");

            var path = new IRSvgPath { Id = pathId, D = sb.ToString().Trim(), SourceTensor = t };
            _paths[pathId] = path;
            LogAppend(op.Name, t.Id, pathId, kernel);
        }

        private void OpSurfaceCompose(ParsedOp op, IRResult result, string kernel)
        {
            var parts = op.Args[0].Split(new[] { "->" }, StringSplitOptions.None);
            if (parts.Length < 2) return;
            var inputs = parts[0].Split(',');
            var surfaceId = inputs[0].Trim();
            var tensorList = inputs.Length > 1 ? inputs[1].Trim() : "";
            var outId = parts[1].Trim();

            var existing = _surfaces.GetValueOrDefault(surfaceId);
            var tensors = new List<IRTensor>();
            var tensorMatches = Regex.Matches(tensorList, @"\b([a-zA-Z_]\w*)\b");
            foreach (Match m in tensorMatches)
            {
                var t = GetTensor(m.Value);
                if (t != null) tensors.Add(t);
            }

            var surface = existing ?? new IRSurface { Id = outId };
            foreach (var t in tensors)
                if (!surface.Tensors.Contains(t)) surface.Tensors.Add(t);

            surface.Id = outId;
            _surfaces[outId] = surface;
            result.LastSurface = surface;
            LogAppend(op.Name, surfaceId + ", [" + string.Join(",", tensors.Select(t => t.Id)) + "]", outId, kernel);
        }

        private void OpRender(ParsedOp op, IRResult result, string kernel)
        {
            var parts = op.Args[0].Split(new[] { "->" }, StringSplitOptions.None);
            if (parts.Length < 2) return;
            var surfaceId = parts[0].Trim();
            var outputId = parts[1].Trim();

            var surface = _surfaces.GetValueOrDefault(surfaceId);
            if (surface == null)
            {
                result.LogMessages.Add($"Render: surface '{surfaceId}' not found");
                return;
            }

            result.LogMessages.Add($"Rendered surface '{surfaceId}' ({surface.Tensors.Count} tensors, {surface.Paths.Count} paths) -> '{outputId}' using {kernel}");
            LogAppend(op.Name, surfaceId, outputId, kernel);
        }

        // ---- Tensor admission / NDArray bridge ----

        public IRTensor AdmitTensor(string id, double[] data, int[] shape, double phase = 0)
        {
            var tensor = NewTensor(id, data, shape, phase);
            _tensors[id] = tensor;
            return tensor;
        }

        public IRTensor AdmitTensor(string id, NDArray array, double phase = 0)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            return AdmitTensor(id, array.ToArray(), array.Shape.ToArray(), phase);
        }

        public NDArray ToNDArray(string id)
        {
            var tensor = RequireTensor(id);
            return new NDArray(tensor.Data.ToArray(), tensor.Shape.ToArray());
        }

        // ---- Helpers ----

        private sealed class ArrowCall
        {
            public string[] Inputs { get; set; }
            public string Output { get; set; }
        }

        private ArrowCall ParseArrowCall(ParsedOp op, int requiredInputs)
        {
            if (op.Args == null || op.Args.Length == 0)
                throw new InvalidOperationException($"{op.Name}: missing arguments");

            // ParseOpText may split comma-separated operands before the arrow.
            // Rejoin here so both "op(a,b -> c)" and preserved grouped forms work.
            var joined = string.Join(",", op.Args);
            var parts = joined.Split(new[] { "->" }, StringSplitOptions.None);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
                throw new InvalidOperationException($"{op.Name}: expected 'inputs -> output'");

            var inputs = parts[0]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .ToArray();

            if (inputs.Length < requiredInputs)
                throw new InvalidOperationException(
                    $"{op.Name}: expected at least {requiredInputs} input(s), got {inputs.Length}");

            return new ArrowCall { Inputs = inputs, Output = parts[1].Trim() };
        }

        private IRTensor GetTensor(string id) =>
            _tensors.TryGetValue(id, out var t) ? t : null;

        private IRTensor RequireTensor(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !_tensors.TryGetValue(id, out var tensor))
                throw new KeyNotFoundException($"IR tensor not found: '{id}'");
            ValidateTensor(tensor);
            return tensor;
        }

        private static void RequireVector(IRTensor tensor, string role)
        {
            ValidateTensor(tensor);
            if (tensor.Rank != 1)
                throw new InvalidOperationException(
                    $"{role} requires rank-1 tensor; '{tensor.Id}' rank={tensor.Rank}");
        }

        private static void RequireMatrix(IRTensor tensor, string role)
        {
            ValidateTensor(tensor);
            if (tensor.Rank != 2)
                throw new InvalidOperationException(
                    $"{role} requires rank-2 tensor; '{tensor.Id}' rank={tensor.Rank}");
        }

        private static void ValidateTensor(IRTensor tensor)
        {
            if (tensor == null) throw new ArgumentNullException(nameof(tensor));
            if (string.IsNullOrWhiteSpace(tensor.Id))
                throw new InvalidOperationException("IR tensor id is required");
            if (tensor.Data == null)
                throw new InvalidOperationException($"Tensor '{tensor.Id}' has null data");
            if (tensor.Shape == null || tensor.Shape.Length == 0)
                throw new InvalidOperationException($"Tensor '{tensor.Id}' has no shape");
            if (tensor.Shape.Any(d => d < 0))
                throw new InvalidOperationException($"Tensor '{tensor.Id}' has a negative dimension");

            long expected = 1;
            foreach (var d in tensor.Shape) expected = checked(expected * d);
            if (expected != tensor.Data.LongLength)
                throw new InvalidOperationException(
                    $"Tensor '{tensor.Id}' shape [{string.Join(",", tensor.Shape)}] expects {expected} values, got {tensor.Data.LongLength}");
        }

        private static IRTensor NewTensor(string id, double[] data, int[] shape, double phase = 0)
        {
            var tensor = new IRTensor
            {
                Id = id,
                Data = data?.ToArray(),
                Shape = shape?.ToArray(),
                Phase = phase
            };
            ValidateTensor(tensor);
            return tensor;
        }

        private void StoreTensor(IRTensor tensor, IRResult result)
        {
            ValidateTensor(tensor);
            _tensors[tensor.Id] = tensor;
            result.LastTensor = tensor;
        }

        private static object TensorRef(IRTensor t) => new
        {
            id = t.Id,
            shape = t.Shape,
            phase = t.Phase,
            data = t.Data
        };

        private void LogAppend(string op, object inputs, object outputs, string kernel)
        {
            _log.Append(++_seq, op, inputs, outputs, kernel);
        }

        // ---- Public state access ----

        public IReadOnlyDictionary<string, IRTensor> Tensors => _tensors;
        public IReadOnlyDictionary<string, IRSvgPath> Paths => _paths;
        public IReadOnlyDictionary<string, IRSurface> Surfaces => _surfaces;
    }
}
