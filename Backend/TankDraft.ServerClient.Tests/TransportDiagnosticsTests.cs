using System.Net.Http;
using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json.Linq;
using TankDraft.Match.ServerClient;
using Xunit;

namespace TankDraft.ServerClient.Tests;

public sealed class TransportDiagnosticsTests
{
    [Fact]
    public void Diagnostic_is_type_only_bounded_and_nonfatal_when_its_directory_disappears()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TankDraftTransportDiagnostics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previous = Environment.GetEnvironmentVariable("TD_LOCAL_AUTO");
        Environment.SetEnvironmentVariable("TD_LOCAL_AUTO", "1");
        using var transport = new ServerClientTransport(new DiagnosticsCredentials(directory), "protocol", "content", 1, 1, 1, 1024, 1);
        try
        {
            const string sentinel = "credential-sentinel-must-not-be-written";
            for (var index = 0; index < 33; index++) WriteDiagnostic(transport, new HttpRequestException(sentinel));

            var path = Path.Combine(directory, "transport-diagnostic-1.jsonl");
            var lines = File.ReadAllLines(path);
            Assert.Equal(32, lines.Length);
            Assert.DoesNotContain(sentinel, string.Join(Environment.NewLine, lines), StringComparison.Ordinal);
            var record = JObject.Parse(lines[0]);
            Assert.Equal(new[] { "AuthGeneration", "Connections", "DurationMs", "ExceptionType", "LifetimeCancelled", "Phase", "Stage", "Suspended" }, record.Properties().Select(property => property.Name).OrderBy(name => name));
            Assert.Equal("Acquire", record.Value<string>("Stage"));
            Assert.Equal(JTokenType.Null, record["Phase"]!.Type);
            Assert.Equal("HttpRequestException", record.Value<string>("ExceptionType"));
            Assert.True(record.Value<long>("DurationMs") >= 0);

            Directory.Delete(directory, true);
            using var unavailable = new ServerClientTransport(new DiagnosticsCredentials(directory), "protocol", "content", 1, 1, 1, 1024, 1);
            WriteDiagnostic(unavailable, new HttpRequestException(sentinel));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TD_LOCAL_AUTO", previous);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    static void WriteDiagnostic(ServerClientTransport transport, Exception error)
    {
        var method = typeof(ServerClientTransport).GetMethod("WriteTransportDiagnostic", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(transport, [error]);
    }

    sealed class DiagnosticsCredentials(string runDirectory) : IMatchCredentials
    {
        public Uri Endpoint { get; } = new("wss://diagnostic.example/");
        public int Side => 1;
        public string RunDirectory { get; } = runDirectory;
        public bool ValidateServerCertificate(X509Certificate certificate, X509Chain chain, SslPolicyErrors errors) => true;
        public Task<MatchAccess> AcquireAsync(CancellationToken token) => Task.FromResult(new MatchAccess("token", "session", "stream", "match", 1, 30));
        public void Dispose() { }
    }
}
