using TankDraft.Contracts.Battle;
using TankDraft.Server.Match;
using TankDraft.Server.Persistence;
using TankDraft.Server.Security;

if (args.Length != 3)
    throw new ArgumentException("Usage: <database-path> <marker-path> <BeforeCommit|AfterCommit|AfterApplyBeforeCommit|ActiveBattleAfterCommit>");

var databasePath = Path.GetFullPath(args[0]);
var markerPath = Path.GetFullPath(args[1]);
var activeBattle = string.Equals(args[2], "ActiveBattleAfterCommit", StringComparison.Ordinal);
var storePoint = default(StoreFaultPoint);
var durablePoint = default(DurableFaultPoint);
var hasStorePoint = !activeBattle && Enum.TryParse(args[2], false, out storePoint);
var hasDurablePoint = Enum.TryParse(args[2], false, out durablePoint);
if (!activeBattle && !hasStorePoint && !hasDurablePoint)
    throw new ArgumentException("Unknown crash point.");

var contentPath = Path.Combine(AppContext.BaseDirectory, "Content", "local-match.json");
var content = File.ReadAllText(contentPath);
var recipe = new MatchRecipe("durable-qa", PersistenceJson.Hash(content), MatchRecipe.CurrentBuildVersion(), content,
    new ServerMatchSettings(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100), 128, 256, 512, 512),
    12345, ["account-0", "account-1"]);
var now = DateTimeOffset.UnixEpoch;
void ReadyAndWait()
{
    WriteMarker("READY");
    Console.Out.WriteLine("READY");
    Console.Out.Flush();
    Thread.Sleep(Timeout.Infinite);
}

Action<StoreFaultPoint>? storeHook = hasStorePoint ? point =>
{
    if (point == storePoint) ReadyAndWait();
} : null;
Action<DurableFaultPoint>? durableHook = hasDurablePoint ? point =>
{
    if (point == durablePoint) ReadyAndWait();
} : null;

using var store = new SqliteMatchStore(databasePath, recipe.Encode(), now, faultHook: storeHook);
var clock = new ProbeClock(now);
DurableMatch? match = null;
if (activeBattle)
{
    durableHook = point =>
    {
        if (point != DurableFaultPoint.AfterCommitBeforePublish || match is null) return;
        var hash = match.GetStateHash();
        var state = match.Capture(new CallerIdentityProxy("account-0", recipe.MatchId).Identity);
        if (state.Phase == "Battle" && state.Entities.Any(entity => entity.Kind is BattleEntityKind.Projectile or BattleEntityKind.Zone))
        {
            WriteMarker("READY\n" + hash);
            Console.Out.WriteLine("READY");
            Console.Out.Flush();
            Thread.Sleep(Timeout.Infinite);
        }
    };
}
using (match = new DurableMatch(store, clock, durableHook))
{
    if (activeBattle)
    {
        for (var index = 0; index < 2_000; index++)
        {
            clock.Advance(index == 0 ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(34));
            _ = match.Pump();
        }
        throw new InvalidOperationException("Active battle state was not reached.");
    }
var sessions = new LocalSessionRegistry(4);
var issue = sessions.Create("account-0", recipe.MatchId, "0", TimeSpan.FromMinutes(5));
var caller = sessions.Authenticate(issue.Token) ?? throw new InvalidOperationException("QA session creation failed.");
var state = match.Capture(caller);
var command = new CommandEnvelope(recipe.MatchId, state.Round.ToString(System.Globalization.CultureInfo.InvariantCulture),
    recipe.ContentVersion, "probe-operation", 1, "Choose", $"{{\"Token\":{state.ChoiceToken},\"OfferIndex\":0}}");
var reply = match.Execute(caller, command);
Console.Out.WriteLine(reply.Code);
}

void WriteMarker(string contents)
{
    var temporary = markerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
    File.WriteAllText(temporary, contents);
    File.Move(temporary, markerPath, overwrite: true);
}

file sealed class ProbeClock(DateTimeOffset initial) : TimeProvider
{
    private long _ticks;
    private DateTimeOffset _utc = initial;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => _utc;
    public void Advance(TimeSpan amount) { _ticks += amount.Ticks; _utc = _utc.Add(amount); }
}

file sealed class CallerIdentityProxy
{
    private readonly LocalSessionRegistry _sessions = new(1);
    public CallerIdentityProxy(string account, string matchId)
    {
        var token = _sessions.Create(account, matchId, "0", TimeSpan.FromDays(1));
        Identity = _sessions.Authenticate(token.Token) ?? throw new InvalidOperationException();
    }
    public CallerIdentity Identity { get; }
}
