using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace TankDraft.Server.Meta;

public sealed record InventoryContentBinding(string ItemId, string ContentId, bool IsCurrency);

/// <summary>Entity Objects profile + Economy V2 inventory; never grants items or creates a second wallet.</summary>
public sealed class PlayFabPlayerDataStore : IPlayerDataStore
{
    readonly PlayFabEntityProfileStore profiles;
    readonly PlayFabMetaTransport transport;
    readonly ImmutableDictionary<string, InventoryContentBinding> bindings;
    readonly string collectionId;
    readonly int maximumPages;

    public PlayFabPlayerDataStore(PlayFabMetaTransport transport, IEnumerable<InventoryContentBinding> bindings, string collectionId, int maximumPages = 20)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        profiles = new PlayFabEntityProfileStore(transport);
        this.bindings = bindings.ToImmutableDictionary(b => b.ItemId, StringComparer.Ordinal);
        if (this.bindings.Count == 0 || this.bindings.Values.Any(b => !Guid.TryParse(b.ItemId, out _) || string.IsNullOrWhiteSpace(b.ContentId)) || this.bindings.Values.Select(b => b.ContentId).Distinct(StringComparer.Ordinal).Count() != this.bindings.Count)
            throw new ArgumentException("Inventory mappings must be explicit and unique.");
        if (!Regex.IsMatch(collectionId ?? "", "^[A-Za-z0-9_-]{1,50}$") || maximumPages is < 1 or > 100) throw new ArgumentException("Invalid inventory bounds.");
        this.collectionId = collectionId!; this.maximumPages = maximumPages;
    }

    static object Entity(PlayerEntity actor)
    {
        if (!Regex.IsMatch(actor.EntityId ?? "", "^[A-Za-z0-9]{5,64}$")) throw new MetaFailureException("invalid_identity");
        return new { Id = actor.EntityId, Type = "title_player_account" };
    }

    public Task<PlayerEntity> ResolveAsync(string verifiedAccountId, CancellationToken ct) => profiles.ResolveAsync(verifiedAccountId, ct);

    public Task<StoredProfile> ReadProfileAsync(PlayerEntity actor, CancellationToken ct) => profiles.ReadProfileAsync(actor, ct);

    public Task<StoredProfile> WriteProfileAsync(PlayerEntity actor, PlayerProfile profile, int expectedVersion, CancellationToken ct) => profiles.WriteProfileAsync(actor, profile, expectedVersion, ct);

    public async Task<InventorySnapshot> ReadInventoryAsync(PlayerEntity actor, CancellationToken ct)
    {
        var owned = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var balances = ImmutableDictionary.CreateBuilder<string, long>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stacks = new HashSet<(string, string)>();
        string? continuation = null, etag = null;
        try
        {
            for (int page = 0; page < maximumPages; page++)
            {
                var data = await transport.CallAsync("Inventory/GetInventoryItems", new { Entity = Entity(actor), CollectionId = collectionId, Count = 50, ContinuationToken = continuation }, ct);
                var currentTag = data.GetProperty("ETag").GetString();
                if (string.IsNullOrEmpty(currentTag) || page > 0 && etag != currentTag) throw new MetaFailureException("inventory_changed");
                etag = currentTag;
                foreach (var item in data.GetProperty("Items").EnumerateArray())
                {
                    var id = item.GetProperty("Id").GetString() ?? "";
                    var stack = item.GetProperty("StackId").GetString() ?? "";
                    if (!stacks.Add((id, stack))) throw new MetaFailureException("invalid_inventory");
                    if (!bindings.TryGetValue(id, out var binding)) continue;
                    var amount = item.GetProperty("Amount").GetInt64();
                    if (amount < 0) throw new MetaFailureException("invalid_inventory");
                    if (binding.IsCurrency) balances[binding.ContentId] = checked(balances.GetValueOrDefault(binding.ContentId) + amount);
                    else if (amount > 0) owned.Add(binding.ContentId);
                }
                continuation = data.TryGetProperty("ContinuationToken", out var next) ? next.GetString() : null;
                if (string.IsNullOrEmpty(continuation)) return new InventorySnapshot(etag, etag, owned.ToImmutable(), balances.ToImmutable());
                if (continuation.Length > 8192 || !seen.Add(continuation)) throw new MetaFailureException("invalid_inventory");
            }
            throw new MetaFailureException("inventory_capacity");
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException) { throw new MetaFailureException("invalid_inventory"); }
    }
}
