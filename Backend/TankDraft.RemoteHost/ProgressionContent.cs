using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using TankDraft.Server.Progression;

namespace TankDraft.RemoteHost;

internal sealed record ProgressionContent(string Version, string BalanceStatus, UnitDefinition[] Units, LevelStep[] Levels,
    MasteryStep[] Mastery, int WinXp, int LossXp, int BoostChipCost, int BoostXp, Dictionary<int,int> MasteryCosts,
    OfferDefinition[] Offers, PackDefinition[] Packs)
{
    public ProgressionRules Rules() => new(Version, Units, Levels, Mastery, WinXp, LossXp, BoostChipCost, BoostXp, MasteryCosts, Offers, Packs);
    public static ProgressionContent Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Content", "progression-rules.json");
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > 65536 || Convert.ToHexStringLower(SHA256.HashData(bytes)) != File.ReadAllText(Path.ChangeExtension(path, ".sha256")).Trim())
            throw new InvalidDataException("Progression content hash mismatch.");
        return JsonSerializer.Deserialize<ProgressionContent>(bytes, AuthoredContent.Json) ?? throw new InvalidDataException();
    }
}
