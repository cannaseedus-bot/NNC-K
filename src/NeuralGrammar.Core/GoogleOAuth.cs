#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NeuralGrammar.Core
{
    /// <summary>
    /// Transport-level identity provider for network-authenticated requests.
    /// XCFE/K'UHUL still owns admission policy; this covers credential binding.
    /// </summary>
    public interface INetworkIdentityProvider
    {
        /// <summary>
        /// Apply the current access token as the Authorization header.
        /// </summary>
        Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken);

        /// <summary>
        /// Return a usable access token, refreshing or re-authorizing as needed.
        /// </summary>
        Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Google OAuth 2.0 desktop identity adapter for MicronautNetworkNode.
    /// Uses Authorization Code + PKCE and a loopback redirect.
    /// Tokens are transport credentials only; XCFE/K'UHUL still owns admission.
    /// </summary>
    public sealed class GoogleOAuth : INetworkIdentityProvider, IDisposable
    {
        private const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

        private readonly HttpClient _http;
        private readonly bool _ownsHttp;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly GoogleOAuthOptions _options;
        private GoogleOAuthToken? _token;
        private GoogleIdentity? _identity;

        public GoogleIdentity? Identity => _identity;

        public GoogleOAuth(GoogleOAuthOptions options, HttpClient? http = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(_options.ClientId))
                throw new ArgumentException("Google OAuth ClientId is required.", nameof(options));

            _ownsHttp = http == null;
            _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            LoadToken();
        }

        public bool HasRefreshToken => !string.IsNullOrWhiteSpace(_token?.RefreshToken);
        public bool HasUsableAccessToken =>
            !string.IsNullOrWhiteSpace(_token?.AccessToken) &&
            _token!.ExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(1);

        public async Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (HasUsableAccessToken) return _token!.AccessToken!;

                if (HasRefreshToken)
                {
                    try
                    {
                        await RefreshAsync(cancellationToken).ConfigureAwait(false);
                        return _token!.AccessToken!;
                    }
                    catch
                    {
                        // Refresh token may have been revoked; interactive authorization is the recovery path.
                    }
                }

                await AuthorizeInteractiveAsync(cancellationToken).ConfigureAwait(false);
                return _token!.AccessToken!;
            }
            finally { _gate.Release(); }
        }

        public async Task AuthorizeInteractiveAsync(CancellationToken cancellationToken = default)
        {
            var state = Base64Url(RandomNumberGenerator.GetBytes(24));
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(48));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

            int port = GetFreeLoopbackPort();
            string redirectUri = $"http://127.0.0.1:{port}/";
            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);
            listener.Start();

            var scopes = _options.Scopes == null || _options.Scopes.Count == 0
                ? "openid email profile"
                : string.Join(" ", _options.Scopes);

            string authUrl = AuthorizationEndpoint +
                "?client_id=" + E(_options.ClientId) +
                "&redirect_uri=" + E(redirectUri) +
                "&response_type=code" +
                "&scope=" + E(scopes) +
                "&code_challenge=" + E(challenge) +
                "&code_challenge_method=S256" +
                "&state=" + E(state) +
                "&access_type=offline" +
                "&include_granted_scopes=true" +
                (_options.PromptConsent ? "&prompt=consent" : "");

            OpenBrowser(authUrl);

            var contextTask = listener.GetContextAsync();
            var completed = await Task.WhenAny(
                contextTask,
                Task.Delay(_options.InteractiveTimeout, cancellationToken)).ConfigureAwait(false);

            if (completed != contextTask)
            {
                listener.Stop();
                throw new TimeoutException("Google OAuth authorization timed out.");
            }

            var context = await contextTask.ConfigureAwait(false);
            var query = context.Request.QueryString;

            string html;
            if (!string.IsNullOrWhiteSpace(query["error"]))
            {
                html = "<html><body>Authorization was not completed. You can close this window.</body></html>";
                await ReplyAsync(context.Response, html).ConfigureAwait(false);
                throw new InvalidOperationException("Google OAuth error: " + query["error"]);
            }

            if (!FixedEquals(state, query["state"]))
            {
                html = "<html><body>Invalid OAuth state. You can close this window.</body></html>";
                await ReplyAsync(context.Response, html).ConfigureAwait(false);
                throw new InvalidOperationException("Google OAuth state validation failed.");
            }

            var code = query["code"];
            if (string.IsNullOrWhiteSpace(code))
                throw new InvalidOperationException("Google OAuth did not return an authorization code.");

            html = "<html><body>NNC-K Google authorization complete. You can close this window.</body></html>";
            await ReplyAsync(context.Response, html).ConfigureAwait(false);

            var fields = new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId!,
                ["code"] = code,
                ["code_verifier"] = verifier,
                ["redirect_uri"] = redirectUri,
                ["grant_type"] = "authorization_code"
            };
            if (!string.IsNullOrWhiteSpace(_options.ClientSecret))
                fields["client_secret"] = _options.ClientSecret!;

            await ExchangeTokenAsync(fields, preserveRefreshToken: false, cancellationToken)
                .ConfigureAwait(false);

            // Fetch the user's Google profile to populate the Identity property.
            await GetIdentityAsync(cancellationToken).ConfigureAwait(false);
        }

        public void ClearCredential()
        {
            _token = null;
            _identity = null;
            if (!string.IsNullOrWhiteSpace(_options.TokenStorePath) &&
                File.Exists(_options.TokenStorePath))
                File.Delete(_options.TokenStorePath);
        }

        private const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v3/userinfo";

        /// <summary>
        /// Fetch the user's Google profile (sub/id, email, name) using the current
        /// access token. Populates the <see cref="Identity"/> property on success.
        /// </summary>
        public async Task<GoogleIdentity?> GetIdentityAsync(CancellationToken cancellationToken = default)
        {
            if (!HasUsableAccessToken) return null;

            using var req = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _token!.AccessToken!);

            using var resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var sub = root.TryGetProperty("sub", out var s) ? s.GetString() : null;
            if (string.IsNullOrWhiteSpace(sub)) return null;

            _identity = new GoogleIdentity
            {
                GoogleId = sub,
                Email = root.TryGetProperty("email", out var e) ? e.GetString() : null,
                Name = root.TryGetProperty("name", out var n) ? n.GetString() : null,
                Picture = root.TryGetProperty("picture", out var p) ? p.GetString() : null
            };
            return _identity;
        }

        private async Task RefreshAsync(CancellationToken cancellationToken)
        {
            var refresh = _token!.RefreshToken;
            var fields = new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId!,
                ["refresh_token"] = refresh!,
                ["grant_type"] = "refresh_token"
            };
            if (!string.IsNullOrWhiteSpace(_options.ClientSecret))
                fields["client_secret"] = _options.ClientSecret!;

            await ExchangeTokenAsync(fields, preserveRefreshToken: true, cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(_token!.RefreshToken))
                _token!.RefreshToken = refresh;
            SaveToken();
        }

        private async Task ExchangeTokenAsync(
            Dictionary<string, string> fields,
            bool preserveRefreshToken,
            CancellationToken cancellationToken)
        {
            string? oldRefresh = preserveRefreshToken ? _token?.RefreshToken : null;
            using var content = new FormUrlEncodedContent(fields);
            using var response = await _http.PostAsync(TokenEndpoint, content, cancellationToken)
                .ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Google OAuth token exchange failed ({(int)response.StatusCode}): {json}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var next = new GoogleOAuthToken
            {
                AccessToken = root.GetProperty("access_token").GetString(),
                TokenType = root.TryGetProperty("token_type", out var tt) ? tt.GetString() : "Bearer",
                RefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : oldRefresh,
                Scope = root.TryGetProperty("scope", out var sc) ? sc.GetString() : null,
                ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(
                    root.TryGetProperty("expires_in", out var ex) ? ex.GetInt32() : 3600)
            };
            _token = next;
            SaveToken();
        }

        private void LoadToken()
        {
            var path = _options.TokenStorePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                _token = JsonSerializer.Deserialize<GoogleOAuthToken>(
                    File.ReadAllText(path), JsonOptions());
            }
            catch { _token = null; }
        }

        private void SaveToken()
        {
            var path = _options.TokenStorePath;
            if (string.IsNullOrWhiteSpace(path) || _token == null) return;

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_token, JsonOptions()), Encoding.UTF8);
            File.Move(temp, path, true);
        }

        private static JsonSerializerOptions JsonOptions() =>
            new() { PropertyNameCaseInsensitive = true, WriteIndented = true };

        private static int GetFreeLoopbackPort()
        {
            var tcp = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            int port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return port;
        }

        private static void OpenBrowser(string url) =>
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        private static async Task ReplyAsync(HttpListenerResponse response, string html)
        {
            var bytes = Encoding.UTF8.GetBytes(html);
            response.StatusCode = 200;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            response.OutputStream.Close();
        }

        private static string E(string? value) => Uri.EscapeDataString(value ?? "");

        private static string Base64Url(byte[] value) =>
            Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static bool FixedEquals(string? a, string? b)
        {
            if (a == null || b == null) return false;
            var aa = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            return aa.Length == bb.Length && CryptographicOperations.FixedTimeEquals(aa, bb);
        }

        public void Dispose()
        {
            _gate.Dispose();
            if (_ownsHttp) _http.Dispose();
        }
    }

    public sealed class GoogleOAuthOptions
    {
        public string? ClientId { get; set; }
        public string? ClientSecret { get; set; }

        public List<string> Scopes { get; set; } = new()
        {
            "openid",
            "email",
            "profile"
        };

        public bool PromptConsent { get; set; } = true;
        public TimeSpan InteractiveTimeout { get; set; } = TimeSpan.FromMinutes(5);

        public string TokenStorePath { get; set; } =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NNC-K", "google-oauth-tokens.json");

        /// <summary>
        /// Load client_id and client_secret from a Google Cloud client secret JSON file.
        /// The file is the one downloaded from the Google Cloud Console (e.g.,
        /// client_secret_*.apps.googleusercontent.com.json).
        /// </summary>
        /// <param name="path">Path to the client secret JSON file.</param>
        /// <returns>Populated options, or null if the file cannot be read.</returns>
        public static GoogleOAuthOptions? LoadFromClientSecretFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);

                // Structure: { "installed": { "client_id": "...", "client_secret": "...", ... } }
                // or { "web": { "client_id": "...", "client_secret": "...", ... } }
                var root = doc.RootElement;

                // Try "installed" first (desktop app), then "web".
                JsonElement config;
                if (!root.TryGetProperty("installed", out config))
                    root.TryGetProperty("web", out config);

                if (config.ValueKind != JsonValueKind.Object)
                    return null;

                var clientId = config.TryGetProperty("client_id", out var cid)
                    ? cid.GetString() : null;
                var clientSecret = config.TryGetProperty("client_secret", out var cs)
                    ? cs.GetString() : null;

                if (string.IsNullOrWhiteSpace(clientId))
                    return null;

                return new GoogleOAuthOptions
                {
                    ClientId = clientId,
                    ClientSecret = clientSecret
                };
            }
            catch
            {
                return null;
            }
        }
    }

    public sealed class GoogleOAuthToken
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? TokenType { get; set; }
        public string? Scope { get; set; }
        public DateTimeOffset ExpiresUtc { get; set; }
    }

    /// <summary>
    /// Google user profile fetched from the userinfo endpoint after authorization.
    /// </summary>
    public sealed class GoogleIdentity
    {
        public string? GoogleId { get; init; }
        public string? Email { get; init; }
        public string? Name { get; init; }
        public string? Picture { get; init; }
    }
}
