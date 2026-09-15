using System.Text.Json;
using TankDraft.Match.Domain;

namespace TankDraft.LocalHost;

public sealed record LocalQueueSettings(string Mode, int QueueTimeoutSeconds, int MaxQueued, int MaxActiveMatches,
    int CompletedRetentionSeconds, int BotThinkMilliseconds, string[] BotDeck, string BotOrderId)
{
    public static LocalQueueSettings Load()
    {
        var value = JsonSerializer.Deserialize<LocalQueueSettings>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "local-queue.json")), AuthoredContent.Json)
            ?? throw new InvalidDataException("Local queue settings missing.");
        if (value.Mode != "LocalOnly" || value.QueueTimeoutSeconds is < 1 or > 60 || value.MaxQueued is < 1 or > 8 ||
            value.MaxActiveMatches is < 1 or > 4 || value.CompletedRetentionSeconds is < 1 or > 600 ||
            value.BotThinkMilliseconds is < 100 or > 5000 || value.BotDeck is null || value.BotDeck.Length != 4 ||
            value.BotOrderId is not "" and not "order.reinforce_armor")
            throw new InvalidDataException("Local queue settings exceed QA bounds.");
        return value;
    }

    public void ValidateFor(AuthoredContent content)
    {
        if (!MatchService.IsSupportedDeck(BotDeck, content.MatchUnits))
            throw new InvalidDataException("Local bot deck is not in the authored QA roster.");
    }
}
