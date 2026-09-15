using System.Text;
using TankDraft.Server.Security;
using Xunit;

namespace TankDraft.Server.Security.Tests;

public sealed class RuntimePolicyTests
{
    [Theory]
    [InlineData("Cloud")]
    [InlineData("")]
    public void ValidateLocalOnly_RejectsUnknownMode(string mode)
    {
        Assert.Throws<RuntimePolicyViolationException>(() => RuntimePolicy.ValidateLocalOnly(mode, ["http://127.0.0.1:5100"]));
    }

    [Theory]
    [InlineData("http://localhost:5100")]
    [InlineData("http://0.0.0.0:5100")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://*:5100")]
    [InlineData("https://127.0.0.1:5100")]
    public void ValidateLocalOnly_RejectsNonConcreteLoopbackEndpoint(string url)
    {
        Assert.Throws<RuntimePolicyViolationException>(() => RuntimePolicy.ValidateLocalOnly("LocalOnly", [url]));
    }

    [Theory]
    [InlineData("http://127.0.0.1:5100")]
    [InlineData("http://[::1]:5100")]
    public void ValidateLocalOnly_AcceptsLoopbackIpWithExplicitPort(string url)
    {
        RuntimePolicy.ValidateLocalOnly("LocalOnly", [url]);
    }
}

public sealed class LocalSessionRegistryTests
{
    [Fact]
    public void Authenticate_RejectsTamperingAndRespectsExpiryAndRevocation()
    {
        var clock = new ManualTimeProvider();
        var registry = new LocalSessionRegistry(2, clock);
        var issue = registry.Create("account", "match", "left", TimeSpan.FromMinutes(5));

        Assert.True(DecodeBase64Url(issue.Token).Length >= 32);
        Assert.Null(registry.Authenticate(issue.Token + "x"));
        Assert.NotNull(registry.Authenticate(issue.Token));
        Assert.True(registry.Revoke(issue.SessionId));
        Assert.Null(registry.Authenticate(issue.Token));

        var expiringIssue = registry.Create("account", "match", "left", TimeSpan.FromMinutes(1));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(registry.Authenticate(expiringIssue.Token));
    }

    [Fact]
    public void Create_RejectsCapUntilExpiredSessionIsPurged()
    {
        var clock = new ManualTimeProvider();
        var registry = new LocalSessionRegistry(1, clock);
        registry.Create("account", "match", "left", TimeSpan.FromMinutes(1));

        Assert.Throws<InvalidOperationException>(() => registry.Create("account2", "match", "right", TimeSpan.FromMinutes(1)));
        clock.Advance(TimeSpan.FromMinutes(1));
        var replacement = registry.Create("account2", "match", "right", TimeSpan.FromMinutes(1));

        Assert.NotNull(registry.Authenticate(replacement.Token));
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        return Convert.FromBase64String(base64);
    }
}

public sealed class InMemoryCommandGateTests
{
    [Fact]
    public void Execute_RejectsCallerBoundToAnotherMatch()
    {
        var gate = new InMemoryCommandGate(10, new ManualTimeProvider());
        var calls = 0;

        var reply = gate.Execute(Caller(), Envelope(matchId: "other-match"), () =>
        {
            calls++;
            return Accepted();
        });

        Assert.Equal("MatchMismatch", reply.Code);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Execute_ParallelDuplicateRunsSideEffectExactlyOnce()
    {
        var gate = new InMemoryCommandGate(10, new ManualTimeProvider());
        var calls = 0;
        var envelope = Envelope();
        var caller = Caller();
        var tasks = Enumerable.Range(0, 20).Select(_ => Task.Run(() => gate.Execute(caller, envelope, () =>
        {
            Interlocked.Increment(ref calls);
            return new CommandReply(true, "Accepted", "result");
        })));

        var replies = await Task.WhenAll(tasks);

        Assert.Equal(1, calls);
        Assert.All(replies, reply => Assert.Equal(new CommandReply(true, "Accepted", "result"), reply));
    }

    [Fact]
    public void Execute_RejectsChangedPayloadForUsedOperation()
    {
        var gate = new InMemoryCommandGate(10, new ManualTimeProvider());
        var caller = Caller();
        Assert.True(gate.Execute(caller, Envelope(payload: "first"), Accepted).Accepted);

        var reply = gate.Execute(caller, Envelope(payload: "changed"), Accepted);

        Assert.Equal("OperationConflict", reply.Code);
    }

    [Fact]
    public void Execute_RejectsSequenceGapAndReplay()
    {
        var gate = new InMemoryCommandGate(10, new ManualTimeProvider());
        var caller = Caller();

        Assert.Equal("UnexpectedSequence", gate.Execute(caller, Envelope(sequence: 2), Accepted).Code);
        Assert.True(gate.Execute(caller, Envelope(sequence: 1), Accepted).Accepted);
        Assert.Equal("UnexpectedSequence", gate.Execute(caller, Envelope(operationId: "operation-2", sequence: 1), Accepted).Code);
    }

    [Theory]
    [InlineData("", "round", "content", "operation", 1L, "Choose", "payload")]
    [InlineData("match", "round", "content", "operation", 1L, "Spend", "payload")]
    [InlineData("match", "round", "content", "operation", 0L, "Choose", "payload")]
    public void Execute_RejectsMalformedOrDisallowedCommands(string matchId, string roundId, string contentVersion, string operationId, long sequence, string kind, string payload)
    {
        var gate = new InMemoryCommandGate(10, new ManualTimeProvider());
        var reply = gate.Execute(Caller(), new CommandEnvelope(matchId, roundId, contentVersion, operationId, sequence, kind, payload), Accepted);

        Assert.False(reply.Accepted);
    }

    [Fact]
    public void Execute_RejectsExpiredCaller()
    {
        var clock = new ManualTimeProvider();
        var registry = new LocalSessionRegistry(1, clock);
        var issue = registry.Create("account", "match", "left", TimeSpan.FromMinutes(1));
        var caller = registry.Authenticate(issue.Token)!;
        var gate = new InMemoryCommandGate(10, clock);
        clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal("SessionExpired", gate.Execute(caller, Envelope(), Accepted).Code);
    }

    [Fact]
    public void Execute_RejectsReceiptCapBeforeMutation()
    {
        var gate = new InMemoryCommandGate(1, new ManualTimeProvider());
        var caller = Caller();
        Assert.True(gate.Execute(caller, Envelope(), Accepted).Accepted);
        var calls = 0;

        var reply = gate.Execute(caller, Envelope(operationId: "operation-2", sequence: 2), () =>
        {
            calls++;
            return Accepted();
        });

        Assert.Equal("ReceiptCapacityReached", reply.Code);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Execute_FaultsClosedAfterCallbackException()
    {
        var gate = new InMemoryCommandGate(10, new ManualTimeProvider());
        var calls = 0;
        var first = gate.Execute(Caller(), Envelope(), () =>
        {
            calls++;
            throw new InvalidOperationException("partial domain mutation simulated");
        });
        var second = gate.Execute(Caller(), Envelope(operationId: "operation-2", sequence: 1), () =>
        {
            calls++;
            return Accepted();
        });

        Assert.Equal("GateFaulted", first.Code);
        Assert.Equal("GateFaulted", second.Code);
        Assert.Equal(1, calls);
    }

    private static CallerIdentity Caller()
    {
        var registry = new LocalSessionRegistry(1);
        var issue = registry.Create("account", "match", "left", TimeSpan.FromHours(1));
        return registry.Authenticate(issue.Token)!;
    }

    private static CommandEnvelope Envelope(string matchId = "match", string operationId = "operation", long sequence = 1, string payload = "payload")
    {
        return new CommandEnvelope(matchId, "round", "content", operationId, sequence, "Choose", payload);
    }

    private static CommandReply Accepted() => new(true, "Accepted", string.Empty);
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow = new(2026, 9, 13, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void Advance(TimeSpan interval) => _utcNow = _utcNow.Add(interval);
}
