using System.Collections.Immutable;

namespace TankDraft.Server.Meta;

/// <summary>Server-resolved PlayFab identity. Client supplied account ids are never accepted here.</summary>
public sealed record PlayerEntity(string AccountId, string EntityId);

public sealed record StoredProfile(int ProfileVersion, PlayerProfile? Profile);

public sealed record InventorySnapshot(
    string SourceVersion,
    string? ETag,
    ImmutableHashSet<string> OwnedContentIds,
    ImmutableDictionary<string, long> Balances);

public enum ContentKind
{
    Unit = 0,
    Order = 1
}

public sealed record MetaContentDefinition(string Id, ContentKind Kind, int RequiredArena);

public sealed record PlayerProfile(
    int SchemaVersion,
    string Name,
    int CommanderLevel,
    int ArenaLevel,
    int ArenaProgress,
    int Mastery,
    ImmutableArray<string> UnitIds,
    ImmutableArray<string> OrderIds,
    string? LastOperationId,
    string? LastOperationFingerprint);

public sealed record MetaRules(
    string ContentVersion,
    int UnitSlots,
    ImmutableArray<int> OrderUnlockLevels,
    ImmutableArray<MetaContentDefinition> Definitions,
    PlayerProfile InitialProfile);

public sealed record MetaSnapshot(PlayerProfile Profile, int ProfileVersion, InventorySnapshot Inventory);

public interface IPlayerDataStore
{
    Task<StoredProfile> ReadProfileAsync(PlayerEntity player, CancellationToken cancellationToken);
    Task<InventorySnapshot> ReadInventoryAsync(PlayerEntity player, CancellationToken cancellationToken);
    Task<StoredProfile> WriteProfileAsync(
        PlayerEntity player,
        PlayerProfile profile,
        int expectedVersion,
        CancellationToken cancellationToken);
}

public sealed class MetaFailureException : Exception
{
    public MetaFailureException(string code, string? message = null, Exception? innerException = null)
        : base(message ?? code, innerException)
    {
        Code = code;
    }

    /// <summary>Safe, client-facing error code. Do not put provider payloads in this field.</summary>
    public string Code { get; }
}
