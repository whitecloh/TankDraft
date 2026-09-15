using System.Text.Json;

namespace TankDraft.LocalHost;

internal sealed record LocalTlsSettings(string Mode, string ListenUrl, string Protocol, int AccessTtlSeconds, int RefreshMarginSeconds,
    int GrantTtlSeconds, int MaxRequestBytes, int MaxResponseBytes, int TimeoutSeconds, int MaxConnections)
{
    public static LocalTlsSettings Load()
    {
        var value = JsonSerializer.Deserialize<LocalTlsSettings>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Config", "local-tls.json")), AuthoredContent.Json)
            ?? throw new InvalidDataException("TLS settings missing.");
        if (value.Mode != "LocalOnly" || value.ListenUrl != "https://127.0.0.1:18783" || value.Protocol != LocalTlsHost.Protocol ||
            value.AccessTtlSeconds is < 1 or > 30 || value.RefreshMarginSeconds is < 1 or > 10 || value.RefreshMarginSeconds >= value.AccessTtlSeconds ||
            value.GrantTtlSeconds is < 1 or > 900 || value.MaxRequestBytes is < 1024 or > 8192 || value.MaxResponseBytes is < 1024 or > 1048576 ||
            value.TimeoutSeconds is < 1 or > 5 || value.MaxConnections is < 1 or > 4) throw new InvalidDataException("TLS settings exceed local bounds.");
        return value;
    }
}
