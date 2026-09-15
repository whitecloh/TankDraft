using TankDraft.Server.Match;
using TankDraft.Server.Security;
using TankDraft.Server.Persistence;

namespace TankDraft.LocalHost;

// Local QA wiring; lifecycle lives in Server.Match, independent of HTTP/Unity/PlayFab.
public sealed class LocalMatchEndpoint : ILocalMatchRouter, IDisposable
{
    public const string MatchId = "local-qa-match";
    private readonly object _sync = new();
    private DurableMatch _runtime;
    private readonly string _recipeJson;
    private readonly TimeProvider _clock;
    private readonly StoreLimits _limits;
    public string DatabasePath { get; }

    public LocalMatchEndpoint(AuthoredContent content, string contentVersion, HostSettings settings, TimeProvider clock,
        string? matchId = null, string[]? accounts = null, string[]? deck0 = null, string[]? deck1 = null,
        string? order0 = null, string? order1 = null)
    {
        var persistence = LocalPersistenceSettings.Load();
        _clock = clock;
        _limits = persistence.Limits;
        DatabasePath = persistence.CreateDatabasePath();
        var rawContent = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Content", "local-match.json"));
        var recipe = new MatchRecipe(matchId ?? MatchId, contentVersion, MatchRecipe.CurrentBuildVersion(), rawContent,
            settings.CreateMatchSettings(), content.Seed, accounts ?? ["qa-player-0", "qa-player-1"])
        { Deck0 = deck0, Deck1 = deck1, Order0 = order0, Order1 = order1 };
        _recipeJson = recipe.Encode();
        _runtime = Open();
    }

    public bool Pump() { lock (_sync) return _runtime.Pump(); }
    public ServerMatchSnapshot Capture(CallerIdentity caller, long? cursor = null)
    { lock (_sync) return _runtime.Capture(caller, cursor); }
    public CommandReply Execute(CallerIdentity caller, CommandEnvelope command)
    { lock (_sync) return _runtime.Execute(caller, command); }
    public long NextCommandSequence(CallerIdentity caller)
    { lock (_sync) return _runtime.NextCommandSequence(caller); }
    public bool Flush() { lock (_sync) return _runtime.Flush(); }
    public void Dispose() { lock (_sync) _runtime.Dispose(); }
    internal void RestartForVerification()
    {
        lock (_sync) { _runtime.Dispose(); _runtime = Open(); }
    }
    private DurableMatch Open() => new(new SqliteMatchStore(DatabasePath, _recipeJson, _clock.GetUtcNow(), _limits), _clock);
}
