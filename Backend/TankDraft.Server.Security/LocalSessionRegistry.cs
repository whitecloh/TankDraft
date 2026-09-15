using System.Security.Cryptography;
using System.Text;

namespace TankDraft.Server.Security;

/// <summary>
/// In-memory credentials for local QA only. They are deliberately not a PlayFab replacement.
/// </summary>
public sealed class LocalSessionRegistry
{
    private const int TokenByteCount = 32;
    private readonly object _sync = new();
    private readonly Dictionary<string, SessionRecord> _sessionsByHash = new(StringComparer.Ordinal);
    private readonly int _activeSessionCapacity;
    private readonly TimeProvider _timeProvider;

    public LocalSessionRegistry(int activeSessionCapacity, TimeProvider? timeProvider = null)
    {
        if (activeSessionCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(activeSessionCapacity));
        }

        _activeSessionCapacity = activeSessionCapacity;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public LocalSessionIssue Create(string accountId, string matchId, string side, TimeSpan ttl)
    {
        ValidateIdentifier(accountId, nameof(accountId));
        ValidateIdentifier(matchId, nameof(matchId));
        ValidateIdentifier(side, nameof(side));
        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl));
        }

        lock (_sync)
        {
            RemoveExpiredSessions(_timeProvider.GetUtcNow());
            if (_sessionsByHash.Count >= _activeSessionCapacity)
            {
                throw new InvalidOperationException("Active local session capacity has been reached.");
            }

            var tokenBytes = RandomNumberGenerator.GetBytes(TokenByteCount);
            var token = Convert.ToBase64String(tokenBytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            CryptographicOperations.ZeroMemory(tokenBytes);

            var tokenHash = HashToken(token);
            var hashKey = Convert.ToHexString(tokenHash);
            var now = _timeProvider.GetUtcNow();
            var sessionId = Guid.NewGuid().ToString("N");
            var expiresAt = now.Add(ttl);
            _sessionsByHash.Add(hashKey, new SessionRecord(tokenHash, accountId, sessionId, matchId, side, expiresAt));

            return new LocalSessionIssue(sessionId, token, expiresAt);
        }
    }

    public CallerIdentity? Authenticate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tokenHash = HashToken(token);
        var hashKey = Convert.ToHexString(tokenHash);
        lock (_sync)
        {
            if (!_sessionsByHash.TryGetValue(hashKey, out var record)
                || !CryptographicOperations.FixedTimeEquals(record.TokenHash, tokenHash))
            {
                return null;
            }

            var now = _timeProvider.GetUtcNow();
            if (record.ExpiresAt <= now)
            {
                _sessionsByHash.Remove(hashKey);
                return null;
            }

            return new CallerIdentity(record.AccountId, record.SessionId, record.MatchId, record.Side, record.ExpiresAt);
        }
    }

    public bool Revoke(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        lock (_sync)
        {
            var found = _sessionsByHash.FirstOrDefault(pair => string.Equals(pair.Value.SessionId, sessionId, StringComparison.Ordinal));
            return !string.IsNullOrEmpty(found.Key) && _sessionsByHash.Remove(found.Key);
        }
    }

    private static byte[] HashToken(string token)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }

    private void RemoveExpiredSessions(DateTimeOffset now)
    {
        var expired = _sessionsByHash
            .Where(pair => pair.Value.ExpiresAt <= now)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in expired)
        {
            _sessionsByHash.Remove(key);
        }
    }

    private static void ValidateIdentifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            throw new ArgumentException("A non-empty identifier up to 128 characters is required.", parameterName);
        }
    }

    private sealed record SessionRecord(
        byte[] TokenHash,
        string AccountId,
        string SessionId,
        string MatchId,
        string Side,
        DateTimeOffset ExpiresAt);
}

public sealed record LocalSessionIssue(string SessionId, string Token, DateTimeOffset ExpiresAt);

public sealed record CallerIdentity
{
    internal CallerIdentity(string accountId, string sessionId, string matchId, string side, DateTimeOffset expiresAt)
    {
        AccountId = accountId;
        SessionId = sessionId;
        MatchId = matchId;
        Side = side;
        ExpiresAt = expiresAt;
    }

    public string AccountId { get; }

    public string SessionId { get; }

    public string MatchId { get; }

    public string Side { get; }

    public DateTimeOffset ExpiresAt { get; }

    internal CallerIdentity WithCommandStream(string streamId) => new(AccountId, streamId, MatchId, Side, ExpiresAt);
}
