using System.Text.Json;

namespace TankDraft.ServerManager;

public sealed record ManagerOptions(string Workspace, string PlayFabSecretPath, string[] AllowlistedAccounts)
{
    public bool AllowPlaintextQa { get; init; }
    public string? MetaSettingsPath { get; init; }
    public string? SettlementSettingsPath { get; init; }
    public static ManagerOptions Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 16384) throw new InvalidDataException("Manager configuration is unavailable.");
        var options = JsonSerializer.Deserialize<ManagerOptions>(File.ReadAllText(path)) ?? throw new InvalidDataException();
        if (!Path.IsPathFullyQualified(options.Workspace) || !Directory.Exists(Path.Combine(options.Workspace, "Assets", "TankDraft")) ||
            !Path.IsPathFullyQualified(options.PlayFabSecretPath) || options.AllowlistedAccounts is not { Length: > 0 and <= 4 } ||
            options.AllowlistedAccounts.Any(x => x.Length is < 5 or > 32 || x.Any(c => !char.IsAsciiLetterOrDigit(c))))
            throw new InvalidDataException("Invalid manager configuration.");
        return options;
    }
    public string GatewayExecutable => Path.Combine(Workspace, "Builds", "Fusion", "Server", "TankDraftFusionServer.exe");
    public string LogDirectory => Path.Combine(Workspace, "Logs", "FusionServer");
}
