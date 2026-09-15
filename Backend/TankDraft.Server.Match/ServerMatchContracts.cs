using TankDraft.Contracts.Battle;
using TankDraft.Match.Domain;

namespace TankDraft.Server.Match;

public sealed record ServerCompletedResult(int Wins0, int Wins1, int Winner, long Revision);

public sealed record ServerMatchSettings
{
    public TimeSpan DraftChoiceDuration { get; }
    public TimeSpan RoundResultDuration { get; }
    public int MaxStepsPerPump { get; }
    public int EventCapacity { get; }
    public int JournalCapacity { get; }
    public int ReceiptCapacity { get; }

    public ServerMatchSettings(TimeSpan draftChoiceDuration, TimeSpan roundResultDuration,
        int maxStepsPerPump, int eventCapacity, int journalCapacity, int receiptCapacity)
    {
        if (draftChoiceDuration <= TimeSpan.Zero || draftChoiceDuration > TimeSpan.FromMinutes(5) ||
            roundResultDuration <= TimeSpan.Zero || roundResultDuration > TimeSpan.FromMinutes(1) ||
            maxStepsPerPump is < 1 or > 1024 || eventCapacity is < 1 or > 8192 ||
            journalCapacity is < 1 or > 4096 || receiptCapacity is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(draftChoiceDuration), "Server match policy outside bounds.");
        DraftChoiceDuration = draftChoiceDuration;
        RoundResultDuration = roundResultDuration;
        MaxStepsPerPump = maxStepsPerPump;
        EventCapacity = eventCapacity;
        JournalCapacity = journalCapacity;
        ReceiptCapacity = receiptCapacity;
    }
}

public sealed record ChoosePayload
{
    public required long Token { get; init; }
    public required int OfferIndex { get; init; }
}

public sealed record OrderPayload
{
    public required long Token { get; init; }
}

public sealed record ServerBattleEvent(long Sequence, int Round, BattleEvent Value);
public sealed record ServerCommittedChoice(string Kind, int OfferIndex, string OfferId, bool Automatic);
public sealed record ServerRoundResult(int Round, int Winner, bool UsedRandomTieBreak, uint TieBreakSeed,
    IReadOnlyList<BattleResolutionHit> TerminalHits);

// Server-side audit only: never serialized into a player's snapshot (contains private choices/RNG state).
public sealed record ServerDecision(long Revision, long AtTicks, int Round, long ChoiceToken,
    int Side, string Kind, bool Automatic, int OfferIndex, string OfferId, uint RngBefore, uint RngAfter);

// All collection instances must be detached read-only copies, not live ECS/domain lists.
public sealed record ServerMatchSnapshot(string MatchId, string ContentVersion, long Revision,
    int Round, string Phase, long ChoiceToken, int ChoiceNumber, bool IsComeback, int BonusSide,
    bool Committed, ServerCommittedChoice? CommittedChoice, bool CanUseOrder, int OrderCharges, IReadOnlyList<ArmyEntry> Army,
    IReadOnlyList<MatchOffer> Offers, int Wins0, int Wins1, int LastWinner,
    DateTimeOffset ServerNow, DateTimeOffset StateAt, DateTimeOffset? DeadlineAt, bool CatchingUp,
    long SimulationTick, IReadOnlyList<BattleEntityState> Entities, long EventSequence,
    bool ResyncRequired, IReadOnlyList<ServerBattleEvent> Events,
    IReadOnlyList<ServerRoundResult> Results, string? Fault)
{
    // Snapshot readers must fail closed when the entity wire contract changes.
    public int BattleStateVersion => BattleEntityState.WireVersion;
}
