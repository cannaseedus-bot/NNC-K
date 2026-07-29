using System;
using System.Linq;

namespace NeuralGrammar.Core
{
    public static class AdvancedMath
    {
        private static Random _rng = new();

        // === LINEAR ALGEBRA ===
        public static NDArray Inverse(NDArray m) {
            if (m.Rank != 2 || m.Shape[0] != m.Shape[1]) throw new ArgumentException("Square matrix required");
            int n = m.Shape[0]; var a = m.ToArray(); var aug = new double[n, 2 * n];
            for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) aug[i, j] = a[i * n + j]; aug[i, n + i] = 1; }
            for (int c = 0; c < n; c++) {
                int mr = c; for (int r = c + 1; r < n; r++) if (Math.Abs(aug[r, c]) > Math.Abs(aug[mr, c])) mr = r;
                for (int j = 0; j < 2 * n; j++) { var t = aug[c, j]; aug[c, j] = aug[mr, j]; aug[mr, j] = t; }
                double pv = aug[c, c]; if (Math.Abs(pv) < 1e-10) throw new InvalidOperationException("Singular matrix");
                for (int j = 0; j < 2 * n; j++) aug[c, j] /= pv;
                for (int r = 0; r < n; r++) { if (r == c) continue; double f = aug[r, c]; for (int j = 0; j < 2 * n; j++) aug[r, j] -= f * aug[c, j]; }
            }
            var rv = new NDArray(n, n); for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) rv[i, j] = aug[i, n + j]; return rv;
        }

        public static (NDArray Q, NDArray R) QR(NDArray m) {
            if (m.Rank != 2) throw new ArgumentException("2D required");
            int rows = m.Shape[0], cols = m.Shape[1]; var Q = new NDArray(rows, cols); var R = new NDArray(cols, cols);
            var q = new double[rows, cols]; var A = m.ToArray();
            for (int j = 0; j < cols; j++) {
                var v = new double[rows]; for (int i = 0; i < rows; i++) v[i] = A[i * cols + j];
                for (int i = 0; i < j; i++) { double dot = 0; for (int k = 0; k < rows; k++) dot += q[k, i] * v[k]; for (int k = 0; k < rows; k++) v[k] -= dot * q[k, i]; R[i, j] = dot; }
                double nrm = Math.Sqrt(v.Sum(x => x * x));
                if (nrm > 1e-10) { for (int i = 0; i < rows; i++) q[i, j] = v[i] / nrm; R[j, j] = nrm; }
            }
            for (int i = 0; i < rows; i++) for (int j = 0; j < cols; j++) Q[i, j] = q[i, j];
            return (Q, R);
        }

        public static NDArray EigVals(NDArray m) {
            if (m.Rank != 2 || m.Shape[0] != m.Shape[1]) throw new ArgumentException("Square required");
            int n = m.Shape[0]; var A = m.ToArray(); var ev = new double[2]; var r = new Random();
            // Power iteration for largest
            var v = new double[n]; for (int i = 0; i < n; i++) v[i] = r.NextDouble() - 0.5;
            for (int iter = 0; iter < 100; iter++) { var Av = new double[n]; for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) Av[i] += A[i * n + j] * v[j]; double nr = Math.Sqrt(Av.Sum(x => x * x)); if (nr < 1e-10) break; for (int i = 0; i < n; i++) v[i] = Av[i] / nr; }
            double num = 0, den = 0; for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) { num += v[i] * A[i * n + j] * v[j]; den += v[i] * v[i]; }
            ev[0] = num / den;
            // Inverse iteration for smallest
            var inv = Inverse(m); var invA = inv.ToArray(); var vs = new double[n]; for (int i = 0; i < n; i++) vs[i] = r.NextDouble() - 0.5;
            for (int iter = 0; iter < 100; iter++) { var iAv = new double[n]; for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) iAv[i] += invA[i * n + j] * vs[j]; double nr = Math.Sqrt(iAv.Sum(x => x * x)); if (nr < 1e-10) break; for (int i = 0; i < n; i++) vs[i] = iAv[i] / nr; }
            double numS = 0; for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) numS += vs[i] * A[i * n + j] * vs[j];
            ev[1] = numS; return new NDArray(ev, 2);
        }

        // === FOURIER ===
        public static NDArray FFT(NDArray input) {
            if (input.Rank != 1) throw new ArgumentException("1D required");
            int n = input.Size; var d = input.ToArray(); var r = new NDArray(n);
            for (int k = 0; k < n; k++) { double re = 0, im = 0; for (int t = 0; t < n; t++) { double a = 2 * Math.PI * k * t / n; re += d[t] * Math.Cos(a); im -= d[t] * Math.Sin(a); } r[k] = Math.Sqrt(re * re + im * im); }
            return r;
        }

        public static NDArray DCT(NDArray input) {
            if (input.Rank != 1) throw new ArgumentException("1D required");
            int n = input.Size; var d = input.ToArray(); var r = new NDArray(n);
            for (int k = 0; k < n; k++) { double s = 0; for (int t = 0; t < n; t++) s += d[t] * Math.Cos(Math.PI * k * (2 * t + 1) / (2 * n)); double sc = k == 0 ? 1.0 / Math.Sqrt(2) : 1.0; r[k] = s * Math.Sqrt(2.0 / n) * sc; }
            return r;
        }

        // === STATISTICS ===
        public static NDArray Cov(NDArray a, NDArray b) { if (a.Size != b.Size) throw new ArgumentException("Same length"); double ma = a.Mean(), mb = b.Mean(), s = 0; for (int i = 0; i < a.Size; i++) s += (a.ToArray()[i] - ma) * (b.ToArray()[i] - mb); var r = new NDArray(1); r[0] = s / (a.Size - 1); return r; }
        public static double Corr(NDArray a, NDArray b) { var c = Cov(a, b).ToArray()[0]; return c / (a.Std() * b.Std()); }

        public static NDArray Percentile(NDArray data, double p) {
            if (p < 0 || p > 100) throw new ArgumentException("0-100"); var s = data.ToArray(); Array.Sort(s);
            double idx = (p / 100.0) * (s.Length - 1); int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
            if (lo == hi) { var r = new NDArray(1); r[0] = s[lo]; return r; }
            var r2 = new NDArray(1); r2[0] = s[lo] + (idx - lo) * (s[hi] - s[lo]); return r2;
        }

        public static NDArray Quantiles(NDArray data, int n = 4) { var r = new NDArray(n + 1); for (int i = 0; i <= n; i++) { var q = Percentile(data, (i / (double)n) * 100); if (q.Size > 0) r[i] = q[0]; } return r; }

        // === RANDOM ===
        public static NDArray Uniform(int[] shape, double lo = 0, double hi = 1) { var a = new NDArray(shape); for (int i = 0; i < a.Size; i++) a[i] = lo + (hi - lo) * _rng.NextDouble(); return a; }
        public static NDArray Normal(int[] shape, double mean = 0, double std = 1) { var a = new NDArray(shape); for (int i = 0; i < a.Size; i++) a[i] = mean + std * Math.Sqrt(-2 * Math.Log(_rng.NextDouble() + 1e-10)) * Math.Cos(2 * Math.PI * _rng.NextDouble()); return a; }
        public static NDArray Poisson(int[] shape, double lambda) { var a = new NDArray(shape); for (int i = 0; i < a.Size; i++) { double L = Math.Exp(-lambda); int k = 0; double p = 1; do { k++; p *= _rng.NextDouble(); } while (p > L); a[i] = k - 1; } return a; }
        public static NDArray Binomial(int[] shape, int n, double p) { var a = new NDArray(shape); for (int i = 0; i < a.Size; i++) { int s = 0; for (int j = 0; j < n; j++) if (_rng.NextDouble() < p) s++; a[i] = s; } return a; }

        // === SPECIAL ===
        public static double Gamma(double x) {
            if (x < 0) return double.NaN; if (x < 1) return Gamma(x + 1) / x;
            var c = new[] { 0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059, 12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7 };
            x -= 1; double y = c[0]; for (int i = 1; i < c.Length; i++) y += c[i] / (x + i);
            double t = x + 7 + 0.5; return Math.Sqrt(2 * Math.PI) * Math.Pow(t, x + 0.5) * Math.Exp(-t) * y;
        }
        public static double Beta(double a, double b) => Gamma(a) * Gamma(b) / Gamma(a + b);
        public static double Erf(double x) {
            if (x < 0) return -Erf(-x);
            double t = 1.0 / (1.0 + 0.3275911 * x);
            return 1 - (((((1.061405429 * t + -1.453152027) * t) + 1.421413741) * t + -0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x);
        }
        public static double BesselJ(int n, double x) {
            if (x == 0) return n == 0 ? 1 : 0;
            double sum = 0, term; int k = 0;
            do { term = Math.Pow(-1, k) * Math.Pow(x / 2, 2 * k + n) / (Gamma(k + 1) * Gamma(k + n + 1)); sum += term; k++; }
            while (Math.Abs(term) > 1e-10 && k < 100);
            return sum;
        }
    }
}
