using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TankDraft.Server.Admission;

namespace TankDraft.AdmissionHttp;

public sealed record AdmissionHttpSettings(int RequestsPerWindow = 20, int WindowSeconds = 10,
    int MaximumInflight = 4, int TimeoutSeconds = 10, bool AllowLoopbackHttpForVerification = false)
{
    public void Validate()
    {
        if (RequestsPerWindow is < 1 or > 120 || WindowSeconds is < 1 or > 60 ||
            MaximumInflight is < 1 or > 16 || TimeoutSeconds is < 1 or > 15)
            throw new ArgumentOutOfRangeException(nameof(RequestsPerWindow));
    }
}

/// <summary>Only the session exchange boundary. Host composition must independently configure TLS, durable assignment and socket authorization.</summary>
public sealed class AdmissionHttpEndpoint
{
    private readonly MatchAdmissionService _admission;
    private readonly AdmissionHttpSettings _settings;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _capacity;
    private readonly object _rateSync = new();
    private long _windowStart;
    private int _requests;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null };

    public AdmissionHttpEndpoint(MatchAdmissionService admission, AdmissionHttpSettings settings, TimeProvider? clock = null)
    {
        _admission = admission; settings.Validate(); _settings = settings; _clock = clock ?? TimeProvider.System;
        _capacity = new SemaphoreSlim(settings.MaximumInflight); _windowStart = _clock.GetTimestamp();
    }

    public void Map(IEndpointRouteBuilder app) => app.MapPost("/v1/session", (Func<HttpContext, Task<IResult>>)ExchangeAsync);

    private async Task<IResult> ExchangeAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        var request = context.Request;
        // Forwarded headers must only be set by trusted host middleware, never read directly from the request here.
        var loopback = context.Connection.RemoteIpAddress is { } remote && IPAddress.IsLoopback(remote)
            && context.Connection.LocalIpAddress is { } local && IPAddress.IsLoopback(local);
        if (!request.IsHttps && !(_settings.AllowLoopbackHttpForVerification && loopback)) return Status(403);
        if (request.Headers.ContainsKey("Origin") || request.Headers.ContainsKey("Cookie") || request.QueryString.HasValue) return Status(400);
        if (!TakeRatePermit()) return Status(429);
        if (!_capacity.Wait(0)) return Status(503);
        try
        {
            if (request.ContentType?.Split(';')[0].Trim().Equals("application/json", StringComparison.OrdinalIgnoreCase) != true) return Status(415);
            if (request.ContentLength > 2048) return Status(413);
            var authorization = request.Headers.Authorization;
            if (authorization.Count != 1 || authorization[0] is not { } header || !header.StartsWith("Bearer ", StringComparison.Ordinal)) return Status(401);
            var ticket = header[7..];
            if (ticket.Length is < 1 or > 4096 || ticket.Any(c => c < 0x21 || c > 0x7e)) return Status(401);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timeout.CancelAfter(TimeSpan.FromSeconds(_settings.TimeoutSeconds));
            using var bytes = new MemoryStream();
            var buffer = new byte[512];
            while (true)
            {
                var read = await request.Body.ReadAsync(buffer, timeout.Token);
                if (read == 0) break;
                if (bytes.Length + read > 2048) return Status(413);
                bytes.Write(buffer, 0, read);
            }
            using var document = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return Status(400);
            var fields = document.RootElement.EnumerateObject().ToArray();
            if (fields.Length != 2 || fields.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 2 ||
                fields.Any(p => p.Name is not "ContentVersion" and not "OperationId" || p.Value.ValueKind != JsonValueKind.String)) return Status(400);
            var content = document.RootElement.GetProperty("ContentVersion").GetString()!;
            var operation = document.RootElement.GetProperty("OperationId").GetString()!;
            var issue = await _admission.ExchangeAsync(ticket, content, operation, timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            return Results.Json(issue, Json);
        }
        catch (AdmissionRejectedException error) { return Status(error.Code is "identity_busy" or "identity_unavailable" or "capacity" ? 503 : 403); }
        catch (ArgumentException) { return Status(400); }
        catch (JsonException) { return Status(400); }
        catch (OperationCanceledException) { return Status(context.RequestAborted.IsCancellationRequested ? 499 : 503); }
        catch { return Status(503); } // Never serialize SDK/provider exception messages or stack traces.
        finally { _capacity.Release(); }
    }

    private bool TakeRatePermit()
    {
        lock (_rateSync)
        {
            var now = _clock.GetTimestamp();
            if (_clock.GetElapsedTime(_windowStart, now) >= TimeSpan.FromSeconds(_settings.WindowSeconds)) { _windowStart = now; _requests = 0; }
            if (_requests >= _settings.RequestsPerWindow) return false;
            _requests++; return true;
        }
    }
    private static IResult Status(int code) => Results.StatusCode(code);
}
