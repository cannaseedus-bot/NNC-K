using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// NNC Interpreter — Executes neural network programs from JSON
    /// </summary>
    public class NNCInterpreter
    {
        private MathMLEngine _mathEngine = new();
        private Dictionary<string, Tensor> _weights = new();
        private Dictionary<string, Tensor> _activations = new();

        [Obsolete("NNC execution is Sek-gated. Use ExecuteAtFold(\"Sek\", ...).", true)]
        public NNCProgramResult Execute(string programJson, Tensor input = null) =>
            throw new NotSupportedException("Direct NNC execution is disabled.");

        private NNCProgramResult ExecuteProgramJson(string programJson, Tensor input = null)
        {
            if (string.IsNullOrWhiteSpace(programJson))
                throw new ArgumentException("NNC program JSON is required.", nameof(programJson));

            var program = JsonSerializer.Deserialize<NNCProgram>(programJson)
                ?? throw new InvalidOperationException("NNC program JSON deserialized to null.");

            return ExecuteProgram(program, input);
        }

        /// <summary>
        /// Scheduler-facing entry point. K'UHUL owns control flow; NNCInterpreter
        /// only performs neural execution when the active fold is Sek.
        /// </summary>
        public NNCProgramResult ExecuteAtFold(string activeFold, string programJson, Tensor input = null)
        {
            if (!string.Equals(activeFold, "Sek", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"NNC execution requires Sek; active fold is '{activeFold}'.");

            return ExecuteProgramJson(programJson, input);
        }

        public NNCProgramResult ExecuteAtFold(string activeFold, NNCProgram program, Tensor input = null)
        {
            if (!string.Equals(activeFold, "Sek", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"NNC execution requires Sek; active fold is '{activeFold}'.");

            return ExecuteProgram(program, input);
        }

        [Obsolete("NNC execution is Sek-gated. Use ExecuteAtFold(\"Sek\", ...).", true)]
        public NNCProgramResult Execute(NNCProgram program, Tensor input = null) =>
            throw new NotSupportedException("Direct NNC execution is disabled.");

        private NNCProgramResult ExecuteProgram(NNCProgram program, Tensor input = null)
        {
            if (program?.Program == null)
                throw new ArgumentException("NNC program and program body are required.", nameof(program));

            if (program.Program.Layers == null || program.Program.Layers.Count == 0)
                throw new InvalidOperationException("NNC program contains no layers.");

            _weights.Clear();
            _activations.Clear();

            var result = new NNCProgramResult
            {
                ProgramName = program.Program.Name,
                Timestamp = DateTime.UtcNow
            };

            // Load weights
            foreach (var layer in program.Program.Layers)
            {
                if (program.Program.Weights != null && program.Program.Weights.ContainsKey(layer.Key))
                {
                    _weights[layer.Key] = program.Program.Weights[layer.Key];
                }
            }

            // Forward pass
            Tensor current = input ?? new Tensor(new double[,] { { 1.0, 0.5 } });
            var layerOutputs = new List<LayerOutput>();

            foreach (var layer in program.Program.Layers)
            {
                if (layer.Value.Type == "input")
                {
                    // Resize input if needed
                    if (layer.Value.Size.HasValue)
                    {
                        current = ResizeTensor(current, 1, layer.Value.Size.Value);
                    }
                    layerOutputs.Add(new LayerOutput
                    {
                        Layer = layer.Key,
                        Type = "input",
                        Output = current,
                        Neurons = current.Cols
                    });
                    continue;
                }

                // Dense layer
                if (layer.Value.Type == "dense")
                {
                    if (!_weights.TryGetValue(layer.Key, out var weights))
                        throw new InvalidOperationException(
                            $"Dense layer '{layer.Key}' has no weight tensor.");

                    if (current.Cols != weights.Rows)
                        throw new InvalidOperationException(
                            $"Dense layer '{layer.Key}' shape mismatch: input cols={current.Cols}, weight rows={weights.Rows}.");

                    var bias = program.Program.Bias?[layer.Key]
                        ?? new Tensor(1, layer.Value.Neurons ?? weights.Cols);

                    if (bias.Rows < 1 || bias.Cols < weights.Cols)
                        throw new InvalidOperationException(
                            $"Dense layer '{layer.Key}' bias shape is smaller than output width {weights.Cols}.");

                    // Matrix multiplication
                    var resultTensor = new Tensor(current.Rows, weights.Cols);
                    for (int i = 0; i < current.Rows; i++)
                        for (int j = 0; j < weights.Cols; j++)
                        {
                            double sum = 0;
                            for (int k = 0; k < current.Cols; k++)
                                sum += current[i, k] * weights[k, j];
                            resultTensor[i, j] = sum + (bias[0, j]);
                        }

                    current = resultTensor;

                    // Apply activation
                    if (!string.IsNullOrEmpty(layer.Value.Activation))
                    {
                        current = ApplyActivation(current, layer.Value.Activation);
                    }

                    layerOutputs.Add(new LayerOutput
                    {
                        Layer = layer.Key,
                        Type = "dense",
                        Neurons = current.Cols,
                        Activation = layer.Value.Activation,
                        Output = current
                    });
                }
            }

            result.LayerOutputs = layerOutputs;
            result.Output = current;
            return result;
        }

        private Tensor ApplyActivation(Tensor input, string activation)
        {
            switch (activation.ToLower())
            {
                case "relu": return new Tensor(_mathEngine.Relu(input).Data);
                case "sigmoid": return new Tensor(_mathEngine.Sigmoid(input).Data);
                case "tanh":
                    var result = new double[input.Rows, input.Cols];
                    for (int i = 0; i < input.Rows; i++)
                        for (int j = 0; j < input.Cols; j++)
                            result[i, j] = Math.Tanh(input[i, j]);
                    return new Tensor(result);
                case "softmax":
                    var softmaxResult = new double[input.Rows, input.Cols];
                    for (int i = 0; i < input.Rows; i++)
                    {
                        double max = double.NegativeInfinity;
                        for (int j = 0; j < input.Cols; j++)
                            max = Math.Max(max, input[i, j]);

                        double sum = 0.0;
                        for (int j = 0; j < input.Cols; j++)
                        {
                            var e = Math.Exp(input[i, j] - max);
                            softmaxResult[i, j] = e;
                            sum += e;
                        }

                        if (sum <= 0 || double.IsNaN(sum) || double.IsInfinity(sum))
                            throw new InvalidOperationException("Softmax normalization became non-finite.");

                        for (int j = 0; j < input.Cols; j++)
                            softmaxResult[i, j] /= sum;
                    }
                    return new Tensor(softmaxResult);
                default: return input;
            }
        }

        private Tensor ResizeTensor(Tensor input, int rows, int cols)
        {
            var result = new Tensor(rows, cols);
            for (int i = 0; i < Math.Min(rows, input.Rows); i++)
                for (int j = 0; j < Math.Min(cols, input.Cols); j++)
                    result[i, j] = input[i, j];
            return result;
        }
    }

    // NNC Program Classes
    public class NNCProgram
    {
        public string Schema { get; set; }
        public ProgramData Program { get; set; }
    }

    public class ProgramData
    {
        public string Name { get; set; }
        public Dictionary<string, LayerData> Layers { get; set; }
        public Dictionary<string, Tensor> Weights { get; set; }
        public Dictionary<string, Tensor> Bias { get; set; }
    }

    public class LayerData
    {
        public string Type { get; set; }
        public int? Neurons { get; set; }
        public int? Size { get; set; }
        public string Activation { get; set; }
        public string MathML { get; set; }
    }

    public class NNCProgramResult
    {
        public string ProgramName { get; set; }
        public DateTime Timestamp { get; set; }
        public List<LayerOutput> LayerOutputs { get; set; }
        public Tensor Output { get; set; }
    }

    public class LayerOutput
    {
        public string Layer { get; set; }
        public string Type { get; set; }
        public int Neurons { get; set; }
        public string Activation { get; set; }
        public Tensor Output { get; set; }
    }
}
