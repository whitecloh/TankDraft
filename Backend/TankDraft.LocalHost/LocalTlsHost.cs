using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using TankDraft.Server.Security;

namespace TankDraft.LocalHost;

internal static partial class LocalTlsHost
{
    public const string Protocol = "tankdraft-server-v2";
    private const string SocketPath = "/v1/socket";

    public static async Task ServeUnityAsync(string unityExecutable, bool automated, bool networkFaults = false)
    {
        var root = FindRepositoryRoot(); var exe = Path.GetFullPath(unityExecutable);
        var allowed = Path.GetFullPath(Path.Combine(root, "Logs", "BackendClient")) + Path.DirectorySeparatorChar;
        if (!Path.IsPathFullyQualified(unityExecutable) || !exe.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !File.Exists(exe)) throw new InvalidOperationException("Unity executable must be an existing absolute path under Logs/BackendClient.");
        var settings = LocalTlsSettings.Load(); var (content, version) = AuthoredContent.Load();
        var families = new LocalAccessFamilies(2); var a = families.Create("qa-player-0", LocalMatchEndpoint.MatchId, "0", TimeSpan.FromSeconds(settings.GrantTtlSeconds)); var b = families.Create("qa-player-1", LocalMatchEndpoint.MatchId, "1", TimeSpan.FromSeconds(settings.GrantTtlSeconds));
        using var endpoint = new LocalMatchEndpoint(content, version, HostSettings.Load(), TimeProvider.System);
        using var cert = CreateCertificate(out var pin); await using var app = await StartAsync(settings, endpoint, families, version, cert, CancellationToken.None, networkFaults);
        var run = Path.Combine(root, "Logs", "BackendClient", (networkFaults ? "tls-fault-" : "tls-") + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run); Console.WriteLine("Local TLS Unity run: " + run);
        await using var proxy = networkFaults ? new LocalFaultProxy(LocalFaultSettings.Load(), run) : null;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(850)); Console.CancelKeyPress += Cancel;
        var processes = new List<Process>();
        var completed = false;
        try
        {
            processes.Add(StartUnity(exe, run, 0, a.Value, pin, automated)); processes.Add(StartUnity(exe, run, 1, b.Value, pin, automated));
            var restarted = false;
            while (!stop.IsCancellationRequested)
            {
                if (automated && !restarted && File.Exists(Path.Combine(run, "restart-request-0")))
                {
                    if (!processes[0].HasExited) processes[0].Kill(true); processes[0].Dispose();
                    // The grant preserves StreamId; next client session is a new physical generation.
                    _ = families.InvalidateAccess(a.Value);
                    processes[0] = StartUnity(exe, run, 0, a.Value, pin, automated, true); restarted = true;
                }
                if (processes.All(p => p.HasExited))
                {
                    var exits = processes.Select(p => p.ExitCode).ToArray();
                    File.WriteAllText(Path.Combine(run, "player-exits.json"), JsonSerializer.Serialize(exits));
                    if (automated && exits.Any(code => code != 0)) throw new InvalidOperationException("Automated Unity client failed; see failure files in the run directory.");
                    completed = true;
                    break;
                }
                await Task.Delay(250, stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally { Console.CancelKeyPress -= Cancel; foreach (var p in processes) { try { if (!p.HasExited) p.Kill(true); } catch (InvalidOperationException) { } p.Dispose(); } endpoint.Flush(); families.Revoke(a); families.Revoke(b); await app.StopAsync(); }
        if (automated && !completed) throw new TimeoutException("Automated Unity match did not complete before cancellation or local run limit.");
        void Cancel(object? _, ConsoleCancelEventArgs e) { e.Cancel = true; stop.Cancel(); }
    }

    public static async Task VerifyAsync()
    {
        var settings = LocalTlsSettings.Load(); var (content, version) = AuthoredContent.Load(); var clock = new LocalVerificationClock();
        var families = new LocalAccessFamilies(2, clock); var grant = families.Create("qa-player-0", LocalMatchEndpoint.MatchId, "0", TimeSpan.FromSeconds(settings.GrantTtlSeconds));
        using var endpoint = new LocalMatchEndpoint(content, version, HostSettings.Load(), clock); using var cert = CreateCertificate(out var pin);
        await using var app = await StartAsync(settings, endpoint, families, version, cert, CancellationToken.None);
        try
        {
            var issue = families.Issue(grant.Value, TimeSpan.FromSeconds(settings.AccessTtlSeconds), TimeSpan.FromSeconds(settings.RefreshMarginSeconds))!;
            using var client = PinnedClient(pin); var response = await GrantPost(client, grant.Value);
            Ensure(response.IsSuccessStatusCode, "valid pinned session issue"); AssertIssueResponse(await response.Content.ReadAsStringAsync(), issue, "session issue response"); var repeated = await GrantPost(client, grant.Value); Ensure(repeated.IsSuccessStatusCode, "lost response retry"); AssertIssueResponse(await repeated.Content.ReadAsStringAsync(), issue, "repeat session issue response");
            using var wrong = PinnedClient("00"); try { await wrong.GetAsync("https://127.0.0.1:18783/v1/local-session"); throw new InvalidOperationException("wrong pin accepted"); } catch (HttpRequestException) { }
            using var defaultClient = new HttpClient(new HttpClientHandler { UseProxy = false }); try { await defaultClient.GetAsync("https://127.0.0.1:18783/v1/local-session"); throw new InvalidOperationException("untrusted cert accepted"); } catch (HttpRequestException) { }
            using var ws = await ConnectAsync(issue.AccessToken, pin); await Send(ws, new { Kind = "Hello", Protocol, ContentVersion = version }); var welcome = await Receive(ws, settings); Ensure(welcome.Contains("\"StreamId\"", StringComparison.Ordinal) && welcome.Contains("\"NextSequence\":1", StringComparison.Ordinal), "welcome stream and sequence");
            clock.Advance(TimeSpan.FromSeconds(settings.AccessTtlSeconds + 1)); await Send(ws, new { Kind = "Poll", AfterEventSequence = (long?)null }); await ExpectClosed(ws, settings, "expired socket");
            var refreshed = families.Issue(grant.Value, TimeSpan.FromSeconds(settings.AccessTtlSeconds), TimeSpan.FromSeconds(settings.RefreshMarginSeconds)); Ensure(refreshed is not null && refreshed.Generation == 2 && refreshed.StreamId == issue.StreamId, "refresh keeps stream");
            Console.WriteLine("PASS local TLS: pin, default trust refusal, grant issue retry, expiry and stable command stream");
        }
        finally { endpoint.Flush(); families.Revoke(grant); await app.StopAsync(); }
        await VerifyExtendedAsync(settings, content, version);
    }

    private static async Task<WebApplication> StartAsync(LocalTlsSettings settings, ILocalMatchRouter endpoint, LocalAccessFamilies families, string contentVersion, X509Certificate2 certificate, CancellationToken cancel, bool behindFaultProxy = false, Action<WebApplication>? addRoutes = null, int requestLimit = 120)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] }); builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        builder.Services.AddHostedService(_ => new MatchPumpService(endpoint, TimeSpan.FromMilliseconds(20)));
        if (requestLimit is < 1 or > 600) throw new ArgumentOutOfRangeException(nameof(requestLimit));
        builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetFixedWindowLimiter("tls", _ => new FixedWindowRateLimiterOptions { PermitLimit = requestLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true })); });
        builder.WebHost.ConfigureKestrel(o => { o.AddServerHeader = false; o.Limits.MaxConcurrentConnections = settings.MaxConnections; o.Limits.MaxConcurrentUpgradedConnections = settings.MaxConnections; o.Limits.MaxRequestBodySize = settings.MaxRequestBytes; o.Limits.MaxRequestHeadersTotalSize = 8192; o.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds); o.Listen(IPAddress.Loopback, behindFaultProxy ? 18784 : 18783, l => { l.Protocols = HttpProtocols.Http1; l.UseHttps(certificate); }); });
        var app = builder.Build();
        app.Use(async (context, next) => { context.Response.Headers.CacheControl = "no-store"; await next(context); });
        app.UseRateLimiter(); app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
        app.MapPost("/v1/local-session", async context =>
        {
            if (context.Request.Method != "POST" || context.Request.Query.Count != 0 || context.Request.Headers.ContainsKey("Origin") || context.Request.Headers.ContainsKey("Transfer-Encoding") || context.Request.ContentLength is > 0) { context.Response.StatusCode = 400; return; }
            var auth = context.Request.Headers.Authorization.ToString(); if (!auth.StartsWith("Bearer ", StringComparison.Ordinal) || auth.Length > 128) { context.Response.StatusCode = 401; return; }
            var issue = families.Issue(auth[7..], TimeSpan.FromSeconds(settings.AccessTtlSeconds), TimeSpan.FromSeconds(settings.RefreshMarginSeconds));
            if (issue is null) { context.Response.StatusCode = 401; return; }
            context.Response.Headers.CacheControl = "no-store"; await context.Response.WriteAsJsonAsync(new { issue.AccessToken, issue.SessionId, issue.StreamId, issue.Generation, issue.ExpiresInSeconds, issue.RefreshAfterSeconds, issue.Side, issue.MatchId }, AuthoredContent.Json);
        });
        var active = new ConcurrentDictionary<string, ActiveSocket>(StringComparer.Ordinal); var slots = new SemaphoreSlim(settings.MaxConnections, settings.MaxConnections);
        app.Map(SocketPath, context => HandleSocket(context, settings, endpoint, families, contentVersion, active, slots)); addRoutes?.Invoke(app); await app.StartAsync(cancel); return app;
    }

    private static async Task HandleSocket(HttpContext context, LocalTlsSettings settings, ILocalMatchRouter endpoint, LocalAccessFamilies families, string version, ConcurrentDictionary<string, ActiveSocket> active, SemaphoreSlim slots)
    {
        if (context.Request.Method != "GET" || !context.WebSockets.IsWebSocketRequest || context.Request.Query.Count != 0 || context.Request.Headers.ContainsKey("Origin")) { context.Response.StatusCode = 403; return; }
        var raw = context.Request.Headers.Authorization.ToString(); var bearer = raw.StartsWith("Bearer ", StringComparison.Ordinal) && raw.Length <= 128 ? raw[7..] : null;
        LocalAccessIssue? meta = null; string? accountId = null; try { families.UseAccess(bearer, x => { meta = new LocalAccessIssue("", x.SessionId, x.StreamId, x.Generation, 0, 0, x.Side, x.MatchId); accountId = x.Caller.AccountId; return 0; }); } catch (UnauthorizedAccessException) { context.Response.StatusCode = 401; return; }
        if (!await slots.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 503; return; }
        var mine = new ActiveSocket(meta!.Generation, accountId!, meta.Side, meta.MatchId, meta.StreamId, bearer!);
        lock (active)
        {
            if (active.TryGetValue(meta.StreamId, out var old))
            {
                if (old.Generation >= meta.Generation) { context.Response.StatusCode = 409; slots.Release(); return; }
                old.Socket?.Abort();
            }
            active[meta.StreamId] = mine;
        }
        WebSocket? socket = null;
        try
        {
            socket = await context.WebSockets.AcceptWebSocketAsync();
            lock (active)
            {
                if (!active.TryGetValue(meta.StreamId, out var owner) || !ReferenceEquals(owner, mine)) { socket.Abort(); return; }
                mine.Socket = socket;
            }
            var hello = await Receive(socket, settings); if (!TryRead(hello, ["Kind", "Protocol", "ContentVersion"], out var h) || ReadString(h, "Kind") != "Hello" || ReadString(h, "Protocol") != Protocol || ReadString(h, "ContentVersion") != version) { await Close(socket, WebSocketCloseStatus.PolicyViolation); return; }
            await Use(bearer, families, x => Send(socket, new { Kind = "Welcome", Protocol, ContentVersion = version, MatchId = x.MatchId, Side = int.Parse(x.Side), SessionId = x.SessionId, StreamId = x.StreamId, Generation = x.Generation, NextSequence = endpoint.NextCommandSequence(x.Caller) }), socket, settings);
            var timestamps = new Queue<long>();
            while (socket.State == WebSocketState.Open)
            {
                var text = await Receive(socket, settings);
                var now = Stopwatch.GetTimestamp(); timestamps.Enqueue(now);
                while (timestamps.Count > 0 && Stopwatch.GetElapsedTime(timestamps.Peek(), now) > TimeSpan.FromSeconds(1)) timestamps.Dequeue();
                if (timestamps.Count > 30) { await Close(socket, (WebSocketCloseStatus)4400); return; }
                if (!TryRead(text, ["Kind", "AfterEventSequence", "Command", "AccessToken"], out var root)) { await Close(socket, WebSocketCloseStatus.InvalidPayloadData); return; }
                if (ReadString(root, "Kind") == "Reauthenticate")
                {
                    if (!HasOnly(root, "Kind", "AccessToken") || !root.TryGetProperty("AccessToken", out var renewed) || renewed.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(renewed.GetString()) || renewed.GetString()!.Length > 128) { await Close(socket, (WebSocketCloseStatus)4401); return; }
                    var nextBearer = renewed.GetString()!;
                    await families.UseAccess(nextBearer, async x =>
                    {
                        lock (active)
                        {
                            if (!active.TryGetValue(mine.StreamId, out var owner) || !ReferenceEquals(owner, mine) || x.Caller.AccountId != mine.AccountId || x.MatchId != mine.MatchId || x.Side != mine.Side || x.StreamId != mine.StreamId || x.Generation < mine.Generation)
                                throw new UnauthorizedAccessException();
                            mine.Generation = x.Generation;
                            mine.Bearer = nextBearer;
                        }
                        await Send(socket, new { Kind = "Reauthenticated", SessionId = x.SessionId, StreamId = x.StreamId, Generation = x.Generation, MatchId = x.MatchId, Side = int.Parse(x.Side) });
                    });
                    bearer = nextBearer;
                }
                else if (ReadString(root, "Kind") == "Poll" && HasOnly(root, "Kind", "AfterEventSequence") && TryCursor(root, out var cursor)) await Use(bearer, families, x => Send(socket, new { Kind = "Snapshot", Snapshot = endpoint.Capture(x.Caller, cursor) }), socket, settings);
                else if (ReadString(root, "Kind") == "Command" && HasOnly(root, "Kind", "Command") && root.GetProperty("Command").ValueKind == JsonValueKind.Object)
                {
                    var value = root.GetProperty("Command");
                    string[] fields = ["MatchId", "RoundId", "ContentVersion", "OperationId", "Sequence", "CommandKind", "Payload"];
                    if (!TryRead(value.GetRawText(), fields, out var commandRoot) || !HasOnly(commandRoot, fields)) { await Close(socket, WebSocketCloseStatus.InvalidPayloadData); return; }
                    var cmd = value.Deserialize<CommandEnvelope>(AuthoredContent.Json); if (cmd is null) { await Close(socket, WebSocketCloseStatus.InvalidPayloadData); return; }
                    await Use(bearer, families, x => Send(socket, new { Kind = "Ack", OperationId = cmd.OperationId, Reply = endpoint.Execute(x.Caller, cmd) }), socket, settings);
                } else { await Close(socket, WebSocketCloseStatus.InvalidPayloadData); return; }
            }
        }
        catch (UnauthorizedAccessException) { if (socket is not null) await Close(socket, (WebSocketCloseStatus)4401); }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (JsonException) { if (socket is not null) await Close(socket, WebSocketCloseStatus.InvalidPayloadData); }
        catch (DecoderFallbackException) { if (socket is not null) await Close(socket, WebSocketCloseStatus.InvalidPayloadData); }
        catch (IOException) { }
        finally { lock (active) active.TryRemove(KeyValuePair.Create(meta.StreamId, mine)); socket?.Dispose(); slots.Release(); }
    }

    private static async Task Use(string? token, LocalAccessFamilies families, Func<LocalAccessContext, Task> action, WebSocket socket, LocalTlsSettings settings) => await families.UseAccess(token, action);
    private static async Task<string> Receive(WebSocket socket, LocalTlsSettings s) { var b = ArrayPool<byte>.Shared.Rent(4096); var bytes = new ArrayBufferWriter<byte>(); try { using var c = new CancellationTokenSource(TimeSpan.FromSeconds(s.TimeoutSeconds)); while (true) { var r = await socket.ReceiveAsync(b.AsMemory(), c.Token); if (r.MessageType == WebSocketMessageType.Close) throw new OperationCanceledException(); if (r.MessageType != WebSocketMessageType.Text || bytes.WrittenCount + r.Count > s.MaxRequestBytes) throw new JsonException(); bytes.Write(b.AsSpan(0, r.Count)); if (r.EndOfMessage) break; } return new UTF8Encoding(false, true).GetString(bytes.WrittenSpan); } finally { ArrayPool<byte>.Shared.Return(b); } }
    private static async Task Send(WebSocket s, object v) { var data = JsonSerializer.SerializeToUtf8Bytes(v, AuthoredContent.Json); if (data.Length > 1048576) throw new JsonException(); using var c = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await s.SendAsync(data, WebSocketMessageType.Text, true, c.Token); }
    private static async Task Close(WebSocket s, WebSocketCloseStatus status)
    {
        if (s.State != WebSocketState.Open) return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await s.CloseOutputAsync(status, "protocol", timeout.Token); }
        catch (WebSocketException) { s.Abort(); }
        catch (OperationCanceledException) { s.Abort(); }
    }
    private static bool TryRead(string t, string[] allowed, out JsonElement root) { root = default; using var d = JsonDocument.Parse(t, new JsonDocumentOptions { MaxDepth = 16 }); if (d.RootElement.ValueKind != JsonValueKind.Object) return false; var found = new HashSet<string>(); foreach (var p in d.RootElement.EnumerateObject()) if (!allowed.Contains(p.Name) || !found.Add(p.Name)) return false; root = d.RootElement.Clone(); return true; }
    private static bool HasOnly(JsonElement root, params string[] n) => root.EnumerateObject().Select(x => x.Name).OrderBy(x => x).SequenceEqual(n.OrderBy(x => x));
    private static string? ReadString(JsonElement r, string n) => r.TryGetProperty(n, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;
    private static bool TryCursor(JsonElement r, out long? c) { c = null; var x = r.GetProperty("AfterEventSequence"); if (x.ValueKind == JsonValueKind.Null) return true; if (x.ValueKind != JsonValueKind.Number || !x.TryGetInt64(out var v) || v < 0) return false; c = v; return true; }
    private static X509Certificate2 CreateCertificate(out string pin) { using var rsa = RSA.Create(2048); var req = new CertificateRequest("CN=TankDraft Local TLS", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); var san = new SubjectAlternativeNameBuilder(); san.AddIpAddress(IPAddress.Loopback); req.CertificateExtensions.Add(san.Build()); req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true)); req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true)); req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true)); using var issued = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(2)); pin = Convert.ToHexString(SHA256.HashData(issued.Export(X509ContentType.Cert))); return X509CertificateLoader.LoadPkcs12(issued.Export(X509ContentType.Pkcs12), password: null, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable); }
    private static HttpClient PinnedClient(string pin) { var h = new HttpClientHandler { UseProxy = false, ServerCertificateCustomValidationCallback = (m, c, ch, e) => c is not null && string.Equals(Convert.ToHexString(SHA256.HashData(c.Export(X509ContentType.Cert))), pin, StringComparison.Ordinal) && (e & ~(SslPolicyErrors.RemoteCertificateChainErrors)) == SslPolicyErrors.None }; return new HttpClient(h) { Timeout = TimeSpan.FromSeconds(5) }; }
    private static Task<HttpResponseMessage> GrantPost(HttpClient client, string grant) { var request = new HttpRequestMessage(HttpMethod.Post, "https://127.0.0.1:18783/v1/local-session") { Content = new ByteArrayContent([]) }; request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", grant); return client.SendAsync(request); }
    private static async Task<ClientWebSocket> ConnectAsync(string token, string pin) { var ws = new ClientWebSocket(); ws.Options.SetRequestHeader("Authorization", "Bearer " + token); ws.Options.RemoteCertificateValidationCallback = (_, c, _, e) => c is not null && e == SslPolicyErrors.RemoteCertificateChainErrors && Convert.ToHexString(SHA256.HashData(c.Export(X509ContentType.Cert))) == pin; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await ws.ConnectAsync(new Uri("wss://127.0.0.1:18783" + SocketPath), timeout.Token); return ws; }
    private static Task Send(ClientWebSocket s, object v) => s.SendAsync(JsonSerializer.SerializeToUtf8Bytes(v, AuthoredContent.Json), WebSocketMessageType.Text, true, CancellationToken.None);
    private static Task<string> Receive(ClientWebSocket s, LocalTlsSettings set) => Receive((WebSocket)s, set);
    private static async Task ExpectClosed(ClientWebSocket s, LocalTlsSettings set, string name) { try { _ = await Receive(s, set); } catch (OperationCanceledException) { return; } catch (WebSocketException) { return; } throw new InvalidOperationException(name); }
    private static void Ensure(bool v, string n) { if (!v) throw new InvalidOperationException("TLS QA failed: " + n); }
    private static void AssertIssueResponse(string json, LocalAccessIssue expected, string name)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var fields = root.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Console.WriteLine("TLS issue fields: " + string.Join(",", fields));
        Ensure(root.ValueKind == JsonValueKind.Object && fields.SequenceEqual(["AccessToken", "ExpiresInSeconds", "Generation", "MatchId", "RefreshAfterSeconds", "SessionId", "Side", "StreamId"]), name + " field names");
        Ensure(root.GetProperty("AccessToken").ValueKind == JsonValueKind.String && root.GetProperty("AccessToken").GetString()!.Length > 0, name + " access token");
        Ensure(root.GetProperty("SessionId").ValueKind == JsonValueKind.String && root.GetProperty("SessionId").GetString()!.Length > 0, name + " session id");
        Ensure(root.GetProperty("StreamId").ValueKind == JsonValueKind.String && root.GetProperty("StreamId").GetString()!.Length > 0, name + " stream id");
        Ensure(root.GetProperty("Generation").GetInt32() > 0 && root.GetProperty("ExpiresInSeconds").GetInt32() > 0 && root.GetProperty("RefreshAfterSeconds").GetInt32() > 0, name + " numeric values");
        Ensure(root.GetProperty("MatchId").GetString() == expected.MatchId && root.GetProperty("Side").GetString() == expected.Side, name + " identity values");
    }
    private static Process StartUnity(string exe, string run, int side, string grant, string pin, bool auto, bool restarted = false) { var i = new ProcessStartInfo(exe, $"-screen-width 450 -screen-height 800 -logFile \"{Path.Combine(run, $"unity-{side}.log")}\"") { UseShellExecute = false }; i.Environment["TD_LOCAL_GRANT"] = grant; i.Environment["TD_LOCAL_ENDPOINT"] = "wss://127.0.0.1:18783/v1/socket"; i.Environment["TD_LOCAL_TLS_PIN"] = pin; i.Environment["TD_LOCAL_SIDE"] = side.ToString(); i.Environment["TD_LOCAL_RUN"] = run; if (auto) i.Environment["TD_LOCAL_AUTO"] = "1"; if (restarted) i.Environment["TD_LOCAL_RESTARTED"] = "1"; return Process.Start(i)!; }
    private static string FindRepositoryRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "Backend", "global.json"))) d = d.Parent; return d?.FullName ?? throw new InvalidOperationException("Repository root missing."); }
    private sealed class ActiveSocket(int generation, string accountId, string side, string matchId, string streamId, string bearer)
    {
        public int Generation { get; set; } = generation;
        public string AccountId { get; } = accountId;
        public string Side { get; } = side;
        public string MatchId { get; } = matchId;
        public string StreamId { get; } = streamId;
        public string Bearer { get; set; } = bearer;
        public WebSocket? Socket { get; set; }
    }
}
