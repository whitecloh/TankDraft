using System.Globalization;
using System.Text.Json;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Domain;
using TankDraft.Server.Security;
using TankDraft.Simulation;

namespace TankDraft.Server.Match;

/// <summary>
/// Single-process authoritative match timeline. Persistence and distributed ownership are deliberately
/// outside this local backend slice; no client input advances time or resolves a battle.
/// </summary>
public sealed class ServerMatchRuntime : IDisposable
{
    private const string FaultCode = "Fault";
    private readonly object _sync = new();
    private readonly string _matchId;
    private readonly string _contentVersion;
    private readonly MatchService _match;
    private readonly BattleDefinitions _definitions;
    private readonly BattleRules _rules;
    private readonly ServerMatchSettings _settings;
    private readonly TimeProvider _clock;
    private readonly string[] _accountIds;
    private readonly InMemoryCommandGate _gate;
    private readonly List<ServerDecision> _decisions = new();
    private readonly List<ServerRoundResult> _results = new();
    private readonly List<ServerBattleEvent> _events = new();
    private readonly List<BattleEntityState> _captureBuffer = new();
    private readonly List<BattleEvent> _eventBuffer = new();
    private readonly DateTimeOffset _startUtc;
    private readonly long _startTimestamp;
    private BattleSimulation? _simulation;
    private IReadOnlyList<BattleEntityState> _draftEntities = Array.AsReadOnly(Array.Empty<BattleEntityState>());
    private TimeSpan? _nextDue;
    private TimeSpan _stateAt;
    private long _revision;
    private long _eventSequence;
    private uint _autoChoiceRng;
    private bool _disposed;
    private string? _fault;

    public ServerMatchRuntime(string matchId, string contentVersion, MatchService match,
        BattleDefinitions definitions, BattleRules rules, ServerMatchSettings settings,
        TimeProvider clock, uint autoChoiceSeed, string[] accountIds)
    {
        ValidateIdentifier(matchId, nameof(matchId));
        ValidateIdentifier(contentVersion, nameof(contentVersion));
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(clock);
        if (rules.AllowDebugCommands)
            throw new ArgumentException("Authoritative matches cannot permit debug battle commands.", nameof(rules));
        if (match.Phase != MatchPhase.Draft || match.RoundNumber != 1 || match.ChoiceNumber != 1
            || match.ChoiceToken != 1 || match.IsComeback || match.BonusSide != -1
            || match.HasCommitted(0) || match.HasCommitted(1))
            throw new ArgumentException("Server runtime requires a fresh, uncommitted initial draft.", nameof(match));
        if (accountIds is null || accountIds.Length != 2)
            throw new ArgumentException("Exactly two account identifiers are required.", nameof(accountIds));
        ValidateIdentifier(accountIds[0], nameof(accountIds));
        ValidateIdentifier(accountIds[1], nameof(accountIds));
        if (string.Equals(accountIds[0], accountIds[1], StringComparison.Ordinal))
            throw new ArgumentException("Match accounts must be distinct.", nameof(accountIds));

        _matchId = matchId;
        _contentVersion = contentVersion;
        _match = match;
        _definitions = definitions;
        _rules = rules;
        _settings = settings;
        _clock = clock;
        _accountIds = (string[])accountIds.Clone();
        _gate = new InMemoryCommandGate(settings.ReceiptCapacity, clock);
        _autoChoiceRng = autoChoiceSeed == 0 ? 1u : autoChoiceSeed;
        _startUtc = clock.GetUtcNow();
        _startTimestamp = clock.GetTimestamp();
        _stateAt = TimeSpan.Zero;
        _nextDue = settings.DraftChoiceDuration;
    }

    /// <returns>True when no authoritative transition remains due at this instant.</returns>
    public bool Pump()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_fault is not null)
                return true;
            try
            {
                return PumpCore(ElapsedNow());
            }
            catch
            {
                Fault();
                return true;
            }
        }
    }

    public CommandReply Execute(CallerIdentity caller, CommandEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(envelope);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_fault is not null)
                return CommandReply.Rejected(FaultCode);
            if (!IsCallerAssigned(caller) || caller.ExpiresAt <= _clock.GetUtcNow())
                return CommandReply.Rejected("Unauthorized");

            TimeSpan admittedAt;
            try
            {
                admittedAt = ElapsedNow();
                if (!PumpCore(admittedAt))
                    return CommandReply.Rejected("CatchingUp");
                if (_fault is not null)
                    return CommandReply.Rejected(FaultCode);
            }
            catch
            {
                Fault();
                return CommandReply.Rejected(FaultCode);
            }

            try
            {
                var reply = _gate.Execute(caller, envelope, () => ExecuteValidated(caller, envelope, admittedAt));
                if (string.Equals(reply.Code, "GateFaulted", StringComparison.Ordinal))
                    Fault();
                return _fault is null ? reply : CommandReply.Rejected(FaultCode);
            }
            catch
            {
                Fault();
                return CommandReply.Rejected(FaultCode);
            }
        }
    }

    // Authority-only persistence projection: no caller impersonation and no player-private draft data.
    public ServerCompletedResult? ReadCompletion()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_fault is not null) throw new InvalidOperationException("Faulted simulation cannot settle.");
            return _match.Phase == MatchPhase.MatchResult
                ? new ServerCompletedResult(_match.Wins(0), _match.Wins(1), _match.LastWinner, _revision) : null;
        }
    }

    public ServerMatchSnapshot Capture(CallerIdentity caller, long? afterEventSequence = null)
    {
        ArgumentNullException.ThrowIfNull(caller);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!IsCallerAssigned(caller) || caller.ExpiresAt <= _clock.GetUtcNow())
                throw new UnauthorizedAccessException();

            var now = ElapsedNow();
            var catchingUp = _fault is null && _nextDue is { } due && due <= now;
            var side = SideOf(caller);
            var entities = CaptureEntities();
            var eventSequence = _eventSequence;
            var committed = _match.HasCommitted(side);
            var committedChoice = CommittedChoiceFor(side, committed);
            var resync = !afterEventSequence.HasValue || IsEventCursorInvalid(afterEventSequence.Value);
            IReadOnlyList<ServerBattleEvent> events = resync
                ? Array.AsReadOnly(Array.Empty<ServerBattleEvent>())
                : Array.AsReadOnly(_events.Where(x => x.Sequence > afterEventSequence!.Value).ToArray());
            DateTimeOffset? deadline = _fault is null && _match.Phase is MatchPhase.Draft or MatchPhase.RoundResult && _nextDue.HasValue
                ? AtUtc(_nextDue.Value)
                : null;
            return new ServerMatchSnapshot(
                _matchId, _contentVersion, _revision, _match.RoundNumber, _match.Phase.ToString(),
                _match.ChoiceToken, _match.ChoiceNumber, _match.IsComeback, _match.BonusSide,
                committed, committedChoice, _match.CanUseOrder(side), _match.Charges(side),
                CopyArmy(_match.GetArmy(side)), CopyOffers(_match.GetOffers(side)),
                _match.Wins(0), _match.Wins(1), _match.LastWinner,
                AtUtc(now), AtUtc(_stateAt), deadline, catchingUp, _simulation?.Tick ?? 0,
                entities, eventSequence, resync, events, CopyResults(), _fault);
        }
    }

    public long NextCommandSequence(CallerIdentity caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!IsCallerAssigned(caller) || caller.ExpiresAt <= _clock.GetUtcNow()) throw new UnauthorizedAccessException();
            return _gate.NextCommandSequence(caller);
        }
    }

    public IReadOnlyList<ServerDecision> InspectDecisions()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            return Array.AsReadOnly(_decisions.ToArray());
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _nextDue = null;
            _simulation?.Dispose();
            _simulation = null;
            _draftEntities = Array.AsReadOnly(Array.Empty<BattleEntityState>());
        }
    }

    private CommandReply ExecuteValidated(CallerIdentity caller, CommandEnvelope envelope, TimeSpan admittedAt)
    {
        try
        {
            if (!string.Equals(envelope.MatchId, _matchId, StringComparison.Ordinal))
                return CommandReply.Rejected("MatchMismatch");
            if (!string.Equals(envelope.ContentVersion, _contentVersion, StringComparison.Ordinal))
                return CommandReply.Rejected("ContentMismatch");
            if (!string.Equals(envelope.RoundId, _match.RoundNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                return CommandReply.Rejected("RoundMismatch");
            if (_match.Phase != MatchPhase.Draft)
                return CommandReply.Rejected("DraftNotActive");
            if (_decisions.Count >= _settings.JournalCapacity)
            {
                Fault();
                return CommandReply.Rejected(FaultCode);
            }

            var side = SideOf(caller);
            var token = 0L;
            var offerIndex = -1;
            var kind = envelope.CommandKind;
            bool accepted;
            string reason;
            string offerId = string.Empty;
            if (string.Equals(kind, "Choose", StringComparison.Ordinal))
            {
                if (!TryParseChoose(envelope.Payload, out var payload))
                    return CommandReply.Rejected("MalformedPayload");
                token = payload.Token;
                offerIndex = payload.OfferIndex;
                var offers = _match.GetOffers(side);
                if (offerIndex >= 0 && offerIndex < offers.Count)
                    offerId = offers[offerIndex].Id;
                accepted = _match.TryChoose(side, token, offerIndex, out reason);
            }
            else if (string.Equals(kind, "Order", StringComparison.Ordinal))
            {
                if (!TryParseOrder(envelope.Payload, out var payload))
                    return CommandReply.Rejected("MalformedPayload");
                token = payload.Token;
                accepted = _match.TryUseOrder(side, token, out reason);
            }
            else
            {
                return CommandReply.Rejected("CommandNotAllowed");
            }

            if (!accepted)
                return CommandReply.Rejected(reason);

            AddDecision(admittedAt, side, kind, false, token, offerIndex, offerId, _autoChoiceRng, _autoChoiceRng);
            TransitionAfterDraftMutation(admittedAt);
            return new CommandReply(true, "Accepted", string.Empty);
        }
        catch
        {
            Fault();
            return CommandReply.Rejected(FaultCode);
        }
    }

    private bool PumpCore(TimeSpan now)
    {
        var steps = 0;
        while (_nextDue is { } due && due <= now)
        {
            if (steps++ >= _settings.MaxStepsPerPump)
                return false;
            ProcessDue(due);
            if (_fault is not null)
                return true;
        }
        return true;
    }

    private void ProcessDue(TimeSpan due)
    {
        switch (_match.Phase)
        {
            case MatchPhase.Draft:
                ResolveDraftDeadline(due);
                break;
            case MatchPhase.Battle:
                StepBattle(due);
                break;
            case MatchPhase.RoundResult:
                _simulation?.Dispose();
                _simulation = null;
                _events.Clear();
                _match.Continue();
                RefreshDraftPreview();
                _revision++;
                _stateAt = due;
                _nextDue = due + _settings.DraftChoiceDuration;
                break;
            default:
                _nextDue = null;
                _stateAt = due;
                break;
        }
    }

    private void ResolveDraftDeadline(TimeSpan due)
    {
        var token = _match.ChoiceToken;
        var round = _match.RoundNumber;
        var eligible = new List<int>(2);
        for (var side = 0; side < 2; side++)
        {
            if (!_match.HasCommitted(side) && _match.GetOffers(side).Count > 0)
                eligible.Add(side);
        }

        foreach (var side in eligible)
        {
            if (_decisions.Count >= _settings.JournalCapacity)
            {
                Fault();
                return;
            }
            var offers = _match.GetOffers(side);
            if (offers.Count == 0)
                continue;
            var before = _autoChoiceRng;
            var index = NextUnbiasedIndex(offers.Count);
            var after = _autoChoiceRng;
            var offer = offers[index];
            if (!_match.TryChoose(side, token, index, out _))
            {
                Fault();
                return;
            }
            AddDecision(due, side, "Choose", true, token, index, offer.Id, before, after, round);
        }
        TransitionAfterDraftMutation(due);
    }

    private void TransitionAfterDraftMutation(TimeSpan at)
    {
        _revision++;
        _stateAt = at;
        if (_match.Phase == MatchPhase.Draft)
        {
            RefreshDraftPreview();
            if (_match.ChoiceToken != 0 && !_match.HasCommitted(0) && !_match.HasCommitted(1))
                _nextDue = at + _settings.DraftChoiceDuration;
            // A single committed choice keeps the original deadline. It is intentionally not extended.
            return;
        }
        if (_match.Phase == MatchPhase.Battle)
        {
            _draftEntities = Array.AsReadOnly(Array.Empty<BattleEntityState>());
            _simulation?.Dispose();
            _simulation = new BattleSimulation(_definitions, _rules, _match.CreateScenario());
            _nextDue = at + ToTickDuration(_rules.TickSeconds);
            return;
        }
        _nextDue = _match.Phase == MatchPhase.RoundResult ? at + _settings.RoundResultDuration : null;
    }

    private void StepBattle(TimeSpan due)
    {
        if (_simulation is null)
        {
            Fault();
            return;
        }
        _simulation.Step();
        _eventBuffer.Clear();
        _simulation.DrainEvents(_eventBuffer);
        foreach (var value in _eventBuffer)
            AddEvent(_match.RoundNumber, value);
        _revision++;
        _stateAt = due;
        if (_simulation.Outcome == BattleOutcome.Running)
        {
            _nextDue = due + ToTickDuration(_rules.TickSeconds);
            return;
        }
        if (_results.Count >= _settings.JournalCapacity)
        {
            Fault();
            return;
        }
        var outcome = _simulation.Outcome;
        var winner = outcome == BattleOutcome.Side0Won ? 0 : outcome == BattleOutcome.Side1Won ? 1 : -1;
        _results.Add(new ServerRoundResult(_match.RoundNumber, winner, _simulation.ResolutionUsedRandomTieBreak,
            _simulation.TieBreakSeed, Array.AsReadOnly(_simulation.TerminalHits.ToArray())));
        _match.ResolveBattle(outcome);
        _nextDue = _match.Phase == MatchPhase.RoundResult ? due + _settings.RoundResultDuration : null;
    }

    private void AddDecision(TimeSpan at, int side, string kind, bool automatic, long token, int offerIndex,
        string offerId, uint rngBefore, uint rngAfter, int? round = null)
    {
        if (_decisions.Count >= _settings.JournalCapacity)
        {
            Fault();
            return;
        }
        _revision++;
        _decisions.Add(new ServerDecision(_revision, at.Ticks, round ?? _match.RoundNumber, token, side,
            kind, automatic, offerIndex, offerId, rngBefore, rngAfter));
    }

    private void AddEvent(int round, BattleEvent value)
    {
        var next = checked(++_eventSequence);
        if (_events.Count == _settings.EventCapacity)
            _events.RemoveAt(0);
        _events.Add(new ServerBattleEvent(next, round, value));
    }

    private bool IsEventCursorInvalid(long cursor)
    {
        if (cursor > _eventSequence)
            return true;
        if (_events.Count == 0)
            return cursor != _eventSequence;
        return cursor < _events[0].Sequence - 1;
    }

    private IReadOnlyList<BattleEntityState> CaptureEntities()
    {
        if (_simulation is null)
            return Array.AsReadOnly(_draftEntities.ToArray());
        _simulation.Capture(_captureBuffer);
        return Array.AsReadOnly(_captureBuffer.ToArray());
    }

    private void RefreshDraftPreview()
    {
        using var preview = new BattleSimulation(_definitions, _rules, _match.CreateScenario(), preparation: true);
        preview.Capture(_captureBuffer);
        _draftEntities = Array.AsReadOnly(_captureBuffer.ToArray());
    }

    private IReadOnlyList<ServerRoundResult> CopyResults()
    {
        return Array.AsReadOnly(_results.Select(x => new ServerRoundResult(x.Round, x.Winner, x.UsedRandomTieBreak,
            x.TieBreakSeed, Array.AsReadOnly(x.TerminalHits.ToArray()))).ToArray());
    }

    private static IReadOnlyList<ArmyEntry> CopyArmy(IReadOnlyList<ArmyEntry> source) =>
        Array.AsReadOnly(source.Select(x => new ArmyEntry(x.UnitId, x.Count, x.UpgradeLevel)).ToArray());

    private static IReadOnlyList<MatchOffer> CopyOffers(IReadOnlyList<MatchOffer> source) =>
        Array.AsReadOnly(source.Select(x => new MatchOffer(x.Id, x.UnitId, x.Kind, x.Amount)).ToArray());

    private ServerCommittedChoice? CommittedChoiceFor(int side, bool committed)
    {
        if (!committed || _match.Phase != MatchPhase.Draft)
            return null;
        for (var index = _decisions.Count - 1; index >= 0; index--)
        {
            var decision = _decisions[index];
            if (decision.Side == side && decision.Round == _match.RoundNumber
                && decision.ChoiceToken == _match.ChoiceToken)
                return new ServerCommittedChoice(decision.Kind, decision.OfferIndex, decision.OfferId, decision.Automatic);
        }
        return null;
    }

    private int NextUnbiasedIndex(int count)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        var limit = uint.MaxValue - (uint.MaxValue % (uint)count);
        uint sample;
        do { sample = NextRandom() - 1u; } while (sample >= limit);
        return (int)(sample % (uint)count);
    }

    private uint NextRandom()
    {
        var x = _autoChoiceRng;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        _autoChoiceRng = x == 0 ? 1u : x;
        return _autoChoiceRng;
    }

    private static TimeSpan ToTickDuration(float seconds)
    {
        var ticks = (long)Math.Round(seconds * TimeSpan.TicksPerSecond, MidpointRounding.AwayFromZero);
        return TimeSpan.FromTicks(Math.Max(1, ticks));
    }

    private bool IsCallerAssigned(CallerIdentity caller) =>
        string.Equals(caller.MatchId, _matchId, StringComparison.Ordinal)
        && (string.Equals(caller.AccountId, _accountIds[0], StringComparison.Ordinal)
            || string.Equals(caller.AccountId, _accountIds[1], StringComparison.Ordinal))
        && ((caller.Side == "0" && caller.AccountId == _accountIds[0])
            || (caller.Side == "1" && caller.AccountId == _accountIds[1]));

    private int SideOf(CallerIdentity caller) => caller.Side == "0" ? 0 : 1;
    private TimeSpan ElapsedNow() => _clock.GetElapsedTime(_startTimestamp, _clock.GetTimestamp());
    private DateTimeOffset AtUtc(TimeSpan elapsed) => _startUtc + elapsed;
    private void Fault() { _fault = FaultCode; _nextDue = null; }
    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(ServerMatchRuntime)); }
    private static void ValidateIdentifier(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            throw new ArgumentException("A non-empty identifier up to 128 characters is required.", name);
    }

    private static bool TryParseChoose(string json, out ChoosePayload payload)
    {
        payload = default!;
        if (!TryReadPayload(json, new HashSet<string>(StringComparer.Ordinal) { "Token", "OfferIndex" }, out var root))
            return false;
        if (!root.TryGetProperty("Token", out var token) || token.ValueKind != JsonValueKind.Number || !token.TryGetInt64(out var tokenValue)
            || !root.TryGetProperty("OfferIndex", out var index) || index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out var indexValue))
            return false;
        payload = new ChoosePayload { Token = tokenValue, OfferIndex = indexValue };
        return true;
    }

    private static bool TryParseOrder(string json, out OrderPayload payload)
    {
        payload = default!;
        if (!TryReadPayload(json, new HashSet<string>(StringComparer.Ordinal) { "Token" }, out var root))
            return false;
        if (!root.TryGetProperty("Token", out var token) || token.ValueKind != JsonValueKind.Number || !token.TryGetInt64(out var tokenValue))
            return false;
        payload = new OrderPayload { Token = tokenValue };
        return true;
    }

    private static bool TryReadPayload(string json, HashSet<string> allowed, out JsonElement root)
    {
        root = default;
        if (json is null || json.Length > 16 * 1024)
            return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            foreach (var property in document.RootElement.EnumerateObject())
                if (!allowed.Contains(property.Name))
                    return false;
            if (document.RootElement.EnumerateObject().Count() != allowed.Count)
                return false;
            root = document.RootElement.Clone();
            return true;
        }
        catch (JsonException) { return false; }
    }
}
