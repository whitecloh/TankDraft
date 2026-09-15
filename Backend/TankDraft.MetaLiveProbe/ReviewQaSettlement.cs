using System.Text.Json;
using System.Text.RegularExpressions;
using TankDraft.Server.Settlement;

// Offline operator tool: no credentials, no HTTP, no arbitrary account/amount input.
// The journal's exclusive writer lock requires the game authority to be stopped.
internal static class ReviewQaSettlement
{
    internal static int Run(string[] args)
    {
        var list = args is ["list"];
        var apply = args.Length == 5 && args[4] == "--apply";
        var action = args.Length > 0 ? args[0] : "";
        var validAction = action is "confirm-applied" or "compensate-once";
        if (!list && (!validAction || args.Length is < 4 or > 5 || (args.Length == 5 && !apply) ||
            !int.TryParse(args[1], out var index) || index is < 0 or > 1 ||
            !Regex.IsMatch(args[2], "^[a-f0-9]{64}$") || !Regex.IsMatch(args[3], "^[A-Za-z0-9][A-Za-z0-9._:-]{0,95}$")))
        {
            Console.Error.WriteLine("Usage: --review-qa-settlement list | (confirm-applied|compensate-once) <qa-index:0|1> <result-id> <evidence-reference> [--apply]");
            return 2;
        }

        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-secrets", "TankDraft");
            var config = JsonSerializer.Deserialize<Settings>(ReadPrivate(root, "playfab-settlement-qa.json", 16384),
                new JsonSerializerOptions { AllowDuplicateProperties = false, MaxDepth = 16 }) ?? throw new InvalidDataException();
            if (config.Mode != "LegacyTestRewards" || config.Policy != new RewardPolicy("qa-r32-v1", "CO", 1, 1, 10))
                throw new InvalidDataException("Only the reviewed tiny Legacy QA policy is supported.");
            var database = Path.GetFullPath(config.DatabasePath);
            if (!database.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(database))
                throw new InvalidDataException("Existing private QA journal required.");
            var accounts = System.Text.Encoding.UTF8.GetString(ReadPrivate(root, "remote-test-allowlist.txt", 1024)).Trim().Split(',').Select(x => x.Trim()).ToArray();
            if (accounts.Length != 2 || accounts.Distinct(StringComparer.Ordinal).Count() != 2 || accounts.Any(x => !Regex.IsMatch(x, "^[A-Za-z0-9]{5,32}$")))
                throw new InvalidDataException("Two existing QA accounts required.");

            using var store = new SqliteSettlementStore(database, config.Policy);
            if (list)
            {
                var report = accounts.Select((account, qaIndex) => new
                {
                    QaIndex = qaIndex,
                    Rewards = store.ReadRewards(account).Select(receipt => new
                    {
                        receipt.Grant.ResultId, receipt.Grant.Currency, receipt.Grant.Amount,
                        State = receipt.State.ToString(), receipt.Reason,
                        Reviews = store.ReadReviewResolutions(receipt.Grant)
                    })
                });
                Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }

            var qa = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
            var receiptToReview = store.ReadRewards(accounts[qa]).Single(x => x.Grant.ResultId == args[2]);
            var resolution = action == "confirm-applied" ? ReviewResolution.ConfirmApplied : ReviewResolution.CompensateOnce;
            if (!apply)
            {
                Console.WriteLine($"Preview only: QA{qa}, result={args[2]}, state={receiptToReview.State}, action={action}, {receiptToReview.Grant.Amount} {receiptToReview.Grant.Currency}. Add --apply to record the operator decision. Compensation can overgrant; no provider call occurs in this tool.");
                return 0;
            }

            var changed = store.ResolveReview(receiptToReview.Grant, resolution, args[3]);
            Console.WriteLine(!changed ? "This exact decision is already recorded; nothing changed." :
                resolution == ReviewResolution.CompensateOnce ? "Compensation authorized. No PlayFab request sent. The next authority start can dispatch one compensation attempt." :
                "External fulfillment confirmation recorded. No PlayFab request sent.");
            return 0;
        }
        catch (Exception error)
        {
            // Provider identities, absolute private paths and secret-bearing exception details stay local.
            Console.Error.WriteLine("QA review failed: " + error.GetType().Name + ". Stop the game authority and check the journal/configuration; never delete the journal to bypass an error.");
            return 1;
        }
    }

    static byte[] ReadPrivate(string root, string name, int limit)
    {
        var path = Path.GetFullPath(Path.Combine(root, name));
        for (var current = Path.GetDirectoryName(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 || info.Length > limit || (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException();
        return File.ReadAllBytes(path);
    }

    sealed record Settings(string Mode, string DatabasePath, RewardPolicy Policy);
}
