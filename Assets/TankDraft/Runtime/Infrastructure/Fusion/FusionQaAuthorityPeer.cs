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
    /// <summary>QA server-side credentials. Construct only for an allowlisted, Custom-Authenticated Photon player.</summary>
    public sealed class FusionQaAuthorityPeer : IDisposable
    {
        readonly FusionAuthorityPeer inner;
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        string lobby, access;
        bool parallelSocket;
        public FusionQaAuthorityPeer(Uri endpoint, string gatewayKey, string verifiedPhotonPlayer)
        { inner = new FusionAuthorityPeer(endpoint, gatewayKey, verifiedPhotonPlayer, allowPhotonQa: true); }
        public async Task<byte[]> ExecuteAsync(byte[] request, CancellationToken token)
        {
            // At most one admitted client command and one server snapshot producer share this peer.
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                var root = FusionQaProtocol.ReadRequest(request);
                var operation = (string)root["Operation"];
                if (operation == "SocketOpen" || operation == "Leave") parallelSocket = false;
                var body = (JObject)root["Body"];
                var authorization = "";
                switch (operation)
                {
                    case "Lobby": operation = "PhotonLobby"; authorization = "photon-verified"; break;
                    case "Session": operation = "PhotonSession"; authorization = "photon-verified"; break;
                    case "Join": case "Status": case "Cancel": case "Leave": case "ProfileGet": case "ProfileSave": case "ProgressionGet": case "ProgressionExecute": authorization = lobby ?? throw new InvalidOperationException("lobby_required"); break;
                    case "SocketOpen": authorization = access ?? throw new InvalidOperationException("session_required"); break;
                    case "SocketExchange":
                        if ((string)body["Kind"] == "Reauthenticate") body["AccessToken"] = access ?? throw new InvalidOperationException("session_required");
                        break;
                }
                var forwarded = Encoding.UTF8.GetBytes(new JObject { ["Operation"] = operation, ["Authorization"] = authorization, ["Body"] = body }.ToString(Formatting.None));
                var response = FusionQaProtocol.Read(await inner.ExecuteAsync(forwarded, token).ConfigureAwait(false), 1048704);
                var result = (JObject)response["Body"];
                if (response.Value<int>("Status") == 200)
                {
                    if (operation == "PhotonLobby") { lobby = result.Value<string>("LobbyToken") ?? throw new InvalidDataException(); result.Remove("LobbyToken"); }
                    if (operation == "PhotonSession")
                    {
                        access = result.Value<string>("AccessToken") ?? throw new InvalidDataException(); result.Remove("AccessToken");
                        if (parallelSocket)
                        {
                            // Bind the rotated private token before releasing the peer gate. Internet RTT must not pause state production.
                            var rebind = new JObject { ["Operation"] = "SocketExchange", ["Authorization"] = "", ["Body"] = new JObject { ["Kind"] = "Reauthenticate", ["AccessToken"] = access } };
                            var bound = FusionQaProtocol.Read(await inner.ExecuteAsync(Encoding.UTF8.GetBytes(rebind.ToString(Formatting.None)), token).ConfigureAwait(false), 1048704);
                            var state = (JObject)bound["Body"];
                            if (bound.Value<int>("Status") != 200 || state.Value<string>("Kind") != "Reauthenticated" || state.Value<string>("MatchId") != result.Value<string>("MatchId") || state.Value<int>("Generation") != result.Value<int>("Generation")) throw new InvalidDataException("Private stream rebind failed.");
                            result["NativeAccessBound"] = true;
                        }
                    }
                    if (result.Value<string>("Kind") == "ParallelRefreshEnabled") parallelSocket = true;
                }
                var bytes = Encoding.UTF8.GetBytes(response.ToString(Formatting.None));
                FusionQaProtocol.ReadResponse(bytes);
                return bytes;
            }
            finally { gate.Release(); }
        }
        public void Dispose() { lobby = null; access = null; inner.Dispose(); }
    }
}
