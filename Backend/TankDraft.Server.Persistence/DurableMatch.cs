using TankDraft.Server.Match;
using TankDraft.Server.Security;

namespace TankDraft.Server.Persistence;

// Owns the store and S2 runtime. No state/ACK escapes this lock before COMMIT.
public sealed class DurableMatch : IDisposable
{
    private readonly object _sync = new();
    private readonly IMatchStore _store;
    private readonly MatchRecipe _recipe;
    private readonly TimeProvider _wallClock;
    private readonly ReplayClock _clock;
    private readonly ServerMatchRuntime _runtime;
    private readonly long _resumeTimestamp;
    private readonly long _resumeLogicalTicks;
    private readonly Action<DurableFaultPoint>? _faultHook;
    private long _lastCommitTicks;
    private int _decisionCount;
    private bool _faulted, _disposed;

    public DurableMatch(IMatchStore store, TimeProvider wallClock, Action<DurableFaultPoint>? faultHook = null)
    {
        _store = store;
        _wallClock = wallClock;
        _faultHook = faultHook;
        try
        {
            _recipe = PersistenceJson.Decode<MatchRecipe>(store.RecipeJson);
            _clock = new ReplayClock(store.StartedUtc);
            _runtime = _recipe.CreateRuntime(_clock);
            foreach (var entry in store.ReadLog())
            {
                _clock.Set(entry.Commit.LogicalTicks);
                CommandReply? reply = entry.Commit.Kind switch
                {
                    "Pump" => ReplayPump(),
                    "Command" => _runtime.Execute(Trusted(entry.Commit.Actor!), entry.Commit.Command!),
                    _ => throw new InvalidDataException("Unknown replay input.")
                };
                var decisions = _runtime.InspectDecisions().Skip(_decisionCount).ToArray();
                if (reply != entry.Commit.Reply || StateHash() != entry.Commit.StateHash ||
                    PersistenceJson.Encode(decisions) != PersistenceJson.Encode(entry.Commit.Decisions))
                    throw new InvalidDataException("Replay diverged from committed state.");
                _decisionCount += decisions.Length;
                _lastCommitTicks = entry.Commit.LogicalTicks;
            }
            _resumeLogicalTicks = _lastCommitTicks;
            _resumeTimestamp = wallClock.GetTimestamp();
        }
        catch
        {
            _runtime?.Dispose();
            store.Dispose();
            throw;
        }
    }

    public long Epoch => _store.Epoch;
    public long CommittedSequence => _store.HeadSequence;

    public bool Pump()
    {
        lock (_sync)
        {
            EnsureUsable();
            try
            {
                var before = InternalCapture(0);
                SetCurrentTime();
                var caughtUp = _runtime.Pump();
                var after = InternalCapture(0);
                // Persist idle clock at least once per second; battle mutations always commit.
                if (before.Revision != after.Revision || before.Fault != after.Fault ||
                    (after.Phase != "MatchResult" && _clock.Ticks - _lastCommitTicks >= TimeSpan.TicksPerSecond))
                    Commit("Pump", null, null, null);
                return caughtUp;
            }
            catch { _faulted = true; throw; }
        }
    }

    // Graceful shutdown must commit the logical clock even between simulation ticks.
    public bool Flush()
    {
        lock (_sync)
        {
            EnsureUsable();
            try
            {
                SetCurrentTime();
                var caughtUp = _runtime.Pump();
                Commit("Pump", null, null, null);
                return caughtUp;
            }
            catch { _faulted = true; throw; }
        }
    }

    public CommandReply Execute(CallerIdentity caller, CommandEnvelope command)
    {
        lock (_sync)
        {
            EnsureUsable();
            if (!IsAuthorized(caller)) return CommandReply.Rejected("Unauthorized");
            if (!IsBounded(command)) return CommandReply.Rejected("MalformedCommand");
            try
            {
                var existing = _store.FindReceipt(caller.AccountId, command.OperationId);
                if (existing is not null)
                    return existing.Fingerprint == PersistenceJson.Hash(PersistenceJson.Encode(command))
                        ? existing.Reply : CommandReply.Rejected("OperationConflict");
                SetCurrentTime();
                var actor = new PersistedActor(caller.AccountId, caller.SessionId, caller.Side);
                var reply = _runtime.Execute(Trusted(actor), command);
                // CatchingUp must not consume sequence/operation identity; its clock/ticks still commit.
                if (reply.Code == "CatchingUp") Commit("Pump", null, null, null);
                else Commit("Command", actor, command, reply);
                return reply;
            }
            catch { _faulted = true; throw; }
        }
    }

    public ServerMatchSnapshot Capture(CallerIdentity caller, long? cursor = null)
    {
        lock (_sync)
        {
            EnsureUsable();
            if (!IsAuthorized(caller)) throw new UnauthorizedAccessException();
            SetCurrentTime();
            return _runtime.Capture(Trusted(new PersistedActor(caller.AccountId, caller.SessionId, caller.Side)), cursor);
        }
    }

    public long NextCommandSequence(CallerIdentity caller)
    {
        lock (_sync)
        {
            EnsureUsable();
            if (!IsAuthorized(caller)) throw new UnauthorizedAccessException();
            SetCurrentTime();
            return _runtime.NextCommandSequence(Trusted(new PersistedActor(caller.AccountId, caller.SessionId, caller.Side)));
        }
    }

    public string GetStateHash()
    {
        lock (_sync) { EnsureUsable(); return StateHash(); }
    }

    public int DeliverOutbox(Func<MatchCompletedEvent, bool> deliver)
    {
        lock (_sync)
        {
            EnsureUsable();
            var delivered = 0;
            try
            {
                foreach (var message in _store.ReadPendingOutbox())
                {
                    if (!deliver(message)) continue;
                    _faultHook?.Invoke(DurableFaultPoint.AfterDeliveryBeforeMark);
                    _store.MarkOutboxDelivered(message.ResultId, _store.Epoch);
                    delivered++;
                }
                return delivered;
            }
            catch { _faulted = true; throw; }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _runtime.Dispose();
            _store.Dispose();
        }
    }

    private void Commit(string kind, PersistedActor? actor, CommandEnvelope? command, CommandReply? reply)
    {
        var state = InternalCapture(0);
        if (state.Fault is not null) throw new InvalidOperationException("Authoritative runtime faulted; publication refused.");
        var decisions = _runtime.InspectDecisions().Skip(_decisionCount).ToArray();
        MatchCompletedEvent? completion = state.Phase == "MatchResult"
            ? new(PersistenceJson.Hash(_recipe.MatchId + "|" + _store.StartedUtc.ToString("O")), _recipe.MatchId,
                _recipe.ContentVersion, state.Wins0, state.Wins1, state.LastWinner, state.Revision) : null;
        var hasReceipt = reply is not null && reply.Code is not ("Unauthorized" or "MalformedCommand" or
            "MatchMismatch" or "CommandNotAllowed" or "UnexpectedSequence" or "ReceiptCapacityReached" or "OperationConflict");
        var commit = new StoreCommit(_clock.Ticks, kind, actor, command, reply, hasReceipt, StateHash(), decisions, completion);
        _faultHook?.Invoke(DurableFaultPoint.AfterApplyBeforeCommit);
        _store.Commit(_store.HeadSequence, _store.Epoch, commit);
        _lastCommitTicks = _clock.Ticks;
        _decisionCount += decisions.Length;
        _faultHook?.Invoke(DurableFaultPoint.AfterCommitBeforePublish);
    }

    private CommandReply? ReplayPump() { _runtime.Pump(); return null; }
    private string StateHash() => PersistenceJson.Hash(PersistenceJson.Encode(new
    {
        Side0 = InternalCapture(0), Side1 = InternalCapture(1), Decisions = _runtime.InspectDecisions()
    }));
    private ServerMatchSnapshot InternalCapture(int side) => _runtime.Capture(
        Trusted(new PersistedActor(_recipe.Accounts[side], "internal-snapshot", side.ToString())), 0);
    private CallerIdentity Trusted(PersistedActor actor) => new(actor.AccountId, actor.SessionId,
        _recipe.MatchId, actor.Side, DateTimeOffset.MaxValue);
    private bool IsAuthorized(CallerIdentity caller) => caller is not null && caller.ExpiresAt > _wallClock.GetUtcNow()
        && caller.MatchId == _recipe.MatchId && caller.Side is "0" or "1"
        && caller.AccountId == _recipe.Accounts[caller.Side == "0" ? 0 : 1];
    private static bool IsBounded(CommandEnvelope command) => command is not null && command.Sequence > 0
        && new[] { command.MatchId, command.RoundId, command.ContentVersion, command.OperationId, command.CommandKind }
            .All(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 128)
        && command.Payload is not null && command.Payload.Length <= 16384;
    private void SetCurrentTime() => _clock.Set(checked(_resumeLogicalTicks +
        _wallClock.GetElapsedTime(_resumeTimestamp, _wallClock.GetTimestamp()).Ticks));
    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_faulted) throw new InvalidOperationException("Durable match unavailable; reopen required.");
    }

    private sealed class ReplayClock(DateTimeOffset startedUtc) : TimeProvider
    {
        public long Ticks { get; private set; }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
        public override DateTimeOffset GetUtcNow() => startedUtc.AddTicks(Ticks);
        public void Set(long ticks)
        {
            if (ticks < Ticks) throw new InvalidDataException("Replay clock moved backwards.");
            Ticks = ticks;
        }
    }
}
