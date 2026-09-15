using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TankDraft.Match.ServerClient
{
    public interface IMatchCredentialsFactory
    {
        IMatchCredentials Create();
    }

    public sealed class LocalMatchCredentialsFactory : IMatchCredentialsFactory
    {
        public IMatchCredentials Create() => new LocalProcessCredentials();
    }

    // The PlayFab SDK owner supplies tickets; this transport never acquires or persists player identity itself.
    public interface IPlayFabSessionSource
    {
        Task<string> AcquireSessionTicketAsync(CancellationToken token);
    }

    public sealed class PlayFabMatchCredentials : IMatchCredentials
    {
        const int ResponseLimit = 16 * 1024;
        readonly Uri _sessionEndpoint;
        readonly string _matchId, _contentVersion, _runDirectory;
        readonly int _side;
        readonly IPlayFabSessionSource _tickets;
        readonly HttpClient _http;
        readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        string _operationId;

        public Uri Endpoint { get; }
        public int Side => _side;
        public string RunDirectory => _runDirectory;
        internal string JournalFileName { get; }

        public PlayFabMatchCredentials(Uri endpoint, string matchId, int side, string contentVersion, string runDirectory, IPlayFabSessionSource tickets)
            : this(endpoint, matchId, side, contentVersion, runDirectory, tickets, new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }) { }

        // Test seam only. Production uses the platform TLS handler without a certificate callback override.
        internal PlayFabMatchCredentials(Uri endpoint, string matchId, int side, string contentVersion, string runDirectory, IPlayFabSessionSource tickets, HttpMessageHandler handler)
        {
            if (!ValidEndpoint(endpoint) || !ValidId(matchId) || side < 0 || side > 1 || !ValidId(contentVersion) || string.IsNullOrEmpty(runDirectory) || tickets == null || handler == null)
                throw new InvalidOperationException("Invalid remote match credentials configuration.");
            Endpoint = endpoint;
            _matchId = matchId;
            _side = side;
            _contentVersion = contentVersion;
            _runDirectory = Path.GetFullPath(runDirectory);
            // Scope local command receipts to the server assignment, not just Side.
            // Hash the opaque id so it cannot become a filesystem path.
            using (var sha = System.Security.Cryptography.SHA256.Create())
                JournalFileName = "intent-" + side + "-" + BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(matchId))).Replace("-", "").ToLowerInvariant() + ".json";
            _tickets = tickets;
            _sessionEndpoint = new UriBuilder(endpoint) { Scheme = Uri.UriSchemeHttps, Port = endpoint.IsDefaultPort ? -1 : endpoint.Port, Path = "/v1/session", Query = string.Empty, Fragment = string.Empty }.Uri;
            _http = new HttpClient(handler, true) { Timeout = Timeout.InfiniteTimeSpan };
        }

        public bool ValidateServerCertificate(X509Certificate certificate, X509Chain chain, SslPolicyErrors errors) => certificate != null && errors == SslPolicyErrors.None;

        public async Task<MatchAccess> AcquireAsync(CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var entered = false;
                try
                {
                    await _gate.WaitAsync(timeout.Token).ConfigureAwait(false);
                    entered = true;
                    var ticket = await AcquireTicket(timeout.Token).ConfigureAwait(false);
                    if (!ValidTicket(ticket)) throw new MatchAuthenticationException("Session ticket rejected.");
                    _operationId ??= Guid.NewGuid().ToString("N");
                    using (var request = new HttpRequestMessage(HttpMethod.Post, _sessionEndpoint))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ticket);
                        request.Content = new StringContent(new JObject { ["ContentVersion"] = _contentVersion, ["OperationId"] = _operationId }.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                        try
                        {
                            using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                            {
                                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                                    throw new MatchAuthenticationException("Session ticket rejected.");
                                if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("Session service unavailable.");
                                var value = await ReadResponse(response, timeout.Token).ConfigureAwait(false);
                                var access = ParseAccess(value);
                                _operationId = null;
                                return access;
                            }
                        }
                        catch (MatchAuthenticationException) { throw; }
                        catch (InvalidDataException) { throw; }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception) { throw new HttpRequestException("Session service unavailable."); }
                    }
                }
                finally { if (entered) _gate.Release(); }
            }
        }

        async Task<string> AcquireTicket(CancellationToken token)
        {
            Task<string> task;
            try { task = _tickets.AcquireSessionTicketAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { throw new MatchAuthenticationException("Session ticket rejected."); }
            if (task == null) throw new MatchAuthenticationException("Session ticket rejected.");
            var canceled = Task.Delay(Timeout.InfiniteTimeSpan, token);
            var completed = await Task.WhenAny(task, canceled).ConfigureAwait(false);
            if (completed != task)
            {
                Observe(task);
                token.ThrowIfCancellationRequested();
                throw new OperationCanceledException(token);
            }
            try { return await task.ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { throw new MatchAuthenticationException("Session ticket rejected."); }
        }

        static async void Observe(Task task)
        {
            try { await task.ConfigureAwait(false); }
            catch { }
        }

        async Task<JObject> ReadResponse(HttpResponseMessage response, CancellationToken token)
        {
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var bytes = new MemoryStream())
            {
                var buffer = new byte[4096];
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                {
                    if (bytes.Length + read > ResponseLimit) throw new InvalidDataException("Invalid session response.");
                    bytes.Write(buffer, 0, read);
                }
                try { return ServerWire.Parse(new UTF8Encoding(false, true).GetString(bytes.ToArray())); }
                catch (Exception error) when (error is Newtonsoft.Json.JsonException || error is InvalidDataException || error is DecoderFallbackException)
                { throw new InvalidDataException("Invalid session response."); }
            }
        }

        MatchAccess ParseAccess(JObject value)
        {
            var accessToken = String(value, "AccessToken", ValidAccessToken);
            var sessionId = String(value, "SessionId", ValidId);
            var streamId = String(value, "StreamId", ValidId);
            var matchId = String(value, "MatchId", ValidId);
            var side = Integer(value, "Side");
            var generation = Integer(value, "Generation");
            var expires = Integer(value, "ExpiresInSeconds");
            var refresh = Integer(value, "RefreshAfterSeconds");
            if (matchId != _matchId || side != _side) throw new MatchAuthenticationException("Session assignment rejected.");
            if (generation < 1 || generation > 128 || expires < 1 || expires > 120 || refresh <= 0 || refresh >= expires)
                throw new InvalidDataException("Invalid session response.");
            return new MatchAccess(accessToken, sessionId, streamId, matchId, generation, refresh);
        }

        static string String(JObject value, string name, Func<string, bool> valid)
        {
            var token = value[name];
            if (token == null || token.Type != JTokenType.String || !valid((string)token)) throw new InvalidDataException("Invalid session response.");
            return (string)token;
        }
        static int Integer(JObject value, string name)
        {
            var token = value[name];
            if (token == null || token.Type != JTokenType.Integer || !int.TryParse(token.ToString(), out var result)) throw new InvalidDataException("Invalid session response.");
            return result;
        }
        static bool ValidEndpoint(Uri endpoint)
        {
            if (endpoint == null || !endpoint.IsAbsoluteUri || endpoint.Scheme != "wss" || endpoint.AbsolutePath != "/v1/socket" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0)
                return false;
            var host = endpoint.Host;
            return Uri.CheckHostName(host) == UriHostNameType.Dns && !string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
        }
        static bool ValidId(string value) => ValidAscii(value);
        static bool ValidTicket(string value)
        {
            if (value == null || value.Length == 0 || value.Length > 4096) return false;
            foreach (var character in value) if (character < '!' || character > '~') return false;
            return true;
        }
        static bool ValidAccessToken(string value)
        {
            if (value == null || value.Length != 43) return false;
            foreach (var character in value)
                if (!(character >= 'a' && character <= 'z' || character >= 'A' && character <= 'Z' || character >= '0' && character <= '9' || character == '-' || character == '_')) return false;
            return true;
        }
        static bool ValidAscii(string value)
        {
            if (value == null || value.Length == 0 || value.Length > 128) return false;
            foreach (var character in value)
                if (!(character >= 'a' && character <= 'z' || character >= 'A' && character <= 'Z' || character >= '0' && character <= '9' || character == '-' || character == '_' || character == '.')) return false;
            return true;
        }
        public void Dispose() { _lifetime.Cancel(); _http.Dispose(); }
    }
}
