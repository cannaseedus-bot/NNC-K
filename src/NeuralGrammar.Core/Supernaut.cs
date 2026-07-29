using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// Integrated NNC-K runtime composition root.
    /// Receives a Google ID, posts it to UserDatabase, and starts the Micronaut runtime.
    /// XCFE/K'UHUL remain the semantic admission/execution authorities.
    /// </summary>
    public sealed class Supernaut : IDisposable
    {
        private readonly SupernautConfig _config;
        private readonly SemaphoreSlim _initializeGate = new(1, 1);
        private bool _disposed;

        public bool IsInitialized { get; private set; }

        public UserDatabase UserDatabase { get; private set; }
        public UserDatabase.User CurrentUser { get; private set; }
        public MicronautNetworkNode MicronautNetwork { get; private set; }

        public string NodeId { get; private set; }
        public string NodePublicKey { get; private set; }

        public string Endpoint => _config.Endpoint;
        public int MaxTokens => _config.MaxTokens;
        public string ProjectDirectory => _config.ProjectDirectory;
        public string MicronautGasUrl => _config.MicronautGasUrl;

        public TaskPlanner TaskPlanner { get; } = new();

        public Supernaut(SupernautConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _config.Validate();
        }

        /// <summary>
        /// post(google-id) -> UserDatabase -> runtime.
        /// The host supplies the Google ID and optional profile metadata.
        /// </summary>
        public async Task InitializeAsync(
            string googleId,
            string email = null,
            string displayName = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (string.IsNullOrWhiteSpace(googleId))
                throw new ArgumentException("Google ID is required.", nameof(googleId));

            if (IsInitialized) return;

            await _initializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (IsInitialized) return;

                Directory.CreateDirectory(_config.DataDirectory);
                Directory.CreateDirectory(_config.IdentityDirectory);

                UserDatabase = new UserDatabase(_config.UserDatabasePath);

                CurrentUser = UserDatabase.PostGoogleId(
                    googleId,
                    email,
                    displayName);

                var nodeIdentity = InstallationNodeIdentity.LoadOrCreate(
                    _config.NodeIdentityPath,
                    googleId);

                NodeId = nodeIdentity.NodeId;
                NodePublicKey = nodeIdentity.PublicKey;

                MicronautNetwork = new MicronautNetworkNode(
                    _config.MicronautGasUrl,
                    NodeId,
                    NodePublicKey);

                try
                {
                    var registration = await MicronautNetwork
                        .RegisterAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (!registration.Ok)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            "Micronaut network registration declined: " +
                            (registration.Error ?? "unknown error"));
                    }
                }
                catch (Exception ex)
                {
                    // Network registration is non-fatal.
                    // The OAuth identity and local state are still valid.
                    System.Diagnostics.Debug.WriteLine(
                        "Micronaut network registration skipped: " + ex.Message);
                }

                IsInitialized = true;
            }
            finally
            {
                _initializeGate.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            MicronautNetwork?.Dispose();
            _initializeGate.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Supernaut));
        }

        private sealed class InstallationNodeIdentity
        {
            public string NodeId { get; set; }
            public string PublicKey { get; set; }
            public string OwnerSubjectHash { get; set; }

            public static InstallationNodeIdentity LoadOrCreate(
                string path,
                string googleSubject)
            {
                if (File.Exists(path))
                {
                    var lines = File.ReadAllLines(path);
                    if (lines.Length >= 3)
                    {
                        return new InstallationNodeIdentity
                        {
                            NodeId = lines[0],
                            PublicKey = lines[1],
                            OwnerSubjectHash = lines[2]
                        };
                    }
                }

                var identity = new InstallationNodeIdentity
                {
                    NodeId = "node-" + Guid.NewGuid().ToString("N"),
                    PublicKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    OwnerSubjectHash = HashSubject(googleSubject)
                };

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllLines(path, new[]
                {
                    identity.NodeId,
                    identity.PublicKey,
                    identity.OwnerSubjectHash
                });

                return identity;
            }

            private static string HashSubject(string subject)
            {
                var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(subject ?? ""));
                return Convert.ToHexString(bytes).ToLowerInvariant();
            }
        }
    }

    public sealed class SupernautConfig
    {
        public string Endpoint { get; set; } = "http://127.0.0.1:1235";
        public int MaxTokens { get; set; } = 1024;

        public string ProjectDirectory { get; set; }

        public string MicronautGasUrl { get; set; }

        public string DataDirectory { get; set; } =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NNC-K");

        public string IdentityDirectory =>
            Path.Combine(DataDirectory, "identity");

        public string NodeIdentityPath =>
            Path.Combine(IdentityDirectory, "micronaut-node.identity");

        public string UserDatabasePath =>
            Path.Combine(DataDirectory, "users.json");

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Endpoint))
                throw new InvalidOperationException("Supernaut Endpoint is required.");

            if (MaxTokens <= 0)
                throw new InvalidOperationException("Supernaut MaxTokens must be greater than zero.");

            if (string.IsNullOrWhiteSpace(ProjectDirectory))
                throw new InvalidOperationException("Supernaut ProjectDirectory is required.");

            if (string.IsNullOrWhiteSpace(MicronautGasUrl))
                throw new InvalidOperationException("Supernaut MicronautGasUrl is required.");

            if (!Uri.TryCreate(MicronautGasUrl, UriKind.Absolute, out var gas) ||
                !string.Equals(gas.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Supernaut MicronautGasUrl must be an absolute HTTPS URL.");


            if (string.IsNullOrWhiteSpace(DataDirectory))
                throw new InvalidOperationException("Supernaut DataDirectory is required.");
        }
    }
}
