using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace TankDraft.ServerManager;

public static class ManagerWebHost
{
    public static WebApplication Create(ServerSupervisor supervisor, int port = 18878)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
        builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => { k.AddServerHeader = false; k.Listen(IPAddress.Loopback, port); k.Limits.MaxRequestBodySize = 1024; k.Limits.MaxConcurrentConnections = 16; });
        var app = builder.Build();
        var csrf = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        app.Use(async (ctx, next) =>
        {
            var host = ctx.Request.Host;
            if (host.Host != "127.0.0.1" || host.Port != ctx.Connection.LocalPort ||
                ctx.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip)) { ctx.Response.StatusCode = 403; return; }
            var origin = ctx.Request.Headers.Origin;
            if (origin.Count > 0 && origin.ToString() != "http://" + host || ctx.Request.Headers["Sec-Fetch-Site"] == "cross-site")
            { ctx.Response.StatusCode = 403; return; }
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; object-src 'none'";
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                var supplied = ctx.Request.Headers["X-TankDraft-Control"].ToString();
                if (supplied.Length != csrf.Length || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(supplied), Encoding.ASCII.GetBytes(csrf)))
                { ctx.Response.StatusCode = 403; return; }
            }
            await next(ctx);
        });
        app.MapGet("/", () => Results.Content(Read("index.html").Replace("CONTROL_TOKEN", csrf, StringComparison.Ordinal), "text/html; charset=utf-8"));
        app.MapGet("/app.js", () => Results.Content(Read("app.js"), "text/javascript; charset=utf-8"));
        app.MapGet("/app.css", () => Results.Content(Read("app.css"), "text/css; charset=utf-8"));
        app.MapGet("/api/status", supervisor.StatusAsync);
        app.MapPost("/api/start", () => Run(() => supervisor.StartAsync(false, CancellationToken.None)));
        app.MapPost("/api/check-local", () => Run(() => supervisor.StartAsync(true, CancellationToken.None)));
        app.MapPost("/api/drain", () => Run(() => supervisor.StopAsync(false)));
        app.MapPost("/api/stop", () => Run(() => supervisor.StopAsync(true)));
        app.MapFallback(() => Results.NotFound());
        return app;
    }
    static async Task<IResult> Run(Func<Task> action)
    {
        try { await action(); return Results.Ok(new { Ok = true }); }
        catch (InvalidOperationException e) when (e.Message is "already_running" or "gateway_build_missing" or "fusion_auth_not_configured")
        { return Results.Json(new { Error = e.Message }, statusCode: 409); }
        catch { return Results.Json(new { Error = "operation_failed" }, statusCode: 503); }
    }
    static string Read(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "wwwroot", name));
}
