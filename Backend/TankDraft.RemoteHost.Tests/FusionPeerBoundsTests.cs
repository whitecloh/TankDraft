using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using TankDraft.Infrastructure.FusionTransport;
using Xunit;

namespace TankDraft.RemoteHost.Tests;

public sealed class FusionPeerBoundsTests
{
    static readonly byte[] Request = Encoding.UTF8.GetBytes("{\"Operation\":\"Lobby\",\"Authorization\":\"fixture\",\"Body\":{}}");

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedResponseIsRejected(bool declaredLength)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.MapPost("/v1/lobby", async context =>
        {
            if (declaredLength) context.Response.ContentLength = 1048577;
            try { await context.Response.Body.WriteAsync(new byte[1048577], context.RequestAborted); }
            catch (OperationCanceledException) { }
        });
        await app.StartAsync();
        using var peer = new FusionAuthorityPeer(new Uri(app.Urls.Single()), new string('A', 64), "acctA");
        await Assert.ThrowsAsync<InvalidDataException>(() => peer.ExecuteAsync(Request, CancellationToken.None));
        await app.StopAsync();
    }

    [Fact]
    public async Task CancellationAfterHeadersReleasesPeerForNextRequest()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        await using var app = builder.Build();
        var headersSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        app.MapPost("/v1/lobby", async context =>
        {
            if (Interlocked.Increment(ref calls) != 1) { await context.Response.WriteAsync("{}"); return; }
            await context.Response.StartAsync();
            await context.Response.Body.FlushAsync();
            headersSent.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, context.RequestAborted); }
            catch (OperationCanceledException) { }
        });
        await app.StartAsync();
        using var peer = new FusionAuthorityPeer(new Uri(app.Urls.Single()), new string('A', 64), "acctA");
        using var cancellation = new CancellationTokenSource();
        var pending = peer.ExecuteAsync(Request, cancellation.Token);
        await headersSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(() => peer.ExecuteAsync(Request, CancellationToken.None));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(2)));
        var next = await peer.ExecuteAsync(Request, CancellationToken.None);
        Assert.Contains("\"Status\":200", Encoding.UTF8.GetString(next));
        await app.StopAsync();
    }
}
