using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TankDraft.Server.Security;

namespace TankDraft.LocalHost;

// Local TLS matchmaking harness. Authentication is supplied by a server-created bearer grant;
// no account, side, match, or bot selection is accepted from a queue request body.
internal static partial class LocalTlsHost
{
    public static async Task VerifyQueueHttpAsync()
    {
        var tls = LocalTlsSettings.Load(); var host = HostSettings.Load(); var queueSettings = LocalQueueSettings.Load(); var (content, version) = AuthoredContent.Load();
        using var queue = new LocalMatchmaking(content, version, host, queueSettings); var accounts = new LocalSessionRegistry(8); var families = new LocalAccessFamilies(8);
        var first = accounts.Create("queue-http-a", "queue-lobby", "0", TimeSpan.FromMinutes(5)); var second = accounts.Create("queue-http-b", "queue-lobby", "0", TimeSpan.FromMinutes(5));
        using var certificate = CreateCertificate(out var pin); var access = new QueueAccess(queue, accounts, families, tls);
        await using var app = await StartAsync(tls, queue, families, version, certificate, CancellationToken.None, requestLimit: 600, addRoutes: web => access.Map(web));
        try
        {
            using var client = PinnedClient(pin); client.Timeout = TimeSpan.FromSeconds(5);
            var unauthenticated = await Post(client, null, "/v1/queue/status", "{}"); Ensure(unauthenticated.StatusCode == System.Net.HttpStatusCode.Unauthorized, "queue unknown account");
            var request0 = JsonSerializer.Serialize(new { RequestId = "http-a", ContentVersion = version, Deck = content.Deck, OrderId = content.OrderId });
            var firstJoin = await Post(client, first.Token, "/v1/queue/join", request0); Ensure(firstJoin.IsSuccessStatusCode, "queue first join");
            var request1 = JsonSerializer.Serialize(new { RequestId = "http-b", ContentVersion = version, Deck = content.Deck, OrderId = content.OrderId });
            var secondJoin = await Post(client, second.Token, "/v1/queue/join", request1); Ensure(secondJoin.IsSuccessStatusCode, "queue second join");
            var status0 = await Post(client, first.Token, "/v1/queue/status", "{}"); Ensure(status0.IsSuccessStatusCode, "queue first status");
            using var parsed = JsonDocument.Parse(await status0.Content.ReadAsStringAsync()); var root = parsed.RootElement;
            Ensure(root.GetProperty("State").GetString() == "Matched" && root.GetProperty("MatchGrant").GetString() is { Length: > 0 }, "queue matched grant");
            var malformed = await Post(client, first.Token, "/v1/queue/cancel", "{}"); Ensure(malformed.StatusCode == System.Net.HttpStatusCode.BadRequest, "queue exact cancel body");
            Console.WriteLine("PASS local queue HTTP: bearer account auth, exact bodies, human pairing, match grant");
        }
        finally { access.Dispose(); await app.StopAsync(); }
        static Task<HttpResponseMessage> Post(HttpClient client, string? token, string path, string json)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://127.0.0.1:18783" + path) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
            if (token is not null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            return client.SendAsync(request);
        }
    }

    public static async Task ServeQueueUnityAsync(string unityExecutable, bool solo, bool automated)
    {
        var root = FindRepositoryRoot(); var exe = Path.GetFullPath(unityExecutable);
        var allowed = Path.GetFullPath(Path.Combine(root, "Logs", "BackendClient")) + Path.DirectorySeparatorChar;
        if (!Path.IsPathFullyQualified(unityExecutable) || !exe.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !File.Exists(exe)) throw new InvalidOperationException("Unity executable must be an existing absolute path under Logs/BackendClient.");
        var settings = LocalTlsSettings.Load() with { MaxConnections = 8 }; var host = HostSettings.Load(); var queueSettings = LocalQueueSettings.Load(); var (content, version) = AuthoredContent.Load();
        using var queue = new LocalMatchmaking(content, version, host, queueSettings); var accounts = new LocalSessionRegistry(8);
        var account0 = accounts.Create("queue-player-0", "queue-lobby", "0", TimeSpan.FromHours(1));
        var account1 = accounts.Create("queue-player-1", "queue-lobby", "0", TimeSpan.FromHours(1));
        var families = new LocalAccessFamilies(8); using var certificate = CreateCertificate(out var pin);
        var access = new QueueAccess(queue, accounts, families, settings);
        await using var app = await StartAsync(settings, queue, families, version, certificate, CancellationToken.None, requestLimit: 600, addRoutes: web => access.Map(web));
        var run = Path.Combine(root, "Logs", "BackendClient", "queue-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run); Console.WriteLine("Local queue TLS run: " + run);
        var processes = new List<Process>(); using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(automated ? 850 : 3600)); var completed = false;
        Console.CancelKeyPress += Cancel;
        try
        {
            processes.Add(StartQueueUnity(exe, Path.Combine(run, "player-0"), 0, account0.Token, pin, automated));
            if (!solo) processes.Add(StartQueueUnity(exe, Path.Combine(run, "player-1"), 1, account1.Token, pin, automated));
            while (!stop.IsCancellationRequested)
            {
                if (processes.All(p => p.HasExited))
                {
                    var exits = processes.Select(p => p.ExitCode).ToArray(); File.WriteAllText(Path.Combine(run, "player-exits.json"), JsonSerializer.Serialize(exits));
                    if (automated && exits.Any(x => x != 0)) throw new InvalidOperationException("Automated queue client failed; inspect its private run directory.");
                    completed = true; break;
                }
                await Task.Delay(250, stop.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            Console.CancelKeyPress -= Cancel;
            foreach (var process in processes) { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } process.Dispose(); }
            access.Dispose(); await app.StopAsync();
        }
        if (automated && !completed) throw new TimeoutException("Automated local queue run timed out.");
        void Cancel(object? _, ConsoleCancelEventArgs e) { e.Cancel = true; stop.Cancel(); }
    }

    public static async Task ServeQueueAndroidAsync(string serial, string unityExecutable, bool solo, bool automated, bool restartDuringSearch = false)
    {
        if (restartDuringSearch && (!solo || !automated)) throw new InvalidOperationException("Search restart QA is available only for automated Android solo mode.");
        if (string.IsNullOrWhiteSpace(serial) || serial.Length > 128) throw new InvalidOperationException("ADB serial is required.");
        var root = FindRepositoryRoot(); var exe = Path.GetFullPath(unityExecutable); var allowed = Path.GetFullPath(Path.Combine(root, "Logs", "BackendClient")) + Path.DirectorySeparatorChar;
        if (!Path.IsPathFullyQualified(unityExecutable) || !exe.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !File.Exists(exe)) throw new InvalidOperationException("Unity executable must be an existing absolute path under Logs/BackendClient.");
        var adbPath = Path.GetFullPath(Path.Combine(root, "..", "..", "UNITY", "6000.3.10f1", "Editor", "Data", "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe"));
        if (!File.Exists(adbPath)) throw new InvalidOperationException("Expected Unity Android adb.exe is missing.");
        var device = new AndroidAdb(adbPath, serial); await device.RequireAuthorizedAsync();
        var settings = LocalTlsSettings.Load() with { MaxConnections = 8 }; var host = HostSettings.Load(); var queueSettings = LocalQueueSettings.Load(); var (content, version) = AuthoredContent.Load();
        using var queue = new LocalMatchmaking(content, version, host, queueSettings); var accounts = new LocalSessionRegistry(8);
        var account0 = accounts.Create("queue-player-0", "queue-lobby", "0", TimeSpan.FromHours(1)); var account1 = accounts.Create("queue-player-1", "queue-lobby", "0", TimeSpan.FromHours(1));
        var families = new LocalAccessFamilies(8); using var certificate = CreateCertificate(out var pin); var access = new QueueAccess(queue, accounts, families, settings);
        await using var app = await StartAsync(settings, queue, families, version, certificate, CancellationToken.None, requestLimit: 600, addRoutes: web => access.Map(web));
        var runId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(); var run = Path.Combine(root, "Logs", "BackendClient", "queue-android-" + runId); Directory.CreateDirectory(run); Console.WriteLine("Local queue Android TLS run: " + run);
        var reverseOwned = false; Process? windows = null; var phonePids = new HashSet<int>(); var completed = false; var restartedSearch = false; using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(automated ? 850 : 3600)); Console.CancelKeyPress += Cancel;
        try
        {
            reverseOwned = await device.EnsureReverseAsync(18783);
            await device.ForceStopAsync();
            await device.WriteBootstrapAsync(solo ? account0.Token : account1.Token, pin, runId, automated, queue: true); await device.StartAppAsync();
            if (!solo && automated) await WaitForPhoneSearchAsync(queue, run, stop.Token);
            if (!solo) windows = StartQueueUnity(exe, Path.Combine(run, "player-0"), 0, account0.Token, pin, automated);
            phonePids.UnionWith(await device.GetAppPidsAsync());
            if (automated)
            {
                var deadline = Stopwatch.StartNew();
                while (deadline.Elapsed < TimeSpan.FromSeconds(850))
                {
                    stop.Token.ThrowIfCancellationRequested();
                    if (!solo) AssertNoBotPair(queue.Status("queue-player-0"), "queue-player-0");
                    if (!solo) AssertNoBotPair(queue.Status("queue-player-1"), "queue-player-1");
                    if (restartDuringSearch && !restartedSearch)
                    {
                        var before = queue.Status("queue-player-0");
                        if (before.State == LocalQueueState.Matched) throw new InvalidOperationException("Queue matched before the requested search-restart checkpoint.");
                        if (before.State == LocalQueueState.Searching)
                        {
                            var ticketId = before.TicketId ?? throw new InvalidOperationException("Searching queue status has no ticket.");
                            await device.ForceStopAsync(); await Task.Delay(TimeSpan.FromSeconds(2), stop.Token); await device.StartAppAsync();
                            phonePids.UnionWith(await device.GetAppPidsAsync());
                            var restored = queue.Status("queue-player-0");
                            if (restored.State is LocalQueueState.Idle || restored.TicketId != ticketId) throw new InvalidOperationException("Search restart did not restore the server queue ticket.");
                            await File.WriteAllTextAsync(Path.Combine(run, "search-restart.json"), JsonSerializer.Serialize(new { TicketId = ticketId, RestoredTicketId = restored.TicketId, Restarted = true }) + Environment.NewLine, new UTF8Encoding(false), stop.Token);
                            restartedSearch = true;
                        }
                    }
                    var pids = await device.GetAppPidsAsync(); phonePids.UnionWith(pids);
                    if ((!restartDuringSearch || restartedSearch) && pids.Count == 0 && (windows is null || windows.HasExited)) { completed = true; break; }
                    await Task.Delay(restartedSearch ? 1000 : 250, stop.Token);
                }
                if (!completed || (restartDuringSearch && !restartedSearch) || (windows is not null && windows.ExitCode != 0)) throw new InvalidOperationException("Automated queue peers did not complete successfully.");
                await device.PullQaFilesAsync(runId, Path.Combine(run, "android"));
            }
            else await Task.Delay(Timeout.InfiniteTimeSpan, stop.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            Console.CancelKeyPress -= Cancel; try { await CollectPhoneRuntimeAsync(device, phonePids, run); } catch (Exception) { } try { if (automated) await device.ForceStopAsync(); } catch (Exception) { } try { await device.DeleteBootstrapAsync(); } catch (Exception) { }
            if (reverseOwned) try { await device.RemoveReverseAsync(18783); } catch (Exception) { }
            try { if (windows is not null && !windows.HasExited) windows.Kill(true); } catch (InvalidOperationException) { } windows?.Dispose(); access.Dispose(); await app.StopAsync();
        }
        if (automated && !completed) throw new TimeoutException("Automated Android queue run did not complete.");
        void Cancel(object? _, ConsoleCancelEventArgs e) { e.Cancel = true; stop.Cancel(); }
    }

    private static async Task WaitForPhoneSearchAsync(LocalMatchmaking queue, string run, CancellationToken token)
    {
        var wait = Stopwatch.StartNew();
        while (wait.Elapsed < TimeSpan.FromSeconds(8))
        {
            token.ThrowIfCancellationRequested();
            var status = queue.Status("queue-player-1");
            if (status.State == LocalQueueState.Matched) throw new InvalidOperationException("Phone matched before Windows peer launch.");
            if (status.State == LocalQueueState.Searching)
            {
                await File.WriteAllTextAsync(Path.Combine(run, "pair-search-ready.json"), JsonSerializer.Serialize(new { TicketId = status.TicketId, State = status.State.ToString(), WaitMilliseconds = wait.ElapsedMilliseconds }) + Environment.NewLine, new UTF8Encoding(false), token);
                return;
            }
            await Task.Delay(100, token);
        }
        throw new TimeoutException("Phone did not reach server Searching state before the pair launch deadline.");
    }

    private static void AssertNoBotPair(LocalQueueStatus status, string account)
    {
        if (status.State == LocalQueueState.Matched && status.OpponentKind == LocalOpponentKind.Bot)
            throw new InvalidOperationException("Automated pair account matched a bot: " + account);
    }

    private static Process StartQueueUnity(string exe, string run, int side, string queueGrant, string pin, bool automated)
    {
        Directory.CreateDirectory(run);
        var info = new ProcessStartInfo(exe, $"-screen-width 450 -screen-height 800 -logFile \"{Path.Combine(run, "unity.log")}\"") { UseShellExecute = false };
        info.Environment["TD_QUEUE_MODE"] = "1"; info.Environment["TD_QUEUE_GRANT"] = queueGrant; info.Environment["TD_QUEUE_RUN"] = run;
        info.Environment["TD_QUEUE_AUTOJOIN"] = automated ? "1" : "0"; info.Environment["TD_QUEUE_AUTO_REMAINING"] = "2";
        info.Environment["TD_LOCAL_ENDPOINT"] = "wss://127.0.0.1:18783/v1/socket"; info.Environment["TD_LOCAL_TLS_PIN"] = pin;
        info.Environment["TD_LOCAL_SIDE"] = side.ToString(System.Globalization.CultureInfo.InvariantCulture); info.Environment["TD_LOCAL_RUN"] = run;
        info.Environment["TD_LOCAL_RESTARTED"] = "1"; info.Environment["TD_LOCAL_AUTO"] = automated ? "1" : "0";
        return Process.Start(info) ?? throw new InvalidOperationException("Could not launch local queue Unity client.");
    }

    private sealed class QueueAccess(LocalMatchmaking queue, LocalSessionRegistry accounts, LocalAccessFamilies families, LocalTlsSettings tls) : IDisposable
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, QueueGrant> _matchGrants = new(StringComparer.Ordinal);
        public void Map(WebApplication app)
        {
            app.MapPost("/v1/queue/join", (Func<HttpContext, Task<IResult>>)JoinAsync);
            app.MapPost("/v1/queue/status", (Func<HttpContext, Task<IResult>>)StatusAsync);
            app.MapPost("/v1/queue/cancel", (Func<HttpContext, Task<IResult>>)CancelAsync);
            app.MapPost("/v1/queue/leave", (Func<HttpContext, Task<IResult>>)LeaveAsync);
        }
        private async Task<IResult> JoinAsync(HttpContext context)
        {
            var account = Authenticate(context); if (account is null) return Results.Unauthorized();
            var body = await Read<QueueJoin>(context, ["RequestId", "ContentVersion", "Deck", "OrderId"]); if (body is null) return Results.BadRequest();
            try { lock (_sync) return Results.Json(Response(account, queue.Join(account, body.RequestId, body.ContentVersion, body.Deck, body.OrderId)), AuthoredContent.Json); }
            catch (ArgumentException) { return Results.BadRequest(); } catch (InvalidOperationException) { return Results.StatusCode(409); }
        }
        private async Task<IResult> StatusAsync(HttpContext context)
        {
            var account = Authenticate(context); if (account is null) return Results.Unauthorized();
            if (await Read<object>(context, []) is null) return Results.BadRequest(); lock (_sync) return Results.Json(Response(account, queue.Status(account)), AuthoredContent.Json);
        }
        private async Task<IResult> CancelAsync(HttpContext context)
        {
            var account = Authenticate(context); if (account is null) return Results.Unauthorized();
            var body = await Read<QueueCancel>(context, ["TicketId"]); if (body is null) return Results.BadRequest();
            try { lock (_sync) return Results.Json(Response(account, queue.Cancel(account, body.TicketId)), AuthoredContent.Json); } catch (ArgumentException) { return Results.BadRequest(); }
        }
        private async Task<IResult> LeaveAsync(HttpContext context)
        {
            var account = Authenticate(context); if (account is null) return Results.Unauthorized();
            var body = await Read<QueueLeave>(context, ["MatchId"]); if (body is null) return Results.BadRequest();
            try { lock (_sync) { var left = queue.LeaveCompleted(account, body.MatchId); if (!left) return Results.StatusCode(409); RevokeLocked(account, body.MatchId); return Results.Json(Response(account, queue.Status(account)), AuthoredContent.Json); } }
            catch (ArgumentException) { return Results.BadRequest(); }
        }
        private string? Authenticate(HttpContext context)
        {
            if (context.Request.Query.Count != 0 || context.Request.Headers.ContainsKey("Origin")) return null;
            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) || authorization.Length > 128) return null;
            return accounts.Authenticate(authorization[7..])?.AccountId;
        }
        private object Response(string account, LocalQueueStatus status)
        {
            string? grant = null;
            if (status.State == LocalQueueState.Matched && status.MatchId is not null && status.Side is not null) grant = GetOrCreate(account, status.MatchId, status.Side.Value);
            return new { status.TicketId, State = status.State.ToString(), status.RemainingSeconds, status.MatchId, status.Side, OpponentKind = status.OpponentKind?.ToString(), MatchGrant = grant };
        }
        private string GetOrCreate(string account, string matchId, int side)
        {
            lock (_sync)
            {
                if (_matchGrants.TryGetValue(account, out var found))
                {
                    if (found.MatchId == matchId && found.Side == side) return found.Grant.Value;
                    // Queue state has already validated this account's new assignment. Retire a
                    // completed/retained grant before issuing the next match capability.
                    families.Revoke(found.Grant); _matchGrants.Remove(account);
                }
                var created = families.Create(account, matchId, side.ToString(System.Globalization.CultureInfo.InvariantCulture), TimeSpan.FromSeconds(tls.GrantTtlSeconds));
                _matchGrants.Add(account, new QueueGrant(matchId, side, created)); return created.Value;
            }
        }
        private void RevokeLocked(string account, string matchId)
        {
            if (_matchGrants.TryGetValue(account, out var found) && found.MatchId == matchId) { _matchGrants.Remove(account); families.Revoke(found.Grant); }
        }
        public void Dispose() { lock (_sync) { foreach (var grant in _matchGrants.Values) families.Revoke(grant.Grant); _matchGrants.Clear(); } }
        private static async Task<T?> Read<T>(HttpContext context, string[] allowed) where T : class
        {
            if (context.Request.ContentLength is not >= 0 and <= 8192 || !context.Request.HasJsonContentType()) return null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var document = await JsonDocument.ParseAsync(context.Request.Body, new JsonDocumentOptions { MaxDepth = 8, AllowTrailingCommas = false }, timeout.Token);
                if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x).SequenceEqual(allowed.OrderBy(x => x)) == false) return null;
                return document.RootElement.Deserialize<T>(AuthoredContent.Json);
            }
            catch (JsonException) { return null; }
            catch (OperationCanceledException) { return null; }
        }
        private sealed record QueueGrant(string MatchId, int Side, LocalGrant Grant);
        private sealed record QueueJoin(string RequestId, string ContentVersion, string[] Deck, string OrderId);
        private sealed record QueueCancel(string TicketId);
        private sealed record QueueLeave(string MatchId);
    }
}
