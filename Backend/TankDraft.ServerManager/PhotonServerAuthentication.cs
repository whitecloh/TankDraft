using System.Net.Http.Json;
using System.Text.Json;

namespace TankDraft.ServerManager;

internal static class PhotonServerAuthentication
{
    public static async Task RefreshAsync(string privateRoot, string outputPath, CancellationToken token)
    {
        var path = Path.Combine(privateRoot, "fusion-server-identity.txt");
        var file = new FileInfo(path);
        if (!file.Exists || file.Length > 256 || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("fusion_auth_not_configured");
        var id = (await File.ReadAllTextAsync(path, token)).Trim();
        const string prefix = "tankdraft-fusion-server-";
        if (id.Length != prefix.Length + 64 || !id.StartsWith(prefix, StringComparison.Ordinal) || id[prefix.Length..].Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(4) });
        using var login = await Call("Client/LoginWithCustomID", new { TitleId = "B16D9", CustomId = id, CreateAccount = false });
        var data = login.RootElement.GetProperty("data");
        using var photon = await Call("Client/GetPhotonAuthenticationToken", new { PhotonApplicationId = "92d5f593-3b30-4493-8168-ca40ae0e55b0" }, data.GetProperty("SessionTicket").GetString());
        var auth = photon.RootElement.GetProperty("data").GetProperty("PhotonCustomAuthenticationToken").GetString();
        if (auth is not { Length: > 0 and <= 4096 }) throw new InvalidDataException();
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(new { UserId = data.GetProperty("PlayFabId").GetString(), PhotonToken = auth }), timeout.Token);

        async Task<JsonDocument> Call(string route, object body, string? ticket = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://B16D9.playfabapi.com/" + route) { Content = JsonContent.Create(body) };
            if (ticket is not null) request.Headers.Add("X-Authorization", ticket);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("fusion_auth_not_configured");
            await response.Content.LoadIntoBufferAsync(262144, timeout.Token);
            var bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token);
            return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24, AllowDuplicateProperties = false });
        }
    }
}
