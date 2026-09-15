using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PlayFab;
using PlayFab.ServerModels;
using TankDraft.AdmissionHttp;
using TankDraft.AdmissionHttpChecks;
using TankDraft.Server.Admission;
using TankDraft.Server.Match;
using TankDraft.Server.PlayFab.Identity;
using TankDraft.Server.Security;

var checks = 0;
var calls = 0;
var clock = new TestClock();
var (content, version) = AuthoredContent.Load();
using var identity = new PlayFabIdentityAdapter(new PlayFabIdentityOptions("B16D9", "offline-test-only"), request =>
{
    Interlocked.Increment(ref calls);
    if (request.SessionTicket == "unavailable") throw new HttpRequestException("sensitive-provider-response");
    var id = request.SessionTicket switch { "player-a-ticket" => "ABCDE1", "player-b-ticket" => "ABCDE2", _ => "ABCDE3" };
    return Task.FromResult(new PlayFabResult<AuthenticateSessionTicketResult> { Result = new() { IsSessionTicketExpired = false, UserInfo = new() { PlayFabId = id } } });
}, clock.GetUtcNow);
var admission = new MatchAdmissionService(identity, [new("http-match", version, "ABCDE1", "ABCDE2", clock.GetUtcNow().AddMinutes(20))],
    new AdmissionSettings(2, TimeSpan.FromSeconds(120), 90), clock);
using var runtime = new ServerMatchRuntime("http-match", version, content.CreateMatch(), content.CreateDefinitions(), content.BattleRules,
    new ServerMatchSettings(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), 1024, 8192, 4096, 4096), clock, 12345, ["ABCDE1", "ABCDE2"]);
await using var app = Build(admission, new AdmissionHttpSettings(120, 10, 4, 2, true));
await app.StartAsync();
try
{
    using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(5) };
    await Expect(http, null, Body("a"), 401, "missing auth");
    await Expect(http, "player-a-ticket", "{\"ContentVersion\":\"x\",\"ContentVersion\":\"y\"}", 400, "duplicate fields");
    await Expect(http, "player-a-ticket", JsonSerializer.Serialize(new { ContentVersion = version, OperationId = "a", Side = 1 }), 400, "client seat rejected");
    await Expect(http, "player-a-ticket", "[1,2]", 400, "nonobject JSON");
    await Expect(http, "player-a-ticket", Body("a") + "{}", 400, "trailing JSON");
    await Expect(http, "player-a-ticket", new string(' ', 2050), 413, "body limit");
    Require(calls == 0, "malformed requests never call provider");
    await Expect(http, "unknown-ticket", Body("a"), 403, "unassigned account");
    await Expect(http, "unavailable", Body("a"), 503, "provider failure is transient");
    await Expect(http, "player-a-ticket", "{\"ContentVersion\":\"wrong\",\"OperationId\":\"a\"}", 403, "wrong content");
    using (var origin = Request("player-a-ticket", Body("origin")))
    {
        origin.Headers.Add("Origin", "https://untrusted.invalid");
        using var rejected = await http.SendAsync(origin); Require((int)rejected.StatusCode == 400, "browser origin rejected");
    }
    using (var query = Request("player-a-ticket", Body("query")))
    {
        query.RequestUri = new Uri("/v1/session?Side=1", UriKind.Relative);
        using var rejected = await http.SendAsync(query); Require((int)rejected.StatusCode == 400, "query rejected");
    }
    var first = await Issue(http, "player-a-ticket", "first");
    var other = await Issue(http, "player-b-ticket", "first");
    Require(first.GetProperty("Side").GetInt32() == 0 && other.GetProperty("Side").GetInt32() == 1, "verified accounts receive assigned seats");
    var firstToken = first.GetProperty("AccessToken").GetString()!;
    var state = admission.UseAccess(firstToken, a => runtime.Capture(a.Caller));
    var command = new CommandEnvelope("http-match", state.Round.ToString(), version, "choose-before-disconnect", 1, "Choose",
        JsonSerializer.Serialize(new { Token = state.ChoiceToken, OfferIndex = 0 }));
    var receipt = admission.UseAccess(firstToken, a => runtime.Execute(a.Caller, command));
    Require(receipt.Accepted, "authenticated command reaches actual core");
    var retry = await Issue(http, "player-a-ticket", "first");
    Require(retry.GetProperty("AccessToken").GetString() == firstToken, "lost HTTP reply retry retains access");
    var rotated = await Issue(http, "player-a-ticket", "reconnect");
    var rotatedToken = rotated.GetProperty("AccessToken").GetString()!;
    Require(rotated.GetProperty("StreamId").GetString() == first.GetProperty("StreamId").GetString(), "reconnect preserves command stream");
    try { admission.UseAccess(firstToken, a => runtime.Capture(a.Caller)); throw new InvalidOperationException("Old access survived"); }
    catch (AdmissionRejectedException) { checks++; }
    var replay = admission.UseAccess(rotatedToken, a => runtime.Execute(a.Caller, command));
    Require(replay == receipt && admission.UseAccess(rotatedToken, a => runtime.NextCommandSequence(a.Caller)) == 2, "lost command ACK not applied twice after reconnect");
    // Both players stop making HTTP requests; the server clock alone continues all rounds.
    var firstTime = clock.GetUtcNow();
    for (var step = 0; step < 61000; step++)
    {
        clock.Advance(TimeSpan.FromMilliseconds(10));
        runtime.Pump();
        if (step % 1000 != 0) continue;
        // Internal inspection only; expired player access is not bypassed to produce a public response.
        if (clock.GetUtcNow() - firstTime > TimeSpan.FromMinutes(10)) break;
    }
    var returned = await Issue(http, "player-a-ticket", "after-offline");
    var returnedToken = returned.GetProperty("AccessToken").GetString()!;
    Require(returned.GetProperty("StreamId").GetString() == first.GetProperty("StreamId").GetString(), "access expiry does not reset stream");
    var final = admission.UseAccess(returnedToken, a => runtime.Capture(a.Caller));
    Require(final.Phase == "MatchResult" && Math.Max(final.Wins0, final.Wins1) == 4 && final.Fault is null, "both offline server finishes match and returning player sees result");
    Require(admission.UseAccess(returnedToken, a => runtime.Execute(a.Caller, command)) == receipt, "receipt persists after offline result");
    Console.WriteLine($"Core result after both offline: rounds={final.Results.Count}, score={final.Wins0}:{final.Wins1}");
}
finally { await app.StopAsync(); }

await using (var strict = Build(admission, new AdmissionHttpSettings()))
{
    await strict.StartAsync();
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri(strict.Urls.Single()) };
        await Expect(http, "player-a-ticket", Body("strict"), 403, "production HTTP rejected");
    }
    finally { await strict.StopAsync(); }
}
await using (var limited = Build(admission, new AdmissionHttpSettings(1, 60, 1, 2, true)))
{
    await limited.StartAsync();
    try
    {
        using var http = new HttpClient { BaseAddress = new Uri(limited.Urls.Single()) };
        await Expect(http, null, "{}", 401, "first rate permit");
        await Expect(http, null, "{}", 429, "global bound includes rejected callers");
    }
    finally { await limited.StopAsync(); }
}
Console.WriteLine($"PASS admission HTTP/core checks: {checks}; real loopback HTTP and core, fake PlayFab responses, no cloud resources.");

string Body(string operation) => JsonSerializer.Serialize(new { ContentVersion = version, OperationId = operation });
void Require(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); checks++; }
HttpRequestMessage Request(string? ticket, string body)
{
    var request = new HttpRequestMessage(HttpMethod.Post, "/v1/session") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    if (ticket is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ticket);
    return request;
}
async Task Expect(HttpClient http, string? ticket, string body, int status, string name)
{
    using var request = Request(ticket, body); using var response = await http.SendAsync(request);
    Require((int)response.StatusCode == status, name);
    Require(string.IsNullOrEmpty(await response.Content.ReadAsStringAsync()), "rejection contains no provider data");
}
async Task<JsonElement> Issue(HttpClient http, string ticket, string operation)
{
    using var request = Request(ticket, Body(operation)); using var response = await http.SendAsync(request);
    Require(response.StatusCode == HttpStatusCode.OK, "session exchange status");
    Require(response.Headers.CacheControl?.NoStore == true, "access never cacheable");
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); return json.RootElement.Clone();
}
WebApplication Build(MatchAdmissionService service, AdmissionHttpSettings settings)
{
    var builder = WebApplication.CreateSlimBuilder(); builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(options => { options.Listen(IPAddress.Loopback, 0); options.Limits.MaxRequestBodySize = 4096; options.Limits.MaxRequestHeadersTotalSize = 8192; });
    var web = builder.Build(); new AdmissionHttpEndpoint(service, settings).Map(web); return web;
}

sealed class TestClock : TimeProvider
{
    private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => _start.AddTicks(_ticks);
    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}
