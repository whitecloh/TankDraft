using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using TankDraft.LocalHost;
using TankDraft.Server.Security;

// Intentionally only local QA transports. No cloud provisioning, account keys or public auth route.
if (args.Length == 2 && (args[0] is "--serve-queue-windows-pair" or "--serve-queue-windows-pair-auto" or "--serve-queue-windows-solo" or "--serve-queue-windows-solo-auto"))
{
    await LocalTlsHost.ServeQueueUnityAsync(args[1], args[0].Contains("solo", StringComparison.Ordinal), args[0].EndsWith("-auto", StringComparison.Ordinal));
    return;
}
if (args.Length == 3 && (args[0] is "--serve-queue-android-pair" or "--serve-queue-android-pair-auto" or "--serve-queue-android-solo" or "--serve-queue-android-solo-auto"))
{
    await LocalTlsHost.ServeQueueAndroidAsync(args[1], args[2], args[0].Contains("solo", StringComparison.Ordinal), args[0].EndsWith("-auto", StringComparison.Ordinal));
    return;
}
if (args.Length == 3 && args[0] == "--serve-queue-android-solo-restart-auto")
{
    await LocalTlsHost.ServeQueueAndroidAsync(args[1], args[2], solo: true, automated: true, restartDuringSearch: true);
    return;
}
if (args.Length == 2 && (args[0] is "--serve-android" or "--serve-android-auto"))
{
    await LocalTlsHost.ServeAndroidAsync(args[1], args[0] == "--serve-android-auto");
    return;
}
if (args.Length == 1 && args[0] == "--verify-socket")
{
    await LocalSocketHost.VerifyAsync();
    return;
}
if (args.Length == 1 && args[0] == "--verify-tls-session")
{
    await LocalTlsHost.VerifyAsync();
    return;
}
if (args.Length == 1 && args[0] == "--verify-queue-http")
{
    await LocalTlsHost.VerifyQueueHttpAsync();
    return;
}
if (args.Length is 2 && (args[0] is "--serve-tls-unity" or "--serve-tls-unity-auto" or "--serve-tls-unity-faults"))
{
    await LocalTlsHost.ServeUnityAsync(args[1], args[0] != "--serve-tls-unity", args[0] == "--serve-tls-unity-faults");
    return;
}
if (args.Length is 2 && (args[0] is "--serve-unity" or "--serve-unity-auto"))
{
    await LocalSocketHost.ServeUnityAsync(args[1], args[0] == "--serve-unity-auto");
    return;
}
if (args.Length != 1 || args[0] != "--verify")
    throw new InvalidOperationException("Permitted modes: --serve-android <adb serial>, --serve-android-auto <adb serial>, --serve-queue-windows-pair[-auto] <absolute exe>, --serve-queue-windows-solo[-auto] <absolute exe>, --serve-queue-android-pair[-auto] <adb serial> <absolute exe>, --serve-queue-android-solo[-auto] <adb serial> <absolute exe>, --verify, --verify-socket, --verify-tls-session, --verify-queue-http, --serve-unity <absolute exe under Logs/BackendClient>, --serve-unity-auto <absolute exe under Logs/BackendClient>, --serve-tls-unity <absolute exe under Logs/BackendClient>, --serve-tls-unity-auto <absolute exe under Logs/BackendClient>, --serve-tls-unity-faults <absolute exe under Logs/BackendClient>.");
var settings = HostSettings.Load();
var (content, version) = AuthoredContent.Load();
var clock = new LocalVerificationClock();
var sessions = new LocalSessionRegistry(settings.MaxSessionCount, clock);
var ttl = TimeSpan.FromSeconds(settings.SessionTtlSeconds);
var player0 = sessions.Create("qa-player-0", LocalMatchEndpoint.MatchId, "0", ttl);
var player1 = sessions.Create("qa-player-1", LocalMatchEndpoint.MatchId, "1", ttl);
using var endpoint = new LocalMatchEndpoint(content, version, settings, clock);
var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
// Environment/CLI/appsettings cannot add listeners or override the local-only config above.
builder.Configuration.Sources.Clear();
builder.Logging.ClearProviders();
builder.Services.AddHostedService(_ => new MatchPumpService(endpoint, TimeSpan.FromMilliseconds(settings.SchedulerPollMilliseconds)));
var uri = new Uri(settings.ListenUrl);
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = settings.MaxRequestBodyBytes;
    options.Limits.MaxConcurrentConnections = settings.MaxConcurrentConnections;
    options.Limits.MaxRequestHeadersTotalSize = 8192;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
    options.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(10);
    options.Listen(IPAddress.Parse(uri.Host), uri.Port, listen => listen.Protocols = HttpProtocols.Http1);
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    // A single bounded partition, including unauthenticated traffic; no arbitrary account/IP cardinality.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetFixedWindowLimiter("local", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = settings.RequestsPerWindow, Window = TimeSpan.FromSeconds(settings.RateWindowSeconds),
            QueueLimit = 0, AutoReplenishment = true
        }));
});
await using var app = builder.Build();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    // This API has no browser/cookie auth. Refuse cross-origin requests, even from other local pages.
    if (context.Request.Headers.ContainsKey("Origin")) { context.Response.StatusCode = 403; return; }
    var authorization = context.Request.Headers.Authorization.ToString();
    var caller = authorization.StartsWith("Bearer ", StringComparison.Ordinal) && authorization.Length <= 128
        ? sessions.Authenticate(authorization[7..]) : null;
    if (caller is null) { context.Response.StatusCode = 401; return; }
    context.Items["caller"] = caller;
    try { await next(context); }
    catch (Microsoft.AspNetCore.Http.BadHttpRequestException error) { context.Response.StatusCode = error.StatusCode; }
    catch (JsonException) { context.Response.StatusCode = 400; }
    catch (UnauthorizedAccessException) { context.Response.StatusCode = 403; }
    catch (Microsoft.Data.Sqlite.SqliteException) { context.Response.StatusCode = 503; }
    catch (IOException) { context.Response.StatusCode = 503; }
    catch (InvalidOperationException) { context.Response.StatusCode = 503; }
});
app.MapGet("/v1/match", (HttpContext context) =>
{
    if (context.Request.Query.Keys.Any(key => key != "afterEventSequence")) return Results.BadRequest();
    long? cursor = null;
    if (context.Request.Query.TryGetValue("afterEventSequence", out var value))
    {
        if (!long.TryParse(value, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
            return Results.BadRequest();
        cursor = parsed;
    }
    return Results.Json(endpoint.Capture((CallerIdentity)context.Items["caller"]!, cursor), AuthoredContent.Json);
});
app.MapPost("/v1/commands", async (HttpContext context) =>
{
    if (!context.Request.HasJsonContentType()) return Results.StatusCode(415);
    var command = await JsonSerializer.DeserializeAsync<CommandEnvelope>(context.Request.Body,
        AuthoredContent.Json, context.RequestAborted);
    return command is null ? Results.BadRequest() :
        Results.Json(endpoint.Execute((CallerIdentity)context.Items["caller"]!, command), AuthoredContent.Json);
});
await app.StartAsync();
try
{
    await LocalVerification.RunAsync(settings, content, version, player0.Token, player1.Token, endpoint, clock, sessions);
}
finally
{
    sessions.Revoke(player0.SessionId);
    sessions.Revoke(player1.SessionId);
    await app.StopAsync();
}
