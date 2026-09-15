using System.Text.RegularExpressions;
using System.Net.Http;
using PlayFab;
using PlayFab.ServerModels;

namespace TankDraft.Server.PlayFab.Identity;

public sealed class PlayFabIdentityOptions
{
    public PlayFabIdentityOptions(string titleId, string developerSecretKey, int maximumInflight = 16) => (TitleId, DeveloperSecretKey, MaximumInflight) = (titleId, developerSecretKey, maximumInflight);
    public string TitleId { get; }
    public string DeveloperSecretKey { get; }
    public int MaximumInflight { get; }
    public override string ToString() => $"PlayFabIdentityOptions {{ TitleId = {TitleId}, DeveloperSecretKey = [REDACTED], MaximumInflight = {MaximumInflight} }}";
}
public sealed record VerifiedPlayFabIdentity(string AccountId, DateTimeOffset ValidUntil);
public sealed class PlayFabIdentityBusyException : Exception { public PlayFabIdentityBusyException() : base("Identity verification is busy.") { } }

public sealed class PlayFabIdentityAdapter : IDisposable
{
    private static readonly Regex Title = new("^[A-Za-z0-9]{5,32}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Ticket = new("^[\\x21-\\x7E]{1,4096}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Secret = new("^[\\x21-\\x7E]{8,512}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Account = new("^[A-Za-z0-9]{5,32}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private readonly SemaphoreSlim _inflight;
    private readonly Func<AuthenticateSessionTicketRequest, Task<PlayFabResult<AuthenticateSessionTicketResult>>>? _authenticate;
    private readonly PlayFabIdentityHttpTransport? _transport;
    private readonly Func<DateTimeOffset> _utcNow;

    public PlayFabIdentityAdapter(PlayFabIdentityOptions options, Func<AuthenticateSessionTicketRequest, Task<PlayFabResult<AuthenticateSessionTicketResult>>>? authenticate = null, Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);
        _inflight = new SemaphoreSlim(options.MaximumInflight, options.MaximumInflight);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        if (authenticate is not null) { _authenticate = authenticate; return; }
        _transport = new PlayFabIdentityHttpTransport(options);
    }

    internal PlayFabIdentityAdapter(PlayFabIdentityOptions options, HttpMessageHandler handler, Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);
        Validate(options);
        _inflight = new SemaphoreSlim(options.MaximumInflight, options.MaximumInflight);
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _transport = new PlayFabIdentityHttpTransport(options, handler);
    }

    internal static void Validate(PlayFabIdentityOptions options)
    {
        if (!Title.IsMatch(options.TitleId ?? string.Empty) || !Secret.IsMatch(options.DeveloperSecretKey ?? string.Empty) || options.MaximumInflight is < 1 or > 128) throw new ArgumentException("Invalid server-only PlayFab identity configuration.", nameof(options));
    }

    public async Task<VerifiedPlayFabIdentity?> VerifyAsync(string sessionTicket, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(sessionTicket) || !Ticket.IsMatch(sessionTicket)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        if (!_inflight.Wait(0)) throw new PlayFabIdentityBusyException();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new AuthenticateSessionTicketRequest { SessionTicket = sessionTicket };
            var response = _transport is not null
                ? await _transport.AuthenticateAsync(request, cancellationToken).ConfigureAwait(false)
                : await _authenticate!(request).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var result = response?.Result;
            var accountId = result?.UserInfo?.PlayFabId;
            if (response?.Error is not null || result is null || result.IsSessionTicketExpired != false || !Account.IsMatch(accountId ?? string.Empty)) return null;
            return new VerifiedPlayFabIdentity(accountId!, _utcNow().AddMinutes(2));
        }
        finally { _inflight.Release(); }
    }

    public void Dispose() => _transport?.Dispose();
}
