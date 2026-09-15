using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TankDraft.Server.Meta;

public sealed record LegacyInventoryContentBinding(string ItemId, string ContentId);

public sealed record LegacyCurrencyBinding(string Code, string ContentId);

/// <summary>Temporary read-only Legacy Catalog/Inventory/Currency adapter. It never grants or changes provider balances.</summary>
public sealed class PlayFabLegacyPlayerDataStore : IPlayerDataStore
{
    const string IdentifierPattern = "^[A-Za-z0-9_.-]{1,128}$";
    readonly PlayFabEntityProfileStore profiles;
    readonly PlayFabMetaTransport transport;
    readonly string catalogVersion;
    readonly ImmutableDictionary<string, LegacyInventoryContentBinding> inventoryBindings;
    readonly ImmutableDictionary<string, LegacyCurrencyBinding> currencyBindings;

    public PlayFabLegacyPlayerDataStore(
        PlayFabMetaTransport transport,
        string catalogVersion,
        IEnumerable<LegacyInventoryContentBinding> bindings,
        IEnumerable<LegacyCurrencyBinding> currencies)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        profiles = new PlayFabEntityProfileStore(transport);
        if (!Regex.IsMatch(catalogVersion ?? "", IdentifierPattern)) throw new ArgumentException("Invalid legacy catalog version.", nameof(catalogVersion));
        this.catalogVersion = catalogVersion!;
        this.inventoryBindings = bindings.ToImmutableDictionary(binding => binding.ItemId, StringComparer.Ordinal);
        this.currencyBindings = currencies.ToImmutableDictionary(binding => binding.Code, StringComparer.Ordinal);
        if (this.inventoryBindings.Count == 0 ||
            this.inventoryBindings.Values.Any(binding => !Regex.IsMatch(binding.ItemId ?? "", IdentifierPattern) || !Regex.IsMatch(binding.ContentId ?? "", IdentifierPattern)) ||
            this.inventoryBindings.Values.Select(binding => binding.ContentId).Distinct(StringComparer.Ordinal).Count() != this.inventoryBindings.Count ||
            this.currencyBindings.Values.Any(binding => !Regex.IsMatch(binding.Code ?? "", "^[A-Z]{2}$") || !Regex.IsMatch(binding.ContentId ?? "", IdentifierPattern)) ||
            this.currencyBindings.Values.Select(binding => binding.ContentId).Distinct(StringComparer.Ordinal).Count() != this.currencyBindings.Count)
            throw new ArgumentException("Legacy mappings must be explicit and unique.");
    }

    public Task<PlayerEntity> ResolveAsync(string verifiedAccountId, CancellationToken ct) => profiles.ResolveAsync(verifiedAccountId, ct);

    public Task<StoredProfile> ReadProfileAsync(PlayerEntity actor, CancellationToken ct) => profiles.ReadProfileAsync(actor, ct);

    public Task<StoredProfile> WriteProfileAsync(PlayerEntity actor, PlayerProfile profile, int expectedVersion, CancellationToken ct) => profiles.WriteProfileAsync(actor, profile, expectedVersion, ct);

    public async Task<InventorySnapshot> ReadInventoryAsync(PlayerEntity actor, CancellationToken ct)
    {
        if (!Regex.IsMatch(actor.AccountId ?? "", "^[A-Za-z0-9]{5,32}$")) throw new MetaFailureException("invalid_identity");
        var owned = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var balances = ImmutableDictionary.CreateBuilder<string, long>(StringComparer.Ordinal);
        foreach (var binding in currencyBindings.Values) balances[binding.ContentId] = 0;
        var instances = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var data = await transport.CallAsync("Server/GetUserInventory", new { PlayFabId = actor.AccountId }, ct);
            if (data.GetProperty("PlayFabId").GetString() != actor.AccountId) throw new MetaFailureException("invalid_inventory");
            var inventory = data.GetProperty("Inventory");
            var currency = data.GetProperty("VirtualCurrency");
            if (inventory.ValueKind != JsonValueKind.Array || currency.ValueKind != JsonValueKind.Object) throw new MetaFailureException("invalid_inventory");

            foreach (var value in currency.EnumerateObject())
            {
                var amount = value.Value.GetInt64();
                if (amount is < 0 or > int.MaxValue) throw new MetaFailureException("invalid_inventory");
                if (currencyBindings.TryGetValue(value.Name, out var binding)) balances[binding.ContentId] = amount;
            }

            foreach (var item in inventory.EnumerateArray())
            {
                var instanceId = item.GetProperty("ItemInstanceId").GetString();
                var itemId = item.GetProperty("ItemId").GetString();
                var catalogVersion = item.GetProperty("CatalogVersion").GetString();
                if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(catalogVersion) || !instances.Add(instanceId))
                    throw new MetaFailureException("invalid_inventory");
                if (!inventoryBindings.TryGetValue(itemId, out var binding) || catalogVersion != this.catalogVersion) continue;
                if (item.TryGetProperty("RemainingUses", out var remaining) && remaining.ValueKind != JsonValueKind.Null)
                {
                    var uses = remaining.GetInt32();
                    if (uses < 0) throw new MetaFailureException("invalid_inventory");
                    if (uses == 0) continue;
                }
                if (item.TryGetProperty("Expiration", out var expiration) && expiration.ValueKind != JsonValueKind.Null)
                {
                    var expiresAt = expiration.GetDateTimeOffset();
                    if (expiresAt <= DateTimeOffset.UtcNow) continue;
                }
                owned.Add(binding.ContentId);
            }

            var frozenOwned = owned.ToImmutable();
            var frozenBalances = balances.ToImmutable();
            return new InventorySnapshot(Fingerprint(frozenOwned, frozenBalances), null, frozenOwned, frozenBalances);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException or JsonException or OverflowException)
        {
            throw new MetaFailureException("invalid_inventory");
        }
    }

    static string Fingerprint(ImmutableHashSet<string> owned, ImmutableDictionary<string, long> balances)
    {
        var canonical = new StringBuilder();
        foreach (var id in owned.Order(StringComparer.Ordinal)) canonical.Append("owned:").Append(id).Append('\n');
        foreach (var balance in balances.OrderBy(pair => pair.Key, StringComparer.Ordinal)) canonical.Append("balance:").Append(balance.Key).Append(':').Append(balance.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        return "legacy:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }
}
