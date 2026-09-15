using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using TankDraft.Match.ServerClient;
using Xunit;

namespace TankDraft.ServerClient.Tests;

public sealed class RemoteQueueDeadlineTests
{
    const string Instance = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const string Content = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    const string Lobby = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopq";
    static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task Synchronous_handler_does_not_block_call_or_deadline_and_retries_never_queue_late_requests()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var handler = new Handler(_ =>
        {
            Interlocked.Increment(ref calls); entered.TrySetResult(true);
            release.Wait(); // Deliberately violates cooperative cancellation, before returning a Task.
            return Task.FromResult(Response(Ready()));
        });
        var context = Context();
        using var queue = new RemoteQueueClient(context, handler, Deadline);
        try
        {
            // If JoinAsync blocks synchronously, this helper's watchdog still returns a test failure.
            var invoked = Task.Run(() => new Holder(queue.JoinAsync(CancellationToken.None)));
            var request = (await invoked.WaitAsync(TimeSpan.FromSeconds(2))).Request;
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(2)));
            for (var i = 0; i < 3; i++)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queue.JoinAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal(1, calls);
            release.Set();
            await Task.Delay(100);
            Assert.Null(context.InstanceId);
            Assert.Null(context.PendingJoinOperation);
            Assert.Equal(1, calls); // Timed-out wire-gate waiters must not wake and send later.
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task Uncooperative_body_returns_by_deadline_then_disposes_when_read_finishes()
    {
        using var stream = new BlockedStream();
        using var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) };
            response.Content.Headers.ContentType = new("application/json");
            return Task.FromResult(response);
        });
        var context = Context();
        using var queue = new RemoteQueueClient(context, handler, Deadline);
        try
        {
            var request = queue.JoinAsync(CancellationToken.None);
            await stream.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("body", queue.DiagnosticPhase);
            Assert.Null(context.InstanceId);
            stream.Release.TrySetResult(0);
            await stream.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Null(context.InstanceId);
        }
        finally { stream.Release.TrySetResult(0); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Late_lobby_or_conflict_does_not_commit_or_clear_context(bool conflict)
    {
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler(_ => { entered.TrySetResult(true); return release.Task; });
        var context = Context(); context.InstanceId = Instance;
        context.PendingOperation = "existing-lobby-operation";
        context.PendingJoinOperation = "existing-join-operation";
        using var queue = new RemoteQueueClient(context, handler, Deadline);
        try
        {
            var request = queue.JoinAsync(CancellationToken.None);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(2)));
            release.TrySetResult(conflict ? new HttpResponseMessage(HttpStatusCode.Conflict)
                : Response(new JObject { ["LobbyToken"] = Lobby, ["ExpiresInSeconds"] = 60 }.ToString()));
            await Task.Delay(100);
            Assert.Equal(Instance, context.InstanceId);
            Assert.Equal("existing-lobby-operation", context.PendingOperation);
            Assert.Equal("existing-join-operation", context.PendingJoinOperation);
            Assert.Null(context.LobbyToken);
        }
        finally { release.TrySetResult(new HttpResponseMessage(HttpStatusCode.Conflict)); }
    }

    static RemoteSessionContext Context() => new(new Tickets(), new Uri("https://remote.example/"), Content, Path.GetTempPath());
    static string Ready() => new JObject { ["InstanceId"] = Instance, ["ContentVersion"] = Content, ["IsDraining"] = false, ["EconomyWritesEnabled"] = false }.ToString();
    static HttpResponseMessage Response(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    sealed record Holder(Task<JObject> Request);
    sealed class Tickets : IPlayFabSessionSource { public Task<string> AcquireSessionTicketAsync(CancellationToken token) => Task.FromResult("ticket-a"); }
    sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request); }
    sealed class BlockedStream : MemoryStream
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<int> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token)
        { Started.TrySetResult(true); return Release.Task; }
        protected override void Dispose(bool disposing) { base.Dispose(disposing); Disposed.TrySetResult(true); }
    }
}
