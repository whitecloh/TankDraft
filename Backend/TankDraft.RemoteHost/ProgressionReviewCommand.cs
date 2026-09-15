using System.Text.Json;
using System.Text.RegularExpressions;
using TankDraft.Server.Meta;
using TankDraft.Server.Progression;

namespace TankDraft.RemoteHost;

// Private offline operator entry. It never exposes a route or accepts arbitrary grant amounts.
internal static class ProgressionReviewCommand
{
    internal static async Task<int> RunAsync(string[] args)
    {
        bool list = args is ["list"];
        bool apply = args.Length == 5 && args[4] == "--apply";
        if (!list && (args.Length is < 4 or > 5 || args.Length == 5 && !apply ||
            !int.TryParse(args[1], out _) || !Guid.TryParseExact(args[2], "N", out _) ||
            !Enum.TryParse<ProgressionReviewAction>(args[0], false, out var action) || !Enum.IsDefined(action) ||
            !Regex.IsMatch(args[3], "^[A-Za-z0-9][A-Za-z0-9._:-]{0,95}$")))
        {
            Console.Error.WriteLine("Usage: --review-progression list | (ConfirmApplied|ForgiveDebit|CompensateCreditOnce) <qa-index> <operation-id-N> <evidence-reference> [--apply]");
            return 2;
        }
        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-secrets", "TankDraft");
            var accounts = Read(root, "remote-test-allowlist.txt", 4096).Trim().Split(',').Select(x => x.Trim()).ToArray();
            if (accounts.Length is < 1 or > 100 || accounts.Distinct().Count() != accounts.Length || accounts.Any(x => !Regex.IsMatch(x, "^[A-Za-z0-9]{5,32}$"))) throw new InvalidDataException();
            var database = Path.Combine(root, "fusion-progression", "operations.sqlite");
            _ = Read(root, "fusion-progression/operations.sqlite", 150_000_000, binary: true);
            using var journal = new SqliteProgressionStore(database); // Exclusive lock: stop authority first.
            using var wallet = new OperatorWallet(root, accounts.ToHashSet(StringComparer.Ordinal));
            var service = new ProgressionService(journal, wallet, ProgressionContent.Load().Rules());
            if (list)
            {
                Console.WriteLine(JsonSerializer.Serialize(service.ReadReviews().Select(x => new {
                    QaIndex = Array.IndexOf(accounts, x.AccountId), x.OperationId, x.Sequence, x.Currency,
                    x.WalletDelta, x.ResolvedUnitIds, x.CompensationAttempted }), new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            int index = int.Parse(args[1]);
            if (index < 0 || index >= accounts.Length) throw new ArgumentException();
            var id = Guid.ParseExact(args[2], "N");
            var entry = service.ReadReviews().Single(x => x.AccountId == accounts[index] && x.OperationId == id);
            if (!apply)
            {
                Console.WriteLine($"Preview only: QA{index}, operation={id:N}, action={args[0]}, walletDelta={entry.WalletDelta} {entry.Currency}, compensationAttempted={entry.CompensationAttempted}. No provider call. --apply records this decision; CompensateCreditOnce can overgrant.");
                return 0;
            }
            var result = await service.ResolveReviewAsync(accounts[index], id, Enum.Parse<ProgressionReviewAction>(args[0]), args[3], CancellationToken.None);
            Console.WriteLine($"Operation={id:N}; status={result.Status}. Decision recorded in the durable audit.");
            return result.Status == ProgressionOperationStatus.Completed ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Progression review failed: " + error.GetType().Name + ". Stop authority; inspect existing private journal. Do not delete it.");
            return 1;
        }
    }
    static string Read(string root, string relative, long maxBytes, bool binary = false)
    {
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
        for (var current = Path.GetDirectoryName(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException();
        var file = new FileInfo(path);
        if (!file.Exists || file.Length <= 0 || file.Length > maxBytes || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidDataException();
        return binary ? "" : File.ReadAllText(path);
    }
    sealed class OperatorWallet(string root, IReadOnlySet<string> accounts) : IProgressionWallet, IDisposable
    {
        PlayFabMetaTransport? transport;
        public Task<int> ReadAsync(string account, string currency, CancellationToken ct) => throw new NotSupportedException();
        public Task DebitAsync(string account, string currency, int amount, CancellationToken ct) => throw new NotSupportedException();
        public Task CreditAsync(string account, string currency, int amount, CancellationToken ct)
        {
            transport ??= new PlayFabMetaTransport("B16D9", Read(root, "playfab-server-key.txt", 2048).Trim());
            return new PlayFabProgressionWallet(transport, accounts).CreditAsync(account, currency, amount, ct);
        }
        public void Dispose() => transport?.Dispose();
    }
}
