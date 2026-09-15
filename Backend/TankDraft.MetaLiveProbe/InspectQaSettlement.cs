using System.Text.Json;
using TankDraft.Server.Meta;
using TankDraft.Server.Settlement;

internal static class InspectQaSettlement
{
    // Read-only provider inspection. Run with the local manager stopped: the store enforces exclusive ownership.
    internal static async Task<int> RunAsync()
    {
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-secrets", "TankDraft");
            using var settings = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "playfab-settlement-qa.json")));
            var config = settings.RootElement;
            if (config.GetProperty("Mode").GetString() != "LegacyTestRewards") throw new InvalidDataException();
            var path = config.GetProperty("DatabasePath").GetString()!;
            if (!File.Exists(path)) throw new InvalidDataException("Existing journal required.");
            var policy = config.GetProperty("Policy").Deserialize<RewardPolicy>()!;
            using var store = new SqliteSettlementStore(path, policy);
            var accounts = File.ReadAllText(Path.Combine(root, "remote-test-allowlist.txt")).Trim().Split(',').Select(x => x.Trim()).ToArray();
            if (accounts.Length != 2) throw new InvalidDataException();
            using var transport = new PlayFabMetaTransport("B16D9", File.ReadAllText(Path.Combine(root, "playfab-server-key.txt")).Trim());
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
            var results = new List<object>();
            foreach (var account in accounts)
            {
                var inventory = await transport.CallAsync("Server/GetUserInventory", new { PlayFabId = account }, timeout.Token);
                if (inventory.GetProperty("PlayFabId").GetString() != account) throw new InvalidDataException();
                var receipts = store.ReadRewards(account);
                results.Add(new { Results = store.ReadResults(account).Count, Applied = receipts.Count(x => x.State == RewardState.Applied),
                    Pending = receipts.Count(x => x.State is RewardState.Pending or RewardState.Sending),
                    NeedsReview = receipts.Count(x => x.State == RewardState.NeedsReview), Skipped = receipts.Count(x => x.State == RewardState.Skipped),
                    AppliedAmount = receipts.Where(x => x.State == RewardState.Applied).Sum(x => x.Grant.Amount),
                    Coins = inventory.GetProperty("VirtualCurrency").GetProperty("CO").GetInt32() });
            }
            var report = JsonSerializer.Serialize(new { TitleId = "B16D9", Utc = DateTimeOffset.UtcNow, QaAccounts = 2, Results = results }, new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory("Logs/Settlement");
            File.WriteAllText("Logs/Settlement/live-inspection.json", report);
            Console.WriteLine(report);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("Settlement inspection failed: " + error.GetType().Name); return 1; }
    }
}
