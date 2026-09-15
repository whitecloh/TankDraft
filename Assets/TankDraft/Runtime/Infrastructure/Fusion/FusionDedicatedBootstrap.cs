using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using Photon.Realtime;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace TankDraft.Infrastructure.FusionTransport
{
    /// <summary>Dedicated connectivity, native projection and bounded client reconnect. No client-host fallback.</summary>
    public sealed class FusionDedicatedBootstrap : MonoBehaviour, INetworkRunnerCallbacks
    {
        [SerializeField] NetworkRunner runner;
        [SerializeField] FusionDiagnosticSceneManager sceneManager;
        [SerializeField] NetworkRunner runnerPrefab;
        [SerializeField] NetworkObject battleReplicaPrefab;
        [SerializeField, Range(.05f, .2f)] float snapshotPeriod = .05f;
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        readonly HashSet<PlayerRef> players = new HashSet<PlayerRef>();
        readonly HashSet<PlayerRef> busy = new HashSet<PlayerRef>();
        readonly Dictionary<PlayerRef, float> lastRequest = new Dictionary<PlayerRef, float>();
        readonly Dictionary<PlayerRef, FusionQaAuthorityPeer> qaPeers = new Dictionary<PlayerRef, FusionQaAuthorityPeer>();
        FusionRequestChannel qaChannel;
        public FusionSnapshotInbox SnapshotInbox { get; } = new FusionSnapshotInbox();
        sealed class NativeStream
        {
            public int Epoch;
            public int Generation;
            public bool Active, Pumping;
            public float Next;
            public float LastPublished;
            public long? Cursor;
            public FusionBattleReplica Replica;
        }
        readonly Dictionary<PlayerRef, NativeStream> streams = new Dictionary<PlayerRef, NativeStream>();
        public FusionQaClient QaClient { get; private set; }
        public string QaInstanceId => config != null && config.SessionName != null ? config.SessionName.Substring("td-qa-".Length) : null;
        public string QaPresentationDirectory => config?.PresentationDirectory;
        public string QaAccountId { get; private set; }
        RuntimeConfig config;
        HttpClient authority;
        bool server, ready, quitting;
        bool reconnecting;
        bool heartbeatRunning, authorityHealthy = true;
        int authorityFailures;
        float authorityFailedAt;
        int reconnectAttempts, connectionSerial;
        float nextHeartbeat, nextProbe, started, lastReply;
        int received, sent;
        string runtimeDirectory;

        async void Start()
        {
            try
            {
                if (runner == null || sceneManager == null) throw new InvalidOperationException();
                var path = Environment.GetEnvironmentVariable("TANKDRAFT_FUSION_RUNTIME_PATH");
                config = Read<RuntimeConfig>(path);
                runtimeDirectory = Path.GetDirectoryName(path);
                server = config.Role != "Client";
                if (config.Role != "Client" && config.Role != "Server") throw new InvalidDataException();
                if (config.SessionName == null || !config.SessionName.StartsWith("td-qa-", StringComparison.Ordinal) || config.SessionName.Length > 80 ||
                    config.LifetimeSeconds < 10 || config.LifetimeSeconds > 1800) throw new InvalidDataException();
                if (Path.GetFullPath(config.StatusPath) != Path.Combine(runtimeDirectory, "status.json")) throw new InvalidDataException();
                var auth = Read<AuthConfig>(config.AuthPath);
                if (string.IsNullOrEmpty(auth.UserId) || auth.UserId.Length > 64 || string.IsNullOrEmpty(auth.PhotonToken) || auth.PhotonToken.Length > 4096) throw new InvalidDataException();
                if (server)
                {
                    if (config.AllowlistedAccounts == null || config.AllowlistedAccounts.Length < 1 || config.AllowlistedAccounts.Length > 4) throw new InvalidDataException();
                    if (!Uri.TryCreate(config.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "http" ||
                        !IPAddress.TryParse(endpoint.Host, out var ip) || !IPAddress.IsLoopback(ip) || config.GatewayKey?.Length != 64) throw new InvalidDataException();
                    authority = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(3) };
                    authority.DefaultRequestHeaders.Add("X-TankDraft-Gateway", config.GatewayKey);
                    await CheckAuthority();
                }
                auth.PhotonToken = null;
                Application.runInBackground = true; Application.targetFrameRate = 60;
                NetworkRunner.CloudConnectionLostCurrentMode = NetworkRunner.CloudConnectionLostMode.QuickRejoin;
                NetworkRunner.CloudConnectionLost += CloudLost;
                var result = await StartRunner();
                if (!result.Ok) throw new InvalidOperationException();
                started = Time.realtimeSinceStartup; ready = true;
                BindClientChannel();
                Debug.Log((server ? "FUSION_DEDICATED_READY capability=" : "FUSION_CLIENT_CONNECTED capability=") + (config.AllowPlaintextQa ? "plaintext-qa-no-economy" : "readiness-only"));
            }
            catch { Debug.LogError("FUSION_START_FAILED: check private auth, provider configuration and connectivity."); await Quit(1); }
        }
        async Task<StartGameResult> StartRunner()
        {
            // Keep the SDK's authored configuration, including its runtime prefab
            // table. The temporary unencrypted build requires explicit private QA.
            ValidateEncryptionPolicy(config.AllowPlaintextQa, NetworkProjectConfig.Global.EncryptionConfig.EnableEncryption);
            runner.GetComponent<FusionRunnerContext>().Owner = this;
            runner.transform.SetParent(null); DontDestroyOnLoad(runner.gameObject);
            var auth = Read<AuthConfig>(config.AuthPath);
            if (QaAccountId != null && QaAccountId != auth.UserId) throw new InvalidDataException("Reconnect account changed.");
            var values = new AuthenticationValues { AuthType = CustomAuthenticationType.Custom };
            values.AddAuthParameter("username", auth.UserId); values.AddAuthParameter("token", auth.PhotonToken); auth.PhotonToken = null;
            runner.ProvideInput = false; runner.AddCallbacks(this);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                var result = await runner.StartGame(new StartGameArgs {
                    GameMode = server ? GameMode.Server : GameMode.Client, SessionName = config.SessionName,
                    PlayerCount = 2, AuthValues = values, SceneManager = sceneManager,
                    IsVisible = false, IsOpen = true, EnableClientSessionCreation = false,
                    DisableNATPunchthrough = true, StartGameCancellationToken = deadline.Token });
                if (result.Ok) QaAccountId = auth.UserId;
                return result;
            }
        }
        internal static void ValidateEncryptionPolicy(bool allowPlaintextQa, bool enableEncryption)
        {
            if (allowPlaintextQa == enableEncryption)
                throw new InvalidOperationException("Fusion encryption must match the explicitly selected private runtime mode.");
        }
        void BindClientChannel()
        {
            if (server || !config.AllowPlaintextQa) return;
            var source = runner;
            var mainThread = SynchronizationContext.Current ?? throw new InvalidOperationException();
            FusionRequestChannel channel = null;
            channel = new FusionRequestChannel((id, bytes) => mainThread.Post(_ => {
                if (!ready || quitting || source != runner || !source || !source.IsRunning) { channel.Dispose(); return; }
                if (channel.CanSend(id)) source.SendReliableDataToServer(ReliableKey.FromInts(2, id, 0, 0), bytes);
            }, null), () => false, TimeSpan.FromSeconds(10), allowPlaintextQa: true);
            qaChannel = channel;
            if (QaClient == null) QaClient = new FusionQaClient(channel, SnapshotInbox); else QaClient.ReplaceChannel(channel);
            connectionSerial++;
        }
        void CloudLost(NetworkRunner source, ShutdownReason reason, bool retrying)
        {
            if (source != runner || quitting) return;
            Debug.Log("FUSION_CLOUD_CONNECTION_LOST reason=" + reason + " retrying=" + retrying);
            // SDK owns quick rejoin. Full runner recreation begins only after shutdown/disconnect.
        }
        async Task ReconnectClient()
        {
            if (quitting || reconnecting || server || QaClient == null) return;
            reconnecting = true; ready = false; players.Clear(); QaClient.ReplaceChannel(null); SnapshotInbox.Disconnect();
            Debug.Log("FUSION_RECONNECT_BEGIN");
            try
            {
                for (int attempt = 0; attempt < 4 && !stop.IsCancellationRequested; attempt++)
                {
                    reconnectAttempts++;
                    var previous = runner;
                    if (previous) { previous.RemoveCallbacks(this); await previous.Shutdown(destroyGameObject: true); }
                    var delay = Math.Min(8, 1 << attempt);
                    var qaDelayPath = Path.Combine(runtimeDirectory, "reconnect-delay-seconds");
                    if (config.AllowPlaintextQa && File.Exists(qaDelayPath))
                    {
                        if (int.TryParse(File.ReadAllText(qaDelayPath), out var qaDelay)) delay = Math.Max(delay, Math.Min(90, Math.Max(0, qaDelay)));
                        File.Delete(qaDelayPath);
                    }
                    await Task.Delay(TimeSpan.FromSeconds(delay), stop.Token);
                    if (!runnerPrefab) throw new InvalidOperationException("Authored runner prefab missing.");
                    runner = Instantiate(runnerPrefab, transform); sceneManager = runner.GetComponent<FusionDiagnosticSceneManager>();
                    var result = await StartRunner();
                    if (result.Ok) { ready = true; BindClientChannel(); Debug.Log("FUSION_RECONNECT_READY serial=" + connectionSerial); return; }
                    Debug.LogWarning("FUSION_RECONNECT_ATTEMPT_FAILED reason=" + result.ShutdownReason);
                    if (result.ShutdownReason == ShutdownReason.InvalidAuthentication || result.ShutdownReason == ShutdownReason.CustomAuthenticationFailed ||
                        result.ShutdownReason == ShutdownReason.AuthenticationTicketExpired || result.ShutdownReason == ShutdownReason.IncompatibleConfiguration) break;
                }
                Debug.LogError("FUSION_RECONNECT_EXHAUSTED"); await Quit(1);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception error) { Debug.LogError("FUSION_RECONNECT_FAILED " + error.GetType().Name); await Quit(1); }
            finally { reconnecting = false; }
        }
        void Update()
        {
            if (config != null && !quitting && started > 0 && (Time.realtimeSinceStartup - started > config.LifetimeSeconds || File.Exists(Path.Combine(runtimeDirectory, "stop"))))
            { _ = Quit(0); return; }
            if (!server && ready && !quitting && config.AllowPlaintextQa && File.Exists(Path.Combine(runtimeDirectory, "disconnect")))
            { File.Delete(Path.Combine(runtimeDirectory, "disconnect")); _ = ReconnectClient(); return; }
            if (!ready || quitting) return;
            var now = Time.realtimeSinceStartup;
            if (now - started > config.LifetimeSeconds || File.Exists(Path.Combine(runtimeDirectory, "stop"))) { _ = Quit(0); return; }
            if (now >= nextHeartbeat && !heartbeatRunning) { nextHeartbeat = now + 1; _ = Heartbeat(); }
            if (server && config.AllowPlaintextQa)
                foreach (var entry in streams)
                    if (entry.Value.Replica != null && entry.Value.Replica.Faulted) { entry.Value.Active = false; runner.Disconnect(entry.Key); }
                    else if (entry.Value.Active && !entry.Value.Pumping && !busy.Contains(entry.Key) && now >= entry.Value.Next && entry.Value.Replica != null && !entry.Value.Replica.HasPending)
                    { entry.Value.Next = now + snapshotPeriod; entry.Value.Pumping = true; _ = PumpSnapshot(entry.Key, qaPeers[entry.Key], entry.Value); }
            if (!server && !config.AllowPlaintextQa && now >= nextProbe)
            {
                nextProbe = now + 1;
                runner.SendReliableDataToServer(ReliableKey.FromInts(1, ++sent, 0, 0), Encoding.UTF8.GetBytes("Ready"));
            }
        }
        async Task CheckAuthority()
        {
            using (var response = await authority.GetAsync("/readyz", HttpCompletionOption.ResponseHeadersRead, stop.Token))
                if (!response.IsSuccessStatusCode) throw new InvalidOperationException();
        }
        async Task Heartbeat()
        {
            heartbeatRunning = true;
            try
            {
                if (server)
                {
                    try { await CheckAuthority(); authorityHealthy = true; authorityFailures = 0; }
                    catch (Exception error)
                    {
                        if (quitting) return;
                        if (authorityFailures++ == 0) authorityFailedAt = Time.realtimeSinceStartup;
                        authorityHealthy = false;
                        Debug.LogWarning("FUSION_AUTHORITY_PROBE_FAILED type=" + error.GetType().Name + " consecutive=" + authorityFailures);
                        if (authorityFailures >= 3 && Time.realtimeSinceStartup - authorityFailedAt >= 10)
                        { Debug.LogError("FUSION_AUTHORITY_UNAVAILABLE sustained=true"); await Quit(1); return; }
                    }
                }
                var status = new GatewayStatus { Ready = ready && authorityHealthy, Players = players.Count, Replies = received, LastReplyAgeSeconds = received == 0 ? -1 : Time.realtimeSinceStartup - lastReply, Capability = config.AllowPlaintextQa ? "plaintext-qa-no-economy" : "readiness-only", ConnectionSerial = connectionSerial, ReconnectAttempts = reconnectAttempts };
                var temporary = config.StatusPath + ".tmp";
                File.WriteAllText(temporary, JsonUtility.ToJson(status));
                if (File.Exists(config.StatusPath)) File.Replace(temporary, config.StatusPath, null); else File.Move(temporary, config.StatusPath);
            }
            catch (IOException error) { if (!quitting) Debug.LogWarning("FUSION_STATUS_WRITE_RETRY type=" + error.GetType().Name); }
            catch (UnauthorizedAccessException error) { if (!quitting) Debug.LogWarning("FUSION_STATUS_WRITE_RETRY type=" + error.GetType().Name); }
            finally { heartbeatRunning = false; }
        }
        async Task Reply(NetworkRunner source, PlayerRef player, ReliableKey key)
        {
            try
            {
                await CheckAuthority();
                if (players.Contains(player) && source.IsRunning) source.SendReliableDataToPlayer(player, key, Encoding.UTF8.GetBytes("AuthorityReady"));
            }
            catch { if (source.IsRunning) source.Disconnect(player); }
            finally { busy.Remove(player); }
        }
        public void OnReliableDataReceived(NetworkRunner source, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data)
        {
            if (source != runner) return;
            key.GetInts(out var kind, out var id, out var reserved0, out var reserved1);
            if (kind == 2)
            {
                if (!ready || !config.AllowPlaintextQa || id <= 0 || reserved0 != 0 || reserved1 != 0 || data.Length > (server ? 8192 : FusionQaWireCodec.MaximumWireBytes))
                { if (server) source.Disconnect(player); else _ = Quit(1); return; }
                if (!server)
                {
                    try { if (qaChannel != null && qaChannel.Receive(id, FusionQaWireCodec.Decode(data.ToArray()))) { received++; lastReply = Time.realtimeSinceStartup; } }
                    catch { _ = Quit(1); }
                    return;
                }
                if (!qaPeers.TryGetValue(player, out var peer) || busy.Contains(player) ||
                    lastRequest.TryGetValue(player, out var previousQa) && Time.realtimeSinceStartup - previousQa < .02f)
                { source.Disconnect(player); return; }
                busy.Add(player); lastRequest[player] = Time.realtimeSinceStartup;
                _ = ReplyQa(source, player, key, peer, data.ToArray()); return;
            }
            if (!ready || data.Length > 64) { if (server) source.Disconnect(player); return; }
            var text = Encoding.UTF8.GetString(data.ToArray());
            if (!server)
            {
                if (text == "AuthorityReady") { received++; lastReply = Time.realtimeSinceStartup; }
                return;
            }
            if (!players.Contains(player) || text != "Ready" || busy.Contains(player) ||
                lastRequest.TryGetValue(player, out var previous) && Time.realtimeSinceStartup - previous < .2f)
            { source.Disconnect(player); return; }
            lastRequest[player] = Time.realtimeSinceStartup; busy.Add(player); _ = Reply(source, player, key);
        }
        public void OnPlayerJoined(NetworkRunner source, PlayerRef player)
        {
            if (server && !config.AllowlistedAccounts.Contains(source.GetPlayerUserId(player), StringComparer.Ordinal)) { source.Disconnect(player); return; }
            players.Add(player);
            if (server && config.AllowPlaintextQa)
            {
                if (qaPeers.TryGetValue(player, out var prior)) prior.Dispose();
                qaPeers[player] = new FusionQaAuthorityPeer(new Uri(config.Endpoint), config.GatewayKey, source.GetPlayerUserId(player));
                streams[player] = new NativeStream();
            }
        }
        async Task ReplyQa(NetworkRunner source, PlayerRef player, ReliableKey key, FusionQaAuthorityPeer peer, byte[] bytes)
        {
            try
            {
                var request = FusionQaProtocol.ReadRequest(bytes);
                var stream = streams[player];
                if (request.Value<string>("Operation") == "SocketOpen" || request.Value<string>("Operation") == "Leave")
                {
                    stream.Active = false; stream.Epoch++; stream.Cursor = null;
                    if (stream.Replica != null) { source.Despawn(stream.Replica.Object); stream.Replica = null; }
                }
                var response = await peer.ExecuteAsync(bytes, stop.Token);
                if (source.IsRunning && qaPeers.TryGetValue(player, out var current) && ReferenceEquals(current, peer))
                {
                    var envelope = FusionQaProtocol.ReadResponse(response); var body = (JObject)envelope["Body"];
                    if (body.Value<string>("Kind") == "Welcome") { body["NativeEpoch"] = stream.Epoch; stream.Generation = body.Value<int>("Generation"); }
                    if (body.Value<string>("Kind") == "ParallelRefreshEnabled")
                    {
                        if (battleReplicaPrefab == null) throw new InvalidOperationException("Native prefab missing.");
                        var obj = source.Spawn(battleReplicaPrefab, inputAuthority: player, onBeforeSpawned: (r, o) => o.GetComponent<FusionBattleReplica>().Configure(player, stream.Epoch));
                        stream.Replica = obj.GetComponent<FusionBattleReplica>(); stream.Active = true;
                    }
                    if (body.Value<string>("Kind") == "Reauthenticated" || body.Value<bool>("NativeAccessBound")) { stream.Generation = body.Value<int>("Generation"); stream.Active = true; }
                    response = Encoding.UTF8.GetBytes(envelope.ToString(Newtonsoft.Json.Formatting.None));
                    source.SendReliableDataToPlayer(player, key, FusionQaWireCodec.Encode(response));
                }
            }
            catch (Exception error) { Debug.LogError("FUSION_QA_REPLY_FAILED " + error.GetType().Name + "\n" + error.StackTrace); if (source.IsRunning && qaPeers.TryGetValue(player, out var current) && ReferenceEquals(current, peer)) source.Disconnect(player); }
            finally { if (qaPeers.TryGetValue(player, out var current) && ReferenceEquals(current, peer)) busy.Remove(player); }
        }
        async Task PumpSnapshot(PlayerRef player, FusionQaAuthorityPeer peer, NativeStream stream)
        {
            var pollStarted = Time.realtimeSinceStartup;
            var epoch = stream.Epoch;
            var generation = stream.Generation;
            try
            {
                var request = new JObject { ["Operation"] = "SocketExchange", ["Body"] = new JObject { ["Kind"] = "Poll", ["AfterEventSequence"] = stream.Cursor.HasValue ? new JValue(stream.Cursor.Value) : JValue.CreateNull() } };
                var response = await peer.ExecuteAsync(Encoding.UTF8.GetBytes(request.ToString(Newtonsoft.Json.Formatting.None)), stop.Token);
                if (!runner.IsRunning || !streams.TryGetValue(player, out var current) || !ReferenceEquals(current, stream) || stream.Epoch != epoch || stream.Replica == null) return;
                var envelope = FusionQaProtocol.ReadResponse(response);
                if (envelope.Value<int>("Status") != 200) throw new InvalidOperationException("Native snapshot rejected.");
                var body = (JObject)envelope["Body"];
                if (body.Value<string>("Kind") == "AccessRefreshRequired")
                {
                    if (generation < stream.Generation) return;
                    body["NativeAccessGeneration"] = generation; stream.Active = false;
                }
                else if (body["Snapshot"] is JObject snapshot) stream.Cursor = snapshot.Value<long>("EventSequence");
                else throw new InvalidOperationException("Unexpected native snapshot.");
                var published = Time.realtimeSinceStartup;
                stream.Replica.Publish(body, (published - pollStarted) * 1000, stream.LastPublished > 0 ? (published - stream.LastPublished) * 1000 : 0);
                stream.LastPublished = published;
            }
            catch (Exception error)
            {
                if (!quitting && streams.TryGetValue(player, out var current) && ReferenceEquals(current, stream) && epoch == stream.Epoch)
                { Debug.LogError("FUSION_NATIVE_PUMP_FAILED " + error.GetType().Name); runner.Disconnect(player); }
            }
            finally { stream.Pumping = false; }
        }
        public void OnPlayerLeft(NetworkRunner source, PlayerRef player)
        { players.Remove(player); busy.Remove(player); lastRequest.Remove(player); if (streams.TryGetValue(player, out var stream)) { stream.Active = false; stream.Epoch++; if (stream.Replica != null && source.IsRunning) source.Despawn(stream.Replica.Object); streams.Remove(player); } if (qaPeers.TryGetValue(player, out var peer)) { qaPeers.Remove(player); peer.Dispose(); } }
        async Task Quit(int code)
        {
            if (quitting) return; quitting = true; ready = false; stop.Cancel(); qaChannel?.Dispose(); SnapshotInbox.Close();
            foreach (var peer in qaPeers.Values) peer.Dispose(); qaPeers.Clear();
            try { if (runner != null && runner.IsRunning) await runner.Shutdown(); } catch { }
            if (!Application.isEditor) Application.Quit(code);
        }
        void OnDestroy() { NetworkRunner.CloudConnectionLost -= CloudLost; stop.Cancel(); qaChannel?.Dispose(); foreach(var peer in qaPeers.Values) peer.Dispose(); qaPeers.Clear(); authority?.Dispose(); stop.Dispose(); }
        static T Read<T>(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) throw new InvalidDataException();
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 16384 || (info.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
            return JsonUtility.FromJson<T>(File.ReadAllText(path));
        }
        public void OnConnectRequest(NetworkRunner r, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { if (server && ready && authorityHealthy) request.Accept(); else request.Refuse(); }
        public void OnShutdown(NetworkRunner r, ShutdownReason reason) { if (r != runner || quitting || reconnecting) return; if (!server && QaClient != null) _ = ReconnectClient(); else _ = Quit(1); }
        public void OnDisconnectedFromServer(NetworkRunner r, NetDisconnectReason reason) { if (r != runner || quitting || reconnecting) return; if (!server && QaClient != null) _ = ReconnectClient(); else _ = Quit(1); }
        public void OnConnectFailed(NetworkRunner r, NetAddress address, NetConnectFailedReason reason) { if (!reconnecting && QaClient != null) _ = ReconnectClient(); }
        public void OnHostMigration(NetworkRunner r, HostMigrationToken token) { _ = Quit(1); }
        public void OnObjectExitAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
        public void OnInput(NetworkRunner r, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner r, PlayerRef player, NetworkInput input) { }
        public void OnConnectedToServer(NetworkRunner r) { }
        public void OnUserSimulationMessage(NetworkRunner r, SimulationMessagePtr message) { }
        public void OnReliableDataProgress(NetworkRunner r, PlayerRef p, ReliableKey key, float progress) { }
        public void OnSessionListUpdated(NetworkRunner r, List<SessionInfo> sessions) { }
        public void OnCustomAuthenticationResponse(NetworkRunner r, Dictionary<string, object> data) { }
        public void OnSceneLoadDone(NetworkRunner r) { }
        public void OnSceneLoadStart(NetworkRunner r) { }
        [Serializable] sealed class AuthConfig { public string UserId; public string PhotonToken; }
        [Serializable] sealed class RuntimeConfig { public string Role; public string Endpoint; public string GatewayKey; public string AuthPath; public string StatusPath; public string SessionName; public string[] AllowlistedAccounts; public int LifetimeSeconds; public bool AllowPlaintextQa; public string PresentationDirectory; }
        [Serializable] sealed class GatewayStatus { public bool Ready; public int Players; public int Replies; public float LastReplyAgeSeconds; public string Capability; public int ConnectionSerial; public int ReconnectAttempts; }
    }
}
