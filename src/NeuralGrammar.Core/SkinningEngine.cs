using System;
using System.Linq;
using System.Text.Json;

namespace NeuralGrammar.Core
{
    public class SkinningEngine
    {
        private NDArray _pos, _norm, _joints, _weights, _invBind, _skinMat, _skPos, _skNorm;
        private int _vc;

        public SkinningEngine() { }
        public int VertexCount => _vc;
        public bool HasResult => _skPos != null;
        public string ActiveFold { get; set; }

        /// <summary>Skin through a fold-algebraic phase transformation.</summary>
        public void SkinPhase(FoldTensor phaseTensor = null)
        {
            if (!HasResult || _skinMat == null) return;

            // Apply phase-aware weight modulation if a fold tensor is provided.
            if (phaseTensor != null)
            {
                var phaseFactor = phaseTensor.Mean;
                for (int i = 0; i < _vc; i++)
                {
                    _skPos[i, 0] *= phaseFactor;
                    _skPos[i, 1] *= phaseFactor;
                    _skPos[i, 2] *= phaseFactor;
                }
            }
        }

        public void Load(float[] pos, float[] norm, uint[] joints, float[] weights, int vc, int jc)
        {
            _vc = vc;
            _pos = new NDArray(pos.Select(x => (double)x).ToArray(), vc, 3);
            _norm = new NDArray(norm.Select(x => (double)x).ToArray(), vc, 3);
            _joints = new NDArray(joints.Select(x => (double)x).ToArray(), vc, 4);
            _weights = new NDArray(weights.Select(x => (double)x).ToArray(), vc, 4);
            _skPos = new NDArray(vc, 3); _skNorm = new NDArray(vc, 3);
            _skinMat = new NDArray(jc, 4, 4); _invBind = new NDArray(jc, 4, 4);
        }
        public void SetMatrices(float[,,] sm, float[,,] ib) { _skinMat = new NDArray(sm); _invBind = new NDArray(ib); }

        public void SkinCPU()
        {
            for (int i = 0; i < _vc; i++)
            {
                var (j0, j1, j2, j3) = GetJ(i); var (w0, w1, w2, w3) = GetW(i);
                var sm = SkMat(GetJM(j0).Mul(w0).Add(GetJM(j1).Mul(w1)).Add(GetJM(j2).Mul(w2)).Add(GetJM(j3).Mul(w3)), 4, 4);
                var p = M44V4(sm, _pos[i, 0], _pos[i, 1], _pos[i, 2]);
                var n = M44V3(sm, _norm[i, 0], _norm[i, 1], _norm[i, 2]);
                _skPos[i, 0] = p.x; _skPos[i, 1] = p.y; _skPos[i, 2] = p.z;
                _skNorm[i, 0] = n.x; _skNorm[i, 1] = n.y; _skNorm[i, 2] = n.z;
            }
        }

        private NDArray GetJM(uint idx)
        {
            var s = new NDArray(4, 4); var i = new NDArray(4, 4);
            for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++) { s[a, b] = _skinMat[(int)idx, a, b]; i[a, b] = _invBind[(int)idx, a, b]; }
            return MM44(s, i);
        }
        private NDArray MM44(NDArray a, NDArray b) { var r = new NDArray(4, 4); for (int i = 0; i < 4; i++) for (int j = 0; j < 4; j++) { double s = 0; for (int k = 0; k < 4; k++) s += a[i, k] * b[k, j]; r[i, j] = s; } return r; }
        private NDArray SkMat(NDArray m, int r, int c) { var n = new NDArray(r, c); for (int i = 0; i < r; i++) for (int j = 0; j < c; j++) n[i, j] = m[i, j]; return n; }
        private (uint, uint, uint, uint) GetJ(int i) => ((uint)_joints[i, 0], (uint)_joints[i, 1], (uint)_joints[i, 2], (uint)_joints[i, 3]);
        private (double, double, double, double) GetW(int i) => (_weights[i, 0], _weights[i, 1], _weights[i, 2], _weights[i, 3]);

        private (double x, double y, double z) M44V3(NDArray m, double x, double y, double z)
        { double r0 = m[0,0]*x+m[0,1]*y+m[0,2]*z, r1 = m[1,0]*x+m[1,1]*y+m[1,2]*z, r2 = m[2,0]*x+m[2,1]*y+m[2,2]*z; return (r0, r1, r2); }
        private (double x, double y, double z, double w) M44V4(NDArray m, double x, double y, double z)
        { double r0 = m[0,0]*x+m[0,1]*y+m[0,2]*z+m[0,3], r1 = m[1,0]*x+m[1,1]*y+m[1,2]*z+m[1,3], r2 = m[2,0]*x+m[2,1]*y+m[2,2]*z+m[2,3], r3 = m[3,0]*x+m[3,1]*y+m[3,2]*z+m[3,3]; return (r0, r1, r2, r3); }

        public void SkinGPU() { SkinCPU(); }
        /// <summary>
        /// Apply skinning to the currently loaded geometry and return the skinned
        /// position tensor. Fold state is owned by XCFE/FoldAlgebra, not by this
        /// geometry engine.
        /// </summary>
        public NDArray SkinMathML(NDArray input)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            SkinCPU();
            return _skPos;
        }

        public (NDArray pos, NDArray norm) GetResult() => (_skPos, _skNorm);
        public void Export(string path) => System.IO.File.WriteAllText(path, JsonSerializer.Serialize(new { vc = _vc, pos = _skPos.ToArray(), norm = _skNorm.ToArray() }));
    }
}