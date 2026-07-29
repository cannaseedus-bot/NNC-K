using System;
using System.Collections.Generic;
using System.Linq;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// Neural Network Trainer — Backpropagation and optimization
    /// </summary>
    public class NeuralTrainer
    {
        private NNCInterpreter _interpreter = new();
        private MathMLEngine _mathEngine = new();
        
        public TrainingResult Train(NNCProgram program, TrainingData data, TrainingConfig config)
        {
            var result = new TrainingResult
            {
                Epochs = config.Epochs,
                FinalLoss = 0,
                LossHistory = new List<double>()
            };

            // Initialize weights
            InitializeWeights(program);

            // Training loop
            for (int epoch = 0; epoch < config.Epochs; epoch++)
            {
                double totalLoss = 0;

                foreach (var batch in data.GetBatches(config.BatchSize))
                {
                    // Forward pass
                    var output = _interpreter.ExecuteAtFold("Sek", program, batch.Input);

                    // Calculate loss
                    var loss = ComputeLoss(output.Output, batch.Expected);
                    totalLoss += loss;

                    // Backpropagation
                    var gradient = ComputeGradients(output, batch.Expected);
                    UpdateWeights(program, gradient, config.LearningRate);

                    // Apply MathML if defined
                    foreach (var layer in program.Program.Layers)
                    {
                        if (!string.IsNullOrEmpty(layer.Value.MathML))
                        {
                            var mathResult = _mathEngine.Evaluate(layer.Value.MathML, 
                                new Dictionary<string, object>
                                {
                                    { "input", batch.Input },
                                    { "weights", program.Program.Weights[layer.Key] }
                                });
                        }
                    }
                }

                double avgLoss = totalLoss / data.Count;
                result.LossHistory.Add(avgLoss);
                result.FinalLoss = avgLoss;

                if (epoch % 10 == 0)
                    Console.WriteLine($"Epoch {epoch}: Loss = {avgLoss:F4}");
            }

            return result;
        }

        private void InitializeWeights(NNCProgram program)
        {
            var random = new Random();
            foreach (var layer in program.Program.Layers)
            {
                if (layer.Value.Type == "dense")
                {
                    var rows = layer.Value.Neurons ?? 4;
                    var cols = 4; // Input size
                    var weights = new Tensor(rows, cols);
                    for (int i = 0; i < rows; i++)
                        for (int j = 0; j < cols; j++)
                            weights[i, j] = random.NextDouble() * 2 - 1; // Xavier init
                    program.Program.Weights[layer.Key] = weights;
                }
            }
        }

        private double ComputeLoss(Tensor predicted, Tensor expected)
        {
            // Mean squared error
            double loss = 0;
            for (int i = 0; i < predicted.Rows; i++)
                for (int j = 0; j < predicted.Cols; j++)
                    loss += Math.Pow(predicted[i, j] - expected[i, j], 2);
            return loss / (predicted.Rows * predicted.Cols);
        }

        private Tensor ComputeGradients(NNCProgramResult output, Tensor expected)
        {
            // Simplified gradient computation
            var gradient = new Tensor(output.Output.Rows, output.Output.Cols);
            for (int i = 0; i < output.Output.Rows; i++)
                for (int j = 0; j < output.Output.Cols; j++)
                    gradient[i, j] = 2 * (output.Output[i, j] - expected[i, j]) * 
                                    SigmoidDerivative(output.Output[i, j]);
            return gradient;
        }

        private void UpdateWeights(NNCProgram program, Tensor gradient, double learningRate)
        {
            foreach (var layer in program.Program.Layers)
            {
                if (program.Program.Weights.ContainsKey(layer.Key))
                {
                    var weights = program.Program.Weights[layer.Key];
                    for (int i = 0; i < weights.Rows; i++)
                        for (int j = 0; j < weights.Cols; j++)
                            weights[i, j] -= learningRate * gradient[i % gradient.Rows, j % gradient.Cols];
                }
            }
        }

        private double SigmoidDerivative(double x) => x * (1 - x);
    }

    public class TrainingData
    {
        public List<Tensor> Inputs { get; set; } = new();
        public List<Tensor> Expected { get; set; } = new();
        public int Count => Inputs.Count;

        public IEnumerable<(Tensor Input, Tensor Expected)> GetBatches(int batchSize)
        {
            for (int i = 0; i < Inputs.Count; i += batchSize)
            {
                var batchInput = Inputs[i];
                var batchExpected = Expected[i];
                yield return (batchInput, batchExpected);
            }
        }
    }

    public class TrainingConfig
    {
        public int Epochs { get; set; } = 100;
        public double LearningRate { get; set; } = 0.01;
        public int BatchSize { get; set; } = 1;
        public string Optimizer { get; set; } = "sgd";
    }

    public class TrainingResult
    {
        public int Epochs { get; set; }
        public double FinalLoss { get; set; }
        public List<double> LossHistory { get; set; }
        public double Accuracy { get; set; }
        public double ValLoss { get; set; }
        public bool Converged { get; set; }
    }
}
