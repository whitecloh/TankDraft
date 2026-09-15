using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace TankDraft.Server.Meta;

/// <summary>
/// Validates the trusted player's persistent loadout against immutable authored rules and Economy ownership.
/// This class owns no currency and never creates inventory grants.
/// </summary>
public sealed class PlayerMetaService
{
    public const int CurrentProfileSchemaVersion = 1;

    private readonly IPlayerDataStore _store;
    private readonly MetaRules _rules;
    private readonly IReadOnlyDictionary<string, MetaContentDefinition> _definitions;
    private readonly SemaphoreSlim _inflight;

    public PlayerMetaService(IPlayerDataStore store, MetaRules rules, int maximumInflight = 4)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        if (maximumInflight is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(maximumInflight));
        _inflight = new SemaphoreSlim(maximumInflight, maximumInflight);
        _definitions = BuildDefinitions(rules);
        ValidateRules(rules);
    }

    public Task<MetaSnapshot> GetAsync(PlayerEntity actor, CancellationToken cancellationToken)
        => ExecuteBoundedAsync(() => GetCoreAsync(ValidateActor(actor), cancellationToken), cancellationToken);

    public Task<MetaSnapshot> RevalidateForMatchAsync(PlayerEntity actor, string contentVersion, CancellationToken cancellationToken)
        => ExecuteBoundedAsync(async () =>
        {
            EnsureContentVersion(contentVersion);
            var player = ValidateActor(actor);
            var snapshot = await ReadSnapshotAsync(player, cancellationToken).ConfigureAwait(false);
            ValidateLoadout(snapshot.Profile, snapshot.Inventory);
            return snapshot;
        }, cancellationToken);

    public Task<MetaSnapshot> SaveLoadoutAsync(
        PlayerEntity actor,
        string contentVersion,
        int expectedProfileVersion,
        Guid operationId,
        IEnumerable<string> unitIds,
        IEnumerable<string> orderIds,
        CancellationToken cancellationToken)
        => ExecuteBoundedAsync(() => SaveLoadoutCoreAsync(
            ValidateActor(actor), contentVersion, expectedProfileVersion, operationId, unitIds, orderIds, cancellationToken), cancellationToken);

    private async Task<MetaSnapshot> GetCoreAsync(PlayerEntity player, CancellationToken cancellationToken)
    {
        var profile = await _store.ReadProfileAsync(player, cancellationToken).ConfigureAwait(false);
        var inventory = ValidateInventory(await _store.ReadInventoryAsync(player, cancellationToken).ConfigureAwait(false));
        ValidateStoredProfile(profile);
        if (profile.Profile is not null)
        {
            ValidateLoadout(profile.Profile, inventory);
            return new MetaSnapshot(profile.Profile, profile.ProfileVersion, inventory);
        }

        var initial = NormalizeProfile(_rules.InitialProfile);
        ValidateLoadout(initial, inventory);
        try
        {
            var written = await _store.WriteProfileAsync(player, initial, profile.ProfileVersion, cancellationToken).ConfigureAwait(false);
            ValidateStoredProfile(written);
            if (written.Profile is null) throw Fail("provider_unavailable");
            ValidateLoadout(written.Profile, inventory);
            return new MetaSnapshot(written.Profile, written.ProfileVersion, inventory);
        }
        catch (MetaFailureException exception) when (exception.Code == "version_conflict")
        {
            var current = await ReadSnapshotAsync(player, cancellationToken).ConfigureAwait(false);
            ValidateLoadout(current.Profile, current.Inventory);
            return current;
        }
    }

    private async Task<MetaSnapshot> SaveLoadoutCoreAsync(
        PlayerEntity player,
        string contentVersion,
        int expectedProfileVersion,
        Guid operationId,
        IEnumerable<string> unitIds,
        IEnumerable<string> orderIds,
        CancellationToken cancellationToken)
    {
        EnsureContentVersion(contentVersion);
        if (expectedProfileVersion < 1) throw Fail("invalid_profile_version");
        if (operationId == Guid.Empty) throw Fail("invalid_operation");
        var units = FreezeIds(unitIds, "invalid_loadout");
        var orders = FreezeIds(orderIds, "invalid_loadout");
        var fingerprint = CreateFingerprint(contentVersion, units, orders);
        var current = await ReadSnapshotAsync(player, cancellationToken).ConfigureAwait(false);

        if (string.Equals(current.Profile.LastOperationId, operationId.ToString("N"), StringComparison.Ordinal))
        {
            if (!string.Equals(current.Profile.LastOperationFingerprint, fingerprint, StringComparison.Ordinal))
                throw Fail("operation_conflict");
            return current;
        }

        if (current.ProfileVersion != expectedProfileVersion) throw Fail("version_conflict");
        var next = NormalizeProfile(current.Profile with
        {
            UnitIds = units,
            OrderIds = orders,
            LastOperationId = operationId.ToString("N"),
            LastOperationFingerprint = fingerprint
        });
        ValidateLoadout(next, current.Inventory);

        var written = await _store.WriteProfileAsync(player, next, expectedProfileVersion, cancellationToken).ConfigureAwait(false);
        ValidateStoredProfile(written);
        if (written.Profile is null) throw Fail("provider_unavailable");
        ValidateLoadout(written.Profile, current.Inventory);
        return new MetaSnapshot(written.Profile, written.ProfileVersion, current.Inventory);
    }

    private async Task<MetaSnapshot> ReadSnapshotAsync(PlayerEntity player, CancellationToken cancellationToken)
    {
        var profile = await _store.ReadProfileAsync(player, cancellationToken).ConfigureAwait(false);
        var inventory = ValidateInventory(await _store.ReadInventoryAsync(player, cancellationToken).ConfigureAwait(false));
        ValidateStoredProfile(profile);
        if (profile.Profile is null) throw Fail("profile_missing");
        return new MetaSnapshot(profile.Profile, profile.ProfileVersion, inventory);
    }

    private async Task<T> ExecuteBoundedAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        if (!await _inflight.WaitAsync(0, cancellationToken).ConfigureAwait(false)) throw Fail("busy");
        try { return await operation().ConfigureAwait(false); }
        catch (MetaFailureException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { throw Fail("provider_unavailable", exception); }
        finally { _inflight.Release(); }
    }

    private void ValidateLoadout(PlayerProfile profile, InventorySnapshot inventory)
    {
        ValidateProfile(profile);
        ValidateLoadoutIds(profile.UnitIds, ContentKind.Unit, _rules.UnitSlots, profile, inventory);
        ValidateOrderSlots(profile.OrderIds, profile, inventory);
    }

    private void ValidateLoadoutIds(
        ImmutableArray<string> ids,
        ContentKind expectedKind,
        int exactSlots,
        PlayerProfile profile,
        InventorySnapshot inventory)
    {
        if (ids.IsDefault || ids.Length != exactSlots) throw Fail("invalid_loadout");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) throw Fail("invalid_loadout");
            if (!_definitions.TryGetValue(id, out var definition)) throw Fail("unknown_content");
            if (definition.Kind != expectedKind) throw Fail("invalid_loadout");
            if (!inventory.OwnedContentIds.Contains(id)) throw Fail("not_owned");
            if (definition.RequiredArena > profile.ArenaLevel) throw Fail("locked");
        }
    }

    private void ValidateOrderSlots(ImmutableArray<string> ids, PlayerProfile profile, InventorySnapshot inventory)
    {
        if (ids.IsDefault || ids.Length != _rules.OrderUnlockLevels.Length) throw Fail("invalid_loadout");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < ids.Length; index++)
        {
            var id = ids[index];
            if (id.Length == 0) continue;
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) throw Fail("invalid_loadout");
            if (profile.CommanderLevel < _rules.OrderUnlockLevels[index]) throw Fail("locked");
            if (!_definitions.TryGetValue(id, out var definition)) throw Fail("unknown_content");
            if (definition.Kind != ContentKind.Order) throw Fail("invalid_loadout");
            if (!inventory.OwnedContentIds.Contains(id)) throw Fail("not_owned");
            if (definition.RequiredArena > profile.ArenaLevel) throw Fail("locked");
        }
    }

    private static PlayerEntity ValidateActor(PlayerEntity actor)
    {
        if (actor is null || string.IsNullOrWhiteSpace(actor.AccountId) || string.IsNullOrWhiteSpace(actor.EntityId))
            throw Fail("unauthorized");
        return actor;
    }

    private static InventorySnapshot ValidateInventory(InventorySnapshot inventory)
    {
        if (inventory is null || string.IsNullOrWhiteSpace(inventory.SourceVersion) || inventory.OwnedContentIds is null || inventory.Balances is null)
            throw Fail("provider_unavailable");
        if (inventory.OwnedContentIds.Any(string.IsNullOrWhiteSpace) || inventory.Balances.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0))
            throw Fail("provider_unavailable");
        return inventory with
        {
            OwnedContentIds = inventory.OwnedContentIds.ToImmutableHashSet(StringComparer.Ordinal),
            Balances = inventory.Balances.ToImmutableDictionary(StringComparer.Ordinal)
        };
    }

    private static void ValidateStoredProfile(StoredProfile stored)
    {
        if (stored is null || stored.ProfileVersion < 0) throw Fail("provider_unavailable");
        if (stored.Profile is not null && stored.ProfileVersion < 1) throw Fail("provider_unavailable");
    }

    private static void ValidateProfile(PlayerProfile profile)
    {
        if (profile is null || profile.SchemaVersion != CurrentProfileSchemaVersion || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 64 ||
            profile.CommanderLevel < 0 || profile.ArenaLevel < 0 || profile.ArenaProgress < 0 || profile.Mastery < 0)
            throw Fail("invalid_profile");
        var hasOperationId = !string.IsNullOrWhiteSpace(profile.LastOperationId);
        var hasFingerprint = !string.IsNullOrWhiteSpace(profile.LastOperationFingerprint);
        if (hasOperationId != hasFingerprint || (hasOperationId && !Guid.TryParseExact(profile.LastOperationId, "N", out _)))
            throw Fail("invalid_profile");
    }

    private static PlayerProfile NormalizeProfile(PlayerProfile profile) => profile with
    {
        UnitIds = profile.UnitIds.IsDefault ? ImmutableArray<string>.Empty : profile.UnitIds.ToImmutableArray(),
        OrderIds = profile.OrderIds.IsDefault ? ImmutableArray<string>.Empty : profile.OrderIds.ToImmutableArray()
    };

    private static ImmutableArray<string> FreezeIds(IEnumerable<string> ids, string errorCode)
    {
        if (ids is null) throw Fail(errorCode);
        try { return ids.Select(id => id ?? string.Empty).ToImmutableArray(); }
        catch (Exception exception) when (exception is not MetaFailureException) { throw Fail(errorCode); }
    }

    private void EnsureContentVersion(string contentVersion)
    {
        if (!string.Equals(contentVersion, _rules.ContentVersion, StringComparison.Ordinal)) throw Fail("content_version_mismatch");
    }

    private static IReadOnlyDictionary<string, MetaContentDefinition> BuildDefinitions(MetaRules rules)
    {
        if (rules.Definitions.IsDefault) throw new ArgumentException("Definitions are required.", nameof(rules));
        var result = new Dictionary<string, MetaContentDefinition>(StringComparer.Ordinal);
        foreach (var definition in rules.Definitions)
        {
            if (definition is null || string.IsNullOrWhiteSpace(definition.Id) || definition.RequiredArena < 0 || !result.TryAdd(definition.Id, definition))
                throw new ArgumentException("Definitions must have unique valid ids.", nameof(rules));
        }
        return result;
    }

    private static void ValidateRules(MetaRules rules)
    {
        if (string.IsNullOrWhiteSpace(rules.ContentVersion) || rules.UnitSlots < 1 || rules.InitialProfile is null || rules.OrderUnlockLevels.IsDefault ||
            rules.OrderUnlockLevels.Any(level => level < 0))
            throw new ArgumentException("Rules are invalid.", nameof(rules));
    }

    private static string CreateFingerprint(string contentVersion, ImmutableArray<string> units, ImmutableArray<string> orders)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, contentVersion);
        Append(hash, "units");
        foreach (var id in units) Append(hash, id);
        Append(hash, "orders");
        foreach (var id in orders) Append(hash, id);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static MetaFailureException Fail(string code, Exception? inner = null) => new(code, null, inner);
}
