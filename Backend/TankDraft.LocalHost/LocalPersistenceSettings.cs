using System.Text.Json;
using TankDraft.Server.Persistence;

namespace TankDraft.LocalHost;

internal sealed record LocalPersistenceSettings(string Mode, string DatabaseFolder, int MaxEntries,
    int MaxRecordBytes, int MaxDatabasePages, string RecoveryTimePolicy)
{
    public StoreLimits Limits => new(MaxEntries, MaxRecordBytes, MaxDatabasePages);
    public static LocalPersistenceSettings Load()
    {
        var settings = JsonSerializer.Deserialize<LocalPersistenceSettings>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Config", "local-persistence.json")), AuthoredContent.Json)
            ?? throw new InvalidDataException("Persistence settings missing.");
        if (settings.Mode != "LocalOnly" || settings.RecoveryTimePolicy != MatchRecipe.RecoveryPolicy ||
            settings.MaxEntries is < 1 or > 50000 || settings.MaxRecordBytes is < 1024 or > 262144 ||
            settings.MaxDatabasePages is < 64 or > 16384)
            throw new InvalidDataException("Only bounded LocalOnly persistence is allowed.");
        return settings;
    }

    public string CreateDatabasePath()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Backend", "global.json"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("QA host must run from its project checkout.");
        var logs = Path.GetFullPath(Path.Combine(root.FullName, "Logs")) + Path.DirectorySeparatorChar;
        var folder = Path.GetFullPath(Path.Combine(root.FullName, DatabaseFolder));
        if (!folder.StartsWith(logs, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("QA databases must remain under ignored project Logs.");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, Guid.NewGuid().ToString("N") + ".sqlite");
    }
}
