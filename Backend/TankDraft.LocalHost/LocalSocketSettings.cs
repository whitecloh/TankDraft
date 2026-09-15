using System.Text.Json;
using TankDraft.Server.Security;

namespace TankDraft.LocalHost;

internal sealed record LocalSocketSettings(string Mode, string ListenUrl, string Protocol, int PollMilliseconds,
    int MaxRequestBytes, int MaxResponseBytes, int TimeoutSeconds, int IdleSeconds, int MaxConnections,
    int MessagesPerSecond, int SessionTtlSeconds)
{
    public static LocalSocketSettings Load()
    {
        var settings = JsonSerializer.Deserialize<LocalSocketSettings>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Config", "local-socket.json")), AuthoredContent.Json)
            ?? throw new InvalidDataException("Socket settings missing.");
        RuntimePolicy.ValidateLocalOnly(settings.Mode, [settings.ListenUrl]);
        if (settings.Protocol != LocalSocketHost.Protocol || settings.PollMilliseconds is < 10 or > 1000 ||
            settings.MaxRequestBytes is < 1024 or > 8192 || settings.MaxResponseBytes is < 1024 or > 1048576 ||
            settings.TimeoutSeconds is < 1 or > 10 || settings.IdleSeconds is < 5 or > 60 ||
            settings.MaxConnections is < 1 or > 4 || settings.MessagesPerSecond is < 1 or > 60 ||
            settings.SessionTtlSeconds != 900)
            throw new InvalidDataException("Socket settings exceed LocalOnly QA bounds.");
        return settings;
    }
}
