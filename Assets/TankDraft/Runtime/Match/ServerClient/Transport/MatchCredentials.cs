using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace TankDraft.Match.ServerClient
{
    public interface IMatchNativePresentation
    {
        bool NativePresentation { get; }
        long NativeSamples { get; }
        double NativeMeanGapMs { get; }
        double NativeMaxGapMs { get; }
        double NativeSourcePollMaxMs { get; }
        double NativeSourceGapMaxMs { get; }
        double NativeDecodeMaxMs { get; }
        int NativeStaleStates { get; }
        bool TryGetPose(string matchId, int round, int id, out TankDraft.Contracts.Battle.BattleVec position, out TankDraft.Contracts.Battle.BattleVec facing, out bool interpolating);
    }
    public interface IMatchIntentLocation { string IntentJournalPath { get; } }
    public interface IMatchOpponentInfo { bool IsBot { get; } }
    public interface IMatchSessionNavigation
    {
        void PrepareReturn(string completedMatchId);
        Task LoadMenuAsync(string menuScene);
    }
    public interface IMatchSocketFactory
    {
        Task<System.Net.WebSockets.WebSocket> ConnectAsync(MatchAccess access, CancellationToken token);
        string JournalFileName { get; }
    }
    public interface IMatchCredentials : IDisposable
    {
        Uri Endpoint { get; }
        int Side { get; }
        string RunDirectory { get; }
        bool ValidateServerCertificate(X509Certificate certificate, X509Chain chain, SslPolicyErrors errors);
        Task<MatchAccess> AcquireAsync(CancellationToken token);
    }

    public sealed class MatchAccess
    {
        public readonly string Token, SessionId, StreamId, MatchId;
        public readonly int Generation;
        public readonly double RefreshAt;
        public MatchAccess(string token, string sessionId, string streamId, string matchId, int generation, double refreshAfterSeconds)
        {
            if (string.IsNullOrEmpty(token) || token.Length > 128 || string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(streamId) || string.IsNullOrEmpty(matchId) ||
                generation < 1 || generation > 128 || refreshAfterSeconds <= 0 || refreshAfterSeconds > 900 || double.IsNaN(refreshAfterSeconds))
                throw new InvalidDataException("Invalid access response: token=" + !string.IsNullOrEmpty(token) + ", session=" + !string.IsNullOrEmpty(sessionId) + ", stream=" + !string.IsNullOrEmpty(streamId) + ", match=" + !string.IsNullOrEmpty(matchId) + ", generation=" + generation + ", refresh=" + refreshAfterSeconds);
            Token = token; SessionId = sessionId; StreamId = streamId; MatchId = matchId; Generation = generation;
            // A small conservative margin avoids a client/server boundary race caused by HTTP response transit.
            RefreshAt = ReceivedServerFrame.Clock + Math.Max(.1, refreshAfterSeconds - .25);
        }
    }

    public sealed class MatchAuthenticationException : Exception
    { public MatchAuthenticationException(string message) : base(message) { } }

    // Explicit local trust policy, scoped to this connection. Never modifies global callbacks or OS trust stores.
    public static class LocalTlsPin
    {
        public static bool Validate(string pin, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors, DateTime utcNow)
        {
            if (pin == null || pin.Length != 64 || certificate == null || (errors & (SslPolicyErrors.RemoteCertificateNotAvailable | SslPolicyErrors.RemoteCertificateNameMismatch)) != 0) return false;
            try
            {
                string actual;
                using (var sha = SHA256.Create()) actual = BitConverter.ToString(sha.ComputeHash(certificate.GetRawCertData())).Replace("-", "");
                if (!string.Equals(actual, pin, StringComparison.OrdinalIgnoreCase)) return false;
#pragma warning disable SYSLIB0057 // Unity .NET Standard profile does not expose X509CertificateLoader.
                using (var leaf = new X509Certificate2(certificate))
#pragma warning restore SYSLIB0057
                {
                    if (utcNow < leaf.NotBefore.ToUniversalTime() || utcNow >= leaf.NotAfter.ToUniversalTime()) return false;
                }
                if ((errors & SslPolicyErrors.RemoteCertificateChainErrors) != 0)
                {
                    if (chain == null) return false;
                    foreach (var status in chain.ChainStatus)
                        if ((status.Status & ~X509ChainStatusFlags.UntrustedRoot) != X509ChainStatusFlags.NoError) return false;
                }
                return true;
            }
            catch (CryptographicException) { return false; }
        }
    }

    // QA login grant is provisioned by the owning launcher; real PlayFab credentials will use another implementation.
    public sealed class LocalProcessCredentials : IMatchCredentials
    {
        readonly string _grant, _pin;
        readonly HttpClient _http;
        readonly Uri _login;
        volatile bool _rejectedCertificate;
        public Uri Endpoint { get; }
        public int Side { get; }
        public string RunDirectory { get; }
        public LocalProcessCredentials()
        {
            Endpoint = new Uri(Environment.GetEnvironmentVariable("TD_LOCAL_ENDPOINT") ?? "about:blank");
            if (Endpoint.Scheme != "wss" || Endpoint.Host != "127.0.0.1" || Endpoint.Port != 18783 || Endpoint.AbsolutePath != "/v1/socket" ||
                Endpoint.Query.Length != 0 || Endpoint.Fragment.Length != 0 || Endpoint.UserInfo.Length != 0) throw new InvalidOperationException("Start this local TLS client with the backend launcher.");
            _grant = Environment.GetEnvironmentVariable("TD_LOCAL_GRANT");
            _pin = Environment.GetEnvironmentVariable("TD_LOCAL_TLS_PIN");
            if (string.IsNullOrEmpty(_grant) || _grant.Length > 128 || _pin == null || _pin.Length != 64) throw new InvalidOperationException("Local credentials or certificate pin missing.");
            foreach (var digit in _pin) if (!Uri.IsHexDigit(digit)) throw new InvalidOperationException("Invalid TLS pin.");
            if (!int.TryParse(Environment.GetEnvironmentVariable("TD_LOCAL_SIDE"), out var side) || side < 0 || side > 1) throw new InvalidOperationException("Invalid local seat.");
            Side = side;
            RunDirectory = Path.GetFullPath(Environment.GetEnvironmentVariable("TD_LOCAL_RUN") ?? throw new InvalidOperationException("Run directory missing."));
            if (!Directory.Exists(RunDirectory)) throw new InvalidOperationException("Run directory missing.");
            _login = new Uri("https://127.0.0.1:18783/v1/local-session");
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
            handler.ServerCertificateCustomValidationCallback = (_, cert, chain, errors) => ValidateServerCertificate(cert, chain, errors);
            _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        }
        public bool ValidateServerCertificate(X509Certificate certificate, X509Chain chain, SslPolicyErrors errors)
        {
            bool valid = LocalTlsPin.Validate(_pin, certificate, chain, errors, DateTime.UtcNow);
            if (!valid) _rejectedCertificate = true;
            return valid;
        }
        public async Task<MatchAccess> AcquireAsync(CancellationToken token)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var request = new HttpRequestMessage(HttpMethod.Post, _login))
            {
                timeout.CancelAfter(5000);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _grant);
                try
                {
                    using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                            throw new MatchAuthenticationException("Login grant expired or revoked.");
                        if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("Session service unavailable.");
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var bytes = new MemoryStream())
                        {
                            var buffer = new byte[4096]; int read;
                            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                            {
                                if (bytes.Length + read > 8192) throw new InvalidDataException("Session response exceeds limit.");
                                bytes.Write(buffer, 0, read);
                            }
                            var data = ServerWire.Parse(new System.Text.UTF8Encoding(false, true).GetString(bytes.ToArray()));
                            if (data.Value<int>("Side") != Side) throw new MatchAuthenticationException("Session account mismatch.");
                            return new MatchAccess(data.Value<string>("AccessToken"), data.Value<string>("SessionId"), data.Value<string>("StreamId"), data.Value<string>("MatchId"),
                                data.Value<int>("Generation"), data.Value<double>("RefreshAfterSeconds"));
                        }
                    }
                }
                catch (HttpRequestException) when (_rejectedCertificate) { throw new MatchAuthenticationException("Server certificate rejected."); }
            }
        }
        public void Dispose() => _http.Dispose();
    }
}
