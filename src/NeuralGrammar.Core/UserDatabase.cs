using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// UserDatabase — Receives Google IDs and maintains per-user runtime data,
    /// API configuration, capabilities, preferences, and buddy-network policy.
    /// Persists to disk as JSON.
    /// </summary>
    public class UserDatabase
    {
        // ---- Data types ----
        public class User
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 16);
            public string Username { get; set; }
            public string ExternalProvider { get; set; }
            public string ExternalUserId { get; set; }
            public string Email { get; set; }
            public string DisplayName { get; set; }
            public string AvatarUrl { get; set; }
            public string PasswordHash { get; set; }
            public string PasswordSalt { get; set; }
            public int PasswordIterations { get; set; } = 210000;
            public string PasswordAlgorithm { get; set; } = "PBKDF2-SHA256";
            public string Role { get; set; } = "user"; // "admin", "user", "service"
            public Dictionary<string, string> Preferences { get; set; } = new();
            public List<string> GrantedCapabilities { get; set; } = new() { "chat", "search", "math" };

            // OAuth token state — stored for session continuity, not exported.
            public string OAuthAccessToken { get; set; }
            public string OAuthRefreshToken { get; set; }
            public DateTime OAuthTokenExpiry { get; set; }

            // Buddy-network privacy defaults are deny-by-default.
            public bool AllowRemoteMicronauts { get; set; } = false;
            public bool AllowPublicMicronautPool { get; set; } = false;
            public string DefaultPrivacyClass { get; set; } = "private";
            public List<string> TrustedBuddyUserIds { get; set; } = new();
            public List<string> TrustedBuddyNodeIds { get; set; } = new();

            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime? LastLogin { get; set; }
            public bool Active { get; set; } = true;
        }

        public class Session
        {
            public string Token { get; set; }
            public string UserId { get; set; }
            public string Username { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);
            public string IpAddress { get; set; }
        }

        public class ApiKey
        {
            public string Key { get; set; }
            public string KeyHash { get; set; }
            public string UserId { get; set; }
            public string Description { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public bool Active { get; set; } = true;
            public List<string> Scopes { get; set; } = new() { "chat", "search" };
        }

        // ---- State ----
        private readonly List<User> _users = new();
        private readonly List<Session> _sessions = new();
        private readonly List<ApiKey> _apiKeys = new();
        private readonly string _storagePath;
        private readonly object _lock = new();

        public UserDatabase(string storagePath = null)
        {
            _storagePath = storagePath ?? Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, ".users", "database.json");
            Load();
            EnsureDefaultUsers();
        }

        public string StoragePath => _storagePath;
        public IReadOnlyList<User> Users { get { lock (_lock) return _users.ToList(); } }
        public IReadOnlyList<Session> Sessions { get { lock (_lock) return _sessions.ToList(); } }
        public IReadOnlyList<ApiKey> ApiKeys { get { lock (_lock) return _apiKeys.ToList(); } }

        // ---- Default users ----

        private void EnsureDefaultUsers()
        {
            lock (_lock)
            {
                if (_users.Count != 0) return;

                // No known default administrator credential is manufactured.
                _users.Add(new User
                {
                    Username = "micronaut",
                    PasswordHash = "",
                    PasswordSalt = "",
                    PasswordAlgorithm = "NONE",
                    Role = "service",
                    GrantedCapabilities = new List<string> { "chat", "search", "math", "plan", "mcp" }
                });
                Save();
            }
        }

        // ---- post(google-id) ----

        public User PostGoogleId(
            string googleId,
            string email = null,
            string displayName = null,
            IEnumerable<string> capabilities = null)
        {
            if (string.IsNullOrWhiteSpace(googleId))
                throw new ArgumentException("Google ID is required.", nameof(googleId));

            googleId = googleId.Trim();

            lock (_lock)
            {
                var existing = _users.FirstOrDefault(u =>
                    string.Equals(u.ExternalProvider, "google", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(u.ExternalUserId, googleId, StringComparison.Ordinal));

                if (existing != null)
                {
                    existing.Active = true;
                    if (!string.IsNullOrWhiteSpace(email))
                        existing.Email = email.Trim();
                    if (!string.IsNullOrWhiteSpace(displayName))
                    {
                        existing.DisplayName = displayName.Trim();
                        existing.Username = displayName.Trim();
                    }
                    if (capabilities != null)
                        existing.GrantedCapabilities = capabilities.ToList();

                    existing.LastLogin = DateTime.UtcNow;
                    Save();
                    return CloneUser(existing);
                }

                var user = new User
                {
                    Username = !string.IsNullOrWhiteSpace(displayName)
                        ? displayName.Trim()
                        : (!string.IsNullOrWhiteSpace(email) ? email.Trim() : "google-user"),
                    ExternalProvider = "google",
                    ExternalUserId = googleId,
                    Email = email?.Trim(),
                    DisplayName = displayName?.Trim(),
                    PasswordHash = "",
                    PasswordSalt = "",
                    PasswordAlgorithm = "NONE",
                    Role = "user",
                    GrantedCapabilities = capabilities?.ToList()
                        ?? new List<string> { "chat", "search" },
                    LastLogin = DateTime.UtcNow
                };

                _users.Add(user);
                Save();
                return CloneUser(user);
            }
        }

        // Compatibility alias; PostGoogleId is the canonical operation.
        public User GetOrCreateGoogleUser(
            string googleId,
            string email = null,
            string displayName = null,
            IEnumerable<string> capabilities = null)
        {
            return PostGoogleId(googleId, email, displayName, capabilities);
        }

        /// <summary>Store OAuth token state on an existing user record.</summary>
        public void SetOAuthToken(string googleId, string accessToken, string refreshToken, DateTime expiry)
        {
            if (string.IsNullOrWhiteSpace(googleId)) return;

            lock (_lock)
            {
                var user = _users.FirstOrDefault(u =>
                    string.Equals(u.ExternalProvider, "google", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(u.ExternalUserId, googleId, StringComparison.Ordinal));

                if (user == null) return;

                user.OAuthAccessToken = accessToken;
                user.OAuthRefreshToken = refreshToken;
                user.OAuthTokenExpiry = expiry;
                Save();
            }
        }

        // ---- Authentication ----

        public User Authenticate(string username, string password)
        {
            lock (_lock)
            {
                var user = _users.FirstOrDefault(u =>
                    u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)
                    && u.Active);
                if (user == null) return null;
                if (!VerifyPassword(user, password)) return null;
                if (!string.Equals(user.PasswordAlgorithm, "PBKDF2-SHA256", StringComparison.OrdinalIgnoreCase))
                    SetPasswordCredential(user, password);

                user.LastLogin = DateTime.UtcNow;
                Save();
                return user;
            }
        }

        public Session CreateSession(User user, string ipAddress = null)
        {
            var session = new Session
            {
                Token = GenerateToken(),
                UserId = user.Id,
                Username = user.Username,
                IpAddress = ipAddress,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24)
            };

            lock (_lock)
            {
                // Clean expired sessions
                _sessions.RemoveAll(s => s.ExpiresAt < DateTime.UtcNow);
                _sessions.Add(session);
                Save();
            }

            return session;
        }

        public User ValidateSession(string token)
        {
            lock (_lock)
            {
                var session = _sessions.FirstOrDefault(s =>
                    s.Token == token && s.ExpiresAt > DateTime.UtcNow);
                if (session == null) return null;
                return _users.FirstOrDefault(u => u.Id == session.UserId && u.Active);
            }
        }

        public void DestroySession(string token)
        {
            lock (_lock)
            {
                _sessions.RemoveAll(s => s.Token == token);
                Save();
            }
        }

        // ---- API Key auth ----

        public ApiKey CreateApiKey(string userId, string description, string[] scopes = null)
        {
            var secret = GenerateToken(32);
            var key = new ApiKey
            {
                Key = null,
                KeyHash = HashSecret(secret),
                UserId = userId,
                Description = description,
                Scopes = scopes?.ToList() ?? new List<string> { "chat" }
            };

            lock (_lock)
            {
                _apiKeys.Add(key);
                Save();
            }

            return key;
        }

        public User ValidateApiKey(string apiKey)
        {
            lock (_lock)
            {
                var suppliedHash = HashSecret(apiKey);
                var key = _apiKeys.FirstOrDefault(k =>
                    k.Active &&
                    ((!string.IsNullOrEmpty(k.KeyHash) && FixedTimeEquals(k.KeyHash, suppliedHash)) ||
                     (!string.IsNullOrEmpty(k.Key) && FixedTimeEquals(k.Key, apiKey))));
                if (key == null) return null;
                return _users.FirstOrDefault(u => u.Id == key.UserId && u.Active);
            }
        }

        public void RevokeApiKey(string apiKey)
        {
            lock (_lock)
            {
                var key = _apiKeys.FirstOrDefault(k => k.Key == apiKey);
                if (key != null) key.Active = false;
                Save();
            }
        }

        // ---- User management ----

        public User CreateUser(string username, string password, string role = "user",
            string[] capabilities = null)
        {
            lock (_lock)
            {
                if (_users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
                    return null; // Already exists

                var user = new User
                {
                    Username = username,
                    Role = role,
                    GrantedCapabilities = capabilities?.ToList()
                        ?? new List<string> { "chat", "search" }
                };
                SetPasswordCredential(user, password);
                _users.Add(user);
                Save();
                return user;
            }
        }

        public User GetUser(string id)
        {
            lock (_lock) return _users.FirstOrDefault(u => u.Id == id && u.Active);
        }

        public User GetUserByUsername(string username)
        {
            lock (_lock)
                return _users.FirstOrDefault(u =>
                    u.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && u.Active);
        }

        public bool DeactivateUser(string id)
        {
            lock (_lock)
            {
                var user = _users.FirstOrDefault(u => u.Id == id);
                if (user == null) return false;
                user.Active = false;
                _sessions.RemoveAll(s => s.UserId == id);
                Save();
                return true;
            }
        }

        // ---- Buddy network trust / privacy ----

        public bool TrustBuddyUser(string userId, string buddyExternalUserId)
        {
            if (string.IsNullOrWhiteSpace(userId) ||
                string.IsNullOrWhiteSpace(buddyExternalUserId))
                return false;

            lock (_lock)
            {
                var user = _users.FirstOrDefault(u => u.Id == userId && u.Active);
                if (user == null) return false;

                if (!user.TrustedBuddyUserIds.Contains(
                    buddyExternalUserId, StringComparer.OrdinalIgnoreCase))
                    user.TrustedBuddyUserIds.Add(buddyExternalUserId);

                Save();
                return true;
            }
        }

        public bool TrustBuddyNode(string userId, string nodeId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(nodeId))
                return false;

            lock (_lock)
            {
                var user = _users.FirstOrDefault(u => u.Id == userId && u.Active);
                if (user == null) return false;

                if (!user.TrustedBuddyNodeIds.Contains(
                    nodeId, StringComparer.OrdinalIgnoreCase))
                    user.TrustedBuddyNodeIds.Add(nodeId);

                Save();
                return true;
            }
        }

        public bool RevokeBuddyUser(string userId, string buddyExternalUserId)
        {
            lock (_lock)
            {
                var user = _users.FirstOrDefault(u => u.Id == userId && u.Active);
                if (user == null) return false;
                var changed = user.TrustedBuddyUserIds.RemoveAll(x =>
                    string.Equals(x, buddyExternalUserId, StringComparison.OrdinalIgnoreCase)) > 0;
                if (changed) Save();
                return changed;
            }
        }

        public bool RevokeBuddyNode(string userId, string nodeId)
        {
            lock (_lock)
            {
                var user = _users.FirstOrDefault(u => u.Id == userId && u.Active);
                if (user == null) return false;
                var changed = user.TrustedBuddyNodeIds.RemoveAll(x =>
                    string.Equals(x, nodeId, StringComparison.OrdinalIgnoreCase)) > 0;
                if (changed) Save();
                return changed;
            }
        }

        public bool SetNetworkPrivacy(
            string userId,
            bool allowRemoteMicronauts,
            bool allowPublicPool,
            string defaultPrivacyClass = "private")
        {
            var privacy = (defaultPrivacyClass ?? "private").Trim().ToLowerInvariant();
            if (privacy != "private" && privacy != "trusted" && privacy != "public")
                return false;

            lock (_lock)
            {
                var user = _users.FirstOrDefault(u => u.Id == userId && u.Active);
                if (user == null) return false;

                user.AllowRemoteMicronauts = allowRemoteMicronauts;
                user.AllowPublicMicronautPool = allowRemoteMicronauts && allowPublicPool;
                user.DefaultPrivacyClass = privacy;
                Save();
                return true;
            }
        }

        // ---- Preferences ----

        public string GetPreference(string userId, string key, string defaultValue = null)
        {
            lock (_lock)
            {
                var user = _users.FirstOrDefault(u => u.Id == userId);
                return user?.Preferences?.GetValueOrDefault(key, defaultValue) ?? defaultValue;
            }
        }

        public void SetPreference(string userId, string key, string value)
        {
            lock (_lock)
            {
                var user = _users.FirstOrDefault(u => u.Id == userId);
                if (user != null)
                {
                    user.Preferences[key] = value;
                    Save();
                }
            }
        }

        // ---- Capability check ----

        /// <summary>
        /// Authorization fact only. XCFE/K'UHUL owns admission and routing.
        /// </summary>
        public bool HasCapability(User user, string capability)
        {
            if (user == null || !user.Active || string.IsNullOrWhiteSpace(capability))
                return false;

            return user.GrantedCapabilities.Any(c =>
                c == "*" || string.Equals(c, capability, StringComparison.OrdinalIgnoreCase));
        }

        // ---- Persistence ----

        private void Load()
        {
            try
            {
                if (File.Exists(_storagePath))
                {
                    var json = File.ReadAllText(_storagePath);
                    var data = JsonSerializer.Deserialize<StorageData>(json);
                    if (data != null)
                    {
                        lock (_lock)
                        {
                            _users.Clear(); _users.AddRange(data.Users ?? new List<User>());
                            _sessions.Clear(); _sessions.AddRange(data.Sessions ?? new List<Session>());
                            _apiKeys.Clear(); _apiKeys.AddRange(data.ApiKeys ?? new List<ApiKey>());
                        }
                    }
                }
            }
            catch { /* Start fresh */ }
        }

        private void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(_storagePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var data = new StorageData
                {
                    Users = _users.ToList(),
                    Sessions = _sessions.ToList(),
                    ApiKeys = _apiKeys.ToList()
                };

                var json = JsonSerializer.Serialize(data,
                    new JsonSerializerOptions { WriteIndented = true });
                var tempPath = _storagePath + ".tmp";
                File.WriteAllText(tempPath, json, Encoding.UTF8);
                File.Move(tempPath, _storagePath, true);
            }
            catch { }
        }

        // ---- Helpers ----

        /// <summary>
        /// Returns a detached user view without credential material.
        /// Includes external identity, capabilities, and buddy-network policy.
        /// </summary>
        private static User CloneUser(User u)
        {
            if (u == null) return null;

            return new User
            {
                Id = u.Id,
                Username = u.Username,
                ExternalProvider = u.ExternalProvider,
                ExternalUserId = u.ExternalUserId,
                Email = u.Email,
                DisplayName = u.DisplayName,

                // Never expose persisted credential material through user lookups.
                PasswordHash = null,
                PasswordSalt = null,
                PasswordIterations = u.PasswordIterations,
                PasswordAlgorithm = u.PasswordAlgorithm,

                Role = u.Role,
                Preferences = u.Preferences != null
                    ? new Dictionary<string, string>(u.Preferences)
                    : new Dictionary<string, string>(),
                GrantedCapabilities = u.GrantedCapabilities?.ToList()
                    ?? new List<string>(),

                AllowRemoteMicronauts = u.AllowRemoteMicronauts,
                AllowPublicMicronautPool = u.AllowPublicMicronautPool,
                DefaultPrivacyClass = u.DefaultPrivacyClass ?? "private",
                TrustedBuddyUserIds = u.TrustedBuddyUserIds?.ToList()
                    ?? new List<string>(),
                TrustedBuddyNodeIds = u.TrustedBuddyNodeIds?.ToList()
                    ?? new List<string>(),

                CreatedAt = u.CreatedAt,
                LastLogin = u.LastLogin,
                Active = u.Active
            };
        }

        private const int DefaultPasswordIterations = 210000;

        private static void SetPasswordCredential(User user, string password)
        {
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);
            using var kdf = new Rfc2898DeriveBytes(
                password ?? "", salt, DefaultPasswordIterations, HashAlgorithmName.SHA256);

            user.PasswordSalt = Convert.ToBase64String(salt);
            user.PasswordHash = Convert.ToBase64String(kdf.GetBytes(32));
            user.PasswordIterations = DefaultPasswordIterations;
            user.PasswordAlgorithm = "PBKDF2-SHA256";
        }

        private static bool VerifyPassword(User user, string password)
        {
            if (user == null || string.IsNullOrEmpty(user.PasswordHash)) return false;

            if (string.Equals(user.PasswordAlgorithm, "PBKDF2-SHA256", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(user.PasswordSalt))
            {
                try
                {
                    var salt = Convert.FromBase64String(user.PasswordSalt);
                    using var kdf = new Rfc2898DeriveBytes(
                        password ?? "", salt, Math.Max(10000, user.PasswordIterations),
                        HashAlgorithmName.SHA256);
                    return FixedTimeEquals(user.PasswordHash,
                        Convert.ToBase64String(kdf.GetBytes(32)));
                }
                catch { return false; }
            }

            // Legacy SHA-256 compatibility; successful login migrates to PBKDF2.
            using var sha = SHA256.Create();
            var legacy = Convert.ToBase64String(
                sha.ComputeHash(Encoding.UTF8.GetBytes(password ?? "")));
            return FixedTimeEquals(user.PasswordHash, legacy);
        }

        private static string HashSecret(string value)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(
                sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")));
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            var aa = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            return aa.Length == bb.Length &&
                   CryptographicOperations.FixedTimeEquals(aa, bb);
        }

        private static string GenerateToken(int length = 24)
        {
            var bytes = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes)
                .Replace("/", "_").Replace("+", "-").Replace("=", "");
        }

        // ---- Storage model ----

        private class StorageData
        {
            public List<User> Users { get; set; } = new();
            public List<Session> Sessions { get; set; } = new();
            public List<ApiKey> ApiKeys { get; set; } = new();
        }
    }
}
