using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TankDraft.Infrastructure.FusionGameplay
{
    // Keeps uncertain join intents stable; server decides cancellation races and bot admission.
    public sealed class FusionQueueClient
    {
        readonly FusionQaRequests requests;
        readonly string instance, version;
        string lobbyOperation = Guid.NewGuid().ToString("N"), joinOperation;
        bool lobbyReady;
        public FusionQueueClient(FusionQaRequests requests, string instance, string version)
        { this.requests = requests; this.instance = instance; this.version = version; }
        public Task<JObject> Send(string operation, string target, CancellationToken token) => Send(operation, target, token, true);
        async Task<JObject> Send(string operation, string target, CancellationToken token, bool refreshAllowed)
        {
            if (!lobbyReady)
            {
                await requests.Send("Lobby", new JObject { ["OperationId"] = lobbyOperation }, token).ConfigureAwait(false);
                lobbyReady = true;
            }
            var body = new JObject { ["InstanceId"] = instance };
            switch (operation)
            {
                case "Join":
                    joinOperation = joinOperation ?? Guid.NewGuid().ToString("N");
                    body["OperationId"] = joinOperation; body["ContentVersion"] = version; break;
                case "Cancel": body["TicketId"] = target; break;
                case "Leave": body["MatchId"] = target; break;
                case "Status": break;
                default: throw new ArgumentException(nameof(operation));
            }
            JObject response;
            try { response = await requests.Send(operation, body, token).ConfigureAwait(false); }
            catch (FusionTransport.FusionAuthorityException error) when (error.Status == 403 && refreshAllowed)
            {
                lobbyReady = false; lobbyOperation = Guid.NewGuid().ToString("N");
                return await Send(operation, target, token, false).ConfigureAwait(false);
            }
            Validate(response);
            if (response.Value<string>("State") == "Idle") joinOperation = null;
            return response;
        }
        void Validate(JObject value)
        {
            if (value.Value<string>("InstanceId") != instance) throw new InvalidDataException("Fusion instance changed.");
            switch (value.Value<string>("State"))
            {
                case "Idle": return;
                case "Searching":
                    if (string.IsNullOrEmpty(value.Value<string>("TicketId")) || value.Value<int>("RemainingSeconds") < 0) throw new InvalidDataException();
                    return;
                case "Matched":
                    var side = value["Side"];
                    if (string.IsNullOrEmpty(value.Value<string>("MatchId")) || side?.Type != JTokenType.Integer || (int)side < 0 || (int)side > 1 ||
                        (value.Value<string>("OpponentKind") != "Human" && value.Value<string>("OpponentKind") != "Bot")) throw new InvalidDataException();
                    return;
                default: throw new InvalidDataException();
            }
        }
    }
}
