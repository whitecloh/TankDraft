using System.Security.Cryptography;
using System.Text.Json;
using TankDraft.Server.Meta;

namespace TankDraft.RemoteHost;

/// <summary>Explicit private closed-QA opt-in. No catalog writes, inventory grants or provider policy mutations.</summary>
internal static class PlayFabMetaBootstrap
{
    internal sealed record Settings(string Mode, bool PlayerWritePolicyVerified,
        string? CollectionId = null, InventoryContentBinding[]? Bindings = null,
        string? CatalogVersion = null, LegacyInventoryContentBinding[]? LegacyBindings = null,
        LegacyCurrencyBinding[]? Currencies = null, bool ProgressionEnabled = false);

    internal static PlayFabMetaTransport Configure(RemoteMatchService service, string settingsPath, string serverSecret)
    {
        if (!Path.IsPathFullyQualified(settingsPath)) throw new InvalidDataException("Meta settings require an absolute private path.");
        var info = new FileInfo(settingsPath);
        if (!info.Exists || info.Length > 65536 || info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException("Meta settings unavailable.");
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllBytes(settingsPath), AuthoredContent.Json) ?? throw new InvalidDataException();
        ValidateSettings(settings);
        var rulesPath = Path.Combine(AppContext.BaseDirectory, "Content", "meta-rules.json");
        var bytes = File.ReadAllBytes(rulesPath);
        if (bytes.Length > 65536 || Convert.ToHexStringLower(SHA256.HashData(bytes)) != File.ReadAllText(Path.ChangeExtension(rulesPath, ".sha256")).Trim())
            throw new InvalidDataException("Authored meta content hash mismatch.");
        var rules = JsonSerializer.Deserialize<MetaRules>(bytes, AuthoredContent.Json) ?? throw new InvalidDataException();
        if (rules.ContentVersion != service.ContentVersion) throw new InvalidDataException("Meta/battle content mismatch.");
        var transport = new PlayFabMetaTransport("B16D9", serverSecret);
        try
        {
            IPlayerDataStore store;
            Func<string, CancellationToken, Task<PlayerEntity>> resolve;
            if (settings.Mode == "PlayFabLegacyClosedQa")
            {
                var legacy = new PlayFabLegacyPlayerDataStore(transport, settings.CatalogVersion!, settings.LegacyBindings!, settings.Currencies!);
                store = legacy;
                resolve = legacy.ResolveAsync;
            }
            else
            {
                var v2 = new PlayFabPlayerDataStore(transport, settings.Bindings!, settings.CollectionId!);
                store = v2;
                resolve = v2.ResolveAsync;
            }
            var meta = new PlayerMetaService(store, rules);
            service.ConfigureMeta(meta, resolve);
            if (settings.ProgressionEnabled)
            {
                if (settings.Mode != "PlayFabLegacyClosedQa") throw new InvalidDataException("Progression wallet requires reviewed Legacy adapter.");
                service.EnablePlayFabProgression(transport, Path.GetDirectoryName(settingsPath)!);
            }
            return transport;
        }
        catch { transport.Dispose(); throw; }
    }

    internal static void ValidateSettings(Settings settings)
    {
        var valid = settings.Mode switch
        {
            "PlayFabLegacyClosedQa" => settings.CatalogVersion is { Length: > 0 } && settings.LegacyBindings is { Length: > 0 }
                && settings.Currencies is not null && settings.CollectionId is null && settings.Bindings is null,
            "PlayFabClosedQa" => settings.CollectionId is { Length: > 0 } && settings.Bindings is { Length: > 0 }
                && settings.CatalogVersion is null && settings.LegacyBindings is null && settings.Currencies is null,
            _ => false
        };
        if (!valid || !settings.PlayerWritePolicyVerified)
            throw new InvalidDataException("Select one explicit reviewed PlayFab economy adapter; mixed or implicit fallback is not supported.");
    }
}
