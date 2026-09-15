using System.Collections.Frozen;
using System.Text.Json;
using System.Text.RegularExpressions;
using TankDraft.Server.Settlement;

namespace TankDraft.Server.Meta;

/// <summary>Bounded Legacy writer. Only preflight reads retry; an ambiguous write never retries automatically.</summary>
public sealed class PlayFabLegacyRewardProvider : IRewardProvider
{
    readonly PlayFabMetaTransport transport;
    readonly IReadOnlySet<string> allowedAccounts;
    readonly string currency;
    readonly int maximumAmount;

    public PlayFabLegacyRewardProvider(PlayFabMetaTransport transport, IReadOnlySet<string> allowedAccounts, string currency, int maximumAmount)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        ArgumentNullException.ThrowIfNull(allowedAccounts);
        this.allowedAccounts = allowedAccounts.ToFrozenSet(StringComparer.Ordinal);
        if (allowedAccounts.Count is < 1 or > 4 || allowedAccounts.Any(account => !IsAccount(account)))
            throw new ArgumentException("Legacy reward accounts must be an explicit bounded allowlist.", nameof(allowedAccounts));
        if (!IsCurrency(currency)) throw new ArgumentException("Legacy reward currency must be two uppercase letters.", nameof(currency));
        if (maximumAmount is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maximumAmount));
        this.currency = currency;
        this.maximumAmount = maximumAmount;
    }

    public async Task GrantAsync(RewardGrant grant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grant);
        if (!IsAccount(grant.AccountId) || !allowedAccounts.Contains(grant.AccountId) ||
            grant.Currency != currency || !IsCurrency(grant.Currency) ||
            grant.Amount < 1 || grant.Amount > maximumAmount || !IsResultId(grant.ResultId))
            throw new MetaFailureException("provider_unavailable");

        try
        {
            var inventory = await ReadInventoryWithRetryAsync(grant.AccountId, cancellationToken).ConfigureAwait(false);
            var before = ReadBalance(inventory, grant.AccountId, currency);
            if (before > int.MaxValue - grant.Amount) throw new MetaFailureException("provider_unavailable");

            var result = await transport.CallAsync("Server/AddUserVirtualCurrency", new
            {
                PlayFabId = grant.AccountId,
                VirtualCurrency = currency,
                Amount = grant.Amount,
                CustomTags = new Dictionary<string, string>(StringComparer.Ordinal) { ["tankdraftResultId"] = grant.ResultId }
            }, cancellationToken).ConfigureAwait(false);

            ValidateWrite(result, grant);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (MetaFailureException) { throw; }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException or FormatException or JsonException or OverflowException)
        {
            throw new MetaFailureException("provider_unavailable");
        }
    }

    async Task<JsonElement> ReadInventoryWithRetryAsync(string accountId, CancellationToken cancellationToken)
    {
        // These are reads only. Keep the total work bounded even during an extended outage.
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await transport.CallAsync("Server/GetUserInventory", new { PlayFabId = accountId }, cancellationToken).ConfigureAwait(false);
            }
            catch (MetaFailureException failure) when (failure.Code == "provider_unavailable" && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(attempt == 0 ? 250 : 750), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    static int ReadBalance(JsonElement data, string account, string currency)
    {
        if (data.GetProperty("PlayFabId").GetString() != account) throw new MetaFailureException("provider_unavailable");
        var balances = data.GetProperty("VirtualCurrency");
        if (balances.ValueKind != JsonValueKind.Object || !balances.TryGetProperty(currency, out var balance) || balance.ValueKind != JsonValueKind.Number)
            throw new MetaFailureException("provider_unavailable");
        var value = balance.GetInt64();
        if (value is < 0 or > int.MaxValue) throw new MetaFailureException("provider_unavailable");
        return (int)value;
    }

    static void ValidateWrite(JsonElement data, RewardGrant grant)
    {
        if (data.GetProperty("PlayFabId").GetString() != grant.AccountId || data.GetProperty("VirtualCurrency").GetString() != grant.Currency)
            throw new MetaFailureException("provider_unavailable");
        var balanceChange = data.GetProperty("BalanceChange").GetInt64();
        var balance = data.GetProperty("Balance").GetInt64();
        // The earlier inventory read is not a transaction snapshot: other legitimate operations
        // may change the balance. Verify this write's delta, not a guessed global balance.
        if (balanceChange != grant.Amount || balance is < 0 or > int.MaxValue)
            throw new MetaFailureException("provider_unavailable");
    }

    static bool IsAccount(string? value) => Regex.IsMatch(value ?? string.Empty, "^[A-Za-z0-9]{5,32}$");
    static bool IsCurrency(string? value) => Regex.IsMatch(value ?? string.Empty, "^[A-Z]{2}$");
    static bool IsResultId(string? value) => Regex.IsMatch(value ?? string.Empty, "^[a-f0-9]{64}$");
}
