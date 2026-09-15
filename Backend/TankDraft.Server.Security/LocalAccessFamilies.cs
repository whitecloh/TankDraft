using System.Security.Cryptography;
using System.Text;

namespace TankDraft.Server.Security;

/// <summary>Ephemeral local-only grant and access-token families. Plaintext credentials never leave process memory.</summary>
public sealed class LocalAccessFamilies
{
    private const int TokenBytes = 32;
    private readonly object _sync = new();
    private readonly Dictionary<string, Family> _grants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Family> _access = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly int _capacity;

    public LocalAccessFamilies(int capacity, TimeProvider? clock = null)
    {
        if (capacity is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity; _clock = clock ?? TimeProvider.System;
    }

    public LocalGrant Create(string accountId, string matchId, string side, TimeSpan grantTtl)
    {
        Validate(accountId); Validate(matchId); if (side is not "0" and not "1") throw new ArgumentException("Side must be 0 or 1.", nameof(side));
        if (grantTtl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(grantTtl));
        lock (_sync)
        {
            RemoveExpiredGrantEntries(_clock.GetUtcNow());
            if (_grants.Count >= _capacity) throw new InvalidOperationException("Local access family capacity reached.");
            var value = Token(); var family = new Family(accountId, matchId, side, value, _clock.GetUtcNow().Add(grantTtl));
            _grants.Add(HashKey(value), family);
            return new LocalGrant(value, family.ExpiresAt);
        }
    }

    public LocalAccessIssue? Issue(string? grant, TimeSpan accessTtl, TimeSpan refreshMargin)
    {
        if (accessTtl <= TimeSpan.Zero || refreshMargin < TimeSpan.Zero || refreshMargin >= accessTtl) throw new ArgumentOutOfRangeException(nameof(accessTtl));
        var family = FindGrant(grant); if (family is null) return null;
        lock (family.Sync)
        {
            var now = _clock.GetUtcNow();
            if (family.Revoked || family.ExpiresAt <= now) return null;
            if (family.Current is { } current && current.ExpiresAt - now > refreshMargin) return ToIssue(family, current, accessTtl, refreshMargin);
            if (family.Generation >= 128) { family.Revoked = true; return null; }
            if (family.Current is { } old) lock (_sync) _access.Remove(HashKey(old.Token));
            var session = new Access(Token(), Guid.NewGuid().ToString("N"), Min(now.Add(accessTtl), family.ExpiresAt));
            family.Current = session; family.Generation++;
            lock (_sync) _access.Add(HashKey(session.Token), family);
            return ToIssue(family, session, accessTtl, refreshMargin);
        }
    }

    public bool Revoke(LocalGrant grant) => RevokeGrant(grant.Value);

    /// <summary>Invalidates only the current physical access session; the grant and its stable command stream survive.</summary>
    public bool InvalidateAccess(string? grant)
    {
        var family = FindGrant(grant); if (family is null) return false;
        lock (family.Sync)
        {
            if (family.Revoked || family.Current is null) return false;
            lock (_sync) _access.Remove(HashKey(family.Current.Token));
            family.Current = null;
            return true;
        }
    }
    public bool RevokeGrant(string? grant)
    {
        var family = FindGrant(grant); if (family is null) return false;
        lock (family.Sync)
        {
            family.Revoked = true;
            lock (_sync) { _grants.Remove(HashKey(family.Grant)); if (family.Current is { } a) _access.Remove(HashKey(a.Token)); }
            return true;
        }
    }

    public T UseAccess<T>(string? token, Func<LocalAccessContext, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var family = FindAccess(token) ?? throw new UnauthorizedAccessException();
        lock (family.Sync)
        {
            var now = _clock.GetUtcNow(); var current = family.Current;
            if (family.Revoked || family.ExpiresAt <= now || current is null || current.ExpiresAt <= now || !FixedEquals(current.Token, token!)) throw new UnauthorizedAccessException();
            var caller = new CallerIdentity(family.AccountId, current.SessionId, family.MatchId, family.Side, current.ExpiresAt).WithCommandStream(family.StreamId);
            return action(new LocalAccessContext(caller, current.SessionId, family.StreamId, family.Generation, current.ExpiresAt, family.MatchId, family.Side));
        }
    }

    private Family? FindGrant(string? grant) => Find(_grants, grant);
    private Family? FindAccess(string? token) => Find(_access, token);
    private Family? Find(Dictionary<string, Family> map, string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        lock (_sync) return map.TryGetValue(HashKey(token), out var f) ? f : null;
    }
    private LocalAccessIssue ToIssue(Family f, Access a, TimeSpan ttl, TimeSpan margin)
    {
        var remaining = a.ExpiresAt - _clock.GetUtcNow();
        return new(a.Token, a.SessionId, f.StreamId, f.Generation, Math.Max(0, (int)Math.Floor(remaining.TotalSeconds)),
            Math.Max(0, (int)Math.Ceiling((remaining - margin).TotalSeconds)), f.Side, f.MatchId);
    }
    // ExpiresAt is immutable; pruning both lookups needs no family lock and keeps lock ordering acyclic.
    private void RemoveExpiredGrantEntries(DateTimeOffset now)
    {
        foreach (var pair in _grants.Where(x => x.Value.ExpiresAt <= now).ToArray()) _grants.Remove(pair.Key);
        foreach (var pair in _access.Where(x => x.Value.ExpiresAt <= now).ToArray()) _access.Remove(pair.Key);
    }
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;
    private static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static string HashKey(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    private static void Validate(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 128) throw new ArgumentException("Identifier required."); }
    private sealed class Family(string accountId, string matchId, string side, string grant, DateTimeOffset expiresAt)
    { public object Sync { get; } = new(); public string AccountId { get; } = accountId; public string MatchId { get; } = matchId; public string Side { get; } = side; public string Grant { get; } = grant; public DateTimeOffset ExpiresAt { get; } = expiresAt; public string StreamId { get; } = Guid.NewGuid().ToString("N"); public int Generation { get; set; } public bool Revoked { get; set; } public Access? Current { get; set; } }
    private sealed record Access(string Token, string SessionId, DateTimeOffset ExpiresAt);
}

public sealed record LocalGrant(string Value, DateTimeOffset ExpiresAt);
public sealed record LocalAccessIssue(string AccessToken, string SessionId, string StreamId, int Generation, int ExpiresInSeconds, int RefreshAfterSeconds, string Side, string MatchId);
public sealed record LocalAccessContext(CallerIdentity Caller, string SessionId, string StreamId, int Generation, DateTimeOffset ExpiresAt, string MatchId, string Side);
