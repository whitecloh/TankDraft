using System.Text.Json;
using TankDraft.Server.Meta;
using TankDraft.Server.Progression;

namespace TankDraft.RemoteHost;

// Additive separate object: never rewrites/migrates the existing tankdraft_profile_v1 or inventory.
internal sealed class PlayFabProgressionStore(PlayFabMetaTransport transport,
    Func<string, CancellationToken, Task<PlayerEntity>> resolve, IReadOnlySet<string> allowlist) : IProgressionStore
{
    const string ObjectName = "tankdraft_progression_v1";
    async Task<object> Entity(string account, CancellationToken ct)
    {
        if (!allowlist.Contains(account)) throw new UnauthorizedAccessException();
        var actor = await resolve(account, ct);
        if (actor.AccountId != account) throw new UnauthorizedAccessException();
        return new { Id = actor.EntityId, Type = "title_player_account" };
    }
    public async Task<StoredProgression> ReadAsync(string accountId, CancellationToken ct)
    {
        var data = await transport.CallAsync("Object/GetObjects", new { Entity = await Entity(accountId, ct), EscapeObject = false }, ct);
        int version = data.GetProperty("ProfileVersion").GetInt32();
        if (version < 0) throw new InvalidDataException("Invalid progression version.");
        if (!data.GetProperty("Objects").TryGetProperty(ObjectName, out var obj)) return new(version, null);
        var raw = obj.GetProperty("DataObject");
        if (raw.GetRawText().Length > 16384) throw new InvalidDataException("Progression object exceeds bounds.");
        return new(version, raw.Deserialize<ProgressionState>(AuthoredContent.Json) ?? throw new InvalidDataException("Invalid progression object."));
    }
    public async Task WriteAsync(string accountId, ProgressionState state, int expectedVersion, CancellationToken ct)
    {
        if (expectedVersion < 0 || JsonSerializer.SerializeToUtf8Bytes(state).Length > 16384) throw new InvalidDataException("Progression object exceeds bounds.");
        try
        {
            var data = await transport.CallAsync("Object/SetObjects", new { Entity = await Entity(accountId, ct), ExpectedProfileVersion = expectedVersion,
                Objects = new[] { new { ObjectName, DataObject = state } } }, ct);
            if (data.GetProperty("ProfileVersion").GetInt32() < expectedVersion ||
                !data.GetProperty("SetResults").EnumerateArray().Any(r => r.GetProperty("ObjectName").GetString() == ObjectName && r.GetProperty("SetResult").GetString() is "Created" or "Updated" or "None"))
                throw new InvalidDataException("Invalid progression acknowledgement.");
        }
        catch (MetaFailureException e) when (e.Code == "version_conflict") { throw new ProgressionConflictException(); }
    }
}

internal sealed class PlayFabProgressionWallet(PlayFabMetaTransport transport, IReadOnlySet<string> allowlist) : IProgressionWallet
{
    static readonly HashSet<string> Currencies = ["CO", "GM"];
    void Validate(string account, string currency, int amount = 0)
    {
        if (!allowlist.Contains(account) || !Currencies.Contains(currency) || amount is < 0 or > 100000) throw new UnauthorizedAccessException();
    }
    public async Task<int> ReadAsync(string accountId, string currency, CancellationToken ct)
    {
        Validate(accountId, currency);
        var data = await transport.CallAsync("Server/GetUserInventory", new { PlayFabId = accountId }, ct);
        if (data.GetProperty("PlayFabId").GetString() != accountId) throw new InvalidDataException();
        var value = data.GetProperty("VirtualCurrency").TryGetProperty(currency, out var balance) ? balance.GetInt32() : 0;
        return value >= 0 ? value : throw new InvalidDataException();
    }
    public Task DebitAsync(string accountId, string currency, int amount, CancellationToken ct) => Change(accountId, currency, amount, false, ct);
    public Task CreditAsync(string accountId, string currency, int amount, CancellationToken ct) => Change(accountId, currency, amount, true, ct);
    async Task Change(string account, string currency, int amount, bool credit, CancellationToken ct)
    {
        Validate(account, currency, amount);
        if (amount == 0) return;
        // Exactly one HTTP attempt. The durable progression journal owns uncertainty and recovery.
        var data = await transport.CallAsync(credit ? "Server/AddUserVirtualCurrency" : "Server/SubtractUserVirtualCurrency",
            new { PlayFabId = account, VirtualCurrency = currency, Amount = amount }, ct);
        if (data.GetProperty("PlayFabId").GetString() != account || data.GetProperty("VirtualCurrency").GetString() != currency ||
            data.GetProperty("BalanceChange").GetInt32() != (credit ? amount : -amount) || data.GetProperty("Balance").GetInt32() < 0)
            throw new InvalidDataException("Uncertain wallet outcome.");
    }
}
