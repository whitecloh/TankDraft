using System.Collections.Immutable;
using TankDraft.Match.Domain;
using TankDraft.Server.Meta;

namespace TankDraft.RemoteHost;

public sealed partial class RemoteMatchService
{
    PlayerMetaService? meta;
    Func<string, CancellationToken, Task<PlayerEntity>>? resolveMetaIdentity;
    readonly HashSet<string> metaPending = new(StringComparer.Ordinal);
    int metaCalls;
    public bool MetaEnabled => meta is not null;

    internal void ConfigureMeta(PlayerMetaService service, Func<string, CancellationToken, Task<PlayerEntity>> resolver)
    {
        lock (sync)
        {
            if (meta is not null || lobbies.Count != 0) throw new InvalidOperationException("Configure meta before accepting clients.");
            meta = service; resolveMetaIdentity = resolver;
        }
    }

    string BeginMeta(string lobby, string version, bool editing)
    {
        lock (sync)
        {
            var account = UseLobby(lobby);
            if (version != ContentVersion) throw new MetaFailureException("content_version");
            if (editing && (queue.ContainsKey(account) || accountMatches.ContainsKey(account))) throw new MetaFailureException("match_active");
            if (metaPending.Count >= 4 || !metaPending.Add(account)) throw new MetaFailureException("busy");
            if (++metaCalls > 1000) { metaPending.Remove(account); throw new MetaFailureException("meta_capacity"); }
            return account;
        }
    }
    void EndMeta(string account) { lock (sync) metaPending.Remove(account); }

    public async Task<object> GetProfileAsync(string lobby, string version, CancellationToken ct)
    {
        var account = BeginMeta(lobby, version, false);
        try
        {
            if (meta is null) return new { Enabled = false };
            var actor = await resolveMetaIdentity!(account, ct);
            if (actor.AccountId != account) throw new UnauthorizedAccessException();
            object[] history;
            lock (sync) { history = SettlementHistory(account); }
            var snapshot = await meta.GetAsync(actor, ct);
            lock (sync) { if (UseLobby(lobby) != account) throw new UnauthorizedAccessException(); }
            // Do not label a reward Applied using a ledger transition that happened AFTER the balance read.
            // A pending projection may lag one refresh, which is safer than promising a fresh wallet snapshot.
            return MetaReply(snapshot, history);
        }
        finally { EndMeta(account); }
    }

    public async Task<object> SaveProfileAsync(string lobby, string version, int expectedVersion, string operationId, string[] units, string[] orders, CancellationToken ct)
    {
        var account = BeginMeta(lobby, version, true);
        try
        {
            if (meta is null) throw new MetaFailureException("meta_disabled");
            var actor = await resolveMetaIdentity!(account, ct);
            if (actor.AccountId != account) throw new UnauthorizedAccessException();
            if (!Guid.TryParseExact(operationId, "N", out var operation)) throw new MetaFailureException("invalid_operation");
            var snapshot = await meta.SaveLoadoutAsync(actor, version, expectedVersion, operation, units.ToImmutableArray(), orders.ToImmutableArray(), ct);
            lock (sync) { if (UseLobby(lobby) != account) throw new UnauthorizedAccessException(); }
            return MetaReply(snapshot, SettlementHistory(account));
        }
        finally { EndMeta(account); }
    }

    public async Task<QueueStatus> JoinAsync(string lobby, string operationId, string version, CancellationToken ct)
    {
        if (meta is null) return Join(lobby, operationId, version);
        CheckOperation(operationId);
        var account = BeginMeta(lobby, version, false);
        try
        {
            lock (sync)
            {
                // Existing assignment/queue already captures its validated profile. Never replace it on a retry.
                if (queue.ContainsKey(account) || accountMatches.ContainsKey(account)) return JoinValidated(lobby, operationId, version, null);
            }
            var actor = await resolveMetaIdentity!(account, ct);
            if (actor.AccountId != account) throw new UnauthorizedAccessException();
            var snapshot = await meta.RevalidateForMatchAsync(actor, version, ct);
            var profile = snapshot.Profile;
            if (!MatchService.IsSupportedDeck(profile.UnitIds.ToArray(), content.MatchUnits) || profile.OrderIds.Count(x => x.Length != 0) > 1 || profile.OrderIds.Any(x => x.Length != 0 && x != content.OrderId))
                throw new MetaFailureException("unsupported_battle_loadout");
            ct.ThrowIfCancellationRequested();
            var bonuses = progression is null ? null : ProgressionBonuses(await progression.GetAsync(account, ct), profile.UnitIds);
            return JoinValidated(lobby, operationId, version, profile, bonuses);
        }
        finally { EndMeta(account); }
    }

    // Never expose account identifiers, provider receipts or inventory credentials on the gameplay connection.
    static object MetaReply(MetaSnapshot snapshot, object[] results) => new
    {
        Enabled = true, snapshot.ProfileVersion,
        RecentResults = results,
        Profile = new { snapshot.Profile.SchemaVersion, snapshot.Profile.Name, snapshot.Profile.CommanderLevel, snapshot.Profile.ArenaLevel,
            snapshot.Profile.ArenaProgress, snapshot.Profile.Mastery, snapshot.Profile.UnitIds, snapshot.Profile.OrderIds },
        Inventory = new { snapshot.Inventory.OwnedContentIds, Balances = new
        {
            energy = snapshot.Inventory.Balances.GetValueOrDefault("energy"),
            gems = snapshot.Inventory.Balances.GetValueOrDefault("gems"),
            coins = snapshot.Inventory.Balances.GetValueOrDefault("coins")
        } }
    };
}
