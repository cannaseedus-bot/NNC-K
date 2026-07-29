using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace NeuralGrammar.Core.XCFE
{
    public class GPUProviderRegistry
    {
        private readonly Dictionary<string, GPUProvider> _providers = new();
        private bool _measured;

        public GPUProviderRegistry()
        {
            RegisterDefaultProviders();
        }

        private void RegisterDefaultProviders()
        {
            Register("d3d11_1",   "d3d",   true,  "cs_5_0",       1.0);
            Register("d3d12",     "d3d",   false, "dxil",          0.9);
            Register("d3d11on12", "d3d",   false, "cs_5_0+dxil",  0.85);
            Register("d3d10",     "d3d",   false, "cs_4_0",       0.6);
            Register("d3d9",      "d3d",   false, "ps_3_0",       0.4);
            Register("d3dcompiler","d3d",  false, "D3DCompiler_47",0.8);
            Register("webgpu",    "web",   false, "wgsl",         0.7);
            Register("webgl2",    "web",   false, "glsl_es_300",  0.5);
            Register("opencl",    "gpgpu", false, "opencl_c_1_2", 0.6);
            Register("cpu",       "cpu",   true,  "any",          0.3);
        }

        private void Register(string name, string kind, bool available, string version, double score)
        {
            _providers[name] = new GPUProvider
            {
                Name = name, Kind = kind, IsAvailable = available,
                Version = version, Priority = 0, Score = score
            };
        }

        public GPUProviderRegistry Measure()
        {
            if (_measured) return this;
            _measured = true;

            string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var dxDlls = new (string name, string dll)[]
            {
                ("d3d9",      "d3d9.dll"),      ("d3d9on12",  "d3d9on12.dll"),
                ("d3d10",     "d3d10.dll"),     ("d3d10_1",   "d3d10_1.dll"),
                ("d3d10warp", "d3d10warp.dll"), ("d3d11",     "d3d11.dll"),
                ("d3d11on12", "d3d11on12.dll"), ("d3d12",     "D3D12.dll"),
                ("d3d12core", "D3D12Core.dll"), ("d3dcompiler","D3DCompiler_47.dll"),
                ("d3dcsx",    "d3dcsx_43.dll"), ("dx9",       "D3DX9_43.dll"),
                ("dx10",      "d3dx10_43.dll"), ("dx11",      "d3dx11_43.dll"),
                ("d2d1",      "d2d1.dll"),      ("d3d10level9","d3d10level9.dll")
            };

            foreach (var (n, dll) in dxDlls)
            {
                bool exists = System.IO.File.Exists(System.IO.Path.Combine(sysDir, dll));
                if (_providers.TryGetValue(n, out var p))
                {
                    p.IsAvailable = exists;
                    p.Version = exists ? $"detected:{dll}" : "unavailable";
                    p.Score = exists ? 1.0 : 0.0;
                }
                else if (exists)
                {
                    _providers[n] = new GPUProvider { Name = n, Kind = "dx_dll", IsAvailable = true, Version = $"detected:{dll}", Priority = 0, Score = 0.5 };
                }
            }

            // Detect system executables
            var exes = new (string name, string exe)[]
            {
                ("python", "py.exe"), ("cmd","cmd.exe"), ("cscript","cscript.exe"),
                ("wscript","wscript.exe"), ("curl","curl.exe"), ("ftp","ftp.exe"),
                ("powershell","pwsh.exe"), ("dotnet","dotnet.exe")
            };
            foreach (var (n, exe) in exes)
            {
                var path = System.IO.Path.Combine(sysDir, exe);
                bool exists = System.IO.File.Exists(path);
                _providers[n] = new GPUProvider { Name = n, Kind = "system_exe", IsAvailable = exists, Version = exists ? $"detected:{exe}" : "unavailable", Priority = 0, Score = exists ? 0.5 : 0.0 };
            }
            return this;
        }

        public bool IsAvailable(string name) =>
            _providers.TryGetValue(name, out var p) && p.IsAvailable;

        public string DetectBackend()
        {
            if (IsAvailable("d3d11")) return "d3d11_1";
            if (IsAvailable("d3d12")) return "d3d12";
            if (IsAvailable("d3d10")) return "d3d10";
            if (IsAvailable("d3d9"))  return "d3d9";
            return "cpu";
        }

        public string[] AvailableD3D => _providers
            .Where(kv => kv.Key.StartsWith("d3d") && kv.Value.IsAvailable)
            .Select(kv => kv.Key).ToArray();

        public string[] AvailableSystemExes => _providers
            .Where(kv => kv.Value.Kind == "system_exe" && kv.Value.IsAvailable)
            .Select(kv => kv.Key).ToArray();

        public IReadOnlyDictionary<string, GPUProvider> Providers => _providers;
    }

    public class GPUProvider
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public bool IsAvailable { get; set; }
        public string Version { get; set; }
        public int Priority { get; set; }
        public double Score { get; set; }
    }
}
