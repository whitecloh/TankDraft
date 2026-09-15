#nullable disable
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TankDraft.Infrastructure.FusionTransport
{
    /// <summary>Server-only peer bridge. verifiedPlayer must come from NetworkRunner.GetPlayerUserId after Custom Auth.</summary>
    public sealed class FusionAuthorityPeer : IDisposable
    {
        const int MaximumRequest = 8192, MaximumResponse = 1048576;
        readonly HttpClient http;
        readonly string gatewayKey, player;
        readonly Uri endpoint;
        readonly bool photonQa;
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        ClientWebSocket socket;
        public FusionAuthorityPeer(Uri privateEndpoint, string privateGatewayKey, string verifiedPlayer, bool allowPhotonQa = false)
        {
            if (privateEndpoint == null || privateEndpoint.Scheme != "http" || !IPAddress.TryParse(privateEndpoint.Host, out var ip) || !IPAddress.IsLoopback(ip) || privateEndpoint.Fragment.Length != 0 ||
                privateEndpoint.PathAndQuery != "/" || privateEndpoint.UserInfo.Length != 0 || privateGatewayKey?.Length != 64 ||
                string.IsNullOrEmpty(verifiedPlayer) || verifiedPlayer.Length > 32 || verifiedPlayer.Any(c => !IsAscii(c))) throw new ArgumentException();
            endpoint = privateEndpoint; gatewayKey = privateGatewayKey; player = verifiedPlayer;
            photonQa = allowPhotonQa;
            http = new HttpClient(new HttpClientHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false }) { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.Add("X-TankDraft-Gateway", gatewayKey);
            http.DefaultRequestHeaders.Add("X-TankDraft-Player", player);
        }
        public async Task<byte[]> ExecuteAsync(byte[] request, CancellationToken token)
        {
            if (!await gate.WaitAsync(0, token).ConfigureAwait(false)) throw new InvalidOperationException("peer_busy");
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(8));
                var socketOperation = false;
                try
                {
                    var root = Parse(request, MaximumRequest);
                    if (!Only(root, "Operation", "Authorization", "Body")) throw new InvalidDataException();
                    var operation = Text(root, "Operation", 32);
                    socketOperation = operation == "SocketOpen" || operation == "SocketExchange";
                    var body = root["Body"] as JObject ?? throw new InvalidDataException();
                    var authorization = Text(root, "Authorization", 4096, allowEmpty: true);
                    if (operation == "SocketOpen")
                    {
                        if (body.Count != 0 || authorization.Length == 0) throw new InvalidDataException();
                        socket?.Dispose(); socket = new ClientWebSocket(); socket.Options.Proxy = null;
                        socket.Options.SetRequestHeader("Authorization", "Bearer " + authorization);
                        socket.Options.SetRequestHeader("X-TankDraft-Gateway", gatewayKey); socket.Options.SetRequestHeader("X-TankDraft-Player", player);
                        await socket.ConnectAsync(new UriBuilder(endpoint) { Scheme = "ws", Path = "/v1/socket" }.Uri, deadline.Token).ConfigureAwait(false);
                        return Encode(200, new JObject { ["Opened"] = true });
                    }
                    if (operation == "SocketExchange")
                    {
                        if (authorization.Length != 0 || socket == null || socket.State != WebSocketState.Open) throw new InvalidDataException();
                        var bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
                        if (bytes.Length > 4096) throw new InvalidDataException();
                        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, deadline.Token).ConfigureAwait(false);
                        using (var stream = new MemoryStream())
                        {
                            var buffer = new byte[16384]; WebSocketReceiveResult received;
                            do
                            {
                                received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), deadline.Token).ConfigureAwait(false);
                                if (received.MessageType != WebSocketMessageType.Text || stream.Length + received.Count > MaximumResponse) throw new InvalidDataException();
                                stream.Write(buffer, 0, received.Count);
                            } while (!received.EndOfMessage);
                            return Encode(200, Parse(stream.ToArray(), MaximumResponse));
                        }
                    }
                    var path = Route(operation);
                    if (authorization.Length == 0) throw new InvalidDataException();
                    using (var message = new HttpRequestMessage(HttpMethod.Post, path))
                    {
                        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorization);
                        message.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                        using (var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                        {
                            if (response.Content.Headers.ContentLength > MaximumResponse) throw new InvalidDataException();
                            byte[] bytes;
                            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var output = new MemoryStream())
                            {
                                var buffer = new byte[16384]; int count;
                                while ((count = await input.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) != 0)
                                {
                                    if (output.Length + count > MaximumResponse) throw new InvalidDataException();
                                    output.Write(buffer, 0, count);
                                }
                                bytes = output.ToArray();
                            }
                            return Encode((int)response.StatusCode, bytes.Length == 0 ? new JObject() : Parse(bytes, MaximumResponse));
                        }
                    }
                }
                catch
                {
                    // An interrupted exchange must never leave a stale reply for the next command.
                    if (socketOperation) { socket?.Abort(); socket?.Dispose(); socket = null; }
                    throw;
                }
                finally { gate.Release(); }
            }
        }
        string Route(string operation)
        {
            switch (operation)
            {
                case "PhotonLobby" when photonQa: return "/v1/fusion-qa/lobby";
                case "PhotonSession" when photonQa: return "/v1/fusion-qa/session";
                case "Lobby": return "/v1/lobby";
                case "Session": return "/v1/session";
                case "Join": return "/v1/queue/join";
                case "ProfileGet": return "/v1/profile/get";
                case "ProgressionGet": return "/v1/progression/get";
                case "ProgressionExecute": return "/v1/progression/execute";
                case "ProfileSave": return "/v1/profile/loadout";
                case "Status": return "/v1/queue/status";
                case "Cancel": return "/v1/queue/cancel";
                case "Leave": return "/v1/queue/leave";
                default: throw new InvalidDataException();
            }
        }
        static JObject Parse(byte[] bytes, int limit)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > limit) throw new InvalidDataException();
            using (var reader = new JsonTextReader(new StringReader(new UTF8Encoding(false, true).GetString(bytes))) { MaxDepth = 16, DateParseHandling = DateParseHandling.None })
            {
                var value = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new InvalidDataException();
                return value;
            }
        }
        static byte[] Encode(int status, JObject body) => Encoding.UTF8.GetBytes(new JObject { ["Status"] = status, ["Body"] = body }.ToString(Formatting.None));
        static bool Only(JObject body, params string[] names) => body.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(names.OrderBy(n => n, StringComparer.Ordinal));
        static string Text(JObject body, string name, int maximum, bool allowEmpty = false)
        { var value = body[name]; if (value?.Type != JTokenType.String || ((string)value).Length > maximum || !allowEmpty && ((string)value).Length == 0) throw new InvalidDataException(); return (string)value; }
        static bool IsAscii(char value) => value >= 'A' && value <= 'Z' || value >= 'a' && value <= 'z' || value >= '0' && value <= '9';
        public void Dispose() { lifetime.Cancel(); socket?.Abort(); socket?.Dispose(); http.Dispose(); }
    }
}
