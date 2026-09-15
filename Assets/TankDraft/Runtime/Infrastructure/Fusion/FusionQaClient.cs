#nullable disable
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TankDraft.Infrastructure.FusionTransport
{
    public sealed class FusionQaClient
    {
        readonly object sync = new object();
        FusionRequestChannel channel;
        public FusionSnapshotInbox Snapshots { get; }
        public FusionQaClient(FusionRequestChannel channel, FusionSnapshotInbox snapshots = null) { this.channel = channel ?? throw new ArgumentNullException(nameof(channel)); Snapshots = snapshots; }
        public void ReplaceChannel(FusionRequestChannel replacement)
        {
            lock (sync) { var prior = channel; channel = replacement; prior?.Dispose(); }
        }
        public async Task<JObject> ExchangeAsync(string operation, JObject body, CancellationToken token)
        {
            FusionRequestChannel current;
            lock (sync) current = channel ?? throw new System.Net.WebSockets.WebSocketException("Fusion reconnecting.");
            var bytes = Encoding.UTF8.GetBytes(new JObject { ["Operation"] = operation, ["Body"] = body?.DeepClone() }.ToString(Formatting.None));
            FusionQaProtocol.ReadRequest(bytes);
            byte[] received;
            try { received = await current.ExchangeAsync(bytes, token).ConfigureAwait(false); }
            catch (System.IO.IOException error) { throw new System.Net.WebSockets.WebSocketException("Fusion connection interrupted.", error); }
            lock (sync)
            {
            if (!ReferenceEquals(current, channel)) throw new System.Net.WebSockets.WebSocketException("Old Fusion connection response.");
            var result = FusionQaProtocol.ReadResponse(received);
            if (result.Value<int>("Status") != 200)
            {
                var codeToken = result["Body"]?["Code"];
                string code = codeToken?.Type == JTokenType.String ? codeToken.Value<string>() : null;
                if (code?.Length > 128) code = null;
                throw new FusionAuthorityException(result.Value<int>("Status"), code);
            }
            var response = (JObject)result["Body"];
            if (response.Value<string>("Kind") == "Welcome" && Snapshots != null) Snapshots.Bind(response.Value<int>("NativeEpoch"));
            if (response.Value<string>("Kind") == "Welcome" || response.Value<string>("Kind") == "Reauthenticated" || response.Value<bool>("NativeAccessBound")) Snapshots?.AcknowledgeAccess(response.Value<int>("Generation"));
            return response;
            }
        }
    }
}
