using System.Text;
using Newtonsoft.Json.Linq;
using TankDraft.Infrastructure.FusionTransport;
using Xunit;

namespace TankDraft.RemoteHost.Tests;

public sealed class FusionRequestChannelTests
{
    [Fact]
    public async Task UnverifiedEncryptionNeverSendsCredentials()
    {
        var sends = 0;
        using var channel = new FusionRequestChannel((_, _) => sends++, () => false, TimeSpan.FromSeconds(1));
        var client = new FusionAuthorityClient(channel);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ExchangeAsync("Lobby", "fixture-ticket", new JObject(), CancellationToken.None));
        Assert.Equal(0, sends);
    }
    [Fact]
    public async Task CancellationAndLateReplyCannotCompleteNextRequest()
    {
        int id = 0;
        using var channel = new FusionRequestChannel((key, _) => id = key, () => true, TimeSpan.FromSeconds(1));
        using var cancel = new CancellationTokenSource();
        var first = channel.ExchangeAsync([1], cancel.Token); var old = id;
        await Assert.ThrowsAsync<InvalidOperationException>(() => channel.ExchangeAsync([2], CancellationToken.None));
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var next = channel.ExchangeAsync([2], CancellationToken.None);
        Assert.False(channel.Receive(old, [9])); Assert.False(next.IsCompleted);
        Assert.True(channel.Receive(id, [3])); Assert.Equal(new byte[] { 3 }, await next);
        Assert.False(channel.Receive(id, [4]));
    }
    [Fact]
    public async Task DeadlineAndDisconnectReleaseWaiter()
    {
        using var channel = new FusionRequestChannel((_, _) => { }, () => true, TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => channel.ExchangeAsync([1], CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)));
        var pending = channel.ExchangeAsync([2], CancellationToken.None);
        channel.Dispose(); await Assert.ThrowsAsync<IOException>(() => pending);
        Assert.False(channel.Receive(2, [3]));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => channel.ExchangeAsync([1], CancellationToken.None));
    }
    [Fact]
    public async Task InvalidResponseAndLostEncryptionFailClosed()
    {
        int id = 0; bool secure = true;
        using var channel = new FusionRequestChannel((key, _) => id = key, () => secure, TimeSpan.FromSeconds(1));
        var pending = channel.ExchangeAsync([1], CancellationToken.None);
        channel.Receive(id, new byte[1048705]); await Assert.ThrowsAsync<InvalidDataException>(() => pending);
        pending = channel.ExchangeAsync([2], CancellationToken.None); secure = false;
        channel.Receive(id, [3]); await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
    }
    [Theory]
    [InlineData("{\"Status\":200,\"Body\":{},\"Extra\":1}")]
    [InlineData("{\"Status\":200,\"Status\":200,\"Body\":{}}")]
    [InlineData("{\"Status\":200,\"Body\":[]} ")]
    public async Task MalformedEnvelopeIsRejected(string response)
    {
        FusionRequestChannel? channel = null;
        using (channel = new FusionRequestChannel((id, _) => channel!.Receive(id, Encoding.UTF8.GetBytes(response)), () => true, TimeSpan.FromSeconds(1)))
        {
            var client = new FusionAuthorityClient(channel);
            var error = await Record.ExceptionAsync(() => client.ExchangeAsync("Status", "fixture", new JObject(), CancellationToken.None));
            Assert.True(error is InvalidDataException or Newtonsoft.Json.JsonReaderException);
        }
    }
    [Fact]
    public async Task RejectionExposesOnlyStatusAndDoesNotRetry()
    {
        FusionRequestChannel? channel = null; var sends = 0;
        using (channel = new FusionRequestChannel((id, _) => { sends++; channel!.Receive(id, Encoding.UTF8.GetBytes("{\"Status\":403,\"Body\":{\"private\":\"fixture\"}}")); }, () => true, TimeSpan.FromSeconds(1)))
        {
            var error = await Assert.ThrowsAsync<FusionAuthorityException>(() => new FusionAuthorityClient(channel).ExchangeAsync("Lobby", "fixture", new JObject(), CancellationToken.None));
            Assert.Equal(403, error.Status); Assert.DoesNotContain("fixture", error.ToString()); Assert.Equal(1, sends);
        }
    }
}
