using System.Security.Cryptography;
using System.Text;
using TankDraft.Match.Domain;
using TankDraft.Server.Admission;
using TankDraft.Server.Match;
using TankDraft.Server.PlayFab.Identity;
using TankDraft.Server.Meta;

namespace TankDraft.RemoteHost;

/// <summary>Bounded server-owned assignments; optional persistent meta and completed-result settlement. Active battles remain process-local.</summary>
public sealed partial class RemoteMatchService : IDisposable
{
    const int HistoryCapacity = 128;
    readonly object sync = new();
    readonly AuthoredContent content;
    readonly RemotePolicy policy;
    readonly PlayFabIdentityAdapter identity;
    readonly HashSet<string> allowlist;
    readonly TimeProvider clock;
    readonly long started;
    readonly DateTimeOffset endsAt;
    readonly Dictionary<string, Lobby> lobbies = new(StringComparer.Ordinal);
    readonly Dictionary<string, AccountState> accounts = new(StringComparer.Ordinal);
    readonly Dictionary<string, Entry> queue = new(StringComparer.Ordinal);
    readonly Dictionary<string, Match> matches = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> accountMatches = new(StringComparer.Ordinal);
    int calls;
    bool draining, disposed;
    long lastPump;
    public string InstanceId { get; } = Guid.NewGuid().ToString("N");
    public string ContentVersion { get; }
    public bool IsDraining { get { lock (sync) return draining || Elapsed >= TimeSpan.FromMinutes(policy.LifetimeMinutes - policy.DrainBeforeStopMinutes); } }
    public bool IsExpired => Elapsed >= TimeSpan.FromMinutes(policy.LifetimeMinutes);
    TimeSpan Elapsed => clock.GetElapsedTime(started, clock.GetTimestamp());
    internal RemoteMatchService(AuthoredContent c, string version, PlayFabIdentityAdapter i, RemoteHostSettings settings, TimeProvider? time = null)
    {
        policy = settings.Policy; policy.Validate();
        content = c; ContentVersion = version; identity = i; clock = time ?? TimeProvider.System;
        allowlist = new HashSet<string>(settings.AllowlistedAccounts, StringComparer.Ordinal);
        if (allowlist.Count is < 1 or > 100 || allowlist.Any(a => a.Length is < 5 or > 32 || a.Any(ch => !char.IsAsciiLetterOrDigit(ch)))) throw new ArgumentException("Invalid tester allowlist.");
        started = clock.GetTimestamp(); endsAt = clock.GetUtcNow().AddMinutes(policy.LifetimeMinutes);
    }
    public async Task<LobbyIssue> LoginAsync(string ticket, string operationId, CancellationToken ct, string? expectedAccount = null)
    {
        CheckOperation(operationId); ReserveCalls(1);
        var verified = await identity.VerifyAsync(ticket, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (verified is null || !allowlist.Contains(verified.AccountId) || expectedAccount is not null && verified.AccountId != expectedAccount) throw new UnauthorizedAccessException();
        return IssueLobby(verified, operationId);
    }
    // Only the explicitly enabled private gateway route may assert this identity after Photon Custom Auth.
    internal LobbyIssue LoginFromPhotonGateway(string account, string operationId)
    {
        CheckOperation(operationId);
        if (!allowlist.Contains(account)) throw new UnauthorizedAccessException();
        return IssueLobby(new VerifiedPlayFabIdentity(account, clock.GetUtcNow().AddMinutes(2)), operationId);
    }
    LobbyIssue IssueLobby(VerifiedPlayFabIdentity verified, string operationId)
    {
        lock (sync)
        {
            EnsureOpen(); PruneLobbies();
            if (!accounts.TryGetValue(verified.AccountId, out var account)) accounts.Add(verified.AccountId, account = new AccountState());
            var old = lobbies.Values.FirstOrDefault(l => l.Account == verified.AccountId);
            if (old is not null && account.LoginOperation == operationId) return old.Issue(clock.GetUtcNow());
            if (account.LoginOperation != operationId && account.LoginHistory.Contains(operationId)) throw new UnauthorizedAccessException();
            if (!account.LoginHistory.Contains(operationId) && account.LoginHistory.Count >= HistoryCapacity) throw new InvalidOperationException("login_history_capacity");
            if (old is not null) lobbies.Remove(old.Key);
            var expiry = verified.ValidUntil < endsAt ? verified.ValidUntil : endsAt;
            if (expiry <= clock.GetUtcNow()) throw new UnauthorizedAccessException();
            var lobby = new Lobby(verified.AccountId, Token(), expiry);
            lobbies.Add(lobby.Key, lobby); account.LoginHistory.Add(operationId); account.LoginOperation = operationId;
            return lobby.Issue(clock.GetUtcNow());
        }
    }
    public QueueStatus Join(string lobbyToken, string operationId, string contentVersion)
    {
        if (meta is not null) throw new InvalidOperationException("server_profile_required");
        return JoinValidated(lobbyToken, operationId, contentVersion, null);
    }
    QueueStatus JoinValidated(string lobbyToken, string operationId, string contentVersion, PlayerProfile? profile, IReadOnlyDictionary<string, PersistentBattleBonus>? bonuses = null)
    {
        CheckOperation(operationId);
        lock (sync)
        {
            var account = UseLobby(lobbyToken); PumpLocked();
            if (contentVersion != ContentVersion) throw new UnauthorizedAccessException();
            if (IsDraining) throw new InvalidOperationException("draining");
            var state = accounts[account];
            if (accountMatches.ContainsKey(account)) return StatusFor(account);
            if (queue.TryGetValue(account, out var existing))
            {
                if (existing.OperationId == operationId) return StatusFor(account);
                if (state.JoinHistory.Contains(operationId)) return Idle();
                throw new InvalidOperationException("already_searching");
            }
            if (state.JoinHistory.Contains(operationId)) return Idle();
            if (state.JoinHistory.Count >= HistoryCapacity || queue.Count >= policy.MaxQueued) throw new InvalidOperationException("queue_capacity");
            state.JoinHistory.Add(operationId);
            queue.Add(account, new Entry(account, clock.GetTimestamp(), operationId, Guid.NewGuid().ToString("N"), profile, bonuses));
            Pair(); return StatusFor(account);
        }
    }
    public QueueStatus Status(string lobbyToken) { lock (sync) { var account = UseLobby(lobbyToken); PumpLocked(); return StatusFor(account); } }
    internal void RequireLobbyAccount(string lobbyToken, string? expectedAccount)
    { lock (sync) { if (expectedAccount is not null && UseLobby(lobbyToken) != expectedAccount) throw new UnauthorizedAccessException(); } }
    public QueueStatus Cancel(string lobbyToken, string ticketId)
    {
        CheckOperation(ticketId);
        lock (sync)
        {
            var account = UseLobby(lobbyToken); PumpLocked();
            if (queue.TryGetValue(account, out var entry) && entry.TicketId == ticketId) queue.Remove(account);
            return StatusFor(account);
        }
    }
    public QueueStatus Leave(string lobbyToken, string matchId)
    {
        CheckOperation(matchId);
        lock (sync)
        {
            var account = UseLobby(lobbyToken); PumpLocked();
            if (accountMatches.TryGetValue(account, out var current) && current == matchId)
            {
                var match = matches[current];
                if (match.Domain.Phase != MatchPhase.MatchResult) throw new InvalidOperationException("match_in_progress");
                accountMatches.Remove(account);
                if (!accountMatches.Values.Contains(current)) RemoveMatch(match);
            }
            return StatusFor(account);
        }
    }
    public async Task<MatchAccessIssue> ExchangeMatchAsync(string ticket, string contentVersion, string operationId, CancellationToken ct, string? expectedAccount = null)
    {
        CheckOperation(operationId);
        if (contentVersion != ContentVersion) throw new UnauthorizedAccessException();
        ReserveCalls(1);
        var verified = await identity.VerifyAsync(ticket, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (verified is null || !allowlist.Contains(verified.AccountId) || expectedAccount is not null && verified.AccountId != expectedAccount) throw new UnauthorizedAccessException();
        return ExchangeKnownIdentity(verified, contentVersion, operationId, ct);
    }
    internal MatchAccessIssue ExchangeFromPhotonGateway(string account, string contentVersion, string operationId, CancellationToken ct)
    {
        CheckOperation(operationId);
        if (!allowlist.Contains(account) || contentVersion != ContentVersion) throw new UnauthorizedAccessException();
        return ExchangeKnownIdentity(new VerifiedPlayFabIdentity(account, clock.GetUtcNow().AddMinutes(2)), contentVersion, operationId, ct);
    }
    MatchAccessIssue ExchangeKnownIdentity(VerifiedPlayFabIdentity verified, string contentVersion, string operationId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Match match;
        lock (sync)
        {
            EnsureOpen(); PumpLocked();
            if (!accountMatches.TryGetValue(verified.AccountId, out var id) || !matches.TryGetValue(id, out match!)) throw new UnauthorizedAccessException();
        }
        ct.ThrowIfCancellationRequested();
        var issue = match.Admission.ExchangeVerified(verified, contentVersion, operationId, ct);
        lock (sync)
        {
            EnsureOpen();
            if (!matches.TryGetValue(match.Id, out var current) || !ReferenceEquals(current, match) || !accountMatches.TryGetValue(verified.AccountId, out var assigned) || assigned != match.Id) throw new UnauthorizedAccessException();
        }
        return issue;
    }
    public T UseAccess<T>(string token, Func<Match, AdmissionAccessContext, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!TokenShape(token)) throw new UnauthorizedAccessException();
        lock (sync)
        {
            EnsureOpen();
            foreach (var match in matches.Values)
            {
                AdmissionAccessContext context;
                try { context = match.Admission.UseAccess(token, a => a); }
                catch (AdmissionRejectedException) { continue; }
                // Hold the admission lock through mutation; a concurrent credential rotation cannot race a command.
                return match.Admission.UseAccess(token, a =>
                {
                    if (settlementFault) throw new InvalidOperationException("settlement_storage_unavailable");
                    var response = action(match, a);
                    PersistCompletion(match);
                    return response;
                });
            }
            throw new UnauthorizedAccessException();
        }
    }
    public void BeginDrain() { lock (sync) { draining = true; queue.Clear(); } }
    public void Pump() { lock (sync) { if (!disposed) { PumpLocked(); lastPump = clock.GetTimestamp(); } } }
    public AuthorityMetrics CaptureMetrics()
    {
        lock (sync) return new(queue.Count, matches.Values.Count(m => m.Domain.Phase != MatchPhase.MatchResult),
            matches.Values.Count(m => m.Domain.Phase == MatchPhase.MatchResult), calls, IsDraining,
            lastPump == 0 ? null : clock.GetElapsedTime(lastPump, clock.GetTimestamp()).TotalMilliseconds);
    }
    void PumpLocked()
    {
        if (settlementFault) throw new InvalidOperationException("settlement_storage_unavailable");
        if (IsExpired) { BeginDrain(); return; }
        PruneLobbies();
        if (IsDraining) BeginDrain();
        foreach (var match in matches.Values.ToArray())
        {
            match.Runtime.Pump();
            if (match.Domain.Phase == MatchPhase.MatchResult)
            {
                PersistCompletion(match);
                match.CompletedAt ??= clock.GetTimestamp();
                if (clock.GetElapsedTime(match.CompletedAt.Value, clock.GetTimestamp()) >= TimeSpan.FromSeconds(policy.CompletedRetentionSeconds)) RemoveMatch(match);
            }
        }
        if (IsDraining) return;
        foreach (var entry in queue.Values.Where(e => Waited(e) >= TimeSpan.FromSeconds(policy.QueueTimeoutSeconds)).ToArray())
        {
            if (matches.Count >= policy.MaxActiveMatches) break;
            Create(entry.Account, "bot-" + Guid.NewGuid().ToString("N"), true); queue.Remove(entry.Account);
        }
        Pair();
    }
    void Pair()
    {
        while (queue.Count >= 2 && matches.Count < policy.MaxActiveMatches)
        {
            var pair = queue.Values.Take(2).ToArray();
            Create(pair[0].Account, pair[1].Account, false);
            queue.Remove(pair[0].Account); queue.Remove(pair[1].Account);
        }
    }
    void Create(string a, string b, bool bot)
    {
        if (matches.Count >= policy.MaxActiveMatches) throw new InvalidOperationException("match_capacity");
        var id = InstanceId + "-" + Guid.NewGuid().ToString("N");
        var admission = new MatchAdmissionService(identity, [new MatchAssignment(id, ContentVersion, a, b, endsAt)], new AdmissionSettings(2, TimeSpan.FromSeconds(60), 30, 128), clock);
        var seed = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        var aProfile = queue[a].Profile;
        var bProfile = bot ? null : queue[b].Profile;
        var domain = aProfile is null ? content.CreateMatch(seed) : new MatchService(content.MatchRules, content.MatchUnits,
            aProfile.UnitIds.ToArray(), bProfile?.UnitIds.ToArray() ?? content.Deck,
            aProfile.OrderIds.FirstOrDefault(x => x.Length != 0) ?? "", seed,
            bProfile is null ? content.OrderId : bProfile.OrderIds.FirstOrDefault(x => x.Length != 0) ?? "", queue[a].Bonuses, bot ? null : queue[b].Bonuses);
        var runtime = new ServerMatchRuntime(id, ContentVersion, domain, content.Definitions(), content.BattleRules,
            new ServerMatchSettings(TimeSpan.FromSeconds(policy.DraftChoiceSeconds), TimeSpan.FromSeconds(policy.RoundResultSeconds), 256, 1024, 256, 512), clock, seed, [a, b]);
        matches.Add(id, new Match(id, admission, runtime, domain, a, b, bot) { Deck0 = aProfile?.UnitIds.ToArray() ?? (string[])content.Deck.Clone(), Deck1 = bProfile?.UnitIds.ToArray() ?? (string[])content.Deck.Clone() });
        accountMatches[a] = id; if (!bot) accountMatches[b] = id;
    }
    void RemoveMatch(Match match)
    {
        PersistCompletion(match);
        matches.Remove(match.Id);
        foreach (var pair in accountMatches.Where(p => p.Value == match.Id).ToArray()) accountMatches.Remove(pair.Key);
        match.Runtime.Dispose();
    }
    string UseLobby(string token)
    {
        EnsureOpen(); PruneLobbies();
        if (!TokenShape(token) || !lobbies.TryGetValue(Hash(token), out var lobby) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(lobby.Token), Encoding.UTF8.GetBytes(token))) throw new UnauthorizedAccessException();
        return lobby.Account;
    }
    void ReserveCalls(int count) { lock (sync) { EnsureOpen(); if (calls > policy.MaxIdentityCalls - count) throw new InvalidOperationException("identity_capacity"); calls += count; } }
    void EnsureOpen() { if (disposed || IsExpired) throw new UnauthorizedAccessException(); }
    void PruneLobbies() { foreach (var pair in lobbies.Where(p => p.Value.Expires <= clock.GetUtcNow()).ToArray()) lobbies.Remove(pair.Key); }
    TimeSpan Waited(Entry entry) => clock.GetElapsedTime(entry.At, clock.GetTimestamp());
    QueueStatus Idle() => new("Idle", "", 0, null, null, InstanceId, null);
    QueueStatus StatusFor(string account)
    {
        if (accountMatches.TryGetValue(account, out var id))
        { var m = matches[id]; return new("Matched", "", 0, id, m.HasBot ? "Bot" : "Human", InstanceId, m.Side0 == account ? 0 : 1); }
        return queue.TryGetValue(account, out var entry) ? new("Searching", entry.TicketId, Math.Max(0, policy.QueueTimeoutSeconds - (int)Waited(entry).TotalSeconds), null, null, InstanceId, null) : Idle();
    }
    static void CheckOperation(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(c => c < 0x21 || c > 0x7e)) throw new ArgumentException("Invalid operation."); }
    static bool TokenShape(string value) => value is { Length: 43 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public void Dispose() { lock (sync) { if (disposed) return; disposed = true; foreach (var m in matches.Values) m.Runtime.Dispose(); matches.Clear(); queue.Clear(); lobbies.Clear(); accounts.Clear(); accountMatches.Clear(); rewards?.Dispose(); settlements?.Dispose(); progressionJournal?.Dispose(); } }
    sealed class AccountState { public string? LoginOperation; public HashSet<string> LoginHistory { get; } = new(StringComparer.Ordinal); public HashSet<string> JoinHistory { get; } = new(StringComparer.Ordinal); }
    sealed record Entry(string Account, long At, string OperationId, string TicketId, PlayerProfile? Profile, IReadOnlyDictionary<string, PersistentBattleBonus>? Bonuses);
    sealed class Lobby(string account, string token, DateTimeOffset expires)
    {
        public string Account { get; } = account; public string Token { get; } = token;
        public string Key { get; } = Hash(token); public DateTimeOffset Expires { get; } = expires;
        public LobbyIssue Issue(DateTimeOffset now) => new(Token, Math.Max(1, (int)(Expires - now).TotalSeconds));
    }
    public sealed class Match(string id, MatchAdmissionService admission, ServerMatchRuntime runtime, MatchService domain, string a, string b, bool bot)
    {
        public string Id { get; } = id; public MatchAdmissionService Admission { get; } = admission; public ServerMatchRuntime Runtime { get; } = runtime;
        internal MatchService Domain { get; } = domain; internal string Side0 { get; } = a; internal string Side1 { get; } = b;
        internal bool HasBot { get; } = bot; internal long? CompletedAt { get; set; }
        internal bool ResultPersisted { get; set; }
        internal string[] Deck0 { get; init; } = [];
        internal string[] Deck1 { get; init; } = [];
    }
}
public sealed record LobbyIssue(string LobbyToken, int ExpiresInSeconds)
{ public override string ToString() => "LobbyIssue { LobbyToken = [REDACTED] }"; }
public sealed record QueueStatus(string State, string TicketId, int RemainingSeconds, string? MatchId, string? OpponentKind, string InstanceId, int? Side);
public sealed record AuthorityMetrics(int Queued, int ActiveMatches, int CompletedMatches, int IdentityCalls, bool Draining, double? PumpAgeMilliseconds);
