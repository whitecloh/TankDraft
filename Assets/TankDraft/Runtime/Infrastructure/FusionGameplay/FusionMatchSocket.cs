#nullable disable
using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TankDraft.Infrastructure.FusionGameplay
{
    // Adapts existing command/reconnect/presentation loop without moving simulation or tokens to the client.
    public sealed class FusionMatchSocket : WebSocket
    {
        readonly FusionQaRequests requests;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly object sync = new object();
        Task<byte[]> response;
        byte[] bytes;
        int offset;
        WebSocketState state = WebSocketState.Open;
        readonly string qaEvidenceDirectory;
        public FusionMatchSocket(FusionQaRequests requests, string qaEvidenceDirectory = null) { this.requests = requests; this.qaEvidenceDirectory = qaEvidenceDirectory; }
        public override WebSocketCloseStatus? CloseStatus => state == WebSocketState.Closed ? WebSocketCloseStatus.NormalClosure : (WebSocketCloseStatus?)null;
        public override string CloseStatusDescription => "";
        public override WebSocketState State => state;
        public override string SubProtocol => null;
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (messageType != WebSocketMessageType.Text || !endOfMessage || buffer.Count > 4096) throw new InvalidDataException();
            lock (sync)
            {
                if (state != WebSocketState.Open || response != null) throw new WebSocketException("Fusion exchange already pending or closed.");
                var text = new UTF8Encoding(false, true).GetString(buffer.Array, buffer.Offset, buffer.Count);
                var body = JObject.Parse(text);
                if (body.Value<string>("Kind") == "Reauthenticate") body.Remove("AccessToken");
                response = body.Value<string>("Kind") == "Poll" && requests.Snapshots != null ? ReadSnapshot(body, token) : Exchange(body, token);
                return Task.CompletedTask;
            }
        }
        async Task<byte[]> ReadSnapshot(JObject body, CancellationToken token)
        {
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                var cursor = body["AfterEventSequence"]?.Type == JTokenType.Integer ? (long?)body.Value<long>("AfterEventSequence") : null;
                JObject value;
                try { value = await requests.Snapshots.ReadAsync(cursor, stop.Token).ConfigureAwait(false); }
                catch (IOException error) { throw new WebSocketException("Fusion snapshot connection interrupted.", error); }
                return Encoding.UTF8.GetBytes(value.ToString(Formatting.None));
            }
        }
        async Task<byte[]> Exchange(JObject body, CancellationToken token)
        {
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                var result = await requests.Send("SocketExchange", body, stop.Token).ConfigureAwait(false);
                if (body.Value<string>("Kind") == "Command" && result.Value<string>("Kind") == "Ack" &&
                    qaEvidenceDirectory != null && Environment.GetEnvironmentVariable("TD_FUSION_QA_COLD_PENDING") == "1")
                {
                    // Closed PC QA: the server has replied, but the application journal has not consumed its ACK.
                    // The owning launcher kills the process here. No server fault endpoint is exposed.
                    File.WriteAllText(Path.Combine(qaEvidenceDirectory, "cold-restart-ready.json"), result.ToString(Formatting.None));
                    await Task.Delay(60000, stop.Token).ConfigureAwait(false);
                }
                return Encoding.UTF8.GetBytes(result.ToString(Formatting.None));
            }
        }
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token)
        {
            Task<byte[]> pending;
            lock (sync) { if (state != WebSocketState.Open || response == null) throw new WebSocketException(); pending = response; }
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                var cancelled = Task.Delay(Timeout.Infinite, stop.Token);
                if (await Task.WhenAny(pending, cancelled).ConfigureAwait(false) != pending) { Abort(); stop.Token.ThrowIfCancellationRequested(); }
                var received = await pending.ConfigureAwait(false); stop.Token.ThrowIfCancellationRequested();
                lock (sync)
                {
                    if (state != WebSocketState.Open) throw new WebSocketException();
                    bytes = bytes ?? received;
                    var count = Math.Min(buffer.Count, bytes.Length - offset);
                    Buffer.BlockCopy(bytes, offset, buffer.Array, buffer.Offset, count); offset += count;
                    var end = offset == bytes.Length;
                    if (end) { response = null; bytes = null; offset = 0; }
                    return new WebSocketReceiveResult(count, WebSocketMessageType.Text, end);
                }
            }
        }
        public override void Abort() { lock (sync) { state = WebSocketState.Aborted; lifetime.Cancel(); } }
        public override void Dispose() { Abort(); }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string description, CancellationToken token) => CloseOutputAsync(closeStatus, description, token);
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string description, CancellationToken token)
        { lock (sync) { state = WebSocketState.Closed; lifetime.Cancel(); } return Task.CompletedTask; }
    }
}
