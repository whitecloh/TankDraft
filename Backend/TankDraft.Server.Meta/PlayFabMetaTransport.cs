using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TankDraft.Server.Meta;

/// <summary>Server-only, bounded PlayFab calls. No SDK global credentials and no automatic write retries.</summary>
public sealed class PlayFabMetaTransport : IDisposable
{
    readonly HttpClient client;
    readonly string secret;
    readonly SemaphoreSlim tokenGate = new(1, 1);
    string? entityToken;
    DateTimeOffset tokenExpires;
    static readonly HashSet<string> Endpoints = ["Authentication/GetEntityToken", "Server/GetUserAccountInfo", "Server/GetUserInventory", "Server/AddUserVirtualCurrency", "Server/SubtractUserVirtualCurrency", "Server/GetCatalogItems", "Object/GetObjects", "Object/SetObjects", "Inventory/GetInventoryItems"];
    static readonly JsonSerializerOptions Json = new() { AllowDuplicateProperties = false, MaxDepth = 32 };

    public PlayFabMetaTransport(string titleId, string serverSecret, HttpMessageHandler? handler = null)
    {
        if (!Regex.IsMatch(titleId ?? "", "^[A-Za-z0-9]{5,32}$") || string.IsNullOrEmpty(serverSecret) || serverSecret.Length is < 8 or > 512 || serverSecret.Any(c => c < 33 || c > 126))
            throw new ArgumentException("Invalid server-only PlayFab configuration.");
        secret = serverSecret;
        client = handler is null
            ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(5) })
            : new HttpClient(handler, false);
        client.BaseAddress = new Uri($"https://{titleId}.playfabapi.com/");
        client.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<JsonElement> CallAsync(string endpoint, object body, CancellationToken ct)
    {
        if (!Endpoints.Contains(endpoint)) throw new ArgumentException("Unsupported meta operation.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            if (endpoint.StartsWith("Server/", StringComparison.Ordinal)) return await SendAsync(endpoint, body, "X-SecretKey", secret, timeout.Token);
            if (endpoint == "Authentication/GetEntityToken") throw new ArgumentException("Title authentication is internal.");
            var token = await GetTokenAsync(timeout.Token);
            return await SendAsync(endpoint, body, "X-EntityToken", token, timeout.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (MetaFailureException) { throw; }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or FormatException)
        { throw new MetaFailureException("provider_unavailable"); }
    }

    async Task<string> GetTokenAsync(CancellationToken ct)
    {
        await tokenGate.WaitAsync(ct);
        try
        {
            if (entityToken is not null && tokenExpires > DateTimeOffset.UtcNow.AddMinutes(1)) return entityToken;
            var data = await SendAsync("Authentication/GetEntityToken", new { }, "X-SecretKey", secret, ct);
            if (data.GetProperty("Entity").GetProperty("Type").GetString() != "title") throw new MetaFailureException("provider_unavailable");
            var value = data.GetProperty("EntityToken").GetString();
            var expires = data.GetProperty("TokenExpiration").GetDateTimeOffset();
            if (string.IsNullOrEmpty(value) || value.Length > 32768 || expires <= DateTimeOffset.UtcNow.AddMinutes(1)) throw new MetaFailureException("provider_unavailable");
            entityToken = value; tokenExpires = expires;
            return value;
        }
        finally { tokenGate.Release(); }
    }

    async Task<JsonElement> SendAsync(string endpoint, object body, string header, string credential, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add(header, credential);
        request.Content = JsonContent.Create(body, options: Json);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) != 0)
        {
            if (buffer.Length + read > 256 * 1024) throw new MetaFailureException("provider_unavailable");
            buffer.Write(chunk, 0, read);
        }
        // Deserialize rejects duplicate property names as well as excessively deep JSON.
        var envelope = JsonSerializer.Deserialize<JsonElement>(buffer.ToArray(), Json);
        if (response.StatusCode != HttpStatusCode.OK || !envelope.TryGetProperty("code", out var code) || code.GetInt32() != 200 || envelope.TryGetProperty("error", out _))
        {
            var error = envelope.TryGetProperty("error", out var value) ? value.GetString() : null;
            throw new MetaFailureException(error is "EntityProfileVersionMismatch" or "ConcurrentEditError" ? "version_conflict" : "provider_unavailable");
        }
        return envelope.GetProperty("data").Clone();
    }

    public void Dispose() { entityToken = null; client.Dispose(); }
}
