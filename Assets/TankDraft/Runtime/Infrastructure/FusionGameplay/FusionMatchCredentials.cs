#nullable disable
using System;
using System.IO;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TankDraft.Infrastructure.FusionTransport;
using TankDraft.Match.ServerClient;

namespace TankDraft.Infrastructure.FusionGameplay
{
    public sealed class FusionQaRequests
    {
        readonly FusionQaClient client;
        public FusionSnapshotInbox Snapshots => client.Snapshots;
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        public FusionQaRequests(FusionQaClient client) { this.client = client ?? throw new ArgumentNullException(nameof(client)); }
        public async Task<JObject> Send(string operation, JObject body, CancellationToken token)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                await gate.WaitAsync(deadline.Token).ConfigureAwait(false);
                try { return await client.ExchangeAsync(operation, body, deadline.Token).ConfigureAwait(false); }
                finally { gate.Release(); }
            }
        }
    }
    public sealed class FusionMatchCredentials : IMatchCredentials, IMatchSocketFactory, IMatchCredentialsFactory, IMatchNativePresentation, IMatchIntentLocation
    {
        readonly FusionQaRequests requests;
        readonly string match, version;
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        string operation;
        public Uri Endpoint { get; } = new Uri("fusion://dedicated/v1/socket");
        public int Side { get; }
        public string RunDirectory { get; }
        public string JournalFileName { get; }
        public string IntentJournalPath { get; }
        public FusionMatchCredentials(FusionQaRequests requests, string matchId, int side, string contentVersion, string directory, string intentJournalPath = null)
        {
            if (requests == null || string.IsNullOrEmpty(matchId) || matchId.Length > 128 || side < 0 || side > 1 || contentVersion?.Length != 64 || !Path.IsPathRooted(directory)) throw new ArgumentException();
            this.requests = requests; match = matchId; version = contentVersion; Side = side; RunDirectory = directory;
            Directory.CreateDirectory(directory);
            using (var hash = SHA256.Create()) JournalFileName = "fusion-intent-" + side + "-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(match))).Replace("-", "").ToLowerInvariant() + ".json";
            IntentJournalPath = intentJournalPath ?? Path.Combine(directory, JournalFileName);
            if (!Path.IsPathRooted(IntentJournalPath)) throw new ArgumentException("Intent path must be absolute.");
        }
        public IMatchCredentials Create() => this;
        public bool NativePresentation => requests.Snapshots != null;
        public long NativeSamples => requests.Snapshots?.BattleSamples ?? 0;
        public double NativeMeanGapMs => requests.Snapshots == null ? 0 : requests.Snapshots.TotalBattleGapMs / Math.Max(1, requests.Snapshots.BattleIntervals);
        public double NativeMaxGapMs => requests.Snapshots?.MaxBattleGapMs ?? 0;
        public double NativeSourcePollMaxMs => requests.Snapshots?.MaxSourcePollMs ?? 0;
        public double NativeSourceGapMaxMs => requests.Snapshots?.MaxSourceGapMs ?? 0;
        public double NativeDecodeMaxMs => requests.Snapshots?.MaxDecodeMs ?? 0;
        public int NativeStaleStates => requests.Snapshots?.StaleNativeStates ?? 0;
        public bool TryGetPose(string matchId, int round, int id, out TankDraft.Contracts.Battle.BattleVec position, out TankDraft.Contracts.Battle.BattleVec facing, out bool interpolating)
        {
            var inbox = requests.Snapshots; position = default; facing = default; interpolating = false;
            if (inbox == null || inbox.RenderMatch != matchId || inbox.RenderRound != round || !inbox.Poses.TryGetValue(id, out var pose)) return false;
            position = new TankDraft.Contracts.Battle.BattleVec(pose.X, pose.Y); facing = new TankDraft.Contracts.Battle.BattleVec(pose.FacingX, pose.FacingY); interpolating = inbox.Interpolating; return true;
        }
        public bool ValidateServerCertificate(X509Certificate certificate, X509Chain chain, SslPolicyErrors errors) => false;
        public async Task<MatchAccess> AcquireAsync(CancellationToken token)
        {
            using (var stop = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                await gate.WaitAsync(stop.Token).ConfigureAwait(false);
                try
                {
                    operation = operation ?? Guid.NewGuid().ToString("N");
                    var value = await requests.Send("Session", new JObject { ["OperationId"] = operation, ["ContentVersion"] = version }, stop.Token).ConfigureAwait(false);
                    if (value.Value<string>("MatchId") != match || value.Value<int>("Side") != Side) throw new MatchAuthenticationException("Fusion assignment changed.");
                    // Compatibility marker only, not a credential; FusionMatchSocket never transmits it.
                    var access = new MatchAccess("gateway-owned", value.Value<string>("SessionId"), value.Value<string>("StreamId"), match, value.Value<int>("Generation"), value.Value<int>("RefreshAfterSeconds"));
                    operation = null; return access;
                }
                finally { gate.Release(); }
            }
        }
        public async Task<WebSocket> ConnectAsync(MatchAccess access, CancellationToken token)
        {
            if (access.MatchId != match) throw new InvalidDataException();
            await requests.Send("SocketOpen", new JObject(), token).ConfigureAwait(false);
            return new FusionMatchSocket(requests, RunDirectory);
        }
        public void Dispose() { lifetime.Cancel(); }
    }
}
