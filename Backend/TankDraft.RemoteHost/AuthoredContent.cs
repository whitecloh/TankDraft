using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Domain;

namespace TankDraft.RemoteHost;

internal sealed class AuthoredContent
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
    internal static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = false, AllowDuplicateProperties = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 16 };
    static AuthoredContent() => TankDraft.Server.Match.ServerMatchJson.Register(Json);
    public static (AuthoredContent Content, string Version) Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Content", "local-match.json"); var bytes = File.ReadAllBytes(path);
        var version = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(version, File.ReadAllText(Path.ChangeExtension(path, ".sha256")).Trim(), StringComparison.Ordinal)) throw new InvalidDataException("Authored content hash mismatch.");
        var value = JsonSerializer.Deserialize<AuthoredContent>(bytes, Json) ?? throw new InvalidDataException("Authored content missing.");
        if (value.SchemaVersion != 1 || value.MatchRules is null || value.BattleRules is null || value.BattleRules.AllowDebugCommands || !MatchService.IsSupportedDeck(value.Deck, value.MatchUnits)) throw new InvalidDataException("Authored content invalid.");
        return (value, version);
    }
    public MatchService CreateMatch(uint? matchSeed = null) => new(MatchRules, MatchUnits, Deck, Deck, OrderId, matchSeed ?? Seed, OrderId);
    public BattleDefinitions Definitions() => new(Units, Projectiles, Zones);
}
