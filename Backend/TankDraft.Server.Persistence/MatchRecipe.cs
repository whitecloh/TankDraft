using System.Security.Cryptography;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Domain;
using TankDraft.Server.Match;
using TankDraft.Server.Security;

namespace TankDraft.Server.Persistence;

// Self-contained replay recipe: authored bytes, policies, roster and exact executing build.
public sealed record MatchRecipe(string MatchId, string ContentVersion, string BuildVersion, string ContentJson,
    ServerMatchSettings Settings, uint AutoChoiceSeed, string[] Accounts)
{
    public string[]? Deck0 { get; init; }
    public string[]? Deck1 { get; init; }
    public string? Order0 { get; init; }
    public string? Order1 { get; init; }
    public const string RecoveryPolicy = "PauseProcessDowntime";
    public string Encode() => PersistenceJson.Encode(this);

    public static string CurrentBuildVersion()
    {
        var assemblies = new[] { typeof(MatchService).Assembly, typeof(ServerMatchRuntime).Assembly,
            typeof(CommandEnvelope).Assembly, typeof(MatchRecipe).Assembly };
        return PersistenceJson.Hash(string.Join("|", assemblies.Select(assembly =>
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location))))));
    }

    public ServerMatchRuntime CreateRuntime(TimeProvider clock)
    {
        if (BuildVersion != CurrentBuildVersion() || PersistenceJson.Hash(ContentJson) != ContentVersion)
            throw new InvalidDataException("Replay build/content version mismatch.");
        var content = PersistenceJson.Decode<ReplayContent>(ContentJson);
        var deck0 = Deck0 ?? content.Deck; var deck1 = Deck1 ?? content.Deck;
        var order0 = Order0 ?? content.OrderId; var order1 = Order1 ?? content.OrderId;
        if (content.SchemaVersion != 1 || content.BattleRules is null || content.MatchRules is null ||
            content.BattleRules.AllowDebugCommands || !MatchService.IsSupportedDeck(deck0, content.MatchUnits) ||
            !MatchService.IsSupportedDeck(deck1, content.MatchUnits) || order0 is not "" and not "order.reinforce_armor" ||
            order1 is not "" and not "order.reinforce_armor")
            throw new InvalidDataException("Invalid persisted authored content.");
        var definitions = new BattleDefinitions(content.Units, content.Projectiles, content.Zones);
        foreach (var unit in content.MatchUnits) _ = definitions.Unit(unit.Id);
        var match = new MatchService(content.MatchRules, content.MatchUnits, deck0, deck1, order0, content.Seed, order1);
        return new ServerMatchRuntime(MatchId, ContentVersion, match, definitions, content.BattleRules,
            Settings, clock, AutoChoiceSeed, Accounts);
    }

    private sealed class ReplayContent
    {
        public int SchemaVersion { get; init; }
        public string SourceAsset { get; init; } = "";
        public MatchRules MatchRules { get; init; } = null!;
        public MatchUnitDefinition[] MatchUnits { get; init; } = [];
        public BattleRules BattleRules { get; init; } = null!;
        public BattleUnitDefinition[] Units { get; init; } = [];
        public BattleProjectileDefinition[] Projectiles { get; init; } = [];
        public BattleZoneDefinition[] Zones { get; init; } = [];
        public string[] Deck { get; init; } = [];
        public string OrderId { get; init; } = "";
        public uint Seed { get; init; }
    }
}
