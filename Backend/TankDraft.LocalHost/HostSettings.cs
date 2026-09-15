using System.Text.Json;
using TankDraft.Server.Security;
using TankDraft.Server.Match;

namespace TankDraft.LocalHost;

public sealed class HostSettings
{
    public required string Mode { get; init; }
    public required string ListenUrl { get; init; }
    public required int MaxRequestBodyBytes { get; init; }
    public required int MaxConcurrentConnections { get; init; }
    public required int RequestsPerWindow { get; init; }
    public required int RateWindowSeconds { get; init; }
    public required int SessionTtlSeconds { get; init; }
    public required int MaxSessionCount { get; init; }
    public required int MaxCommandReceipts { get; init; }
    public required int MaxSimulationTicksPerRound { get; init; }
    public required double DraftChoiceSeconds { get; init; }
    public required double RoundResultSeconds { get; init; }
    public required int SchedulerPollMilliseconds { get; init; }
    public required int MaxStepsPerPump { get; init; }
    public required int EventCapacity { get; init; }
    public required int JournalCapacity { get; init; }

    public ServerMatchSettings CreateMatchSettings() => new(TimeSpan.FromSeconds(DraftChoiceSeconds),
        TimeSpan.FromSeconds(RoundResultSeconds), MaxStepsPerPump, EventCapacity, JournalCapacity, MaxCommandReceipts);

    public static HostSettings Load()
    {
        var settings = JsonSerializer.Deserialize<HostSettings>(File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Config", "local-host.json")), AuthoredContent.Json)
            ?? throw new InvalidDataException("Local host settings missing.");
        RuntimePolicy.ValidateLocalOnly(settings.Mode, [settings.ListenUrl]);
        if (settings.MaxRequestBodyBytes is < 1024 or > 16384 ||
            settings.MaxConcurrentConnections is < 1 or > 16 ||
            settings.RequestsPerWindow is < 1 or > 240 ||
            settings.RateWindowSeconds is < 1 or > 60 ||
            settings.SessionTtlSeconds is < 1 or > 900 ||
            settings.MaxSessionCount is < 2 or > 8 ||
            settings.MaxCommandReceipts is < 1 or > 1024 ||
            settings.MaxSimulationTicksPerRound is < 1 or > 20000 ||
            settings.SchedulerPollMilliseconds is < 1 or > 1000)
            throw new InvalidDataException("Local host settings exceed QA bounds.");
        _ = settings.CreateMatchSettings();
        return settings;
    }
}
