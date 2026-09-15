using System.Text.Json;
using TankDraft.Match.Domain;
using TankDraft.Server.Match;
using TankDraft.Server.Security;

namespace TankDraft.LocalHost;

public enum LocalQueueState { Idle, Searching, Matched }
public enum LocalOpponentKind { Human, Bot }

public sealed record LocalQueueStatus(string? TicketId, LocalQueueState State, int RemainingSeconds,
    string? MatchId, int? Side, LocalOpponentKind? OpponentKind);

/// <summary>Bounded, server-owned local matchmaking coordinator. Outer transports authenticate accounts first.</summary>
public sealed class LocalMatchmaking : ILocalMatchRouter, IDisposable
{
    private const string BotAccountPrefix = "local-qa-bot-";
    private readonly object _sync = new();
    private readonly AuthoredContent _content;
    private readonly string _contentVersion;
    private readonly HostSettings _host;
    private readonly LocalQueueSettings _settings;
    private readonly TimeProvider _clock;
    private readonly List<Ticket> _queue = [];
    private readonly Dictionary<string, Ticket> _ticketsByAccount = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ActiveMatch> _active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _leaveTombstones = new(StringComparer.Ordinal);
    private bool _disposed;

    public LocalMatchmaking(AuthoredContent content, string contentVersion, HostSettings host, LocalQueueSettings settings, TimeProvider? clock = null)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _contentVersion = Require(contentVersion, nameof(contentVersion));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.ValidateFor(_content); _clock = clock ?? TimeProvider.System;
    }

    public LocalQueueStatus Join(string accountId, string requestId, string contentVersion, IReadOnlyList<string> deck, string orderId)
    {
        lock (_sync)
        {
            EnsureOpen(); PumpLocked(); accountId = Require(accountId, nameof(accountId)); requestId = Require(requestId, nameof(requestId));
            if (!string.Equals(contentVersion, _contentVersion, StringComparison.Ordinal)) throw new ArgumentException("Unsupported content version.", nameof(contentVersion));
            var validatedDeck = ValidateDeck(deck); ValidateOrder(orderId);
            if (_ticketsByAccount.TryGetValue(accountId, out var existing))
            {
                if (existing.RequestId != requestId && existing.State == LocalQueueState.Searching) throw new InvalidOperationException("Account already has an outstanding ticket.");
                return StatusFor(existing);
            }
            if (_queue.Count >= _settings.MaxQueued) throw new InvalidOperationException("Local queue is full.");
            var now = QueueNow(); var deadline = checked(now + checked((long)_settings.QueueTimeoutSeconds * _clock.TimestampFrequency));
            var ticket = new Ticket(Guid.NewGuid().ToString("N"), accountId, requestId, validatedDeck, orderId, deadline);
            _queue.Add(ticket); _ticketsByAccount.Add(accountId, ticket); MatchWaitingLocked(now);
            return StatusFor(ticket);
        }
    }

    public LocalQueueStatus Status(string accountId)
    {
        lock (_sync) { EnsureOpen(); PumpLocked(); return _ticketsByAccount.TryGetValue(Require(accountId, nameof(accountId)), out var ticket) ? StatusFor(ticket) : Idle(); }
    }

    public LocalQueueStatus Cancel(string accountId, string ticketId)
    {
        lock (_sync)
        {
            EnsureOpen(); PumpLocked(); accountId = Require(accountId, nameof(accountId)); ticketId = Require(ticketId, nameof(ticketId));
            if (!_ticketsByAccount.TryGetValue(accountId, out var ticket)) return Idle();
            if (ticket.Id != ticketId) return StatusFor(ticket);
            if (ticket.State == LocalQueueState.Matched) return StatusFor(ticket);
            if (ticket.State != LocalQueueState.Searching) return Idle();
            _queue.Remove(ticket); _ticketsByAccount.Remove(accountId); return Idle();
        }
    }

    public bool LeaveCompleted(string accountId, string matchId)
    {
        lock (_sync)
        {
            EnsureOpen(); PumpLocked(); accountId = Require(accountId, nameof(accountId)); matchId = Require(matchId, nameof(matchId));
            var tombstone = TombstoneKey(accountId, matchId);
            if (_leaveTombstones.TryGetValue(tombstone, out var expiresAt) && expiresAt > _clock.GetUtcNow()) return true;
            _leaveTombstones.Remove(tombstone);
            // Retention may have already released a completed match. The client is allowed to
            // retry its old leave without changing a ticket subsequently created by this account.
            if (!_active.TryGetValue(matchId, out var match)) return true;
            if (!match.IsCompleted || !match.OwnsAccount(accountId)) return false;
            if (match.LeftAccounts.Contains(accountId)) return true;
            if (!_ticketsByAccount.TryGetValue(accountId, out var ticket) || ticket.State != LocalQueueState.Matched || ticket.MatchId != matchId) return false;
            match.LeftAccounts.Add(accountId);
            _ticketsByAccount.Remove(accountId);
            AddTombstone(tombstone);
            if (match.LeftAccounts.Count == 1 && match.HasBot) RemoveMatchLocked(match);
            else if (match.LeftAccounts.Count == 2) RemoveMatchLocked(match);
            return true;
        }
    }

    public bool Pump() { lock (_sync) { EnsureOpen(); return PumpLocked(); } }

    public ServerMatchSnapshot Capture(CallerIdentity caller, long? cursor = null)
    {
        lock (_sync) { var match = MatchFor(caller); return match.Endpoint.Capture(caller, cursor); }
    }

    public CommandReply Execute(CallerIdentity caller, CommandEnvelope command)
    {
        lock (_sync) { var match = MatchFor(caller); return match.Endpoint.Execute(caller, command); }
    }

    public long NextCommandSequence(CallerIdentity caller)
    {
        lock (_sync) { var match = MatchFor(caller); return match.Endpoint.NextCommandSequence(caller); }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return; _disposed = true;
            foreach (var match in _active.Values) match.Dispose(); _active.Clear(); _queue.Clear(); _ticketsByAccount.Clear(); _leaveTombstones.Clear();
        }
    }

    private bool PumpLocked()
    {
        var progressed = false;
        var utcNow = _clock.GetUtcNow(); var queueNow = QueueNow();
        foreach (var expired in _leaveTombstones.Where(x => x.Value <= utcNow).Select(x => x.Key).ToArray()) _leaveTombstones.Remove(expired);
        foreach (var ticket in _queue.Where(x => x.Deadline <= queueNow).ToArray()) progressed |= CreateBotMatchLocked(ticket);
        progressed |= MatchWaitingLocked(queueNow);
        foreach (var match in _active.Values.ToArray())
        {
            progressed |= match.Endpoint.Pump();
            if (match.HasBot && utcNow >= match.NextBotAt) progressed |= ThinkBotLocked(match, utcNow);
            if (!match.IsCompleted && match.Endpoint.Capture(match.InternalCaller(0)).Phase == "MatchResult") { match.IsCompleted = true; match.CompletedAt = utcNow; progressed = true; }
            if (match.IsCompleted && utcNow - match.CompletedAt >= TimeSpan.FromSeconds(_settings.CompletedRetentionSeconds)) { RemoveMatchLocked(match); progressed = true; }
        }
        return progressed;
    }

    private bool MatchWaitingLocked(long now)
    {
        var matched = false;
        while (_active.Count < _settings.MaxActiveMatches && _queue.Count >= 2)
        {
            var first = _queue.FirstOrDefault(x => x.Deadline > now);
            if (first is null) return matched;
            var second = _queue.FirstOrDefault(x => !ReferenceEquals(x, first) && x.Deadline > now);
            if (second is null) return matched;
            CreateMatchLocked(first, second, LocalOpponentKind.Human);
            matched = true;
        }
        return matched;
    }

    private bool CreateBotMatchLocked(Ticket ticket)
    {
        if (_active.Count >= _settings.MaxActiveMatches) return false;
        CreateMatchLocked(ticket, new Ticket("bot", BotAccountPrefix + Guid.NewGuid().ToString("N"), "bot", (string[])_settings.BotDeck.Clone(), _settings.BotOrderId, long.MaxValue), LocalOpponentKind.Bot);
        return true;
    }

    private void CreateMatchLocked(Ticket first, Ticket second, LocalOpponentKind opponent)
    {
        _queue.Remove(first); _queue.Remove(second);
        var matchId = "queue-" + Guid.NewGuid().ToString("N");
        var endpoint = new LocalMatchEndpoint(_content, _contentVersion, _host, _clock, matchId, [first.AccountId, second.AccountId], first.Deck, second.Deck, first.OrderId, second.OrderId);
        var match = new ActiveMatch(matchId, endpoint, first, second, opponent == LocalOpponentKind.Bot, _clock);
        _active.Add(matchId, match);
        first.SetMatched(match, 0, opponent); second.SetMatched(match, 1, opponent == LocalOpponentKind.Bot ? LocalOpponentKind.Human : LocalOpponentKind.Human);
    }

    private bool ThinkBotLocked(ActiveMatch match, DateTimeOffset now)
    {
        match.NextBotAt = now.AddMilliseconds(_settings.BotThinkMilliseconds);
        var caller = match.InternalCaller(1); var snapshot = match.Endpoint.Capture(caller);
        if (snapshot.Phase != "Draft" || snapshot.Committed || snapshot.Offers.Count == 0) return false;
        var sequence = match.Endpoint.NextCommandSequence(caller);
        var payload = JsonSerializer.Serialize(new { Token = snapshot.ChoiceToken, OfferIndex = (int)(snapshot.ChoiceNumber % snapshot.Offers.Count) }, AuthoredContent.Json);
        var command = new CommandEnvelope(match.Id, snapshot.Round.ToString(System.Globalization.CultureInfo.InvariantCulture), _contentVersion,
            "bot-" + snapshot.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), sequence, "Choose", payload);
        return match.Endpoint.Execute(caller, command).Accepted;
    }

    private ActiveMatch MatchFor(CallerIdentity caller)
    {
        lock (_sync)
        {
            EnsureOpen(); if (caller is null || !_active.TryGetValue(caller.MatchId, out var match) || !match.Owns(caller)) throw new UnauthorizedAccessException();
            return match;
        }
    }

    private void RemoveMatchLocked(ActiveMatch match)
    {
        if (!_active.Remove(match.Id)) return;
        foreach (var ticket in new[] { match.First, match.Second }) if (_ticketsByAccount.TryGetValue(ticket.AccountId, out var present) && present.MatchId == match.Id) _ticketsByAccount.Remove(ticket.AccountId);
        match.Dispose();
    }

    private LocalQueueStatus StatusFor(Ticket ticket) => ticket.State switch
    {
        LocalQueueState.Searching => new(ticket.Id, ticket.State, RemainingSeconds(ticket.Deadline), null, null, null),
        LocalQueueState.Matched => new(ticket.Id, ticket.State, 0, ticket.MatchId, ticket.Side, ticket.Opponent),
        _ => Idle()
    };
    private static LocalQueueStatus Idle() => new(null, LocalQueueState.Idle, 0, null, null, null);
    private long QueueNow()
    {
        if (_clock.TimestampFrequency <= 0) throw new InvalidOperationException("TimeProvider timestamp frequency must be positive.");
        return _clock.GetTimestamp();
    }
    private int RemainingSeconds(long deadline)
    {
        var remaining = deadline - QueueNow(); if (remaining <= 0) return 0;
        return (int)Math.Min(int.MaxValue, Math.Ceiling(remaining / (double)_clock.TimestampFrequency));
    }
    private void AddTombstone(string key)
    {
        if (_leaveTombstones.Count >= Math.Min(16, _settings.MaxActiveMatches * 2))
        {
            var oldest = _leaveTombstones.OrderBy(x => x.Value).First(); _leaveTombstones.Remove(oldest.Key);
        }
        _leaveTombstones[key] = _clock.GetUtcNow().AddSeconds(_settings.CompletedRetentionSeconds);
    }
    private static string TombstoneKey(string accountId, string matchId) => accountId.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + accountId + matchId.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + matchId;
    private string[] ValidateDeck(IReadOnlyList<string> deck)
    {
        if (deck is null || deck.Count != 4) throw new ArgumentException("Deck must contain four units.", nameof(deck));
        var copy = deck.ToArray(); if (!MatchService.IsSupportedDeck(copy, _content.MatchUnits)) throw new ArgumentException("Deck is not in the authored local QA roster.", nameof(deck)); return copy;
    }
    private static void ValidateOrder(string orderId) { if (orderId is not "" and not "order.reinforce_armor") throw new ArgumentException("Unsupported order.", nameof(orderId)); }
    private static string Require(string value, string name) => string.IsNullOrWhiteSpace(value) || value.Length > 128 ? throw new ArgumentException("Bounded identifier required.", name) : value;
    private void EnsureOpen() { ObjectDisposedException.ThrowIf(_disposed, this); }

    private sealed class Ticket(string id, string accountId, string requestId, string[] deck, string orderId, long deadline)
    {
        public string Id { get; } = id; public string AccountId { get; } = accountId; public string RequestId { get; } = requestId; public string[] Deck { get; } = deck; public string OrderId { get; } = orderId; public long Deadline { get; } = deadline;
        public LocalQueueState State { get; private set; } = LocalQueueState.Searching; public string? MatchId { get; private set; } public int? Side { get; private set; } public LocalOpponentKind? Opponent { get; private set; }
        public void SetMatched(ActiveMatch match, int side, LocalOpponentKind opponent) { State = LocalQueueState.Matched; MatchId = match.Id; Side = side; Opponent = opponent; }
    }

    private sealed class ActiveMatch(string id, LocalMatchEndpoint endpoint, Ticket first, Ticket second, bool hasBot, TimeProvider clock) : IDisposable
    {
        readonly LocalSessionRegistry _sessions = new(2, clock); readonly LocalSessionIssue[] _issues = new LocalSessionIssue[2];
        public string Id { get; } = id; public LocalMatchEndpoint Endpoint { get; } = endpoint; public Ticket First { get; } = first; public Ticket Second { get; } = second; public bool HasBot { get; } = hasBot;
        public DateTimeOffset NextBotAt { get; set; } = clock.GetUtcNow(); public DateTimeOffset CompletedAt { get; set; } public bool IsCompleted { get; set; } public HashSet<string> LeftAccounts { get; } = new(StringComparer.Ordinal);
        public CallerIdentity InternalCaller(int side)
        {
            if (_issues[side] is null) _issues[side] = _sessions.Create(side == 0 ? First.AccountId : Second.AccountId, Id, side.ToString(), TimeSpan.FromHours(1));
            return _sessions.Authenticate(_issues[side].Token) ?? throw new InvalidOperationException("Local match caller unavailable.");
        }
        public bool Owns(CallerIdentity caller) => caller.MatchId == Id && caller.Side is "0" or "1" && caller.AccountId == (caller.Side == "0" ? First.AccountId : Second.AccountId);
        public bool OwnsAccount(string accountId) => accountId == First.AccountId || accountId == Second.AccountId;
        public void Dispose() { Endpoint.Dispose(); }
    }
}
