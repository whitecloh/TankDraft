namespace TankDraft.RemoteHost;

public sealed record RemoteHostSettings(IReadOnlySet<string> AllowlistedAccounts, bool VerifyLoopbackHttp = false, int Port = 0)
{
    public const int QueueLimit = 40;
    public const int ActiveMatchLimit = 20;
    public RemotePolicy Policy { get; init; } = new();
    public static RemoteHostSettings FromEnvironment()
    {
        var configured = Environment.GetEnvironmentVariable("TANKDRAFT_REMOTE_ALLOWLIST") ?? "";
        var accounts = configured.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (accounts.Count is < 1 or > 100 || accounts.Any(x => x.Length > 128)) throw new InvalidOperationException("A private allowlist of 1..100 account ids is required.");
        var path = Path.Combine(AppContext.BaseDirectory, "Config", "remote-host.json");
        var policy = System.Text.Json.JsonSerializer.Deserialize<RemotePolicy>(File.ReadAllBytes(path), AuthoredContent.Json) ?? throw new InvalidDataException("Remote host policy missing.");
        policy.Validate();
        return new RemoteHostSettings(accounts, false, 0) { Policy = policy };
    }
}

public sealed record RemotePolicy
{
    public int MaxQueued { get; init; } = 40;
    public int MaxActiveMatches { get; init; } = 20;
    public int MaxIdentityCalls { get; init; } = 500;
    public int LifetimeMinutes { get; init; } = 50;
    public int DrainBeforeStopMinutes { get; init; } = 5;
    public int QueueTimeoutSeconds { get; init; } = 10;
    public int CompletedRetentionSeconds { get; init; } = 120;
    public int DraftChoiceSeconds { get; init; } = 5;
    public int RoundResultSeconds { get; init; } = 2;
    public void Validate()
    {
        if (MaxQueued is < 1 or > 40 || MaxActiveMatches is < 1 or > 20 || MaxIdentityCalls is < 1 or > 500 ||
            LifetimeMinutes is < 2 or > 50 || DrainBeforeStopMinutes < 1 || DrainBeforeStopMinutes >= LifetimeMinutes ||
            QueueTimeoutSeconds != 10 || CompletedRetentionSeconds is < 30 or > 600 ||
            DraftChoiceSeconds is < 1 or > 60 || RoundResultSeconds is < 1 or > 10)
            throw new InvalidDataException("Remote host policy exceeds prototype bounds.");
    }
}
