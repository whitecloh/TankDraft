using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using TankDraft.Server.Admission;
using TankDraft.Server.PlayFab.Identity;
using TankDraft.Server.Meta;

namespace TankDraft.RemoteHost;

public static class RemoteWebHost
{
    internal static WebApplication Create(RemoteMatchService service, bool loopbackVerification, X509Certificate2? certificate = null, EdgegapIngress? edgegapIngress = null, string? gatewayKey = null, bool allowPhotonQa = false)
    {
        if (allowPhotonQa && (gatewayKey is null || !loopbackVerification)) throw new ArgumentException("Photon QA identity requires private gateway ingress.");
        if (gatewayKey is not null && (!loopbackVerification || gatewayKey.Length != 64)) throw new ArgumentException("Gateway ingress is private loopback only.");
        if (!loopbackVerification && certificate is null && edgegapIngress is null) throw new InvalidOperationException("Public ingress requires native TLS.");
        if (loopbackVerification && edgegapIngress is not null) throw new InvalidOperationException("Managed ingress cannot use loopback verification.");
        if (certificate is not null && edgegapIngress is not null) throw new InvalidOperationException("Managed ingress does not use native TLS.");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false; k.Limits.MaxRequestBodySize = 4096;
            k.Limits.MaxConcurrentConnections = 64; k.Limits.MaxConcurrentUpgradedConnections = 40;
            k.Limits.MaxRequestHeadersTotalSize = 8192; k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
            k.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(15);
            if (loopbackVerification) k.Listen(IPAddress.Loopback, 0, x => x.Protocols = HttpProtocols.Http1);
            else if (edgegapIngress is not null) k.ListenAnyIP(8080, x => x.Protocols = HttpProtocols.Http1);
            else k.ListenAnyIP(8080, x => { x.Protocols = HttpProtocols.Http1; x.UseHttps(certificate!); });
        });
        builder.Services.AddHostedService(p => new PumpService(service, p.GetRequiredService<IHostApplicationLifetime>()));
        builder.Services.AddHostedService(p => new RewardService(service, p.GetRequiredService<IHostApplicationLifetime>()));
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ => RateLimitPartition.GetFixedWindowLimiter("remote", _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromSeconds(10), QueueLimit = 0 }));
        });
        var app = builder.Build();
        app.UseRateLimiter();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store"; context.Response.Headers.Pragma = "no-cache";
            if (gatewayKey is not null && !GatewayAuthorized(context, gatewayKey))
            { context.Response.StatusCode = 403; return; }
            if (gatewayKey is not null && context.Request.Path.StartsWithSegments("/v1"))
            {
                try
                {
                    var player = GatewayPlayer(context);
                    if (context.Request.Path == "/v1/socket")
                        service.UseAccess(RemoteSocketEndpoint.Bearer(context), (_, access) => access.Caller.AccountId == player ? 0 : throw new UnauthorizedAccessException());
                }
                catch { context.Response.StatusCode = 403; return; }
            }
            if (context.Request.Headers.ContainsKey("Origin") || context.Request.Headers.ContainsKey("Cookie") || context.Request.QueryString.HasValue)
            { context.Response.StatusCode = 400; return; }
            if (!context.Request.IsHttps && edgegapIngress is null && !(loopbackVerification && context.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote)))
            { context.Response.StatusCode = 403; return; }
            try { await next(context); }
            catch { if (!context.Response.HasStarted) context.Response.StatusCode = 503; else context.Abort(); }
        });
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
        app.MapGet("/healthz", () => Results.Json(new { Status = "ok" }, AuthoredContent.Json));
        app.MapGet("/readyz", () => Results.Json(new { service.InstanceId, service.ContentVersion, service.IsDraining, EconomyWritesEnabled = service.TestRewardsEnabled, service.MetaEnabled }, AuthoredContent.Json));
        Map("/v1/lobby", ["OperationId"], async (context, token, body, ct) => await service.LoginAsync(token, Text(body, "OperationId"), ct, GatewayPlayer(context)));
        Map("/v1/session", ["ContentVersion", "OperationId"], async (context, token, body, ct) => await service.ExchangeMatchAsync(token, Text(body, "ContentVersion"), Text(body, "OperationId"), ct, GatewayPlayer(context)));
        if (allowPhotonQa)
        {
            Map("/v1/fusion-qa/lobby", ["OperationId"], (context, _, body, _) => Task.FromResult<object>(service.LoginFromPhotonGateway(GatewayPlayer(context)!, Text(body, "OperationId"))));
            Map("/v1/fusion-qa/session", ["ContentVersion", "OperationId"], (context, _, body, ct) => Task.FromResult<object>(service.ExchangeFromPhotonGateway(GatewayPlayer(context)!, Text(body, "ContentVersion"), Text(body, "OperationId"), ct)));
        }
        Map("/v1/queue/join", ["InstanceId", "OperationId", "ContentVersion"], async (context, token, body, ct) =>
        {
            RequireInstance(context, token, body);
            return await service.JoinAsync(token, Text(body, "OperationId"), Text(body, "ContentVersion"), ct);
        });
        Map("/v1/profile/get", ["InstanceId", "ContentVersion"], async (context, token, body, ct) =>
        {
            RequireInstance(context, token, body);
            return await service.GetProfileAsync(token, Text(body, "ContentVersion"), ct);
        });
        Map("/v1/progression/get", ["InstanceId", "ContentVersion"], async (context, token, body, ct) =>
        {
            RequireInstance(context, token, body);
            return await service.GetProgressionAsync(token, Text(body, "ContentVersion"), ct);
        });
        Map("/v1/progression/execute", ["InstanceId", "ContentVersion", "Kind", "TargetId", "OperationId", "ExpectedSequence"], async (context, token, body, ct) =>
        {
            RequireInstance(context, token, body);
            if (!body.GetProperty("ExpectedSequence").TryGetInt64(out var sequence) || sequence < 0) throw new InvalidDataException();
            return await service.ExecuteProgressionAsync(token, Text(body, "ContentVersion"), Text(body, "Kind"), Text(body, "TargetId"), Text(body, "OperationId"), sequence, ct);
        }, false);
        Map("/v1/profile/loadout", ["InstanceId", "ContentVersion", "ExpectedProfileVersion", "OperationId", "UnitIds", "OrderIds"], async (context, token, body, ct) =>
        {
            RequireInstance(context, token, body);
            if (!body.GetProperty("ExpectedProfileVersion").TryGetInt32(out var version) || version < 0) throw new InvalidDataException();
            return await service.SaveProfileAsync(token, Text(body, "ContentVersion"), version, Text(body, "OperationId"), Ids(body, "UnitIds", 4), Ids(body, "OrderIds", 3), ct);
        }, false);
        MapQueue("status", ["InstanceId"], (token, _) => service.Status(token));
        MapQueue("cancel", ["InstanceId", "TicketId"], (token, body) => service.Cancel(token, Text(body, "TicketId")));
        MapQueue("leave", ["InstanceId", "MatchId"], (token, body) => service.Leave(token, Text(body, "MatchId")));
        new RemoteSocketEndpoint(service).Map(app);
        app.MapFallback(() => Results.StatusCode(404));
        return app;

        void MapQueue(string operation, string[] fields, Func<string, JsonElement, object> run) =>
            Map("/v1/queue/" + operation, fields, (context, token, body, ct) =>
            {
                service.RequireLobbyAccount(token, GatewayPlayer(context));
                _ = service.Status(token);
                if (Text(body, "InstanceId") != service.InstanceId) throw new InstanceExpiredException();
                return Task.FromResult(run(token, body));
            });
        void RequireInstance(HttpContext context, string token, JsonElement body)
        {
            service.RequireLobbyAccount(token, GatewayPlayer(context));
            _ = service.Status(token);
            if (Text(body, "InstanceId") != service.InstanceId) throw new InstanceExpiredException();
        }
        static string[] Ids(JsonElement body, string name, int length)
        {
            var array = body.GetProperty(name);
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() != length) throw new InvalidDataException();
            return array.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String && x.GetString() is { Length: <= 128 } id ? id : throw new InvalidDataException()).ToArray();
        }
        void Map(string path, string[] fields, Func<HttpContext, string, JsonElement, CancellationToken, Task<object>> run, bool stringFields = true) =>
            app.MapPost(path, (Func<HttpContext, Task<IResult>>)(async context =>
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted); timeout.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    var bearer = RemoteSocketEndpoint.Bearer(context);
                    if (context.Request.ContentType?.Split(';')[0].Trim() != "application/json") return Results.StatusCode(415);
                    var root = await ReadBody(context, timeout.Token);
                    if (!RemoteSocketEndpoint.Only(root, fields) || stringFields && root.EnumerateObject().Any(p => p.Value.ValueKind != JsonValueKind.String || p.Value.GetString() is not { Length: > 0 and <= 128 })) return Results.StatusCode(400);
                    var result = await run(context, bearer, root, timeout.Token); timeout.Token.ThrowIfCancellationRequested();
                    return Results.Json(result, AuthoredContent.Json);
                }
                catch (InstanceExpiredException) { return Results.Json(new { Code = "InstanceExpired", service.InstanceId }, AuthoredContent.Json, statusCode: 409); }
                catch (AdmissionRejectedException error) { return Results.StatusCode(error.Code is "identity_busy" or "identity_unavailable" or "capacity" ? 503 : 403); }
                catch (UnauthorizedAccessException) { return Results.StatusCode(403); }
                catch (PlayFabIdentityBusyException) { return Results.StatusCode(503); }
                catch (MetaFailureException error) { return Results.Json(new { Code = error.Code }, AuthoredContent.Json, statusCode: error.Code is "version_conflict" or "stale_progression_sequence" or "inventory_changed" or "match_active" ? 409 : error.Code is "provider_unavailable" or "busy" or "meta_capacity" ? 503 : 403); }
                catch (JsonException) { return Results.StatusCode(400); }
                catch (ArgumentException) { return Results.StatusCode(400); }
                catch (InvalidDataException) { return Results.StatusCode(400); }
                catch { return Results.StatusCode(503); }
            }));
        string? GatewayPlayer(HttpContext context)
        {
            if (gatewayKey is null) return null;
            var header = context.Request.Headers["X-TankDraft-Player"];
            if (header.Count != 1 || header[0] is not { Length: >= 5 and <= 32 } player || player.Any(c => !char.IsAsciiLetterOrDigit(c))) throw new UnauthorizedAccessException();
            return player;
        }
    }
    static bool GatewayAuthorized(HttpContext context, string expected)
    {
        var header = context.Request.Headers["X-TankDraft-Gateway"];
        return header.Count == 1 && header[0] is { Length: 64 } actual &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(actual), System.Text.Encoding.UTF8.GetBytes(expected));
    }
    static string Text(JsonElement body, string name) => body.GetProperty(name).GetString()!;
    static async Task<JsonElement> ReadBody(HttpContext context, CancellationToken token)
    {
        if (context.Request.ContentLength > 4096) throw new InvalidDataException();
        using var bytes = new MemoryStream(); var buffer = new byte[1024]; int count;
        while ((count = await context.Request.Body.ReadAsync(buffer, token)) != 0)
        { if (bytes.Length + count > 4096) throw new InvalidDataException(); bytes.Write(buffer, 0, count); }
        return RemoteSocketEndpoint.Parse(bytes.ToArray());
    }
    sealed class InstanceExpiredException : Exception;
    sealed class RewardService(RemoteMatchService service, IHostApplicationLifetime lifetime) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try { while (await timer.WaitForNextTickAsync(token)) await service.DispatchRewardsAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch { lifetime.StopApplication(); } // A broken durable store must not silently lose settlement work.
        }
    }
    sealed class PumpService(RemoteMatchService service, IHostApplicationLifetime lifetime) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            try { while (await timer.WaitForNextTickAsync(token)) { service.Pump(); if (service.IsExpired) { lifetime.StopApplication(); return; } } }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch { lifetime.StopApplication(); }
        }
    }
}
