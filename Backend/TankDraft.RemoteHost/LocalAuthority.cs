using System.Net;
using System.Security.Cryptography;
using TankDraft.Server.PlayFab.Identity;

namespace TankDraft.RemoteHost;

/// <summary>Private same-machine ingress for the Fusion gateway. Never publishes a game listener.</summary>
public sealed class LocalAuthority : IAsyncDisposable
{
    readonly WebApplication app;
    readonly PlayFabIdentityAdapter identity;
    readonly RemoteMatchService service;
    TankDraft.Server.Meta.PlayFabMetaTransport? metaTransport;
    public string Endpoint { get; private set; } = "";
    public string GatewayKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string InstanceId => service.InstanceId;
    public bool IsRunning => !app.Lifetime.ApplicationStopping.IsCancellationRequested;
    public AuthorityMetrics Metrics => service.CaptureMetrics();
    public bool CanFinishDrain => service.CanFinishDrain;
    LocalAuthority(WebApplication application, PlayFabIdentityAdapter adapter, RemoteMatchService runtime, string key)
    { app = application; identity = adapter; service = runtime; GatewayKey = key; }

    public static async Task<LocalAuthority> StartAsync(string secretPath, IReadOnlySet<string> accounts, CancellationToken token, bool allowPhotonQa = false, string? metaSettingsPath = null, string? settlementSettingsPath = null)
    {
        if (!Path.IsPathFullyQualified(secretPath)) throw new InvalidDataException("Private key path must be absolute.");
        var info = new FileInfo(secretPath);
        if (!info.Exists || info.Length is < 8 or > 2048 || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidDataException("Private key file is unavailable.");
        var key = (await File.ReadAllTextAsync(secretPath, token)).Trim();
        if (key.Length < 8 || key.Any(c => c < 0x21 || c > 0x7e)) throw new InvalidDataException("Invalid private key file.");
        var (content, version) = AuthoredContent.Load();
        var adapter = new PlayFabIdentityAdapter(new PlayFabIdentityOptions("B16D9", key, 4));
        RemoteMatchService? runtime = null;
        WebApplication? app = null;
        TankDraft.Server.Meta.PlayFabMetaTransport? meta = null;
        try
        {
            // Dedicated tests are bounded; reward writes require a separate private QA opt-in.
            var policy = new RemotePolicy { MaxActiveMatches = 2, MaxQueued = 4, LifetimeMinutes = 30, DrainBeforeStopMinutes = 5 };
            runtime = new RemoteMatchService(content, version, adapter, new RemoteHostSettings(accounts) { Policy = policy });
            if (metaSettingsPath is not null) meta = PlayFabMetaBootstrap.Configure(runtime, metaSettingsPath, key);
            if (settlementSettingsPath is not null)
            {
                if (meta is null || metaSettingsPath is null) throw new InvalidDataException("Rewards require reviewed PlayFab meta.");
                PlayFabSettlementBootstrap.Configure(runtime, settlementSettingsPath, metaSettingsPath, meta, accounts);
            }
            var gatewayKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            app = RemoteWebHost.Create(runtime, true, gatewayKey: gatewayKey, allowPhotonQa: allowPhotonQa);
            await app.StartAsync(token);
            var result = new LocalAuthority(app, adapter, runtime, gatewayKey) { Endpoint = app.Urls.Single(), metaTransport = meta };
            if (!Uri.TryCreate(result.Endpoint, UriKind.Absolute, out var uri) || !IPAddress.TryParse(uri.Host, out var ip) || !IPAddress.IsLoopback(ip))
                throw new InvalidOperationException("Private ingress binding failed.");
            return result;
        }
        catch { if (app is not null) await app.DisposeAsync(); runtime?.Dispose(); meta?.Dispose(); adapter.Dispose(); throw; }
    }
    public void BeginDrain() => service.BeginDrain();
    public async ValueTask DisposeAsync()
    {
        service.BeginDrain();
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await app.StopAsync(timeout.Token); }
        finally { await app.DisposeAsync(); service.Dispose(); metaTransport?.Dispose(); identity.Dispose(); }
    }
}
