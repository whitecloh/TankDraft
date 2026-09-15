using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using TankDraft.Server.Admission;
using TankDraft.Server.Security;

namespace TankDraft.RemoteHost;

public sealed class RemoteSocketEndpoint(RemoteMatchService service)
{
    const string Protocol = "tankdraft-server-v2";
    readonly object _sync = new();
    readonly Dictionary<string, SocketOwner> _owners = new(StringComparer.Ordinal);
    readonly SemaphoreSlim _sockets = new(40, 40);
    public void Map(WebApplication app) => app.MapGet("/v1/socket", Handle);
    async Task Handle(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
        if (!_sockets.Wait(0)) { context.Response.StatusCode = 503; return; }
        SocketOwner? mine = null; WebSocket? socket = null;
        try
        {
            var token = Bearer(context);
            var access = service.UseAccess(token, (_, a) => a);
            mine = new SocketOwner(access);
            lock (_sync)
            {
                if (_owners.TryGetValue(access.StreamId, out var old)) old.Socket?.Abort();
                _owners[access.StreamId] = mine;
            }
            socket = await context.WebSockets.AcceptWebSocketAsync();
            lock (_sync)
            {
                if (!_owners.TryGetValue(access.StreamId, out var owner) || !ReferenceEquals(owner, mine)) { socket.Abort(); return; }
                mine.Socket = socket;
            }
            var hello = await Receive(socket, context.RequestAborted);
            if (!Only(hello, "Kind", "Protocol", "ContentVersion") || String(hello, "Kind") != "Hello" || String(hello, "Protocol") != Protocol || String(hello, "ContentVersion") != service.ContentVersion) throw new JsonException();
            await Send(socket, service.UseAccess(token, (match, a) => (object)new { Kind = "Welcome", Protocol, ContentVersion = service.ContentVersion, ParallelAccessRefresh = true, a.MatchId, Side = int.Parse(a.Side), a.SessionId, a.StreamId, a.Generation, NextSequence = match.Runtime.NextCommandSequence(a.Caller) }), context.RequestAborted);
            var requests = new Queue<long>();
            var parallelRefreshEnabled = false;
            var awaitingReauthentication = false;
            while (socket.State == WebSocketState.Open)
            {
                var root = await Receive(socket, context.RequestAborted);
                var now = Stopwatch.GetTimestamp(); requests.Enqueue(now);
                while (requests.Count > 0 && Stopwatch.GetElapsedTime(requests.Peek(), now) > TimeSpan.FromSeconds(1)) requests.Dequeue();
                if (requests.Count > 30) throw new JsonException();
                object response;
                lock (_sync)
                {
                    if (!_owners.TryGetValue(mine.StreamId, out var owner) || !ReferenceEquals(owner, mine)) throw new UnauthorizedAccessException();
                    if (awaitingReauthentication && !IsKind(root, "Reauthenticate", "Kind", "AccessToken"))
                        throw new UnauthorizedAccessException();
                    if (String(root, "Kind") == "EnableParallelRefresh" && Only(root, "Kind"))
                    {
                        service.UseAccess(token, (_, _) => 0);
                        parallelRefreshEnabled = true;
                        response = new { Kind = "ParallelRefreshEnabled" };
                    }
                    else if (String(root, "Kind") == "Reauthenticate" && Only(root, "Kind", "AccessToken"))
                    {
                        var next = String(root, "AccessToken");
                        response = service.UseAccess(next, (_, a) =>
                        {
                            if (a.StreamId != mine.StreamId || a.MatchId != mine.MatchId || a.Caller.AccountId != mine.Account || a.Side != mine.Side || a.Generation < mine.Generation) throw new UnauthorizedAccessException();
                            mine.Generation = a.Generation;
                            return (object)new { Kind = "Reauthenticated", a.SessionId, a.StreamId, a.Generation, a.MatchId, Side = int.Parse(a.Side) };
                        });
                        token = next;
                        awaitingReauthentication = false;
                    }
                    else if (String(root, "Kind") == "Poll" && Only(root, "Kind", "AfterEventSequence"))
                    {
                        var value = root.GetProperty("AfterEventSequence");
                        long? cursor = value.ValueKind == JsonValueKind.Null ? null : value.TryGetInt64(out var number) && number >= 0 ? number : throw new JsonException();
                        try { response = service.UseAccess(token, (match, a) => (object)new { Kind = "Snapshot", Snapshot = match.Runtime.Capture(a.Caller, cursor) }); }
                        catch (Exception error) when (parallelRefreshEnabled && error is AdmissionRejectedException or UnauthorizedAccessException)
                        {
                            awaitingReauthentication = true;
                            response = new { Kind = "AccessRefreshRequired" };
                        }
                    }
                    else if (String(root, "Kind") == "Command" && Only(root, "Kind", "Command"))
                    {
                        var value = root.GetProperty("Command");
                        if (!Only(value, "MatchId", "RoundId", "ContentVersion", "OperationId", "Sequence", "CommandKind", "Payload")) throw new JsonException();
                        var command = value.Deserialize<CommandEnvelope>(AuthoredContent.Json) ?? throw new JsonException();
                        response = service.UseAccess(token, (match, a) => (object)new { Kind = "Ack", command.OperationId, Reply = match.Runtime.Execute(a.Caller, command) });
                    }
                    else throw new JsonException();
                }
                await Send(socket, response, context.RequestAborted);
            }
        }
        catch (Exception error) when (error is UnauthorizedAccessException or AdmissionRejectedException)
        { if (socket is null) context.Response.StatusCode = 403; else await Close(socket, 4401); }
        catch { if (socket is null) context.Response.StatusCode = 400; else await Close(socket, 4400); }
        finally
        {
            if (mine is not null) lock (_sync) { if (_owners.TryGetValue(mine.StreamId, out var owner) && ReferenceEquals(owner, mine)) _owners.Remove(mine.StreamId); }
            socket?.Dispose(); _sockets.Release();
        }
    }
    internal static string Bearer(HttpContext context)
    {
        var headers = context.Request.Headers.Authorization;
        if (headers.Count != 1 || headers[0] is not { } value || !value.StartsWith("Bearer ", StringComparison.Ordinal)) throw new UnauthorizedAccessException();
        var token = value[7..];
        if (token.Length is < 1 or > 4096 || token.Any(c => c < 0x21 || c > 0x7e)) throw new UnauthorizedAccessException();
        return token;
    }
    internal static JsonElement Parse(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12, AllowDuplicateProperties = false });
        return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : throw new JsonException();
    }
    internal static bool Only(JsonElement root, params string[] names) => root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal));
    static bool IsKind(JsonElement root, string kind, params string[] names) => Only(root, names) && root.TryGetProperty("Kind", out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == kind;
    static string String(JsonElement root, string field) => root.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new JsonException();
    static async Task<JsonElement> Receive(WebSocket socket, CancellationToken stop)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            using var bytes = new MemoryStream();
            while (true)
            {
                var received = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
                if (received.MessageType != WebSocketMessageType.Text || bytes.Length + received.Count > 4096) throw new JsonException();
                bytes.Write(buffer, 0, received.Count);
                if (received.EndOfMessage) return Parse(bytes.ToArray());
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
    static async Task Send(WebSocket socket, object value, CancellationToken stop)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, AuthoredContent.Json);
        if (bytes.Length > 1048576) throw new JsonException();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, timeout.Token);
    }
    static async Task Close(WebSocket socket, int code)
    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1)); try { await socket.CloseOutputAsync((WebSocketCloseStatus)code, "", timeout.Token); } catch { socket.Abort(); } }
    sealed class SocketOwner(AdmissionAccessContext access)
    {
        public string StreamId { get; } = access.StreamId;
        public string MatchId { get; } = access.MatchId;
        public string Account { get; } = access.Caller.AccountId;
        public string Side { get; } = access.Side;
        public int Generation { get; set; } = access.Generation;
        public WebSocket? Socket { get; set; }
    }
}
