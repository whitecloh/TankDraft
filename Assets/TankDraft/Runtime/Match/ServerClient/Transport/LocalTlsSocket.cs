using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TankDraft.Match.ServerClient
{
    // Local WSS transport for Unity profiles where ClientWebSocket never reaches its scoped validation callback.
    public static class LocalTlsSocket
    {
        const int MaxHeaders = 8192;
        const string WebSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

        public static async Task<WebSocket> ConnectAsync(IMatchCredentials credentials, MatchAccess access, CancellationToken token)
        {
            if (credentials == null) throw new ArgumentNullException(nameof(credentials));
            if (access == null) throw new ArgumentNullException(nameof(access));
            var endpoint = ValidateEndpoint(credentials.Endpoint);
            ValidateHeaderValue(access.Token, "access token");
            var tcp = new TcpClient(AddressFamily.InterNetwork);
            SslStream ssl = null;
            try
            {
                using (token.Register(() => tcp.Dispose()))
                {
                    await tcp.ConnectAsync(endpoint.Host, endpoint.Port).ConfigureAwait(false);
                    ssl = new SslStream(tcp.GetStream(), false, (sender, certificate, chain, errors) =>
                        credentials.ValidateServerCertificate(certificate, chain, errors));
                    await ssl.AuthenticateAsClientAsync(endpoint.Host, null, SslProtocols.Tls12, false).ConfigureAwait(false);

                    var keyBytes = new byte[16];
                    using (var random = RandomNumberGenerator.Create()) random.GetBytes(keyBytes);
                    var key = Convert.ToBase64String(keyBytes);
                    var request = "GET " + endpoint.AbsolutePath + " HTTP/1.1\r\n" +
                        "Host: " + endpoint.Host + ":" + endpoint.Port + "\r\n" +
                        "Authorization: Bearer " + access.Token + "\r\n" +
                        "Connection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: " + key + "\r\n\r\n";
                    var requestBytes = Encoding.ASCII.GetBytes(request);
                    await ssl.WriteAsync(requestBytes, 0, requestBytes.Length, token).ConfigureAwait(false);
                    await ssl.FlushAsync(token).ConfigureAwait(false);
                    ValidateUpgradeResponse(await ReadHeadersAsync(ssl, token).ConfigureAwait(false), key);
                    return WebSocket.CreateFromStream(ssl, false, null, TimeSpan.FromSeconds(15));
                }
            }
            catch
            {
                if (ssl != null) ssl.Dispose();
                tcp.Dispose();
                throw;
            }
        }

        public static void ValidateUpgradeResponse(string response, string key)
        {
            if (string.IsNullOrEmpty(response) || string.IsNullOrEmpty(key)) throw new InvalidDataException("Invalid WebSocket upgrade response.");
            if (!response.EndsWith("\r\n\r\n", StringComparison.Ordinal)) throw new InvalidDataException("WebSocket headers are incomplete.");
            var lines = response.Substring(0, response.Length - 2).Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length < 2 || lines[lines.Length - 1].Length != 0)
                throw new InvalidDataException("WebSocket upgrade was rejected.");
            if (lines[0] == "HTTP/1.1 401 Unauthorized" || lines[0] == "HTTP/1.1 409 Conflict" || lines[0] == "HTTP/1.1 429 Too Many Requests" || lines[0] == "HTTP/1.1 503 Service Unavailable")
                throw new WebSocketException(WebSocketError.ConnectionClosedPrematurely, "Local server temporarily rejected WebSocket upgrade.");
            if (lines[0] != "HTTP/1.1 101 Switching Protocols") throw new InvalidDataException("WebSocket upgrade was rejected.");
            if (lines.Length < 3) throw new InvalidDataException("Malformed WebSocket header.");
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Length - 1; i++)
            {
                var separator = lines[i].IndexOf(':');
                if (separator <= 0 || separator == lines[i].Length - 1) throw new InvalidDataException("Malformed WebSocket header.");
                var name = lines[i].Substring(0, separator);
                var value = lines[i].Substring(separator + 1).Trim();
                if (!IsHeaderName(name) || value.Length == 0 || !headers.TryAdd(name, value)) throw new InvalidDataException("Invalid WebSocket header.");
            }
            if (!headers.TryGetValue("Connection", out var connection) || !ContainsToken(connection, "Upgrade") ||
                !headers.TryGetValue("Upgrade", out var upgrade) || !string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase) ||
                !headers.TryGetValue("Sec-WebSocket-Accept", out var accept) || !FixedEquals(accept, ExpectedAccept(key)))
                throw new InvalidDataException("Invalid WebSocket upgrade response.");
            if (headers.ContainsKey("Sec-WebSocket-Extensions") || headers.ContainsKey("Sec-WebSocket-Protocol") ||
                headers.TryGetValue("Transfer-Encoding", out _) || headers.TryGetValue("Content-Length", out var length) && length != "0")
                throw new InvalidDataException("Unexpected WebSocket response body or negotiation.");
        }

        static Uri ValidateEndpoint(Uri endpoint)
        {
            if (endpoint == null || endpoint.Scheme != "wss" || endpoint.Port != 18783 || endpoint.AbsolutePath != "/v1/socket" ||
                endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0 ||
                endpoint.Host != "127.0.0.1")
                throw new InvalidOperationException("Invalid local TLS endpoint.");
            return endpoint;
        }

        static async Task<string> ReadHeadersAsync(Stream stream, CancellationToken token)
        {
            var bytes = new List<byte>(256);
            var one = new byte[1];
            while (bytes.Count < MaxHeaders)
            {
                if (await stream.ReadAsync(one, 0, 1, token).ConfigureAwait(false) != 1) throw new EndOfStreamException("WebSocket upgrade ended before headers.");
                var value = one[0];
                if (value > 127 || value == 0 || value < 32 && value != '\r' && value != '\n') throw new InvalidDataException("Non-ASCII WebSocket header.");
                bytes.Add(value);
                var count = bytes.Count;
                if (count >= 4 && bytes[count - 4] == '\r' && bytes[count - 3] == '\n' && bytes[count - 2] == '\r' && bytes[count - 1] == '\n')
                    return Encoding.ASCII.GetString(bytes.ToArray());
            }
            throw new InvalidDataException("WebSocket headers exceed limit.");
        }

        static string ExpectedAccept(string key)
        {
            using (var sha = SHA1.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + WebSocketGuid)));
        }
        static bool ContainsToken(string value, string token) => Array.Exists(value.Split(','), item => string.Equals(item.Trim(), token, StringComparison.OrdinalIgnoreCase));
        static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
        static bool IsHeaderName(string value)
        {
            foreach (var character in value)
                if (!(character >= '0' && character <= '9' || character >= 'A' && character <= 'Z' || character >= 'a' && character <= 'z' || character == '-')) return false;
            return true;
        }
        static void ValidateHeaderValue(string value, string name)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128) throw new InvalidDataException("Invalid " + name + ".");
            foreach (var character in value) if (character < 33 || character > 126) throw new InvalidDataException("Invalid " + name + ".");
        }
    }
}
