using System.Security.Cryptography.X509Certificates;
using TankDraft.RemoteHost;
using TankDraft.Server.PlayFab.Identity;

if (args.Length > 0 && args[0] == "--review-progression")
{
    Environment.ExitCode = await ProgressionReviewCommand.RunAsync(args[1..]);
    return;
}

var stage = "arguments";
try
{
    if (args.Length != 1 || args[0] is not "--serve" and not "--loopback-verify" and not "--edgegap-managed-tls")
        throw new InvalidOperationException("Permitted modes: --serve, --loopback-verify, --edgegap-managed-tls.");
    var loopback = args[0] == "--loopback-verify";
    var managedEdgegap = args[0] == "--edgegap-managed-tls";
    if (!managedEdgegap && (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TANKDRAFT_EDGEGAP_TLS_UPGRADE"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ARBITRIUM_REQUEST_ID"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ARBITRIUM_PORTS_MAPPING"))
        || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TANKDRAFT_PLAYFAB_SERVER_SECRET"))))
        throw new InvalidOperationException("Managed Edgegap environment is only permitted for --edgegap-managed-tls.");
    stage = "ingress";
    using var certificate = loopback || managedEdgegap ? null : LoadCertificate();
    var ingress = managedEdgegap ? EdgegapIngress.FromEnvironment() : null;
    stage = "settings";
    var settings = RemoteHostSettings.FromEnvironment();
    stage = "content";
    var (content, version) = AuthoredContent.Load();
    stage = "identity";
    using var identity = new PlayFabIdentityAdapter(new PlayFabIdentityOptions("B16D9", ReadSecret(managedEdgegap), 4));
    stage = "service";
    using var service = new RemoteMatchService(content, version, identity, settings);
    stage = "listener";
    await using var app = RemoteWebHost.Create(service, loopback, certificate, ingress);
    app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("REMOTE_HOST_STARTED"));
    await app.RunAsync();
}
catch
{
    // No exception message or environment dump: both can carry credentials. Hold briefly so
    // an operator can read startup diagnostics before the provider expires container logs.
    Console.Error.WriteLine("REMOTE_HOST_FAILED stage=" + stage);
    if (args.Length == 1 && args[0] == "--edgegap-managed-tls")
    {
        Console.Error.WriteLine(EdgegapIngress.DiagnosticShape());
        await Task.Delay(TimeSpan.FromSeconds(45));
    }
    Environment.ExitCode = 1;
}

static string ReadSecret(bool managed)
{
    var runtimeSecret = Environment.GetEnvironmentVariable("TANKDRAFT_PLAYFAB_SERVER_SECRET");
    if (!managed && !string.IsNullOrEmpty(runtimeSecret)) throw new InvalidOperationException("Runtime server key is only permitted for managed Edgegap TLS.");
    if (managed)
    {
        if (string.IsNullOrEmpty(runtimeSecret) || !IsPrivateKey(runtimeSecret)) throw new InvalidOperationException("Invalid runtime server key.");
        return runtimeSecret;
    }
    var path = Environment.GetEnvironmentVariable("TANKDRAFT_PLAYFAB_SERVER_SECRET_PATH");
    if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new InvalidOperationException("Private server key path is required.");
    var file = new FileInfo(path);
    if (!file.Exists || file.Length is < 8 or > 2048 || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Invalid private server key file.");
    var key = File.ReadAllText(path).Trim();
    if (!IsPrivateKey(key)) throw new InvalidOperationException("Invalid private server key file.");
    return key;
}
static bool IsPrivateKey(string value) => value.Length is >= 8 and <= 2048 && value.All(character => character is >= '\x21' and <= '\x7e');
static X509Certificate2 LoadCertificate()
{
    var path = Environment.GetEnvironmentVariable("TANKDRAFT_REMOTE_TLS_PFX_PATH");
    if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new InvalidOperationException("Native TLS certificate is required for a public listener.");
    X509Certificate2 certificate;
    try { certificate = X509CertificateLoader.LoadPkcs12FromFile(path, Environment.GetEnvironmentVariable("TANKDRAFT_REMOTE_TLS_PFX_PASSWORD"), X509KeyStorageFlags.EphemeralKeySet); }
    catch { throw new InvalidOperationException("Native TLS certificate could not be loaded."); }
    if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
    { certificate.Dispose(); throw new InvalidOperationException("Native TLS certificate is not currently usable."); }
    return certificate;
}
