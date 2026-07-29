using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NeuralGrammar.Core.XCFE
{
    /// <summary>
    /// GPU Compute Pipeline — generates and dispatches compute kernels across
    /// D3D11_1 (HLSL cs_5_0), WebGL2 (GLSL ES 300), and OpenCL (OpenCL C 1.2).
    /// Falls back to CPU when no GPU backend is available.
    /// </summary>
    public class GPUComputePipeline
    {
        private readonly GPUProviderRegistry _gpu;
        private string _activeBackend;
        private readonly Dictionary<string, GpuKernel> _kernelCache = new();
        private readonly HashSet<string> _nativeDispatchBackends =
            new(StringComparer.OrdinalIgnoreCase);

        public GPUComputePipeline(GPUProviderRegistry gpu = null)
        {
            _gpu = gpu ?? new GPUProviderRegistry();
            _gpu.Measure();
            _activeBackend = SelectBackend();
        }

        /// <summary>
        /// Preferred compute backend discovered from provider capabilities.
        /// Discovery is not execution admission and does not prove native dispatch.
        /// </summary>
        public string ActiveBackend => _activeBackend;

        public bool HasNativeDispatch(string backend) =>
            !string.IsNullOrWhiteSpace(backend) &&
            _nativeDispatchBackends.Contains(backend);

        // ---- Backend discovery ----

        private string SelectBackend()
        {
            if (_gpu.IsAvailable("d3d11_1")) return "d3d11_1";
            if (_gpu.IsAvailable("opencl")) return "opencl";

            // WebGL2 has no compute-shader stage. Do not advertise GLSL compute
            // syntax as executable WebGL2 compute.
            return "cpu";
        }

        // ---- Kernel types ----

        public enum KernelType
        {
            MatMul,
            ReLU,
            Sigmoid,
            Tanh,
            Softmax,
            AddVec,
            SubVec,
            MulVec,
            Backprop,
            AdamUpdate
        }

        public class GpuKernel
        {
            public string Name { get; set; }
            public string ShaderSource { get; set; }
            public KernelType Type { get; set; }
            public string Backend { get; set; }
            public int ThreadsX { get; set; } = 64;
            public int ThreadsY { get; set; } = 1;
            public int ThreadsZ { get; set; } = 1;
            public int SharedMemBytes { get; set; }
            public string EntryPoint { get; set; } = "main";
        }

        public class ComputeResult
        {
            public bool Success { get; set; }
            public double[,] Output { get; set; }
            public string Backend { get; set; }
            public string RequestedBackend { get; set; }
            public bool NativeDispatch { get; set; }
            public bool Fallback { get; set; }
            public string FallbackReason { get; set; }
            public long ElapsedMicroseconds { get; set; }
            public string Error { get; set; }
        }

        // ---- Kernel compilation (shader source generation) ----

        public GpuKernel GetKernel(KernelType type)
        {
            var key = $"{_activeBackend}:{type}";
            if (_kernelCache.TryGetValue(key, out var cached))
                return cached;

            var kernel = CompileKernel(type, _activeBackend);
            _kernelCache[key] = kernel;
            return kernel;
        }

        private GpuKernel CompileKernel(KernelType type, string backend)
        {
            return backend switch
            {
                "d3d11_1" => CompileHLSL(type),
                "opencl"  => CompileOpenCL(type),
                "webgl2"  => CompileUnsupportedWebGL2(type),
                _         => CompileStub(type) // CPU fallback
            };
        }

        // ---- D3D11_1: HLSL cs_5_0 ----

        private GpuKernel CompileHLSL(KernelType type)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// D3D11_1 HLSL cs_5_0");
            sb.AppendLine("#define THREADS 64");
            sb.AppendLine("[numthreads(THREADS, 1, 1)]");
            sb.AppendLine("void main(");
            sb.AppendLine("  uint3 id : SV_DispatchThreadID,");
            sb.AppendLine("  uint3 gid : SV_GroupThreadID)");
            sb.AppendLine("{");

            switch (type)
            {
                case KernelType.MatMul:
                    sb.AppendLine("  // matmul: C[i,j] = sum_k A[i,k] * B[k,j]");
                    sb.AppendLine("  uint i = id.x; uint j = id.y;");
                    sb.AppendLine("  if (i >= N || j >= M) return;");
                    sb.AppendLine("  float sum = 0;");
                    sb.AppendLine("  for (uint k = 0; k < K; k++)");
                    sb.AppendLine("    sum += A[i * K + k] * B[k * M + j];");
                    sb.AppendLine("  C[i * M + j] = sum;");
                    break;

                case KernelType.ReLU:
                    sb.AppendLine("  // ReLU: y = max(0, x)");
                    sb.AppendLine("  uint i = id.x;");
                    sb.AppendLine("  if (i >= N) return;");
                    sb.AppendLine("  Out[i] = max(0, In[i]);");
                    break;

                case KernelType.Sigmoid:
                    sb.AppendLine("  // Sigmoid: y = 1/(1+exp(-x))");
                    sb.AppendLine("  uint i = id.x;");
                    sb.AppendLine("  if (i >= N) return;");
                    sb.AppendLine("  Out[i] = 1.0 / (1.0 + exp(-In[i]));");
                    break;

                case KernelType.Tanh:
                    sb.AppendLine("  // Tanh: y = tanh(x)");
                    sb.AppendLine("  uint i = id.x;");
                    sb.AppendLine("  if (i >= N) return;");
                    sb.AppendLine("  Out[i] = tanh(In[i]);");
                    break;

                case KernelType.AddVec:
                    sb.AppendLine("  uint i = id.x;");
                    sb.AppendLine("  if (i >= N) return;");
                    sb.AppendLine("  Out[i] = A[i] + B[i];");
                    break;

                case KernelType.Backprop:
                    sb.AppendLine("  // dL/dW = input^T * gradient");
                    sb.AppendLine("  uint i = id.x; uint j = id.y;");
                    sb.AppendLine("  if (i >= N || j >= M) return;");
                    sb.AppendLine("  float sum = 0;");
                    sb.AppendLine("  for (uint k = 0; k < K; k++)");
                    sb.AppendLine("    sum += Input[k * N + i] * Grad[k * M + j];");
                    sb.AppendLine("  DW[i * M + j] = sum / BatchSize;");
                    break;
            }

            sb.AppendLine("}");
            return new GpuKernel
            {
                Name = $"matmul_hlsl_{type}",
                ShaderSource = sb.ToString(),
                Type = type,
                Backend = "d3d11_1",
                ThreadsX = 64
            };
        }

        // ---- GLSL compute reference ----
        // Kept as a translation/reference emitter only. WebGL2 itself does not
        // expose compute shaders, SSBO compute dispatch, or gl_GlobalInvocationID.

        private GpuKernel CompileGLSLReference(KernelType type)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#version 300 es");
            sb.AppendLine("#define THREADS 64");
            sb.AppendLine("precision highp float;");
            sb.AppendLine("precision highp int;");
            sb.AppendLine("layout(local_size_x = THREADS) in;");

            switch (type)
            {
                case KernelType.MatMul:
                    sb.AppendLine("layout(std430, binding=0) buffer A { float a[]; };");
                    sb.AppendLine("layout(std430, binding=1) buffer B { float b[]; };");
                    sb.AppendLine("layout(std430, binding=2) buffer C { float c[]; };");
                    sb.AppendLine("uniform int N, M, K;");
                    sb.AppendLine("void main() {");
                    sb.AppendLine("  uint i = gl_GlobalInvocationID.x;");
                    sb.AppendLine("  uint j = gl_GlobalInvocationID.y;");
                    sb.AppendLine("  if (i >= uint(N) || j >= uint(M)) return;");
                    sb.AppendLine("  float sum = 0.0;");
                    sb.AppendLine("  for (uint k = 0u; k < uint(K); k++)");
                    sb.AppendLine("    sum += a[i * uint(K) + k] * b[k * uint(M) + j];");
                    sb.AppendLine("  c[i * uint(M) + j] = sum;");
                    sb.AppendLine("}");
                    break;

                case KernelType.ReLU:
                    sb.AppendLine("layout(std430, binding=0) buffer In { float in_data[]; };");
                    sb.AppendLine("layout(std430, binding=1) buffer Out { float out_data[]; };");
                    sb.AppendLine("uniform int N;");
                    sb.AppendLine("void main() {");
                    sb.AppendLine("  uint i = gl_GlobalInvocationID.x;");
                    sb.AppendLine("  if (i >= uint(N)) return;");
                    sb.AppendLine("  out_data[i] = max(0.0, in_data[i]);");
                    sb.AppendLine("}");
                    break;

                case KernelType.Sigmoid:
                    sb.AppendLine("layout(std430, binding=0) buffer In { float in_data[]; };");
                    sb.AppendLine("layout(std430, binding=1) buffer Out { float out_data[]; };");
                    sb.AppendLine("uniform int N;");
                    sb.AppendLine("void main() {");
                    sb.AppendLine("  uint i = gl_GlobalInvocationID.x;");
                    sb.AppendLine("  if (i >= uint(N)) return;");
                    sb.AppendLine("  out_data[i] = 1.0 / (1.0 + exp(-in_data[i]));");
                    sb.AppendLine("}");
                    break;

                default:
                    sb.AppendLine("void main() { }");
                    break;
            }

            return new GpuKernel
            {
                Name = $"glsl_compute_reference_{type}",
                ShaderSource = sb.ToString(),
                Type = type,
                Backend = "glsl_compute_reference",
                ThreadsX = 64
            };
        }

        private GpuKernel CompileUnsupportedWebGL2(KernelType type)
        {
            return new GpuKernel
            {
                Name = $"webgl2_unsupported_{type}",
                ShaderSource =
                    "// WebGL2 compute dispatch is unsupported. " +
                    "Use D3D11_1/OpenCL or an admitted CPU fallback.",
                Type = type,
                Backend = "webgl2_unsupported"
            };
        }

        // ---- OpenCL: OpenCL C 1.2 ----

        private GpuKernel CompileOpenCL(KernelType type)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// OpenCL C 1.2");
            sb.AppendLine("#define THREADS 64");

            switch (type)
            {
                case KernelType.MatMul:
                    sb.AppendLine("__kernel void matmul(");
                    sb.AppendLine("  __global const float *A,");
                    sb.AppendLine("  __global const float *B,");
                    sb.AppendLine("  __global float *C,");
                    sb.AppendLine("  const int N, const int M, const int K) {");
                    sb.AppendLine("  int i = get_global_id(0);");
                    sb.AppendLine("  int j = get_global_id(1);");
                    sb.AppendLine("  if (i >= N || j >= M) return;");
                    sb.AppendLine("  float sum = 0.0f;");
                    sb.AppendLine("  for (int k = 0; k < K; k++)");
                    sb.AppendLine("    sum += A[i * K + k] * B[k * M + j];");
                    sb.AppendLine("  C[i * M + j] = sum;");
                    sb.AppendLine("}");
                    break;

                case KernelType.ReLU:
                    sb.AppendLine("__kernel void relu(");
                    sb.AppendLine("  __global const float *In,");
                    sb.AppendLine("  __global float *Out,");
                    sb.AppendLine("  const int N) {");
                    sb.AppendLine("  int i = get_global_id(0);");
                    sb.AppendLine("  if (i >= N) return;");
                    sb.AppendLine("  Out[i] = fmax(0.0f, In[i]);");
                    sb.AppendLine("}");
                    break;

                case KernelType.Sigmoid:
                    sb.AppendLine("__kernel void sigmoid(");
                    sb.AppendLine("  __global const float *In,");
                    sb.AppendLine("  __global float *Out,");
                    sb.AppendLine("  const int N) {");
                    sb.AppendLine("  int i = get_global_id(0);");
                    sb.AppendLine("  if (i >= N) return;");
                    sb.AppendLine("  Out[i] = 1.0f / (1.0f + exp(-In[i]));");
                    sb.AppendLine("}");
                    break;

                case KernelType.Backprop:
                    sb.AppendLine("__kernel void backprop(");
                    sb.AppendLine("  __global const float *Input,");
                    sb.AppendLine("  __global const float *Grad,");
                    sb.AppendLine("  __global float *DW,");
                    sb.AppendLine("  const int N, const int M, const int K, const float invBatch) {");
                    sb.AppendLine("  int i = get_global_id(0);");
                    sb.AppendLine("  int j = get_global_id(1);");
                    sb.AppendLine("  if (i >= N || j >= M) return;");
                    sb.AppendLine("  float sum = 0.0f;");
                    sb.AppendLine("  for (int k = 0; k < K; k++)");
                    sb.AppendLine("    sum += Input[k * N + i] * Grad[k * M + j];");
                    sb.AppendLine("  DW[i * M + j] = sum * invBatch;");
                    sb.AppendLine("}");
                    break;

                default:
                    sb.AppendLine("__kernel void nop() { }");
                    break;
            }

            return new GpuKernel
            {
                Name = $"matmul_ocl_{type}",
                ShaderSource = sb.ToString(),
                Type = type,
                Backend = "opencl",
                ThreadsX = 64
            };
        }

        // ---- CPU fallback ----

        private GpuKernel CompileStub(KernelType type)
        {
            return new GpuKernel
            {
                Name = $"cpu_stub_{type}",
                ShaderSource = "// CPU fallback — no GPU available",
                Type = type,
                Backend = "cpu"
            };
        }

        // ---- Dispatch: matmul ----

        public ComputeResult MatMul(double[,] a, double[,] b, int rowsA, int colsA, int colsB)
        {
            return MatMul(a, b, rowsA, colsA, colsB, admitted: false);
        }

        public ComputeResult MatMul(
            double[,] a, double[,] b, int rowsA, int colsA, int colsB,
            bool admitted)
        {
            ValidateMatMul(a, b, rowsA, colsA, colsB);

            if (_activeBackend == "cpu")
                return CpuMatMul(a, b, rowsA, colsA, colsB);

            if (!admitted)
                return CpuFallbackMatMul(
                    a, b, rowsA, colsA, colsB,
                    _activeBackend,
                    "GPU backend discovered but XCFE @gpu.dispatch was not admitted.");

            // Generate/cache the kernel so the admitted execution contract has
            // a concrete backend artifact, but never claim native execution
            // until a native dispatcher has been registered.
            _ = GetKernel(KernelType.MatMul);

            if (!HasNativeDispatch(_activeBackend))
                return CpuFallbackMatMul(
                    a, b, rowsA, colsA, colsB,
                    _activeBackend,
                    $"No native {_activeBackend} dispatcher is registered.");

            return new ComputeResult
            {
                Success = false,
                Backend = _activeBackend,
                RequestedBackend = _activeBackend,
                NativeDispatch = false,
                Error = $"Native {_activeBackend} dispatcher is registered but not bound to MatMul."
            };
        }

        public ComputeResult Activation(double[] input, KernelType activation)
        {
            return Activation(input, activation, admitted: false);
        }

        public ComputeResult Activation(
            double[] input,
            KernelType activation,
            bool admitted)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            if (_activeBackend == "cpu")
                return CpuActivation(input, activation);

            if (!admitted)
                return CpuFallbackActivation(
                    input, activation, _activeBackend,
                    "GPU backend discovered but XCFE @gpu.dispatch was not admitted.");

            _ = GetKernel(activation);

            if (!HasNativeDispatch(_activeBackend))
                return CpuFallbackActivation(
                    input, activation, _activeBackend,
                    $"No native {_activeBackend} dispatcher is registered.");

            return new ComputeResult
            {
                Success = false,
                Backend = _activeBackend,
                RequestedBackend = _activeBackend,
                NativeDispatch = false,
                Error = $"Native {_activeBackend} dispatcher is registered but not bound to {activation}."
            };
        }

        public ComputeResult Backprop(double[,] input, double[,] grad, int batchSize)
        {
            return Backprop(input, grad, batchSize, admitted: false);
        }

        public ComputeResult Backprop(
            double[,] input,
            double[,] grad,
            int batchSize,
            bool admitted)
        {
            int n = input.GetLength(0);
            int m = grad.GetLength(1);
            int k = input.GetLength(1);

            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var dw = new double[n, m];
            double invBatch = 1.0 / Math.Max(batchSize, 1);

            for (int i = 0; i < n; i++)
                for (int j = 0; j < m; j++)
                {
                    double sum = 0;
                    for (int b = 0; b < k; b++)
                        sum += input[b, i] * grad[b, j];
                    dw[i, j] = sum * invBatch;
                }

            var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - start)
                          * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;

            return new ComputeResult
            {
                Success = true,
                Output = dw,
                Backend = "cpu",
                RequestedBackend = _activeBackend,
                NativeDispatch = false,
                Fallback = _activeBackend != "cpu",
                FallbackReason = _activeBackend == "cpu"
                    ? null
                    : admitted
                        ? $"No native {_activeBackend} Backprop dispatcher is bound."
                        : "GPU Backprop was not admitted by XCFE.",
                ElapsedMicroseconds = elapsed
            };
        }

        // ---- CPU implementations ----

        private ComputeResult CpuMatMul(double[,] a, double[,] b, int rowsA, int colsA, int colsB)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = new double[rowsA, colsB];

            for (int i = 0; i < rowsA; i++)
                for (int j = 0; j < colsB; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < colsA; k++)
                        sum += a[i, k] * b[k, j];
                    result[i, j] = sum;
                }

            var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - start)
                          * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;

            return new ComputeResult
            {
                Success = true,
                Output = result,
                Backend = "cpu",
                RequestedBackend = "cpu",
                NativeDispatch = false,
                Fallback = false,
                ElapsedMicroseconds = elapsed
            };
        }

        private ComputeResult CpuActivation(double[] input, KernelType activation)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var result = new double[1, input.Length];

            for (int i = 0; i < input.Length; i++)
            {
                result[0, i] = activation switch
                {
                    KernelType.ReLU    => Math.Max(0, input[i]),
                    KernelType.Sigmoid => 1.0 / (1.0 + Math.Exp(-input[i])),
                    KernelType.Tanh    => Math.Tanh(input[i]),
                    _                  => input[i]
                };
            }

            var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - start)
                          * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;

            return new ComputeResult
            {
                Success = true,
                Output = result,
                Backend = "cpu",
                RequestedBackend = "cpu",
                NativeDispatch = false,
                Fallback = false,
                ElapsedMicroseconds = elapsed
            };
        }

        private ComputeResult CpuFallbackMatMul(
            double[,] a, double[,] b, int rowsA, int colsA, int colsB,
            string requestedBackend, string reason)
        {
            var result = CpuMatMul(a, b, rowsA, colsA, colsB);
            result.RequestedBackend = requestedBackend;
            result.Fallback = true;
            result.FallbackReason = reason;
            return result;
        }

        private ComputeResult CpuFallbackActivation(
            double[] input, KernelType activation,
            string requestedBackend, string reason)
        {
            var result = CpuActivation(input, activation);
            result.RequestedBackend = requestedBackend;
            result.Fallback = true;
            result.FallbackReason = reason;
            return result;
        }

        private static void ValidateMatMul(
            double[,] a, double[,] b, int rowsA, int colsA, int colsB)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (rowsA < 0 || colsA < 0 || colsB < 0)
                throw new ArgumentOutOfRangeException("Matrix dimensions cannot be negative.");
            if (a.GetLength(0) < rowsA || a.GetLength(1) < colsA)
                throw new ArgumentException("Matrix A dimensions do not satisfy rowsA/colsA.");
            if (b.GetLength(0) < colsA || b.GetLength(1) < colsB)
                throw new ArgumentException("Matrix B dimensions do not satisfy colsA/colsB.");
        }

        // ---- Training integration ----

        /// <summary>Run a full forward pass on GPU (or CPU fallback)</summary>
        public ComputeResult ForwardPass(double[,] input, double[,] weights, double[] bias,
            KernelType activation, int batchSize)
        {
            return ForwardPass(input, weights, bias, activation, batchSize, admitted: false);
        }

        public ComputeResult ForwardPass(
            double[,] input, double[,] weights, double[] bias,
            KernelType activation, int batchSize, bool admitted)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (weights == null) throw new ArgumentNullException(nameof(weights));
            if (bias == null || bias.Length == 0)
                throw new ArgumentException("Bias must contain at least one value.", nameof(bias));

            // Layer = activation(matmul(input, weights) + bias)
            var matmulResult = MatMul(input, weights,
                input.GetLength(0), input.GetLength(1), weights.GetLength(1), admitted);

            if (!matmulResult.Success) return matmulResult;

            var output = matmulResult.Output;
            int rows = output.GetLength(0);
            int cols = output.GetLength(1);

            // Add bias
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    output[i, j] += bias[j % bias.Length];

            // Activation
            var flat = new double[rows * cols];
            Buffer.BlockCopy(output, 0, flat, 0, flat.Length * sizeof(double));
            var actResult = Activation(flat, activation, admitted);
            if (!actResult.Success) return actResult;

            return new ComputeResult
            {
                Success = true,
                Output = actResult.Output,
                Backend = actResult.Backend,
                RequestedBackend = _activeBackend,
                NativeDispatch = matmulResult.NativeDispatch && actResult.NativeDispatch,
                Fallback = matmulResult.Fallback || actResult.Fallback,
                FallbackReason = string.Join("; ",
                    new[] { matmulResult.FallbackReason, actResult.FallbackReason }
                        .Where(s => !string.IsNullOrWhiteSpace(s))),
                ElapsedMicroseconds =
                    matmulResult.ElapsedMicroseconds + actResult.ElapsedMicroseconds
            };
        }
    }

    /// <summary>
    /// GPU Training Engine — runs neural training loops using the GPU compute pipeline.
    /// Supports D3D11_1, WebGL2, OpenCL, and CPU fallback.
    /// </summary>
    public class GPUTrainer
    {
        private readonly GPUComputePipeline _compute;
        private readonly GPUProviderRegistry _gpu;

        public GPUTrainer(GPUProviderRegistry gpu = null)
        {
            _gpu = gpu ?? new GPUProviderRegistry();
            _compute = new GPUComputePipeline(_gpu);
        }

        public string ActiveBackend => _compute.ActiveBackend;

        public class TrainingMetrics
        {
            public int Epoch { get; set; }
            public double Loss { get; set; }
            public long ElapsedMicroseconds { get; set; }
            public string Backend { get; set; }
        }

        /// <summary>Train a single layer using GPU compute</summary>
        public TrainingMetrics TrainStep(double[,] input, double[,] target,
            double[,] weights, double[] bias, double learningRate,
            GPUComputePipeline.KernelType activation, int batchSize, int epoch)
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();

            // Forward pass
            // Training does not self-authorize GPU execution. Until XCFE supplies
            // an admitted training dispatch, this path remains truthful CPU fallback.
            var forward = _compute.ForwardPass(
                input, weights, bias, activation, batchSize, admitted: false);
            if (!forward.Success)
                return new TrainingMetrics { Epoch = epoch, Loss = -1, Backend = _compute.ActiveBackend };

            // Compute loss (MSE)
            double loss = 0;
            int count = 0;
            for (int i = 0; i < forward.Output.GetLength(0); i++)
                for (int j = 0; j < forward.Output.GetLength(1); j++)
                {
                    double diff = forward.Output[i, j] - target[i % target.GetLength(0), j % target.GetLength(1)];
                    loss += diff * diff;
                    count++;
                }
            loss /= Math.Max(count, 1);

            // Backward pass (gradient)
            var grad = new double[input.GetLength(0), weights.GetLength(1)];
            for (int i = 0; i < grad.GetLength(0); i++)
                for (int j = 0; j < grad.GetLength(1); j++)
                {
                    double pred = forward.Output[i % forward.Output.GetLength(0), j % forward.Output.GetLength(1)];
                    double targ = target[i % target.GetLength(0), j % target.GetLength(1)];
                    grad[i, j] = 2 * (pred - targ) / Math.Max(batchSize, 1);
                }

            // Update weights (SGD)
            for (int i = 0; i < weights.GetLength(0); i++)
                for (int j = 0; j < weights.GetLength(1); j++)
                {
                    double gradSum = 0;
                    for (int b = 0; b < batchSize; b++)
                        gradSum += input[b % input.GetLength(0), i] * grad[b % grad.GetLength(0), j];
                    weights[i, j] -= learningRate * gradSum / Math.Max(batchSize, 1);
                }

            var elapsed = (System.Diagnostics.Stopwatch.GetTimestamp() - start)
                          * 1_000_000 / System.Diagnostics.Stopwatch.Frequency;

            return new TrainingMetrics
            {
                Epoch = epoch,
                Loss = loss,
                ElapsedMicroseconds = elapsed,
                Backend = forward.Backend
            };
        }
    }
}
