using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// Dense N-dimensional numerical tensor substrate for Neural Grammar Core.
    /// This class owns numeric shape/data algebra only; K'UHUL/XCFE owns control flow.
    /// </summary>
    public class NDArray
    {
        public int[] Shape { get; private set; }
        public int Rank => Shape.Length;
        public int Size { get; private set; }
        public DType DataType { get; set; } = DType.Float64;
        public bool IsScalar => Size == 1;
        public bool IsVector => Rank == 1;
        public bool IsMatrix => Rank == 2;
        public bool IsTensor => Rank >= 3;

        private double[] _data;
        private int[] _strides;

        public NDArray(params int[] shape)
        {
            Initialize(shape);
            _data = new double[Size];
        }

        public NDArray(double[] flatData, params int[] shape)
        {
            if (flatData == null) throw new ArgumentNullException(nameof(flatData));
            Initialize(shape);
            if (flatData.Length != Size)
                throw new ArgumentException($"Data length {flatData.Length} != shape size {Size}");
            _data = (double[])flatData.Clone();
        }

        public NDArray(double[,] matrix)
        {
            if (matrix == null) throw new ArgumentNullException(nameof(matrix));
            var r = matrix.GetLength(0);
            var c = matrix.GetLength(1);
            Initialize(new[] { r, c });
            _data = new double[Size];
            for (int i = 0; i < r; i++)
                for (int j = 0; j < c; j++)
                    this[i, j] = matrix[i, j];
        }

        public NDArray(double[,,] t3)
        {
            if (t3 == null) throw new ArgumentNullException(nameof(t3));
            var d1 = t3.GetLength(0);
            var d2 = t3.GetLength(1);
            var d3 = t3.GetLength(2);
            Initialize(new[] { d1, d2, d3 });
            _data = new double[Size];
            for (int i = 0; i < d1; i++)
                for (int j = 0; j < d2; j++)
                    for (int k = 0; k < d3; k++)
                        this[i, j, k] = t3[i, j, k];
        }

        public NDArray(float[,,] t3)
        {
            if (t3 == null) throw new ArgumentNullException(nameof(t3));
            var d1 = t3.GetLength(0);
            var d2 = t3.GetLength(1);
            var d3 = t3.GetLength(2);
            Initialize(new[] { d1, d2, d3 });
            _data = new double[Size];
            for (int i = 0; i < d1; i++)
                for (int j = 0; j < d2; j++)
                    for (int k = 0; k < d3; k++)
                        this[i, j, k] = t3[i, j, k];
        }

        private void Initialize(int[] shape)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            if (shape.Length == 0)
                throw new ArgumentException("Shape must contain at least one dimension", nameof(shape));
            if (shape.Any(d => d <= 0))
                throw new ArgumentException(
                    $"All dimensions must be positive: {string.Join("x", shape)}", nameof(shape));

            Shape = (int[])shape.Clone();

            long size = 1;
            foreach (var d in Shape)
            {
                size *= d;
                if (size > int.MaxValue)
                    throw new ArgumentException("Tensor exceeds maximum supported element count");
            }
            Size = (int)size;

            _strides = ComputeStrides(Shape);
        }

        private static int[] ComputeStrides(int[] shape)
        {
            var strides = new int[shape.Length];
            var stride = 1;
            for (int i = shape.Length - 1; i >= 0; i--)
            {
                strides[i] = stride;
                checked { stride *= shape[i]; }
            }
            return strides;
        }

        public double this[params int[] idx]
        {
            get => _data[Flat(idx)];
            set => _data[Flat(idx)] = value;
        }

        public double this[int i]
        {
            get
            {
                if (Rank != 1) throw new InvalidOperationException("1D indexer requires rank 1");
                ValidateIndex(i, 0);
                return _data[i];
            }
            set
            {
                if (Rank != 1) throw new InvalidOperationException("1D indexer requires rank 1");
                ValidateIndex(i, 0);
                _data[i] = value;
            }
        }

        public double this[int i, int j]
        {
            get
            {
                if (Rank != 2) throw new InvalidOperationException("2D indexer requires rank 2");
                ValidateIndex(i, 0); ValidateIndex(j, 1);
                return _data[i * _strides[0] + j * _strides[1]];
            }
            set
            {
                if (Rank != 2) throw new InvalidOperationException("2D indexer requires rank 2");
                ValidateIndex(i, 0); ValidateIndex(j, 1);
                _data[i * _strides[0] + j * _strides[1]] = value;
            }
        }

        public double this[int i, int j, int k]
        {
            get
            {
                if (Rank != 3) throw new InvalidOperationException("3D indexer requires rank 3");
                ValidateIndex(i, 0); ValidateIndex(j, 1); ValidateIndex(k, 2);
                return _data[i * _strides[0] + j * _strides[1] + k * _strides[2]];
            }
            set
            {
                if (Rank != 3) throw new InvalidOperationException("3D indexer requires rank 3");
                ValidateIndex(i, 0); ValidateIndex(j, 1); ValidateIndex(k, 2);
                _data[i * _strides[0] + j * _strides[1] + k * _strides[2]] = value;
            }
        }

        private void ValidateIndex(int index, int axis)
        {
            if (axis < 0 || axis >= Rank)
                throw new ArgumentOutOfRangeException(nameof(axis));
            if (index < 0 || index >= Shape[axis])
                throw new IndexOutOfRangeException(
                    $"Index {index} outside axis {axis} with size {Shape[axis]}");
        }

        private int Flat(int[] idx)
        {
            if (idx == null) throw new ArgumentNullException(nameof(idx));
            if (idx.Length != Rank)
                throw new ArgumentException($"Expected {Rank} indices, received {idx.Length}");

            var flat = 0;
            for (int i = 0; i < Rank; i++)
            {
                ValidateIndex(idx[i], i);
                flat += idx[i] * _strides[i];
            }
            return flat;
        }

        private int[] CoordinatesFromFlat(int flat)
        {
            if (flat < 0 || flat >= Size) throw new ArgumentOutOfRangeException(nameof(flat));
            var coords = new int[Rank];
            for (int d = 0; d < Rank; d++)
            {
                coords[d] = flat / _strides[d];
                flat %= _strides[d];
            }
            return coords;
        }

        public NDArray Flatten() => new NDArray(_data, Size);
        public double[] ToArray() => (double[])_data.Clone();

        public NDArray Reshape(params int[] newShape)
        {
            if (newShape == null || newShape.Length == 0)
                throw new ArgumentException("New shape is required", nameof(newShape));
            if (newShape.Any(d => d <= 0))
                throw new ArgumentException("All reshape dimensions must be positive", nameof(newShape));

            long size = 1;
            foreach (var d in newShape) size *= d;
            if (size != Size)
                throw new ArgumentException(
                    $"New shape {string.Join("x", newShape)} size {size} != {Size}");

            return new NDArray(_data, newShape);
        }

        public NDArray Transpose()
        {
            if (Rank != 2) throw new InvalidOperationException("Transpose requires 2D");
            var r = new NDArray(Shape[1], Shape[0]);
            for (int i = 0; i < Shape[0]; i++)
                for (int j = 0; j < Shape[1]; j++)
                    r[j, i] = this[i, j];
            return r;
        }

        public NDArray Add(NDArray o) => Elementwise(o, (a, b) => a + b);
        public NDArray Sub(NDArray o) => Elementwise(o, (a, b) => a - b);
        public NDArray Mul(NDArray o) => Elementwise(o, (a, b) => a * b);
        public NDArray Div(NDArray o) => Elementwise(o, (a, b) => a / b);

        private NDArray Elementwise(NDArray other, Func<double, double, double> op)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            var (a, b) = Broadcast(this, other);
            var r = new NDArray(a.Shape);
            for (int i = 0; i < r.Size; i++)
                r._data[i] = op(a._data[i], b._data[i]);
            return r;
        }

        public NDArray Pow(double e) => Map(v => Math.Pow(v, e));
        public NDArray Exp() => Map(Math.Exp);
        public NDArray Log() => Map(Math.Log);
        public NDArray Sqrt() => Map(Math.Sqrt);
        public NDArray Abs() => Map(Math.Abs);
        public NDArray Sin() => Map(Math.Sin);
        public NDArray Cos() => Map(Math.Cos);
        public NDArray Tan() => Map(Math.Tan);
        public NDArray Sigmoid() => Map(v => 1.0 / (1.0 + Math.Exp(-v)));
        public NDArray ReLU() => Map(v => Math.Max(0, v));
        public NDArray Clone() => new NDArray(_data, Shape);

        public NDArray Map(Func<double, double> fn)
        {
            if (fn == null) throw new ArgumentNullException(nameof(fn));
            var r = new NDArray(Shape);
            for (int i = 0; i < Size; i++) r._data[i] = fn(_data[i]);
            return r;
        }

        /// <summary>Softmax over all elements (legacy behavior).</summary>
        public NDArray Softmax()
        {
            var r = new NDArray(Shape);
            var max = Max();
            double sum = 0;
            for (int i = 0; i < Size; i++)
            {
                var v = Math.Exp(_data[i] - max);
                r._data[i] = v;
                sum += v;
            }
            if (sum == 0 || double.IsNaN(sum))
                throw new ArithmeticException("Softmax normalization failed");
            for (int i = 0; i < Size; i++) r._data[i] /= sum;
            return r;
        }

        /// <summary>Numerically stable softmax along one axis.</summary>
        public NDArray Softmax(int axis)
        {
            axis = NormalizeAxis(axis);
            var result = new NDArray(Shape);
            var outerShape = Shape.Where((_, i) => i != axis).ToArray();
            var outerSize = outerShape.Length == 0 ? 1 : outerShape.Aggregate(1, (a, b) => a * b);

            for (int outer = 0; outer < outerSize; outer++)
            {
                var baseCoords = ExpandOuterCoordinates(outer, axis);
                double max = double.NegativeInfinity;

                for (int a = 0; a < Shape[axis]; a++)
                {
                    baseCoords[axis] = a;
                    max = Math.Max(max, this[baseCoords]);
                }

                double sum = 0;
                for (int a = 0; a < Shape[axis]; a++)
                {
                    baseCoords[axis] = a;
                    var v = Math.Exp(this[baseCoords] - max);
                    result[baseCoords] = v;
                    sum += v;
                }

                for (int a = 0; a < Shape[axis]; a++)
                {
                    baseCoords[axis] = a;
                    result[baseCoords] /= sum;
                }
            }

            return result;
        }

        /// <summary>
        /// Vector dot product. Matrix multiplication remains supported for
        /// compatibility; prefer MatMul for explicit matrix algebra.
        /// </summary>
        public NDArray Dot(NDArray o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));

            if (Rank == 1 && o.Rank == 1)
            {
                if (Size != o.Size) throw new ArgumentException("Vector length mismatch");
                double sum = 0;
                for (int i = 0; i < Size; i++) sum += _data[i] * o._data[i];
                return new NDArray(new[] { sum }, 1);
            }

            if (Rank == 2 && o.Rank == 2)
                return MatMul(o);

            throw new InvalidOperationException("Dot supports vector dot or 2D matrix compatibility");
        }

        public NDArray MatMul(NDArray o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            if (Rank != 2 || o.Rank != 2)
                throw new InvalidOperationException("MatMul currently requires two rank-2 arrays");
            if (Shape[1] != o.Shape[0])
                throw new ArgumentException(
                    $"Dim mismatch: {Shape[0]}x{Shape[1]} @ {o.Shape[0]}x{o.Shape[1]}");

            var r = new NDArray(Shape[0], o.Shape[1]);
            for (int i = 0; i < Shape[0]; i++)
                for (int j = 0; j < o.Shape[1]; j++)
                {
                    double sum = 0;
                    for (int k = 0; k < Shape[1]; k++)
                        sum += this[i, k] * o[k, j];
                    r[i, j] = sum;
                }
            return r;
        }

        public double Sum()
        {
            double s = 0;
            for (int i = 0; i < Size; i++) s += _data[i];
            return s;
        }

        public double Mean()
        {
            if (Size == 0) throw new InvalidOperationException("Mean of empty tensor");
            return Sum() / Size;
        }

        public double Var(bool sample = true)
        {
            if (sample && Size < 2)
                throw new InvalidOperationException("Sample variance requires at least two elements");
            var mean = Mean();
            double sum = 0;
            for (int i = 0; i < Size; i++)
            {
                var d = _data[i] - mean;
                sum += d * d;
            }
            return sum / (sample ? Size - 1 : Size);
        }

        public double Std(bool sample = true) => Math.Sqrt(Var(sample));

        public double Min()
        {
            var m = double.PositiveInfinity;
            for (int i = 0; i < Size; i++) if (_data[i] < m) m = _data[i];
            return m;
        }

        public double Max()
        {
            var m = double.NegativeInfinity;
            for (int i = 0; i < Size; i++) if (_data[i] > m) m = _data[i];
            return m;
        }

        public double Norm()
        {
            double sum = 0;
            for (int i = 0; i < Size; i++) sum += _data[i] * _data[i];
            return Math.Sqrt(sum);
        }

        /// <summary>Reduce an axis by summation; the reduced axis is removed.</summary>
        public NDArray Sum(int axis)
        {
            axis = NormalizeAxis(axis);
            var resultShape = Shape.Where((_, i) => i != axis).ToArray();
            if (resultShape.Length == 0) return new NDArray(new[] { Sum() }, 1);

            var result = new NDArray(resultShape);
            for (int i = 0; i < Size; i++)
            {
                var coords = CoordinatesFromFlat(i);
                var outCoords = coords.Where((_, d) => d != axis).ToArray();
                result[outCoords] += _data[i];
            }
            return result;
        }

        public NDArray Mean(int axis)
        {
            axis = NormalizeAxis(axis);
            return Sum(axis).Div(Shape[axis]);
        }

        public NDArray Add(double s) => Map(v => v + s);
        public NDArray Mul(double s) => Map(v => v * s);
        public NDArray Div(double s)
        {
            if (s == 0) throw new DivideByZeroException();
            return Map(v => v / s);
        }

        public static (NDArray, NDArray) Broadcast(NDArray a, NDArray b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));

            var rank = Math.Max(a.Rank, b.Rank);
            var sa = Pad(a.Shape, rank);
            var sb = Pad(b.Shape, rank);
            var target = new int[rank];

            for (int i = 0; i < rank; i++)
            {
                if (sa[i] != sb[i] && sa[i] != 1 && sb[i] != 1)
                    throw new ArgumentException(
                        $"Cannot broadcast {string.Join("x", a.Shape)} and {string.Join("x", b.Shape)}");
                target[i] = Math.Max(sa[i], sb[i]);
            }

            return (DoBroadcast(a, target), DoBroadcast(b, target));
        }

        private static int[] Pad(int[] shape, int targetRank)
        {
            var padded = Enumerable.Repeat(1, targetRank).ToArray();
            Array.Copy(shape, 0, padded, targetRank - shape.Length, shape.Length);
            return padded;
        }

        private static NDArray DoBroadcast(NDArray source, int[] targetShape)
        {
            var paddedSourceShape = Pad(source.Shape, targetShape.Length);
            if (paddedSourceShape.SequenceEqual(targetShape) &&
                source.Rank == targetShape.Length)
                return source;

            var result = new NDArray(targetShape);
            var targetStrides = ComputeStrides(targetShape);
            var rankOffset = targetShape.Length - source.Rank;

            for (int flat = 0; flat < result.Size; flat++)
            {
                var remainder = flat;
                var sourceCoords = new int[source.Rank];

                for (int d = 0; d < targetShape.Length; d++)
                {
                    var coord = remainder / targetStrides[d];
                    remainder %= targetStrides[d];

                    var sourceAxis = d - rankOffset;
                    if (sourceAxis >= 0)
                        sourceCoords[sourceAxis] =
                            source.Shape[sourceAxis] == 1 ? 0 : coord;
                }

                result._data[flat] = source[sourceCoords];
            }

            return result;
        }

        private int NormalizeAxis(int axis)
        {
            if (axis < 0) axis += Rank;
            if (axis < 0 || axis >= Rank)
                throw new ArgumentOutOfRangeException(nameof(axis),
                    $"Axis must be in [-{Rank}, {Rank - 1}]");
            return axis;
        }

        private int[] ExpandOuterCoordinates(int outerFlat, int excludedAxis)
        {
            var coords = new int[Rank];
            var outerShape = Shape.Where((_, i) => i != excludedAxis).ToArray();

            for (int d = outerShape.Length - 1; d >= 0; d--)
            {
                var coord = outerFlat % outerShape[d];
                outerFlat /= outerShape[d];

                var actualAxis = d >= excludedAxis ? d + 1 : d;
                coords[actualAxis] = coord;
            }

            return coords;
        }

        public static NDArray Zeros(params int[] shape) => new NDArray(shape);

        public static NDArray Ones(params int[] shape)
        {
            var a = new NDArray(shape);
            for (int i = 0; i < a.Size; i++) a._data[i] = 1;
            return a;
        }

        public static NDArray Full(double value, params int[] shape)
        {
            var a = new NDArray(shape);
            for (int i = 0; i < a.Size; i++) a._data[i] = value;
            return a;
        }

        public static NDArray Arange(double start, double stop, double step = 1.0)
        {
            if (step == 0) throw new ArgumentException("Step cannot be zero", nameof(step));
            if ((step > 0 && start >= stop) || (step < 0 && start <= stop))
                throw new ArgumentException("Step direction does not reach stop");

            var n = (int)Math.Ceiling((stop - start) / step);
            var a = new NDArray(n);
            for (int i = 0; i < n; i++) a._data[i] = start + i * step;
            return a;
        }

        public static NDArray Linspace(double start, double stop, int num)
        {
            if (num <= 0) throw new ArgumentOutOfRangeException(nameof(num));
            var a = new NDArray(num);
            if (num == 1)
            {
                a._data[0] = start;
                return a;
            }

            var step = (stop - start) / (num - 1);
            for (int i = 0; i < num; i++) a._data[i] = start + i * step;
            return a;
        }

        public static NDArray Eye(int n)
        {
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
            var a = new NDArray(n, n);
            for (int i = 0; i < n; i++) a[i, i] = 1;
            return a;
        }

        public static NDArray Random(
            int[] shape,
            double mean = 0,
            double std = 1,
            int? seed = null)
        {
            if (std < 0) throw new ArgumentOutOfRangeException(nameof(std));
            var a = new NDArray(shape);
            var rng = seed.HasValue ? new Random(seed.Value) : new Random();

            // Box-Muller normal distribution.
            for (int i = 0; i < a.Size; i += 2)
            {
                var u1 = Math.Max(rng.NextDouble(), double.Epsilon);
                var u2 = rng.NextDouble();
                var mag = Math.Sqrt(-2.0 * Math.Log(u1));
                var z0 = mag * Math.Cos(2.0 * Math.PI * u2);
                var z1 = mag * Math.Sin(2.0 * Math.PI * u2);

                a._data[i] = mean + std * z0;
                if (i + 1 < a.Size)
                    a._data[i + 1] = mean + std * z1;
            }

            return a;
        }

        public override string ToString()
        {
            if (IsScalar) return _data[0].ToString("F4");
            if (Rank == 1)
                return $"[{string.Join(", ", _data.Select(d => d.ToString("F4")))}]";

            if (Rank == 2)
            {
                var rows = new List<string>();
                for (int i = 0; i < Shape[0]; i++)
                {
                    var vals = new List<string>();
                    for (int j = 0; j < Shape[1]; j++)
                        vals.Add(this[i, j].ToString("F4"));
                    rows.Add($"[{string.Join(", ", vals)}]");
                }
                return $"[{string.Join(",\n ", rows)}]";
            }

            return $"[NDArray Shape: {string.Join("x", Shape)}, Size: {Size}, DType: {DataType}]";
        }
    }

    public enum DType
    {
        Float32,
        Float64,
        Int32,
        Int64,
        Bool
    }
}
