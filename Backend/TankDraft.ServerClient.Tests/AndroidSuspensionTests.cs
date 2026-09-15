using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using TankDraft.Match.ServerClient;
using Xunit;

namespace TankDraft.ServerClient.Tests;

public sealed class AndroidSuspensionTests
{
    [Fact]
    public async Task Suspension_before_start_prevents_login_until_resume_and_disposal_releases_the_run_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TankDraftAndroidSuspensionTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var credentials = new BlockingCredentials(directory);
        var transport = new ServerClientTransport(credentials, "tankdraft-server-v2", "content-v1", 20, 20, 100, 1024, 8);

        try
        {
            transport.SetSuspended(true);
            transport.Start();

            await Task.Delay(75);
            Assert.Equal(0, credentials.AcquireCount);

            transport.SetSuspended(false);
            await credentials.AcquireStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal(1, credentials.AcquireCount);

            transport.SetSuspended(true);
            transport.SetSuspended(false);
            transport.Dispose();
            transport.SetSuspended(true);
            transport.SetSuspended(false);

            await credentials.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            transport.Dispose();
            await credentials.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Directory.Delete(directory, true);
        }
    }

    private sealed class BlockingCredentials(string runDirectory) : IMatchCredentials
    {
        private readonly TaskCompletionSource<bool> _acquireStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _acquireCount;

        public TaskCompletionSource<bool> AcquireStarted => _acquireStarted;
        public TaskCompletionSource<bool> Disposed => _disposed;
        public int AcquireCount => Volatile.Read(ref _acquireCount);
        public Uri Endpoint { get; } = new("wss://127.0.0.1:18783/v1/socket");
        public int Side => 1;
        public string RunDirectory { get; } = runDirectory;
        public bool ValidateServerCertificate(X509Certificate certificate, X509Chain chain, SslPolicyErrors errors) => true;

        public async Task<MatchAccess> AcquireAsync(CancellationToken token)
        {
            Interlocked.Increment(ref _acquireCount);
            _acquireStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Cancellation should not return an access token.");
        }

        public void Dispose() => _disposed.TrySetResult(true);
    }
}
