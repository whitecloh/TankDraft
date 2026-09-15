using System.Security.Cryptography;
using System.Text;
using TankDraft.Server.PlayFab.Identity;
using TankDraft.Server.Security;

namespace TankDraft.Server.Admission;

/// <summary>Process-local PlayFab-gated match access. It is not a durable store or distributed lease.</summary>
public sealed class MatchAdmissionService
{
    private const int TokenBytes = 32;
    private const int MaximumGeneration = 128;
    private readonly PlayFabIdentityAdapter _identity;
    private readonly AdmissionSettings _settings;
    private readonly TimeProvider _clock;
    private readonly object _sync = new();
    private readonly Dictionary<string, MatchAssignment> _assignmentByAccount;
    private readonly Dictionary<string, Family> _families = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Family> _familyByTokenHash = new(StringComparer.Ordinal);

    public MatchAdmissionService(PlayFabIdentityAdapter identity, IReadOnlyList<MatchAssignment> trustedAssignments, AdmissionSettings settings, TimeProvider timeProvider)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        ArgumentNullException.ThrowIfNull(trustedAssignments);
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _clock = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _assignmentByAccount = CopyAndValidate(trustedAssignments, Math.Min(8192, checked(_settings.FamilyCapacity * 2)));
    }

    public async Task<MatchAccessIssue> ExchangeAsync(string sessionTicket, string contentVersion, string operationId, CancellationToken cancellationToken)
    {
        ValidateNetworkValue(contentVersion, nameof(contentVersion));
        ValidateNetworkValue(operationId, nameof(operationId));
        var verified = await VerifyAsync(sessionTicket, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (verified is null) throw Denied("unauthorized");
        return ExchangeVerified(verified, contentVersion, operationId, cancellationToken);
    }

    /// <summary>
    /// Issues or renews access from an identity verified by this process's <see cref="PlayFabIdentityAdapter"/>.
    /// This is a server-only trusted boundary: callers must pass only a fresh result of
    /// <see cref="PlayFabIdentityAdapter.VerifyAsync"/>, never a client/network DTO.
    /// </summary>
    public MatchAccessIssue ExchangeVerified(VerifiedPlayFabIdentity verified, string contentVersion, string operationId, CancellationToken cancellationToken)
    {
        ValidateNetworkValue(contentVersion, nameof(contentVersion));
        ValidateNetworkValue(operationId, nameof(operationId));
        cancellationToken.ThrowIfCancellationRequested();
        if (verified is null || string.IsNullOrWhiteSpace(verified.AccountId)
            || !_assignmentByAccount.TryGetValue(verified.AccountId, out var assignment)
            || !string.Equals(assignment.ContentVersion, contentVersion, StringComparison.Ordinal))
            throw Denied("unauthorized");

        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = _clock.GetUtcNow();
            if (assignment.AdmissionUntil <= now || verified.ValidUntil <= now) throw Denied("expired");
            PruneExpired(now);
            if (!_families.TryGetValue(verified.AccountId, out var family))
            {
                if (_families.Count >= _settings.FamilyCapacity) throw Denied("capacity");
                family = new Family(assignment, verified.AccountId);
                _families.Add(verified.AccountId, family);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (family.Current is { } same && string.Equals(family.CurrentOperationId, operationId, StringComparison.Ordinal))
            {
                if (same.ExpiresAt - now >= TimeSpan.FromSeconds(2)) return ToIssue(family, same, now);
                if (family.Generation >= MaximumGeneration) { RevokeCurrent(family); throw Denied("generation_exhausted"); }
                return Issue(family, assignment, verified, operationId, now, alreadySeen: true);
            }
            if (family.SeenOperations.Contains(operationId)) throw Denied("operation_replayed");
            if (family.Generation >= MaximumGeneration) { RevokeCurrent(family); throw Denied("generation_exhausted"); }
            if (family.SeenOperations.Count >= _settings.OperationHistoryCapacity) throw Denied("operation_capacity");
            return Issue(family, assignment, verified, operationId, now, alreadySeen: false);
        }
    }

    public T UseAccess<T>(string? accessToken, Func<AdmissionAccessContext, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!IsAccessTokenShape(accessToken)) throw Denied("unauthorized");
        var suppliedHash = Hash(accessToken!);
        lock (_sync)
        {
            if (!_familyByTokenHash.TryGetValue(HashKey(suppliedHash), out var family)) throw Denied("unauthorized");
            var now = _clock.GetUtcNow();
            var current = family.Current;
            if (current is null || current.ExpiresAt <= now || !CryptographicOperations.FixedTimeEquals(current.TokenHash, suppliedHash)) throw Denied("unauthorized");
            var caller = new CallerIdentity(family.AccountId, family.StreamId, family.Assignment.MatchId, family.Assignment.SideFor(family.AccountId), current.ExpiresAt);
            return action(new AdmissionAccessContext(caller, current.SessionId, family.StreamId, family.Generation, current.ExpiresAt, family.Assignment.MatchId, family.Assignment.SideFor(family.AccountId)));
        }
    }

    private async Task<VerifiedPlayFabIdentity?> VerifyAsync(string ticket, CancellationToken cancellationToken)
    {
        try { return await _identity.VerifyAsync(ticket, cancellationToken).ConfigureAwait(false); }
        catch (PlayFabIdentityBusyException) { throw Denied("identity_busy"); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { throw Denied("identity_unavailable"); }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        foreach (var pair in _families.Where(pair => pair.Value.Assignment.AdmissionUntil <= now).ToArray())
        {
            RevokeCurrent(pair.Value);
            _families.Remove(pair.Key);
        }
        foreach (var family in _families.Values.Where(family => family.Current?.ExpiresAt <= now)) RemoveAccessLookup(family);
    }

    private void RevokeCurrent(Family family)
    {
        RemoveAccessLookup(family);
        family.Current = null;
        family.CurrentOperationId = null;
    }

    private MatchAccessIssue Issue(Family family, MatchAssignment assignment, VerifiedPlayFabIdentity verified, string operationId, DateTimeOffset now, bool alreadySeen)
    {
        var expiresAt = Min(now.Add(_settings.AccessTtl), Min(assignment.AdmissionUntil, verified.ValidUntil));
        if ((int)Math.Floor((expiresAt - now).TotalSeconds) < 2) throw Denied("expired");
        RevokeCurrent(family);
        var rawToken = NewToken();
        var access = new Access(rawToken, Hash(rawToken), Guid.NewGuid().ToString("N"), expiresAt);
        family.Current = access;
        family.CurrentOperationId = operationId;
        family.Generation++;
        if (!alreadySeen) family.SeenOperations.Add(operationId);
        _familyByTokenHash.Add(HashKey(access.TokenHash), family);
        return ToIssue(family, access, now);
    }

    private void RemoveAccessLookup(Family family)
    {
        if (family.Current is { } current) _familyByTokenHash.Remove(HashKey(current.TokenHash));
    }

    private MatchAccessIssue ToIssue(Family family, Access access, DateTimeOffset now)
    {
        var expires = Math.Max(1, (int)Math.Floor((access.ExpiresAt - now).TotalSeconds));
        var refresh = Math.Max(1, Math.Min(_settings.RefreshAfterSeconds, expires - 1));
        return new MatchAccessIssue(access.Token, access.SessionId, family.StreamId, family.Assignment.MatchId,
            family.Assignment.SideForInt(family.AccountId), family.Generation, expires, refresh);
    }

    private static Dictionary<string, MatchAssignment> CopyAndValidate(IReadOnlyList<MatchAssignment> source, int maximumAssignments)
    {
        if (source.Count > maximumAssignments) throw new ArgumentOutOfRangeException(nameof(source));
        var copied = new Dictionary<string, MatchAssignment>(StringComparer.Ordinal);
        var matchIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var input in source)
        {
            ArgumentNullException.ThrowIfNull(input);
            var assignment = new MatchAssignment(input.MatchId, input.ContentVersion, input.Side0AccountId, input.Side1AccountId, input.AdmissionUntil);
            ValidateTrustedValue(assignment.MatchId, nameof(input.MatchId));
            ValidateTrustedValue(assignment.ContentVersion, nameof(input.ContentVersion));
            ValidateTrustedValue(assignment.Side0AccountId, nameof(input.Side0AccountId));
            ValidateTrustedValue(assignment.Side1AccountId, nameof(input.Side1AccountId));
            if (assignment.AdmissionUntil == default || string.Equals(assignment.Side0AccountId, assignment.Side1AccountId, StringComparison.Ordinal)) throw new ArgumentException("Trusted match assignment is invalid.", nameof(source));
            if (!matchIds.Add(assignment.MatchId)) throw new ArgumentException("A trusted match may have only one assignment.", nameof(source));
            if (!copied.TryAdd(assignment.Side0AccountId, assignment) || !copied.TryAdd(assignment.Side1AccountId, assignment)) throw new ArgumentException("An account may have only one trusted assignment.", nameof(source));
        }
        return copied;
    }

    private static AdmissionRejectedException Denied(string code) => new(code);
    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) => left <= right ? left : right;
    private static string NewToken() { var bytes = RandomNumberGenerator.GetBytes(TokenBytes); try { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); } finally { CryptographicOperations.ZeroMemory(bytes); } }
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private static string HashKey(byte[] hash) => Convert.ToHexString(hash);
    private static void ValidateNetworkValue(string? value, string name) { if (string.IsNullOrWhiteSpace(value) || value.Length > 128) throw Denied("invalid_request"); }
    private static bool IsAccessTokenShape(string? value) => value is { Length: 43 } && value.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');
    private static void ValidateTrustedValue(string? value, string name) { if (string.IsNullOrWhiteSpace(value) || value.Length > 128) throw new ArgumentException("Trusted assignment identifier is invalid.", name); }

    private sealed class Family(MatchAssignment assignment, string accountId)
    {
        public MatchAssignment Assignment { get; } = assignment;
        public string AccountId { get; } = accountId;
        public string StreamId { get; } = Guid.NewGuid().ToString("N");
        public int Generation { get; set; }
        public string? CurrentOperationId { get; set; }
        public Access? Current { get; set; }
        public HashSet<string> SeenOperations { get; } = new(StringComparer.Ordinal);
    }
    private sealed record Access(string Token, byte[] TokenHash, string SessionId, DateTimeOffset ExpiresAt);
}

public sealed record MatchAssignment(string MatchId, string ContentVersion, string Side0AccountId, string Side1AccountId, DateTimeOffset AdmissionUntil)
{
    internal string SideFor(string accountId) => accountId == Side0AccountId ? "0" : "1";
    internal int SideForInt(string accountId) => accountId == Side0AccountId ? 0 : 1;
}

public sealed class AdmissionSettings
{
    public AdmissionSettings(int familyCapacity, TimeSpan accessTtl, int refreshAfterSeconds, int operationHistoryCapacity = 16)
    {
        if (familyCapacity is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(familyCapacity));
        if (accessTtl < TimeSpan.FromSeconds(2) || accessTtl > TimeSpan.FromSeconds(120)) throw new ArgumentOutOfRangeException(nameof(accessTtl));
        if (refreshAfterSeconds < 1 || refreshAfterSeconds >= accessTtl.TotalSeconds) throw new ArgumentOutOfRangeException(nameof(refreshAfterSeconds));
        if (operationHistoryCapacity is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(operationHistoryCapacity));
        FamilyCapacity = familyCapacity; AccessTtl = accessTtl; RefreshAfterSeconds = refreshAfterSeconds; OperationHistoryCapacity = operationHistoryCapacity;
    }
    public int FamilyCapacity { get; }
    public TimeSpan AccessTtl { get; }
    public int RefreshAfterSeconds { get; }
    public int OperationHistoryCapacity { get; }
}

public sealed class AdmissionRejectedException(string code) : Exception("Match admission rejected.")
{
    public string Code { get; } = string.IsNullOrWhiteSpace(code) ? "rejected" : code;
}

public sealed record MatchAccessIssue(string AccessToken, string SessionId, string StreamId, string MatchId, int Side, int Generation, int ExpiresInSeconds, int RefreshAfterSeconds)
{
    public override string ToString() => $"MatchAccessIssue {{ SessionId = {SessionId}, StreamId = {StreamId}, MatchId = {MatchId}, Side = {Side}, Generation = {Generation}, ExpiresInSeconds = {ExpiresInSeconds}, RefreshAfterSeconds = {RefreshAfterSeconds}, AccessToken = [REDACTED] }}";
}

public sealed record AdmissionAccessContext(CallerIdentity Caller, string SessionId, string StreamId, int Generation, DateTimeOffset ExpiresAt, string MatchId, string Side);
