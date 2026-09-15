using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

// Operator-owned test accounts only. No listener and no economy/profile writes.
var stage = "arguments";
try
{
    if (args.Length != 1 || args[0] is not "--check-addon" and not "--check-chat" and not "--prepare-qa" and not "--prepare-editor") throw new InvalidOperationException();
    var prepare = args[0] == "--prepare-qa";
    var editor = args[0] == "--prepare-editor";
    var appId = args[0] == "--check-chat" ? "23d5a647-4d6e-4edd-85ad-7e666c04e787" : "92d5f593-3b30-4493-8168-ca40ae0e55b0";
    var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-secrets", "TankDraft");
    stage = "private-input";
    var testers = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(Path.Combine(root, "playfab-test-identities.json")))!;
    if (testers is not { Length: 2 } || testers.Distinct().Count() != 2 || testers.Any(x => !x.StartsWith("tankdraft-r1-", StringComparison.Ordinal) || x.Length != 77)) throw new InvalidDataException();
    using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(5) }) { Timeout = TimeSpan.FromSeconds(12) };
    var accounts = new List<string>();
    for (var i = 0; i < (prepare ? 2 : 1); i++)
    {
        stage = "tester-login";
        using var login = await Call("Client/LoginWithCustomID", new { TitleId = "B16D9", CustomId = testers[i], CreateAccount = false });
        var data = login.RootElement.GetProperty("data");
        var id = data.GetProperty("PlayFabId").GetString()!;
        stage = "photon-addon";
        using var photon = await Call("Client/GetPhotonAuthenticationToken", new { PhotonApplicationId = appId }, data.GetProperty("SessionTicket").GetString());
        if (prepare || editor) await SaveAuth("fusion-client-" + i + "-auth.json", id, photon);
        accounts.Add(id);
    }
    if (editor) { Console.WriteLine("PASS existing Editor tester token refreshed. No server identity or settings changed."); return; }
    if (!prepare) { Console.WriteLine("PASS PlayFab issues an authentication token for the " + (args[0] == "--check-chat" ? "Chat" : "Fusion") + " App ID. No Photon connection started."); return; }
    stage = "server-identity";
    var serverIdentityPath = Path.Combine(root, "fusion-server-identity.txt");
    if (!File.Exists(serverIdentityPath))
    {
        // Persist once BEFORE provisioning so an uncertain response cannot create a new account on retry.
        using var file = new FileStream(serverIdentityPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(file);
        await writer.WriteAsync("tankdraft-fusion-server-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant());
    }
    var customId = (await File.ReadAllTextAsync(serverIdentityPath)).Trim();
    const string serverPrefix = "tankdraft-fusion-server-";
    if (!customId.StartsWith(serverPrefix, StringComparison.Ordinal) || customId.Length != serverPrefix.Length + 64) throw new InvalidDataException();
    var secret = (await File.ReadAllTextAsync(Path.Combine(root, "playfab-server-key.txt"))).Trim();
    using var provision = await Call("Server/LoginWithCustomID", new { CustomId = customId, CreateAccount = true }, secret: secret);
    using var serverLogin = await Call("Client/LoginWithCustomID", new { TitleId = "B16D9", CustomId = customId, CreateAccount = false });
    var serverData = serverLogin.RootElement.GetProperty("data");
    stage = "server-photon-token";
    using var serverToken = await Call("Client/GetPhotonAuthenticationToken", new { PhotonApplicationId = "92d5f593-3b30-4493-8168-ca40ae0e55b0" }, serverData.GetProperty("SessionTicket").GetString());
    await SaveAuth("fusion-server-auth.json", serverData.GetProperty("PlayFabId").GetString()!, serverToken);
    Console.WriteLine("PASS private Fusion tokens prepared for two existing testers and one dedicated QA server identity. No server keys in gateway/client files; no Photon session started.");

    async Task SaveAuth(string name, string userId, JsonDocument response)
    {
        var token = response.RootElement.GetProperty("data").GetProperty("PhotonCustomAuthenticationToken").GetString();
        if (string.IsNullOrEmpty(token) || token.Length > 4096) throw new InvalidDataException();
        await File.WriteAllTextAsync(Path.Combine(root, name), JsonSerializer.Serialize(new { UserId = userId, PhotonToken = token }));
    }
    async Task<JsonDocument> Call(string route, object body, string? ticket = null, string? secret = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://B16D9.playfabapi.com/" + route) { Content = JsonContent.Create(body) };
        if (ticket is not null) request.Headers.Add("X-Authorization", ticket);
        if (secret is not null) request.Headers.Add("X-SecretKey", secret);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await response.Content.LoadIntoBufferAsync(262144);
        using var stream = await response.Content.ReadAsStreamAsync();
        var json = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions { MaxDepth = 32, AllowDuplicateProperties = false });
        if (!response.IsSuccessStatusCode || json.RootElement.GetProperty("code").GetInt32() != 200)
        {
            var numeric = json.RootElement.TryGetProperty("errorCode", out var code) && code.TryGetInt32(out var number) ? number : 0;
            Console.Error.WriteLine("PROVIDER_REJECTED stage=" + stage + " http=" + (int)response.StatusCode + " errorCode=" + numeric);
            json.Dispose(); throw new InvalidOperationException();
        }
        return json;
    }
}
catch { Console.Error.WriteLine("FUSION_QA_SETUP_FAILED stage=" + stage + "; provider payload and credentials suppressed; no automatic retries."); Environment.ExitCode = 1; }
