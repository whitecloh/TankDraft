using System.Text;
using Newtonsoft.Json.Linq;
using TankDraft.Infrastructure.FusionTransport;
using Xunit;

namespace TankDraft.RemoteHost.Tests;

public sealed class FusionQaProtocolTests
{
    [Theory]
    [InlineData("Authorization")]
    [InlineData("SessionTicket")]
    [InlineData("AccountId")]
    [InlineData("AccessToken")]
    public async Task PlaintextModeRejectsCredentialOrIdentityFieldsBeforeSending(string property)
    {
        var sends = 0;
        using var channel = new FusionRequestChannel((_, _) => sends++, () => false, TimeSpan.FromSeconds(1), allowPlaintextQa: true);
        var body = new JObject { ["OperationId"] = "qa-test", [property] = "fixture" };
        await Assert.ThrowsAsync<InvalidDataException>(() => new FusionQaClient(channel).ExchangeAsync("Lobby", body, CancellationToken.None));
        Assert.Equal(0, sends);
    }
    [Fact]
    public async Task LegacyCredentialEnvelopeCannotUsePlaintextChannel()
    {
        var sends = 0;
        using var channel = new FusionRequestChannel((_, _) => sends++, () => false, TimeSpan.FromSeconds(1), allowPlaintextQa: true);
        await Assert.ThrowsAsync<InvalidDataException>(() => new FusionAuthorityClient(channel).ExchangeAsync("Lobby", "fixture-ticket", new JObject { ["OperationId"] = "x" }, CancellationToken.None));
        Assert.Equal(0, sends);
    }
    [Fact]
    public async Task CredentialInResponseIsNeverReturnedToCaller()
    {
        FusionRequestChannel? channel = null;
        using (channel = new FusionRequestChannel((id, _) => channel!.Receive(id, Encoding.UTF8.GetBytes("{\"Status\":200,\"Body\":{\"AccessToken\":\"fixture\"}}")), () => false, TimeSpan.FromSeconds(1), allowPlaintextQa: true))
        {
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => new FusionQaClient(channel).ExchangeAsync("Lobby", new JObject { ["OperationId"] = "x" }, CancellationToken.None));
            Assert.DoesNotContain("fixture", error.ToString());
        }
    }
}
