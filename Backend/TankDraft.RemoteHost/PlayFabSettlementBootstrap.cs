using System.Text.Json;
using TankDraft.Server.Meta;
using TankDraft.Server.Settlement;

namespace TankDraft.RemoteHost;

internal static class PlayFabSettlementBootstrap
{
    internal sealed record Settings(string Mode, string DatabasePath, RewardPolicy Policy);

    internal static void Configure(RemoteMatchService service, string settingsPath, string metaSettingsPath,
        PlayFabMetaTransport transport, IReadOnlySet<string> accounts)
    {
        if (!Path.IsPathFullyQualified(settingsPath)) throw new InvalidDataException("Private settlement settings required.");
        var info = new FileInfo(settingsPath);
        if (!info.Exists || info.Length > 16384 || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Settlement settings unavailable.");
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllBytes(settingsPath), AuthoredContent.Json) ?? throw new InvalidDataException();
        var meta = JsonSerializer.Deserialize<PlayFabMetaBootstrap.Settings>(File.ReadAllBytes(metaSettingsPath), AuthoredContent.Json) ?? throw new InvalidDataException();
        PlayFabMetaBootstrap.ValidateSettings(meta);
        // This opt-in implements only tiny Legacy QA rewards, not purchases or a general economy endpoint.
        if (settings.Mode != "LegacyTestRewards" || meta.Mode != "PlayFabLegacyClosedQa" ||
            settings.Policy is null || settings.Policy.Version != "qa-r32-v1" || settings.Policy.Currency != "CO" ||
            settings.Policy.WinAmount != 1 || settings.Policy.LossAmount != 1 || settings.Policy.MaximumGrantsPerAccount != 10 ||
            accounts.Count is < 1 or > 4 || !Path.IsPathFullyQualified(settings.DatabasePath))
            throw new InvalidDataException("Unreviewed QA reward configuration.");
        var privateRoot = Path.GetFullPath(Path.GetDirectoryName(settingsPath)!) + Path.DirectorySeparatorChar;
        var databasePath = Path.GetFullPath(settings.DatabasePath);
        if (!databasePath.StartsWith(privateRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Settlement database must remain under the private settings directory.");
        var store = new SqliteSettlementStore(databasePath, settings.Policy);
        try { service.ConfigureSettlement(store, new PlayFabLegacyRewardProvider(transport, accounts, "CO", 1)); }
        catch { store.Dispose(); throw; }
    }
}
