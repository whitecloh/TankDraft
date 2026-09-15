using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayFab;
using PlayFab.ServerModels;

namespace TankDraft.Server.PlayFab.Identity;

public sealed class PlayFabIdentityUnavailableException : Exception
{
    public PlayFabIdentityUnavailableException() : base("Identity verification is temporarily unavailable.") { }
}

public sealed class PlayFabIdentityHttpTransport : IDisposable
{
    private const int MaximumResponseBytes = 256 * 1024;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly HttpClient _client;
    private readonly PlayFabApiSettings _settings;
    private readonly string _secret;
    private readonly Uri _endpoint;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private int _disposed;

    public PlayFabIdentityHttpTransport(PlayFabIdentityOptions options) : this(options, null) { }

    internal PlayFabIdentityHttpTransport(PlayFabIdentityOptions options, HttpMessageHandler? handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        PlayFabIdentityAdapter.Validate(options);
        _settings = new PlayFabApiSettings { TitleId = options.TitleId, DeveloperSecretKey = options.DeveloperSecretKey };
        _secret = _settings.DeveloperSecretKey;
        _endpoint = new Uri($"https://{_settings.TitleId}.playfabapi.com/Server/AuthenticateSessionTicket", UriKind.Absolute);
        _client = handler is null
            ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = ConnectTimeout }, true)
            : new HttpClient(handler, false);
        _client.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async Task<PlayFabResult<AuthenticateSessionTicketResult>> AuthenticateAsync(AuthenticateSessionTicketRequest request, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(request);
        using var deadline = new CancellationTokenSource(RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token, _disposeCancellation.Token);
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            message.Headers.Add("X-SecretKey", _secret);
            message.Content = new StringContent(JsonConvert.SerializeObject(new { SessionTicket = request.SessionTicket }), Encoding.UTF8, "application/json");
            using var response = await _client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
            if (response.StatusCode is >= HttpStatusCode.BadRequest and < (HttpStatusCode)500 && response.StatusCode != (HttpStatusCode)429)
                return new PlayFabResult<AuthenticateSessionTicketResult> { Error = new PlayFabError() };
            if (response.StatusCode != HttpStatusCode.OK)
                throw new PlayFabIdentityUnavailableException();

            var body = await ReadBodyAsync(response.Content, linked.Token).ConfigureAwait(false);
            using var reader = new StringReader(body);
            using var json = new JsonTextReader(reader) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
            var envelope = JObject.Load(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Ignore, LineInfoHandling = LineInfoHandling.Ignore });
            if (json.Read()) throw new PlayFabIdentityUnavailableException();
            RejectCaseInsensitiveDuplicates(envelope);
            if (envelope["code"]?.Type != JTokenType.Integer || envelope.Value<int>("code") != 200 || envelope["status"]?.Type != JTokenType.String || !string.Equals(envelope.Value<string>("status"), "OK", StringComparison.Ordinal) || envelope["error"] is not null || envelope["data"] is not JObject data || !HasValidIdentityTypes(data)) throw new PlayFabIdentityUnavailableException();
            var result = data.ToObject<AuthenticateSessionTicketResult>(JsonSerializer.CreateDefault(new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore }));
            if (result is null) throw new PlayFabIdentityUnavailableException();
            return new PlayFabResult<AuthenticateSessionTicketResult> { Result = result };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PlayFabIdentityUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw new PlayFabIdentityUnavailableException();
        }
        catch (Exception)
        {
            throw new PlayFabIdentityUnavailableException();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _disposeCancellation.Cancel();
        _client.Dispose();
        _disposeCancellation.Dispose();
    }

    private sealed class PlayFabEnvelope { [JsonProperty("data")] public AuthenticateSessionTicketResult? Data { get; set; } }

    private static async Task<string> ReadBodyAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (buffer.Length + read > MaximumResponseBytes) throw new InvalidDataException();
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        return new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
    }

    private static bool HasValidIdentityTypes(JObject data)
    {
        if (data["IsSessionTicketExpired"]?.Type != JTokenType.Boolean) return false;
        return data.Value<bool>("IsSessionTicketExpired") || data["UserInfo"] is JObject user && user["PlayFabId"]?.Type == JTokenType.String;
    }

    private static void RejectCaseInsensitiveDuplicates(JToken token)
    {
        if (token is JObject obj)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in obj.Properties())
            {
                if (!names.Add(property.Name)) throw new PlayFabIdentityUnavailableException();
                RejectCaseInsensitiveDuplicates(property.Value);
            }
        }
        else if (token is JArray array) foreach (var item in array) RejectCaseInsensitiveDuplicates(item);
    }

}
