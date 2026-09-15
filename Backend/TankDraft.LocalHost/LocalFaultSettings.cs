using System.Text.Json;

namespace TankDraft.LocalHost;

internal sealed record LocalFaultSettings(
    string Mode,
    string Profile,
    int BaseDelayMs,
    int JitterMs,
    int Seed,
    int MaxConnections,
    int BufferBytes)
{
    public static LocalFaultSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Config", "local-faults.json");
        var value = JsonSerializer.Deserialize<LocalFaultSettings>(File.ReadAllText(path), AuthoredContent.Json)
            ?? throw new InvalidDataException("Fault settings missing.");

        if (value.Mode != "LocalOnly" || value.Profile != "tls-loopback-v1" ||
            value.BaseDelayMs is < 1 or > 100 || value.JitterMs is < 0 or > 200 ||
            value.Seed is < 1 or > int.MaxValue || value.MaxConnections is < 1 or > 8 ||
            value.BufferBytes is < 1024 or > 65536 || value.BufferBytes != 16384)
            throw new InvalidDataException("Fault settings exceed LocalOnly QA bounds.");

        return value;
    }
}
