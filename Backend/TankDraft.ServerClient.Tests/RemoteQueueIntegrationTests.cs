using TankDraft.Match.ServerClient;
using Xunit;

namespace TankDraft.ServerClient.Tests;

public sealed class RemoteQueueIntegrationTests
{
    [Theory]
    [InlineData("http://remote.example/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://user@remote.example/")]
    [InlineData("https://remote.example/not-root")]
    public void Remote_context_accepts_only_trusted_https_dns_root(string endpoint)
    {
        Assert.Throws<InvalidOperationException>(() => new RemoteSessionContext(new Tickets(), new Uri(endpoint), new string('a', 64), Path.GetTempPath()));
    }
    sealed class Tickets : IPlayFabSessionSource { public Task<string> AcquireSessionTicketAsync(CancellationToken token) => Task.FromResult("ticket-a"); }
}
