using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// ServiceWorker — Manages lifecycle of all runtime services.
    /// Handles startup sequencing, health monitoring, graceful shutdown,
    /// and automatic restart of failed services.
    /// Reads server.manifest.json for configuration.
    /// </summary>
    public class ServiceWorker : IDisposable
    {
        public enum ServiceState { Stopped, Starting, Running, Degraded, Failed, Stopping }

        public class ServiceInfo
        {
            public string Name { get; set; }
            public ServiceState State { get; set; } = ServiceState.Stopped;
            public DateTime StartedAt { get; set; }
            public int RestartCount { get; set; }
            public string Error { get; set; }
            public Func<Task> StartAction { get; set; }
            public Func<Task> StopAction { get; set; }
        }

        private readonly Dictionary<string, ServiceInfo> _services = new();
        private readonly List<Func<Task>> _startupChain = new();
        private readonly List<Func<Task>> _shutdownChain = new();
        private readonly CancellationTokenSource _cts = new();
        private ServiceState _overallState = ServiceState.Stopped;
        private DateTime _startedAt;
        private readonly HealthMonitor _health;

        public class HealthMonitor
        {
            private readonly Dictionary<string, HealthEntry> _entries = new();
            public class HealthEntry
            {
                public bool Healthy { get; set; }
                public string Message { get; set; }
                public DateTime LastCheck { get; set; }
            }

            public void Report(string service, bool healthy, string message = null)
            {
                _entries[service] = new HealthEntry
                {
                    Healthy = healthy,
                    Message = message,
                    LastCheck = DateTime.UtcNow
                };
            }

            public bool IsHealthy(string service) =>
                _entries.TryGetValue(service, out var e) && e.Healthy;

            public IReadOnlyDictionary<string, HealthEntry> All => _entries;
        }

        public ServiceWorker()
        {
            _health = new HealthMonitor();
        }

        public ServiceState State => _overallState;
        public HealthMonitor Health => _health;
        public DateTime StartedAt => _startedAt;
        public IReadOnlyDictionary<string, ServiceInfo> Services => _services;

        // ---- Service registration ----

        public ServiceWorker Register(string name,
            Func<Task> startAction,
            Func<Task> stopAction = null,
            bool autoStart = true)
        {
            _services[name] = new ServiceInfo
            {
                Name = name,
                State = ServiceState.Stopped,
                StartAction = startAction,
                StopAction = stopAction
            };
            if (autoStart)
                _startupChain.Add(async () =>
                {
                    await StartService(name);
                });
            return this;
        }

        public ServiceWorker AddStartupHook(Func<Task> hook)
        {
            _startupChain.Insert(0, hook);
            return this;
        }

        public ServiceWorker AddShutdownHook(Func<Task> hook)
        {
            _shutdownChain.Add(hook);
            return this;
        }

        // ---- Startup ----

        public async Task StartAsync()
        {
            _overallState = ServiceState.Starting;
            _startedAt = DateTime.UtcNow;

            // Pre-startup hooks (e.g., load manifests)
            foreach (var hook in _startupChain.Where((_, i) => i < _startupChain.Count - _services.Count))
                await SafeRun(hook, "startup_hook");

            // Start all registered services in parallel
            var tasks = _services.Values
                .Where(s => s.State == ServiceState.Stopped)
                .Select(s => StartService(s.Name));

            await Task.WhenAll(tasks);

            // Post-startup hooks
            foreach (var hook in _startupChain.Where((_, i) => i >= _startupChain.Count - _services.Count))
                await SafeRun(hook, "post_startup");

            _overallState = _services.Values.All(s => s.State == ServiceState.Running)
                ? ServiceState.Running
                : ServiceState.Degraded;

            // Start health monitor loop
            _ = Task.Run(() => HealthMonitorLoop(_cts.Token));
        }

        private async Task StartService(string name)
        {
            if (!_services.TryGetValue(name, out var svc)) return;

            try
            {
                svc.State = ServiceState.Starting;
                if (svc.StartAction != null)
                    await svc.StartAction();
                svc.State = ServiceState.Running;
                svc.StartedAt = DateTime.UtcNow;
                _health.Report(name, true, "Running");
            }
            catch (Exception ex)
            {
                svc.State = ServiceState.Failed;
                svc.Error = ex.Message;
                _health.Report(name, false, ex.Message);
            }
        }

        // ---- Shutdown ----

        public async Task StopAsync()
        {
            _overallState = ServiceState.Stopping;
            _cts.Cancel();

            // Reverse order shutdown
            foreach (var hook in _shutdownChain.AsEnumerable().Reverse())
                await SafeRun(hook, "shutdown_hook");

            foreach (var svc in _services.Values.Reverse())
                await StopService(svc);

            _overallState = ServiceState.Stopped;
        }

        private async Task StopService(ServiceInfo svc)
        {
            if (svc.State == ServiceState.Stopped) return;
            try
            {
                svc.State = ServiceState.Stopping;
                if (svc.StopAction != null)
                    await svc.StopAction();
            }
            catch { }
            svc.State = ServiceState.Stopped;
        }

        // ---- Restart ----

        public async Task RestartService(string name)
        {
            if (!_services.TryGetValue(name, out var svc)) return;
            await StopService(svc);
            svc.RestartCount++;
            await StartService(name);
        }

        // ---- Health monitor loop ----

        private async Task HealthMonitorLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    foreach (var kv in _services)
                    {
                        if (kv.Value.State == ServiceState.Failed && kv.Value.RestartCount < 3)
                        {
                            _health.Report(kv.Key, false, "Attempting restart...");
                            await RestartService(kv.Key);
                        }
                    }

                    // Update overall state
                    _overallState = _services.Values.All(s => s.State == ServiceState.Running)
                        ? ServiceState.Running
                        : ServiceState.Degraded;

                    await Task.Delay(5000, ct);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }

        // ---- Helpers ----

        private async Task SafeRun(Func<Task> action, string context)
        {
            try { await action(); }
            catch (Exception ex) { _health.Report(context, false, ex.Message); }
        }

        // ---- Serialize state to JSON ----

        public string GetStatusJson()
        {
            return JsonSerializer.Serialize(new
            {
                state = _overallState.ToString(),
                uptime = (DateTime.UtcNow - _startedAt).TotalSeconds,
                services = _services.ToDictionary(kv => kv.Key, kv => new
                {
                    state = kv.Value.State.ToString(),
                    started = kv.Value.StartedAt,
                    restarts = kv.Value.RestartCount,
                    error = kv.Value.Error
                }),
                health = _health.All.ToDictionary(kv => kv.Key, kv => new
                {
                    healthy = kv.Value.Healthy,
                    message = kv.Value.Message,
                    last_check = kv.Value.LastCheck
                })
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        // ---- Factory: create from server.manifest.json ----

        public static ServiceWorker FromManifest(string manifestPath = null)
        {
            var worker = new ServiceWorker();
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            var paths = new[]
            {
                manifestPath,
                Path.Combine(baseDir, "server.manifest.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "server.manifest.json")
            };

            foreach (var path in paths)
            {
                if (path != null && File.Exists(path))
                {
                    try
                    {
                        var json = File.ReadAllText(path);
                        using var doc = JsonDocument.Parse(json);
                        var server = doc.RootElement.GetProperty("server");

                        // Register services from manifest lifecycle
                        if (server.TryGetProperty("lifecycle", out var lifecycle))
                        {
                            // Add startup hooks
                            if (lifecycle.TryGetProperty("startup", out var startup))
                            {
                                foreach (var step in startup.EnumerateArray())
                                {
                                    worker.AddStartupHook(async () =>
                                    {
                                        System.Console.WriteLine($"[ServiceWorker] Start: {step.GetString()}");
                                        await Task.CompletedTask;
                                    });
                                }
                            }

                            // Add shutdown hooks
                            if (lifecycle.TryGetProperty("shutdown", out var shutdown))
                            {
                                foreach (var step in shutdown.EnumerateArray())
                                {
                                    worker.AddShutdownHook(async () =>
                                    {
                                        System.Console.WriteLine($"[ServiceWorker] Stop: {step.GetString()}");
                                        await Task.CompletedTask;
                                    });
                                }
                            }
                        }
                    }
                    catch { }
                    break;
                }
            }

            return worker;
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}
