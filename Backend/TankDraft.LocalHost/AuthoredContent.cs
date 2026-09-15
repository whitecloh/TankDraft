using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Domain;

namespace TankDraft.LocalHost;

public sealed class AuthoredContent
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

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = false,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };

    static AuthoredContent() => TankDraft.Server.Match.ServerMatchJson.Register(Json);

    public static (AuthoredContent Content, string Version) Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Content", "local-match.json");
        var bytes = File.ReadAllBytes(path);
        var version = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var expected = File.ReadAllText(Path.ChangeExtension(path, ".sha256")).Trim();
        if (!string.Equals(version, expected, StringComparison.Ordinal))
            throw new InvalidDataException("Authored content hash mismatch.");
        var content = JsonSerializer.Deserialize<AuthoredContent>(bytes, Json)
            ?? throw new InvalidDataException("Authored content missing.");
        if (content.SchemaVersion != 1 || content.MatchRules is null || content.BattleRules is null || content.BattleRules.AllowDebugCommands ||
            !MatchService.IsSupportedDeck(content.Deck, content.MatchUnits))
            throw new InvalidDataException("Authored content schema or deck invalid.");
        var definitions = content.CreateDefinitions();
        foreach (var unit in content.MatchUnits) _ = definitions.Unit(unit.Id);
        foreach (var unitId in content.Deck) _ = definitions.Unit(unitId);
        if (content.OrderId is not "" and not "order.reinforce_armor")
            throw new InvalidDataException("Order not supported by the current match domain.");
        return (content, version);
    }

    public BattleDefinitions CreateDefinitions() => new(Units, Projectiles, Zones);
    public MatchService CreateMatch() => new(MatchRules, MatchUnits, Deck, Deck, OrderId, Seed, OrderId);
}
