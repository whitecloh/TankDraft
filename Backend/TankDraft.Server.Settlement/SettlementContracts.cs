namespace TankDraft.Server.Settlement;

public record CompletedMatch(string ResultId, string MatchId, string ContentVersion, string Account0, string? Account1,
    int Wins0, int Wins1, int Winner, long FinalRevision);
public record RewardPolicy(string Version, string Currency, int WinAmount, int LossAmount, int MaximumGrantsPerAccount);
public record RewardGrant(string ResultId, string AccountId, string Currency, int Amount);
public enum RewardState { Pending, Sending, Applied, NeedsReview, Skipped }
public record RewardReceipt(RewardGrant Grant, RewardState State, string? Reason);
public enum ReviewResolution { ConfirmApplied, CompensateOnce }
public record ReviewResolutionRecord(string ResultId, ReviewResolution Resolution, string EvidenceReference, string? OriginalReason, DateTimeOffset ResolvedAtUtc);
public interface IRewardProvider { Task GrantAsync(RewardGrant grant, CancellationToken cancellationToken); }
