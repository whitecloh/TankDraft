using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TankDraft.Server.Match;
using TankDraft.Server.Security;

namespace TankDraft.Server.Persistence;

public static class PersistenceJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false, AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32
    };
    static PersistenceJson() => ServerMatchJson.Register(Options);
    public static string Encode<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Decode<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)
        ?? throw new InvalidDataException("Missing persisted value.");
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public sealed record PersistedActor(string AccountId, string SessionId, string Side);
public sealed record MatchCompletedEvent(string ResultId, string MatchId, string ContentVersion,
    int Wins0, int Wins1, int Winner, long FinalRevision);
public sealed record StoreCommit(long LogicalTicks, string Kind, PersistedActor? Actor, CommandEnvelope? Command,
    CommandReply? Reply, bool HasReceipt, string StateHash, ServerDecision[] Decisions, MatchCompletedEvent? Completion);
public sealed record StoredTransition(long Sequence, long Epoch, string PreviousHash, string Hash, StoreCommit Commit);
public sealed record DurableReceipt(string Fingerprint, CommandReply Reply);
public sealed record StoreLimits(int MaxEntries = 50000, int MaxRecordBytes = 262144, int MaxDatabasePages = 16384);
public enum StoreFaultPoint { BeforeTransaction, BeforeCommit, AfterCommit }
public enum DurableFaultPoint { AfterApplyBeforeCommit, AfterCommitBeforePublish, AfterDeliveryBeforeMark }

public interface IMatchStore : IDisposable
{
    string RecipeJson { get; }
    DateTimeOffset StartedUtc { get; }
    long Epoch { get; }
    long HeadSequence { get; }
    IReadOnlyList<StoredTransition> ReadLog();
    DurableReceipt? FindReceipt(string accountId, string operationId);
    StoredTransition Commit(long expectedHead, long expectedEpoch, StoreCommit commit);
    IReadOnlyList<MatchCompletedEvent> ReadPendingOutbox();
    void MarkOutboxDelivered(string resultId, long expectedEpoch);
}
