#nullable disable
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TankDraft.Infrastructure.FusionTransport
{
    /// <summary>Protocol adapter. Existing queue/session/intent logic owns operation IDs and retries.</summary>
    public sealed class FusionAuthorityClient
    {
        readonly FusionRequestChannel channel;
        public FusionAuthorityClient(FusionRequestChannel channel) { this.channel = channel ?? throw new ArgumentNullException(nameof(channel)); }
        public async Task<JObject> ExchangeAsync(string operation, string authorization, JObject body, CancellationToken token)
        {
            if (body == null || authorization == null || authorization.Length > 4096) throw new ArgumentException();
            switch (operation)
            {
                case "Lobby": case "Session": case "Join": case "Status": case "Cancel": case "Leave": case "SocketOpen": case "SocketExchange": break;
                default: throw new InvalidDataException();
            }
            var request = Encoding.UTF8.GetBytes(new JObject { ["Operation"] = operation, ["Authorization"] = authorization, ["Body"] = body.DeepClone() }.ToString(Formatting.None));
            var response = await channel.ExchangeAsync(request, token).ConfigureAwait(false);
            using (var reader = new JsonTextReader(new StringReader(new UTF8Encoding(false, true).GetString(response))) { MaxDepth = 32, DateParseHandling = DateParseHandling.None })
            {
                var root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read() || root.Count != 2 || root["Status"]?.Type != JTokenType.Integer || !(root["Body"] is JObject result)) throw new InvalidDataException();
                var status = root.Value<int>("Status");
                if (status < 100 || status > 599) throw new InvalidDataException();
                if (status != 200) throw new FusionAuthorityException(status);
                return result;
            }
        }
    }
    public sealed class FusionAuthorityException : Exception
    {
        public int Status { get; }
        public string Code { get; }
        public FusionAuthorityException(int status, string code = null) : base("fusion_authority_rejected") { Status = status; Code = code; }
    }
}
