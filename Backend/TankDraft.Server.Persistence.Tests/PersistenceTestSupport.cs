using System.Security.Cryptography;
using TankDraft.Server.Match;
using TankDraft.Server.Persistence;
using TankDraft.Server.Security;

namespace TankDraft.Server.Persistence.Tests;

internal sealed class PersistenceTestClock(DateTimeOffset initial) : TimeProvider
{
    private long _ticks;
    private DateTimeOffset _utc = initial;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => _utc;
    public void Advance(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(amount));
        _ticks = checked(_ticks + amount.Ticks);
        _utc = _utc.Add(amount);
    }
}

internal sealed class PersistenceFixture : IDisposable
{
    private readonly string _directory;
    public PersistenceTestClock Clock { get; } = new(DateTimeOffset.UnixEpoch);
    public LocalSessionRegistry Sessions { get; }
    public MatchRecipe Recipe { get; }
    public string DatabasePath { get; }

    public PersistenceFixture(int maxStepsPerPump = 128)
    {
        _directory = Path.Combine(RepositoryRoot(), "Logs", "BackendPersistenceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        DatabasePath = Path.Combine(_directory, "match.db");
        var content = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Content", "local-match.json"));
        Recipe = new MatchRecipe("durable-qa", PersistenceJson.Hash(content), MatchRecipe.CurrentBuildVersion(), content,
            new ServerMatchSettings(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100), maxStepsPerPump, 256, 512, 512),
            12345, ["account-0", "account-1"]);
        Sessions = new LocalSessionRegistry(128, Clock);
    }

    public SqliteMatchStore OpenStore(Action<StoreFaultPoint>? hook = null) =>
        new(DatabasePath, Recipe.Encode(), Clock.GetUtcNow(), faultHook: hook);
    public DurableMatch Open(Action<StoreFaultPoint>? storeHook = null, Action<DurableFaultPoint>? durableHook = null) =>
        new(OpenStore(storeHook), Clock, durableHook);
    public CallerIdentity Caller(int side, TimeSpan? ttl = null)
    {
        var issued = Sessions.Create($"account-{side}", Recipe.MatchId, side.ToString(System.Globalization.CultureInfo.InvariantCulture), ttl ?? TimeSpan.FromMinutes(5));
        return Sessions.Authenticate(issued.Token) ?? throw new InvalidOperationException("Session issue failed.");
    }
    public CommandEnvelope Choose(DurableMatch match, CallerIdentity caller, string operation, long sequence = 1, int index = 0)
    {
        var state = match.Capture(caller);
        return new CommandEnvelope(Recipe.MatchId, state.Round.ToString(System.Globalization.CultureInfo.InvariantCulture), Recipe.ContentVersion,
            operation, sequence, "Choose", $"{{\"Token\":{state.ChoiceToken},\"OfferIndex\":{index}}}");
    }
    public void Dispose() { }

    public void DriveToResult(DurableMatch match, CallerIdentity caller)
    {
        for (var step = 0; step < 2_000; step++)
        {
            if (match.Capture(caller).Phase == "MatchResult") return;
            Clock.Advance(TimeSpan.FromSeconds(1));
            _ = match.Pump();
        }
        throw new Xunit.Sdk.XunitException("Authoritative match did not finish within the bounded QA drive.");
    }
    public static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Backend", "global.json"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("TankDraft repository root not found.");
    }
}
