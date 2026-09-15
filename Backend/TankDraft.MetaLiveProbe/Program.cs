using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using TankDraft.Server.Meta;

// Explicit manual QA tool. Never runs as part of tests or game startup.
if (args is ["--inspect-qa-settlement"]) return await InspectQaSettlement.RunAsync();
if (args.Length > 0 && args[0] == "--review-qa-settlement") return ReviewQaSettlement.Run(args[1..]);
if (args is not ["--existing-two-qa-legacy"])
{
    Console.Error.WriteLine("Requires --existing-two-qa-legacy. Uses existing B16D9 QA accounts and saves/restores their loadouts.");
    return 2;
}
try
{
    var root = Directory.GetCurrentDirectory();
    var privateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-secrets", "TankDraft");
    var options = new JsonSerializerOptions { AllowDuplicateProperties = false, MaxDepth = 16 };
    var settingsBytes = File.ReadAllBytes(Path.Combine(privateRoot, "playfab-legacy-meta.json"));
    using var settings = JsonDocument.Parse(settingsBytes);
    var config = settings.RootElement;
    if (config.GetProperty("Mode").GetString() != "PlayFabLegacyClosedQa" || !config.GetProperty("PlayerWritePolicyVerified").GetBoolean())
        throw new InvalidOperationException("Legacy player policy not verified.");
    var accounts = File.ReadAllText(Path.Combine(privateRoot, "remote-test-allowlist.txt")).Split(',').Select(x => x.Trim()).ToArray();
    if (accounts.Length != 2 || accounts.Distinct().Count() != 2 || accounts.Any(x => !Regex.IsMatch(x, "^[A-Fa-f0-9]{5,32}$")))
        throw new InvalidOperationException("Existing two QA accounts required.");
    var bytes = File.ReadAllBytes(Path.Combine(root, "Backend", "Content", "meta-rules.json"));
    if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != File.ReadAllText(Path.Combine(root, "Backend", "Content", "meta-rules.sha256")).Trim())
        throw new InvalidDataException("Rules hash mismatch.");
    var rules = JsonSerializer.Deserialize<MetaRules>(bytes, options) ?? throw new InvalidDataException();
    var bindings = config.GetProperty("LegacyBindings").Deserialize<LegacyInventoryContentBinding[]>(options)!;
    var currencies = config.GetProperty("Currencies").Deserialize<LegacyCurrencyBinding[]>(options)!;
    using var transport = new PlayFabMetaTransport("B16D9", File.ReadAllText(Path.Combine(privateRoot, "playfab-server-key.txt")).Trim());
    var store = new PlayFabLegacyPlayerDataStore(transport, config.GetProperty("CatalogVersion").GetString()!, bindings, currencies);
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    var ct = timeout.Token;
    var results = new List<object>();
    foreach (var account in accounts)
    {
        var actor = await store.ResolveAsync(account, ct);
        var service = new PlayerMetaService(store, rules);
        var before = await service.GetAsync(actor, ct);
        var changedUnits = before.Profile.UnitIds.Reverse().ToArray();
        var operation = Guid.NewGuid();
        var changed = await service.SaveLoadoutAsync(actor, rules.ContentVersion, before.ProfileVersion, operation, changedUnits, before.Profile.OrderIds, ct);
        var retry = await service.SaveLoadoutAsync(actor, rules.ContentVersion, before.ProfileVersion, operation, changedUnits, before.Profile.OrderIds, ct);
        Require(retry.ProfileVersion == changed.ProfileVersion && retry.Profile.UnitIds.SequenceEqual(changedUnits), "Exact retry failed.");
        var reloaded = await new PlayerMetaService(store, rules).GetAsync(actor, ct);
        Require(reloaded.Profile.UnitIds.SequenceEqual(changedUnits), "Persistence failed.");
        await Reject(async () => await service.SaveLoadoutAsync(actor, rules.ContentVersion, before.ProfileVersion, Guid.NewGuid(), before.Profile.UnitIds, before.Profile.OrderIds, ct), "version_conflict");
        await Reject(async () => await store.WriteProfileAsync(actor, before.Profile, before.ProfileVersion, ct), "version_conflict");
        var invalid = changedUnits.ToArray(); invalid[0] = "unit.repair_vehicle";
        await Reject(async () => await service.SaveLoadoutAsync(actor, rules.ContentVersion, changed.ProfileVersion, Guid.NewGuid(), invalid, before.Profile.OrderIds, ct), "not_owned");
        var restored = await service.SaveLoadoutAsync(actor, rules.ContentVersion, changed.ProfileVersion, Guid.NewGuid(), before.Profile.UnitIds, before.Profile.OrderIds, ct);
        var queue = await service.RevalidateForMatchAsync(actor, rules.ContentVersion, ct);
        Require(queue.Profile.UnitIds.SequenceEqual(before.Profile.UnitIds), "Queue revalidation failed.");
        Require(restored.Inventory.SourceVersion.StartsWith("legacy:") && restored.Inventory.ETag is null, "Inventory source incorrect.");
        results.Add(new { Owned = restored.Inventory.OwnedContentIds.Count, Balances = restored.Inventory.Balances, ProfileVersion = restored.ProfileVersion,
            Saved = true, Reloaded = true, ExactRetry = true, StaleServiceRejected = true, ProviderCasRejected = true, UnownedRejected = true, LoadoutRestored = true, QueueRevalidated = true });
    }
    var report = JsonSerializer.Serialize(new { TitleId = "B16D9", Utc = DateTimeOffset.UtcNow, Legacy = true, QaAccounts = 2, Results = results }, new JsonSerializerOptions { WriteIndented = true });
    Directory.CreateDirectory(Path.Combine(root, "Logs", "MetaLegacy"));
    File.WriteAllText(Path.Combine(root, "Logs", "MetaLegacy", "live-profile.json"), report);
    Console.WriteLine(report);
    return 0;
}
catch (MetaFailureException e) { Console.Error.WriteLine("Legacy live QA failed: " + e.Code); return 1; }
catch (Exception e) { Console.Error.WriteLine("Legacy live QA failed (details suppressed): " + e.GetType().Name); return 1; }

static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static async Task Reject(Func<Task> action, string code)
{
    try { await action(); }
    catch (MetaFailureException e) when (e.Code == code) { return; }
    throw new InvalidOperationException("Expected rejection: " + code);
}
