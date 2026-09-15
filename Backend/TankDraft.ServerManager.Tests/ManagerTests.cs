using System.Net;
using System.Text.RegularExpressions;
using TankDraft.RemoteHost;
using TankDraft.ServerManager;
using Xunit;

public sealed class ManagerTests : IAsyncLifetime
{
    readonly string root = Path.Combine(Path.GetTempPath(), "TankDraftManagerTest-" + Guid.NewGuid().ToString("N"));
    ManagerOptions options = null!;
    public Task InitializeAsync()
    {
        Directory.CreateDirectory(root);
        var key = Path.Combine(root, "fake-key.txt"); File.WriteAllText(key, "TEST_ONLY_NOT_A_REAL_KEY");
        options = new ManagerOptions(root, key, ["TEST001", "TEST002"]);
        return Task.CompletedTask;
    }
    public Task DisposeAsync()
    {
        // Only this test-owned directory; assert the absolute containment before recursive cleanup.
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(root));
        Directory.Delete(root, true); return Task.CompletedTask;
    }
    [Fact]
    public async Task PrivateIngressRequiresGatewayKeyEvenOnLoopback()
    {
        await using var authority = await LocalAuthority.StartAsync(options.PlayFabSecretPath, options.AllowlistedAccounts.ToHashSet(), CancellationToken.None);
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(authority.Endpoint + "/readyz")).StatusCode);
        client.DefaultRequestHeaders.Add("X-TankDraft-Gateway", new string('0', 64));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(authority.Endpoint + "/readyz")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-TankDraft-Gateway");
        client.DefaultRequestHeaders.Add("X-TankDraft-Gateway", authority.GatewayKey);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(authority.Endpoint + "/readyz")).StatusCode);
        Assert.Equal(0, authority.Metrics.IdentityCalls);
    }
    [Fact]
    public async Task DuplicateStartDoesNotKillRunningAuthorityAndRestartIsFresh()
    {
        await using var manager = new ServerSupervisor(options);
        await manager.StartAsync(true, CancellationToken.None);
        var first = await manager.StatusAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(true, CancellationToken.None));
        Assert.Equal(first.InstanceId, (await manager.StatusAsync()).InstanceId);
        await manager.StopAsync(false);
        Assert.Equal("Stopped", (await manager.StatusAsync()).State);
        await manager.StartAsync(true, CancellationToken.None);
        var next = await manager.StatusAsync();
        Assert.NotEqual(first.InstanceId, next.InstanceId);
        Assert.Equal(2, next.Starts);
        Assert.Equal(0, next.Authority!.IdentityCalls);
    }
    [Fact]
    public async Task MissingGatewayDoesNotPretendReadyOrLaunchAuthority()
    {
        await using var manager = new ServerSupervisor(options);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(false, CancellationToken.None));
        Assert.Null((await manager.StatusAsync()).InstanceId);
    }
    [Fact]
    public async Task ControlApiRejectsCrossOriginMissingTokenAndReboundHost()
    {
        await using var manager = new ServerSupervisor(options);
        await using var app = ManagerWebHost.Create(manager, 0); await app.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()) };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/check-local", null)).StatusCode);
        var html = await client.GetStringAsync("/");
        var token = Regex.Match(html, "name=\"control-token\" content=\"([A-F0-9]{64})\"").Groups[1].Value;
        Assert.Equal(64, token.Length);
        client.DefaultRequestHeaders.Add("X-TankDraft-Control", token);
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/check-local", null)).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Host = "attacker.invalid";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/status")).StatusCode);
        client.DefaultRequestHeaders.Host = null;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/check-local", null)).StatusCode);
        Assert.Equal("AuthorityOnly", (await manager.StatusAsync()).State);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/drain", null)).StatusCode);
        Assert.Equal("Stopped", (await manager.StatusAsync()).State);
        await app.StopAsync();
    }
}
