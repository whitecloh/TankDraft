using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.RateLimiting;
using TankDraft.Server.Match;
using TankDraft.Server.Security;

namespace TankDraft.LocalHost;

// Local loopback WebSocket adapter. It deliberately exposes the durable endpoint only; no Unity state is authored here.
internal static class LocalSocketHost
{
    public const string Protocol = "tankdraft-server-v1";
    private const string SocketPath = "/v1/socket";

    public static async Task ServeUnityAsync(string unityExecutable, bool automated)
    {
        var root = FindRepositoryRoot();
        var fullExe = Path.GetFullPath(unityExecutable);
        var allowed = Path.GetFullPath(Path.Combine(root, "Logs", "BackendClient")) + Path.DirectorySeparatorChar;
        if (!Path.IsPathFullyQualified(unityExecutable) || !fullExe.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(fullExe))
            throw new InvalidOperationException("Unity executable must be an existing absolute path under Logs/BackendClient.");

        var settings = LocalSocketSettings.Load();
        var (content, version) = AuthoredContent.Load();
        var sessions = new LocalSessionRegistry(2, TimeProvider.System);
        var ttl = TimeSpan.FromSeconds(900); // Fixed S4 local-launch TTL; config validation requires 900 seconds.
        var side0 = sessions.Create("qa-player-0", LocalMatchEndpoint.MatchId, "0", ttl);
        var side1 = sessions.Create("qa-player-1", LocalMatchEndpoint.MatchId, "1", ttl);
        using var endpoint = new LocalMatchEndpoint(content, version, HostSettings.Load(), TimeProvider.System);
        await using var host = await StartAsync(settings, endpoint, sessions, version, CancellationToken.None);
        var run = Path.Combine(root, "Logs", "BackendClient", "socket-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(850));
        Console.CancelKeyPress += Cancel;
        var children = new List<Process>();
        try
        {
            children.Add(StartUnity(fullExe, run, 0, side0.Token, automated));
            children.Add(StartUnity(fullExe, run, 1, side1.Token, automated));
            Console.WriteLine("Local socket Unity run: " + run);
            var restarted = false;
            while (!shutdown.IsCancellationRequested)
            {
                if (automated && !restarted && File.Exists(Path.Combine(run, "restart-request-0")))
                {
                    var original = children[0];
                    try { if (!original.HasExited) original.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                    original.Dispose();
                    children[0] = StartUnity(fullExe, run, 0, side0.Token, automated, restarted: true);
                    restarted = true;
                }
                if (children.All(child => child.HasExited)) break;
                await Task.Delay(250, shutdown.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Console.CancelKeyPress -= Cancel;
            foreach (var child in children)
            {
                try { if (!child.HasExited) child.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                child.Dispose();
            }
            endpoint.Flush();
            sessions.Revoke(side0.SessionId);
            sessions.Revoke(side1.SessionId);
            await host.StopAsync();
        }
        void Cancel(object? _, ConsoleCancelEventArgs e) { e.Cancel = true; shutdown.Cancel(); }
    }

    public static async Task VerifyAsync()
    {
        var settings = LocalSocketSettings.Load();
        var (content, version) = AuthoredContent.Load();
        var clock = new LocalVerificationClock();
        var sessions = new LocalSessionRegistry(4, clock);
        var a = sessions.Create("qa-player-0", LocalMatchEndpoint.MatchId, "0", TimeSpan.FromSeconds(900));
        var b = sessions.Create("qa-player-1", LocalMatchEndpoint.MatchId, "1", TimeSpan.FromSeconds(900));
        using var endpoint = new LocalMatchEndpoint(content, version, HostSettings.Load(), clock);
        await using var host = await StartAsync(settings, endpoint, sessions, version, CancellationToken.None);
        try
        {
            var initialRevision = endpoint.Capture(sessions.Authenticate(a.Token)!, null).Revision;
            await ExpectConnectRejectedAsync(null, null, "missing bearer authentication");
            await ExpectConnectRejectedAsync(a.Token, "http://localhost", "origin header rejected");
            await ExpectClosedAfterHelloAsync(a.Token, $"{{\"Kind\":\"Hello\",\"Protocol\":\"{Protocol}\",\"ContentVersion\":\"wrong\"}}", "content mismatch");
            await ExpectClosedAfterHelloAsync(a.Token, "{\"Kind\":\"Hello\",\"Kind\":\"Hello\",\"Protocol\":\"tankdraft-server-v1\",\"ContentVersion\":\"x\"}", "duplicate hello field");
            Assert(endpoint.Capture(sessions.Authenticate(a.Token)!, null).Revision == initialRevision, "rejected handshake cannot mutate state");
            await VerifyFragmentationAsync(a.Token, version, settings);
            await VerifyOversizeAsync(a.Token, version, settings);
            using var first = await ConnectAsync(a.Token);
            await first.SendAsync(Encoding.UTF8.GetBytes($"{{\"Kind\":\"Hello\",\"Protocol\":\"{Protocol}\",\"ContentVersion\":\"{version}\"}}"), WebSocketMessageType.Text, true, CancellationToken.None);
            var welcome = await ReadTextAsync(first, settings, CancellationToken.None);
            Assert(welcome.Contains("\"Kind\":\"Welcome\"", StringComparison.Ordinal), "welcome");
            await first.SendAsync(Encoding.UTF8.GetBytes("{\"Kind\":\"Poll\",\"AfterEventSequence\":null}"), WebSocketMessageType.Text, true, CancellationToken.None);
            var privateA = await ReadTextAsync(first, settings, CancellationToken.None);
            using var second = await ConnectAsync(b.Token);
            await second.SendAsync(Encoding.UTF8.GetBytes($"{{\"Kind\":\"Hello\",\"Protocol\":\"{Protocol}\",\"ContentVersion\":\"{version}\"}}"), WebSocketMessageType.Text, true, CancellationToken.None);
            _ = await ReadTextAsync(second, settings, CancellationToken.None);
            await second.SendAsync(Encoding.UTF8.GetBytes("{\"Kind\":\"Poll\",\"AfterEventSequence\":999999}"), WebSocketMessageType.Text, true, CancellationToken.None);
            var privateB = await ReadTextAsync(second, settings, CancellationToken.None);
            var wireA = ExtractSnapshot(privateA);
            var wireB = ExtractSnapshot(privateB);
            Assert(wireA.Offers.Count == 3 && wireB.Offers.Count == 3 && !wireA.Committed && !wireB.Committed &&
                wireA.Army.SequenceEqual(endpoint.Capture(sessions.Authenticate(a.Token)!, null).Army) &&
                wireB.Army.SequenceEqual(endpoint.Capture(sessions.Authenticate(b.Token)!, null).Army) &&
                !privateA.Contains("RngBefore", StringComparison.Ordinal) && !privateA.Contains("Decisions", StringComparison.Ordinal) &&
                privateA.Contains("\"ResyncRequired\":true", StringComparison.Ordinal) && privateB.Contains("\"ResyncRequired\":true", StringComparison.Ordinal),
                "private wire snapshot omits decision journal and preserves caller view");
            var command = CreateChoose(ExtractSnapshot(privateA), "lost-ack", 1);
            await SendCommandAsync(first, command);
            ServerMatchSnapshot? acceptedBeforeAbort = null;
            for (var wait = 0; wait < 20; wait++)
            {
                var observed = endpoint.Capture(sessions.Authenticate(a.Token)!, null);
                if (observed.Committed) { acceptedBeforeAbort = observed; break; }
                await Task.Delay(settings.PollMilliseconds);
            }
            Assert(acceptedBeforeAbort is not null && acceptedBeforeAbort.CommittedChoice is not null, "first lost-ACK command committed before abort");
            first.Abort();
            await Task.Delay(settings.PollMilliseconds * 2);
            sessions.Revoke(a.SessionId);
            var rejoin = sessions.Create("qa-player-0", LocalMatchEndpoint.MatchId, "0", TimeSpan.FromSeconds(900));
            using var retry = await ConnectAndHelloAsync(rejoin.Token, version, settings);
            await SendCommandAsync(retry, command);
            var retryAck = await ReadTextAsync(retry, settings, CancellationToken.None);
            Assert(ReadAck(retryAck).Accepted, "lost ACK exact durable retry accepted");
            var afterRetry = endpoint.Capture(sessions.Authenticate(rejoin.Token)!, null);
            Assert(afterRetry.Revision == acceptedBeforeAbort!.Revision && afterRetry.Army.SequenceEqual(acceptedBeforeAbort.Army) &&
                afterRetry.CommittedChoice == acceptedBeforeAbort.CommittedChoice, "exact retry leaves committed state unchanged");
            var opponent = CreateChoose(ExtractSnapshot(privateB), "opponent-commit", 1);
            await SendCommandAsync(second, opponent);
            Assert(ReadAck(await ReadTextAsync(second, settings, CancellationToken.None)).Accepted, "opponent command accepted");
            var stale = command with { OperationId = "stale-command", Sequence = 1 };
            await SendCommandAsync(retry, stale);
            var staleReply = ReadAck(await ReadTextAsync(retry, settings, CancellationToken.None));
            Assert(!staleReply.Accepted && staleReply.Code == "stale-token", "stale token rejected after exact retry");
            retry.Abort();
            sessions.Revoke(rejoin.SessionId);
            await Task.Delay(settings.PollMilliseconds * 2);
            var slowIssue = sessions.Create("qa-player-0", LocalMatchEndpoint.MatchId, "0", TimeSpan.FromSeconds(900));
            using var slow = await ConnectAndHelloAsync(slowIssue.Token, version, settings);
            var beforeSlow = ExtractSnapshot(await PollAsync(second, settings)).Revision;
            await slow.SendAsync(Encoding.UTF8.GetBytes("{\"Kind\":\"Poll\",\"AfterEventSequence\":null}"), WebSocketMessageType.Text, true, CancellationToken.None);
            // The client deliberately leaves this response unread. Scheduler ownership remains outside socket handlers.
            clock.Advance(TimeSpan.FromSeconds(25));
            await Task.Delay(settings.PollMilliseconds * 4);
            var afterSlow = ExtractSnapshot(await PollAsync(second, settings)).Revision;
            Assert(afterSlow > beforeSlow, "no-reader socket does not block background pump");
            slow.Abort();
            sessions.Revoke(slowIssue.SessionId);
            await Task.Delay(settings.PollMilliseconds * 2);
            var expiring = sessions.Create("qa-player-0", LocalMatchEndpoint.MatchId, "0", TimeSpan.FromSeconds(1));
            using var expiry = await ConnectAndHelloAsync(expiring.Token, version, settings);
            clock.Advance(TimeSpan.FromSeconds(2));
            await expiry.SendAsync(Encoding.UTF8.GetBytes("{\"Kind\":\"Poll\",\"AfterEventSequence\":null}"), WebSocketMessageType.Text, true, CancellationToken.None);
            await ExpectClosedAsync(expiry, settings, "expired session is revalidated after welcome");
            var revokedAfterHello = sessions.Create("qa-player-0", LocalMatchEndpoint.MatchId, "0", TimeSpan.FromSeconds(900));
            using var revokedOpen = await ConnectAndHelloAsync(revokedAfterHello.Token, version, settings);
            sessions.Revoke(revokedAfterHello.SessionId);
            await revokedOpen.SendAsync(Encoding.UTF8.GetBytes("{\"Kind\":\"Poll\",\"AfterEventSequence\":null}"), WebSocketMessageType.Text, true, CancellationToken.None);
            await ExpectClosedAsync(revokedOpen, settings, "revoked session is revalidated after welcome");
            var finalIssue = sessions.Create("qa-player-0", LocalMatchEndpoint.MatchId, "0", TimeSpan.FromSeconds(900));
            await ExpectConnectStatusAsync(b.Token, 409, "second active socket rejected with 409");
            ServerMatchSnapshot? final0 = null, final1 = null;
            for (var step = 0; step < 100; step++)
            {
                // No WebSocket poll is issued while the authoritative clock advances.
                clock.Advance(TimeSpan.FromSeconds(5));
                await Task.Delay(settings.PollMilliseconds * 3);
                var current0 = endpoint.Capture(sessions.Authenticate(finalIssue.Token)!, null);
                var current1 = endpoint.Capture(sessions.Authenticate(b.Token)!, null);
                if (current0.Phase == "MatchResult" && current1.Phase == "MatchResult") { final0 = current0; final1 = current1; break; }
            }
            Assert(final0 is not null && final1 is not null && final0.Results.Count >= 2 &&
                final0.Wins0 == final1.Wins0 && final0.Wins1 == final1.Wins1 && final0.Results.Count == final1.Results.Count,
                "offline clock reaches final result with identical public score");
            Assert(final0!.Revision > initialRevision, "pump advances without socket polls");
            sessions.Revoke(finalIssue.SessionId);
            using var revoked = await ConnectAsync(finalIssue.Token, expectFailure: true);
            Console.WriteLine("PASS local socket: 22 checks; auth/origin/version/duplicate/fragment/size guards, welcome, private snapshots, cursor resync, lost-ACK durable retry, stale command, no-reader pump, expiry/revoke after welcome, active-session guard, offline multi-round final state, background pump");
        }
        finally
        {
            endpoint.Flush();
            sessions.Revoke(a.SessionId); sessions.Revoke(b.SessionId);
            await host.StopAsync();
        }
    }

    private static async Task<WebApplication> StartAsync(LocalSocketSettings settings, LocalMatchEndpoint endpoint,
        LocalSessionRegistry sessions, string contentVersion, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        builder.Services.AddHostedService(_ => new MatchPumpService(endpoint, TimeSpan.FromMilliseconds(HostSettings.Load().SchedulerPollMilliseconds)));
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
                RateLimitPartition.GetFixedWindowLimiter("local-socket", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 120, Window = TimeSpan.FromSeconds(60), QueueLimit = 0, AutoReplenishment = true }));
        });
        var uri = new Uri(settings.ListenUrl);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.AddServerHeader = false; options.Limits.MaxConcurrentConnections = settings.MaxConnections;
            options.Limits.MaxConcurrentUpgradedConnections = settings.MaxConnections;
            options.Limits.MaxRequestBodySize = settings.MaxRequestBytes;
            options.Limits.MaxRequestHeadersTotalSize = 8192;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            options.Listen(IPAddress.Loopback, uri.Port, listen => listen.Protocols = HttpProtocols.Http1);
        });
        var app = builder.Build();
        app.UseRateLimiter();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(settings.IdleSeconds) });
        var activeSessions = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var slots = new SemaphoreSlim(settings.MaxConnections, settings.MaxConnections);
        app.Map(SocketPath, context => HandleAsync(context, settings, endpoint, sessions, contentVersion, activeSessions, slots));
        await app.StartAsync(cancellationToken);
        return app;
    }

    private static async Task HandleAsync(HttpContext context, LocalSocketSettings settings, LocalMatchEndpoint endpoint,
        LocalSessionRegistry sessions, string contentVersion, ConcurrentDictionary<string, byte> activeSessions, SemaphoreSlim slots)
    {
        if (context.Request.Method != "GET" || !context.WebSockets.IsWebSocketRequest || context.Request.Query.Count != 0 ||
            context.Request.Headers.ContainsKey("Origin")) { context.Response.StatusCode = 403; return; }
        var auth = context.Request.Headers.Authorization.ToString();
        var token = auth.StartsWith("Bearer ", StringComparison.Ordinal) && auth.Length <= 128 ? auth[7..] : null;
        var caller = sessions.Authenticate(token);
        if (caller is null) { context.Response.StatusCode = 401; return; }
        var sessionId = caller.SessionId;
        if (!await slots.WaitAsync(0, context.RequestAborted)) { context.Response.StatusCode = 503; return; }
        if (!activeSessions.TryAdd(sessionId, 0)) { slots.Release(); context.Response.StatusCode = 409; return; }
        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var hello = await ReceiveTextAsync(socket, settings.MaxRequestBytes, settings.TimeoutSeconds, CancellationToken.None);
            if (!TryRead(hello, ["Kind", "Protocol", "ContentVersion"], out var root) ||
                ReadString(root, "Kind") != "Hello" || ReadString(root, "Protocol") != Protocol || ReadString(root, "ContentVersion") != contentVersion)
            { await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, CancellationToken.None); return; }
            caller = sessions.Authenticate(token);
            if (caller is null) { await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, CancellationToken.None); return; }
            await SendAsync(socket, new { Kind = "Welcome", Protocol, ContentVersion = contentVersion, MatchId = caller.MatchId,
                Side = int.Parse(caller.Side, System.Globalization.CultureInfo.InvariantCulture) }, settings, CancellationToken.None);
            var timestamps = new Queue<long>();
            while (socket.State == WebSocketState.Open)
            {
                var request = await ReceiveTextAsync(socket, settings.MaxRequestBytes, settings.TimeoutSeconds, CancellationToken.None);
                var now = Stopwatch.GetTimestamp(); timestamps.Enqueue(now);
                while (timestamps.Count > 0 && Stopwatch.GetElapsedTime(timestamps.Peek(), now) > TimeSpan.FromSeconds(1)) timestamps.Dequeue();
                if (timestamps.Count > settings.MessagesPerSecond) { await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, CancellationToken.None); return; }
                caller = sessions.Authenticate(token);
                if (caller is null) { await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, CancellationToken.None); return; }
                if (!TryRead(request, ["Kind", "AfterEventSequence", "Command"], out root)) { await CloseAsync(socket, WebSocketCloseStatus.InvalidPayloadData, CancellationToken.None); return; }
                var kind = ReadString(root, "Kind");
                if (kind == "Poll" && HasOnly(root, "Kind", "AfterEventSequence") && TryCursor(root, out var cursor))
                    await SendAsync(socket, new { Kind = "Snapshot", Snapshot = endpoint.Capture(caller, cursor) }, settings, CancellationToken.None);
                else if (kind == "Command" && HasOnly(root, "Kind", "Command") && root.GetProperty("Command").ValueKind == JsonValueKind.Object)
                {
                    var commandValue = root.GetProperty("Command");
                    if (!TryRead(commandValue.GetRawText(), ["MatchId", "RoundId", "ContentVersion", "OperationId", "Sequence", "CommandKind", "Payload"], out var commandRoot) ||
                        !HasOnly(commandRoot, "MatchId", "RoundId", "ContentVersion", "OperationId", "Sequence", "CommandKind", "Payload"))
                    { await CloseAsync(socket, WebSocketCloseStatus.InvalidPayloadData, CancellationToken.None); return; }
                    var command = commandValue.Deserialize<CommandEnvelope>(AuthoredContent.Json);
                    if (command is null) { await CloseAsync(socket, WebSocketCloseStatus.InvalidPayloadData, CancellationToken.None); return; }
                    var reply = endpoint.Execute(caller, command);
                    await SendAsync(socket, new { Kind = "Ack", OperationId = command.OperationId, Reply = reply }, settings, CancellationToken.None);
                }
                else { await CloseAsync(socket, WebSocketCloseStatus.InvalidPayloadData, CancellationToken.None); return; }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (JsonException) { }
        catch (UnauthorizedAccessException) { }
        catch (DecoderFallbackException) { }
        catch (IOException) { }
        catch (InvalidOperationException) { }
        catch (Microsoft.Data.Sqlite.SqliteException) { }
        finally { activeSessions.TryRemove(sessionId, out _); slots.Release(); }
    }

    private static async Task<string> ReceiveTextAsync(WebSocket socket, int maximumBytes, int timeoutSeconds, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096); var bytes = new ArrayBufferWriter<byte>();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
                if (result.MessageType == WebSocketMessageType.Close) throw new OperationCanceledException();
                if (result.MessageType != WebSocketMessageType.Text || bytes.WrittenCount + result.Count > maximumBytes) throw new JsonException();
                bytes.Write(buffer.AsSpan(0, result.Count)); if (result.EndOfMessage) break;
            }
            return new UTF8Encoding(false, true).GetString(bytes.WrittenSpan);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static async Task SendAsync(WebSocket socket, object value, LocalSocketSettings settings, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, AuthoredContent.Json);
        if (bytes.Length > settings.MaxResponseBytes) throw new JsonException();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
    }
    private static async Task CloseAsync(WebSocket socket, WebSocketCloseStatus status, CancellationToken token)
    {
        if (socket.State != WebSocketState.Open) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try { await socket.CloseOutputAsync(status, "protocol", timeout.Token); }
        catch (OperationCanceledException) { socket.Abort(); }
        catch (WebSocketException) { socket.Abort(); }
    }
    private static bool TryRead(string text, string[] allowed, out JsonElement root)
    {
        root = default;
        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 16 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in doc.RootElement.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !found.Add(property.Name)) return false;
        root = doc.RootElement.Clone(); return true;
    }
    private static bool HasOnly(JsonElement root, params string[] names)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in root.EnumerateObject()) if (!names.Contains(p.Name, StringComparer.Ordinal) || !found.Add(p.Name)) return false;
        return found.Count == names.Length;
    }
    private static string? ReadString(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool TryCursor(JsonElement root, out long? cursor)
    { cursor = null; var value = root.GetProperty("AfterEventSequence"); if (value.ValueKind == JsonValueKind.Null) return true; if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var v) || v < 0) return false; cursor = v; return true; }
    private static Process StartUnity(string exe, string run, int side, string token, bool automated, bool restarted = false)
    {
        var info = new ProcessStartInfo(exe, $"-screen-width 450 -screen-height 800 -logFile \"{Path.Combine(run, $"unity-{side}.log")}\"") { UseShellExecute = false };
        info.Environment["TD_LOCAL_TOKEN"] = token; info.Environment["TD_LOCAL_ENDPOINT"] = "ws://127.0.0.1:18782/v1/socket";
        info.Environment["TD_LOCAL_SIDE"] = side.ToString(System.Globalization.CultureInfo.InvariantCulture); info.Environment["TD_LOCAL_RUN"] = run;
        if (automated) info.Environment["TD_LOCAL_AUTO"] = "1";
        if (restarted) info.Environment["TD_LOCAL_RESTARTED"] = "1";
        return Process.Start(info) ?? throw new InvalidOperationException("Unity process did not start.");
    }
    private static async Task<ClientWebSocket> ConnectAsync(string token, bool expectFailure = false)
    {
        var socket = new ClientWebSocket(); socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        try { await socket.ConnectAsync(new Uri("ws://127.0.0.1:18782" + SocketPath), CancellationToken.None); }
        catch when (expectFailure) { return socket; }
        if (expectFailure) throw new InvalidOperationException("Revoked socket connected."); return socket;
    }
    private static async Task ExpectConnectRejectedAsync(string? token, string? origin, string name)
    {
        using var socket = new ClientWebSocket();
        if (token is not null) socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        if (origin is not null) socket.Options.SetRequestHeader("Origin", origin);
        try { await socket.ConnectAsync(new Uri("ws://127.0.0.1:18782" + SocketPath), CancellationToken.None); }
        catch (WebSocketException) { return; }
        throw new InvalidOperationException("Socket QA failed: " + name);
    }
    private static async Task ExpectConnectStatusAsync(string token, int expectedStatus, string name)
    {
        using var socket = new ClientWebSocket(); socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        try { await socket.ConnectAsync(new Uri("ws://127.0.0.1:18782" + SocketPath), CancellationToken.None); }
        catch (WebSocketException error) when (error.InnerException is System.Net.Http.HttpRequestException response && (int?)response.StatusCode == expectedStatus) { return; }
        catch (WebSocketException error) when (error.Message.Contains("status code '" + expectedStatus.ToString(System.Globalization.CultureInfo.InvariantCulture) + "'", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Socket QA failed: " + name);
    }
    private static async Task ExpectClosedAfterHelloAsync(string token, string hello, string name)
    {
        using var socket = await ConnectAsync(token);
        await socket.SendAsync(Encoding.UTF8.GetBytes(hello), WebSocketMessageType.Text, true, CancellationToken.None);
        try { _ = await ReadTextAsync(socket, LocalSocketSettings.Load(), CancellationToken.None); }
        catch (OperationCanceledException) { return; }
        catch (WebSocketException) { return; }
        throw new InvalidOperationException("Socket QA failed: " + name);
    }
    private static async Task VerifyFragmentationAsync(string token, string version, LocalSocketSettings settings)
    {
        using var socket = await ConnectAsync(token);
        await socket.SendAsync(Encoding.UTF8.GetBytes($"{{\"Kind\":\"Hello\",\"Protocol\":\"{Protocol}\",\"ContentVersion\":\"{version}\"}}"), WebSocketMessageType.Text, true, CancellationToken.None);
        _ = await ReadTextAsync(socket, settings, CancellationToken.None);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"Kind\":\"Poll\","), WebSocketMessageType.Text, false, CancellationToken.None);
        await socket.SendAsync(Encoding.UTF8.GetBytes("\"AfterEventSequence\":null}"), WebSocketMessageType.Text, true, CancellationToken.None);
        Assert((await ReadTextAsync(socket, settings, CancellationToken.None)).Contains("\"Kind\":\"Snapshot\"", StringComparison.Ordinal), "fragmented poll");
    }
    private static async Task VerifyOversizeAsync(string token, string version, LocalSocketSettings settings)
    {
        using var socket = await ConnectAsync(token);
        await socket.SendAsync(Encoding.UTF8.GetBytes($"{{\"Kind\":\"Hello\",\"Protocol\":\"{Protocol}\",\"ContentVersion\":\"{version}\"}}"), WebSocketMessageType.Text, true, CancellationToken.None);
        _ = await ReadTextAsync(socket, settings, CancellationToken.None);
        await socket.SendAsync(Encoding.UTF8.GetBytes(new string('x', settings.MaxRequestBytes + 1)), WebSocketMessageType.Text, true, CancellationToken.None);
        try { _ = await ReadTextAsync(socket, settings, CancellationToken.None); }
        catch (OperationCanceledException) { return; }
        catch (WebSocketException) { return; }
        throw new InvalidOperationException("Socket QA failed: oversized frame");
    }
    private static async Task<ClientWebSocket> ConnectAndHelloAsync(string token, string version, LocalSocketSettings settings)
    {
        var socket = await ConnectAsync(token);
        await socket.SendAsync(Encoding.UTF8.GetBytes($"{{\"Kind\":\"Hello\",\"Protocol\":\"{Protocol}\",\"ContentVersion\":\"{version}\"}}"), WebSocketMessageType.Text, true, CancellationToken.None);
        Assert((await ReadTextAsync(socket, settings, CancellationToken.None)).Contains("\"Kind\":\"Welcome\"", StringComparison.Ordinal), "rejoin welcome");
        return socket;
    }
    private static async Task<string> PollAsync(ClientWebSocket socket, LocalSocketSettings settings)
    {
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"Kind\":\"Poll\",\"AfterEventSequence\":null}"), WebSocketMessageType.Text, true, CancellationToken.None);
        return await ReadTextAsync(socket, settings, CancellationToken.None);
    }
    private static async Task ExpectClosedAsync(ClientWebSocket socket, LocalSocketSettings settings, string name)
    {
        try { _ = await ReadTextAsync(socket, settings, CancellationToken.None); }
        catch (OperationCanceledException) { return; }
        catch (WebSocketException) { return; }
        throw new InvalidOperationException("Socket QA failed: " + name);
    }
    private static ServerMatchSnapshot ExtractSnapshot(string message)
    {
        using var document = JsonDocument.Parse(message);
        return document.RootElement.GetProperty("Snapshot").Deserialize<ServerMatchSnapshot>(AuthoredContent.Json)
            ?? throw new InvalidOperationException("Socket QA snapshot missing.");
    }
    private static CommandReply ReadAck(string message)
    {
        using var document = JsonDocument.Parse(message);
        if (document.RootElement.GetProperty("Kind").GetString() != "Ack") throw new InvalidOperationException("Socket QA ACK missing.");
        return document.RootElement.GetProperty("Reply").Deserialize<CommandReply>(AuthoredContent.Json)
            ?? throw new InvalidOperationException("Socket QA ACK reply missing.");
    }
    private static CommandEnvelope CreateChoose(ServerMatchSnapshot snapshot, string operation, long sequence) => new(
        snapshot.MatchId, snapshot.Round.ToString(System.Globalization.CultureInfo.InvariantCulture), snapshot.ContentVersion,
        operation, sequence, "Choose", $"{{\"Token\":{snapshot.ChoiceToken},\"OfferIndex\":0}}");
    private static Task SendCommandAsync(ClientWebSocket socket, CommandEnvelope command) => socket.SendAsync(
        JsonSerializer.SerializeToUtf8Bytes(new { Kind = "Command", Command = command }, AuthoredContent.Json), WebSocketMessageType.Text, true, CancellationToken.None);
    private static async Task<string> ReadTextAsync(ClientWebSocket socket, LocalSocketSettings settings, CancellationToken token) => await ReceiveTextAsync(socket, settings.MaxResponseBytes, settings.TimeoutSeconds, token);
    private static void Assert(bool value, string name) { if (!value) throw new InvalidOperationException("Socket QA failed: " + name); }
    private static string FindRepositoryRoot()
    { var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "Backend", "global.json"))) root = root.Parent; return root?.FullName ?? throw new InvalidOperationException("Repository root missing."); }
}
