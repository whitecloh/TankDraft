using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using TankDraft.Server.PlayFab.Identity;
using TankDraft.Server.Admission;

// Operator-only verification. No listener, no live economy, no credentials in output.
try
{
    if (args.Length != 1 || args[0] is not "--login-existing" and not "--provision-two-test-players")
        throw new InvalidOperationException("Explicit existing-login or two-test-player provision mode required.");
    var provision = args[0] == "--provision-two-test-players";
    var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-secrets", "TankDraft");
    var keyPath = Path.Combine(directory, "playfab-server-key.txt");
    var identitiesPath = Path.Combine(directory, "playfab-test-identities.json");
    var keyInfo = new FileInfo(keyPath);
    if (!keyInfo.Exists) throw new FileNotFoundException("Private PlayFab key file is missing.");
    if (keyInfo.Length > 2048) throw new InvalidDataException("Private PlayFab key file is too large.");
    var key = (await File.ReadAllTextAsync(keyPath)).Trim().TrimStart('\uFEFF').Trim();
    if (!Regex.IsMatch(key, "^[\\x21-\\x7E]{8,512}$")) throw new InvalidDataException("Invalid private key format.");
    if (!File.Exists(identitiesPath))
    {
        if (!provision) throw new InvalidOperationException("Provision the two stable test identities first.");
        // Persist BEFORE sending: a lost provider reply must not create additional identities on retry.
        await using var file = new FileStream(identitiesPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await JsonSerializer.SerializeAsync(file, new[] { NewIdentity(), NewIdentity() });
        file.Flush(flushToDisk: true);
    }
    var identities = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(identitiesPath));
    if (identities is null || identities.Length != 2 || identities.Distinct().Count() != 2 ||
        identities.Any(id => !Regex.IsMatch(id, "^tankdraft-r1-[a-f0-9]{64}$")))
        throw new InvalidDataException("Private test identities invalid; no provider calls made.");
    using var http = new HttpClient(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(5)
    }) { Timeout = Timeout.InfiniteTimeSpan };
    using var verifier = new PlayFabIdentityAdapter(new PlayFabIdentityOptions("B16D9", key, 2));
    var accounts = new HashSet<string>(StringComparer.Ordinal);
    var authenticated = new List<(string Account, string Ticket)>();
    var calls = 0;
    foreach (var customId in identities)
    {
        if (provision)
        {
            using var created = await Call("Server/LoginWithCustomID", new { CustomId = customId, CreateAccount = true }, key);
            calls++;
            // Do not print/store provider payload: it contains the session and entity tickets.
        }
        using var login = await Call("Client/LoginWithCustomID", new { TitleId = "B16D9", CustomId = customId, CreateAccount = false }, null);
        calls++;
        var data = login.RootElement.GetProperty("data");
        var account = data.GetProperty("PlayFabId").GetString();
        var ticket = data.GetProperty("SessionTicket").GetString();
        if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(ticket)) throw new InvalidDataException("Incomplete PlayFab login result.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var verified = await verifier.VerifyAsync(ticket, deadline.Token);
        calls++;
        if (verified is null || verified.AccountId != account || verified.ValidUntil <= DateTimeOffset.UtcNow || !accounts.Add(account))
            throw new InvalidDataException("PlayFab identity verification did not match distinct test players.");
        authenticated.Add((account, ticket));
    }
    // Fixed operator-owned assignment exercises admission without exposing a public game endpoint.
    var admission = new MatchAdmissionService(verifier,
        [new("live-identity-probe", "identity-probe-v1", authenticated[0].Account, authenticated[1].Account, DateTimeOffset.UtcNow.AddMinutes(5))],
        new AdmissionSettings(2, TimeSpan.FromSeconds(120), 90), TimeProvider.System);
    var first = await admission.ExchangeAsync(authenticated[0].Ticket, "identity-probe-v1", "first", CancellationToken.None); calls++;
    var second = await admission.ExchangeAsync(authenticated[1].Ticket, "identity-probe-v1", "first", CancellationToken.None); calls++;
    if (first.Side != 0 || second.Side != 1) throw new InvalidDataException("Verified seats differ from assignment.");
    var repeated = await admission.ExchangeAsync(authenticated[0].Ticket, "identity-probe-v1", "first", CancellationToken.None); calls++;
    if (first.AccessToken != repeated.AccessToken) throw new InvalidDataException("Lost response retry changed access.");
    var rotated = await admission.ExchangeAsync(authenticated[0].Ticket, "identity-probe-v1", "reconnect", CancellationToken.None); calls++;
    if (rotated.StreamId != first.StreamId || rotated.Generation != 2 ||
        admission.UseAccess(rotated.AccessToken, context => context.Caller.AccountId) != authenticated[0].Account)
        throw new InvalidDataException("Verified reconnect binding failed.");
    var rejected = false;
    try { admission.UseAccess(first.AccessToken, context => context.Caller.AccountId); }
    catch (AdmissionRejectedException) { rejected = true; }
    if (!rejected) throw new InvalidDataException("Old access survived rotation.");
    Console.WriteLine($"PASS live PlayFab B16D9: two distinct players, client login, server verification, assigned seats, lost reply retry and reconnect rotation; {calls} API calls. No tickets/account IDs written to logs. No remote PvP/storage acceptance.");

    async Task<JsonDocument> Call(string route, object body, string? secret)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://B16D9.playfabapi.com/" + route) { Content = JsonContent.Create(body) };
        if (secret is not null) request.Headers.Add("X-SecretKey", secret);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException("PlayFab operator login rejected (HTTP " + (int)response.StatusCode + "); no automatic retries.");
        if (response.Content.Headers.ContentLength > 262144) throw new InvalidDataException("Provider response too large.");
        using var bytes = new MemoryStream();
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        var buffer = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, deadline.Token);
            if (count == 0) break;
            if (bytes.Length + count > 262144) throw new InvalidDataException("Provider response too large.");
            bytes.Write(buffer, 0, count);
        }
        var json = JsonDocument.Parse(bytes.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        try
        {
            RejectDuplicates(json.RootElement);
            if (json.RootElement.GetProperty("code").GetInt32() != 200 || json.RootElement.TryGetProperty("error", out _))
                throw new InvalidDataException("Provider result rejected.");
            return json;
        }
        catch { json.Dispose(); throw; }
    }
}
catch (Exception error)
{
    // Exceptions may embed provider/user content: only allow bounded type names into diagnostic output.
    Console.Error.WriteLine("PlayFab live probe failed: " + error.GetType().Name + ". Check local setup and provider status; no automatic retry was sent.");
    Environment.ExitCode = 1;
}

static string NewIdentity() => "tankdraft-r1-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
static void RejectDuplicates(JsonElement element)
{
    if (element.ValueKind == JsonValueKind.Object)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException("Ambiguous provider JSON.");
            RejectDuplicates(property.Value);
        }
    }
    else if (element.ValueKind == JsonValueKind.Array) foreach (var child in element.EnumerateArray()) RejectDuplicates(child);
}
