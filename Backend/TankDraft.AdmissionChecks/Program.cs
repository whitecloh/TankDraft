using PlayFab;
using PlayFab.ServerModels;
using TankDraft.Server.Admission;
using TankDraft.Server.PlayFab.Identity;

var checks = 0;
var clock = new TestClock(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
await DenialsAsync();
await RotationAndReplayAsync();
await DeadlineCapacityAndCancellationAsync();
await ReconnectAndGenerationAsync();
await ConcurrencyAsync();
Console.WriteLine($"PASS admission offline checks: {checks} cases; no credentials or access tokens logged.");

async Task DenialsAsync()
{
    using var spoofAdapter = Adapter(_ => Task.FromResult(Result("ZZZZZ1")));
    await Rejects(ServiceWithAdapter(spoofAdapter), "spoof-ticket", "content-1", "op-1", "unauthorized", "spoof ticket");
    await Rejects(Service("ABCDE1"), Ticket("ABCDE1"), "other", "op-1", "unauthorized", "content mismatch");
    var expired = Service("ABCDE1", clock.GetUtcNow().AddSeconds(-1));
    await Rejects(expired, Ticket("ABCDE1"), "content-1", "op-1", "expired", "expired assignment");
    await Rejects(Service("ABCDE1"), Ticket("ABCDE1"), "content-1", "", "invalid_request", "invalid operation");
}

async Task RotationAndReplayAsync()
{
    using var adapter = Adapter(_ => Task.FromResult(Result("ABCDE1")));
    var service = ServiceWithAdapter(adapter);
    var first = await service.ExchangeAsync(Ticket("ABCDE1"), "content-1", "op-1", CancellationToken.None);
    var retry = await service.ExchangeAsync(Ticket("ABCDE1"), "content-1", "op-1", CancellationToken.None);
    Require(first == retry, "lost response retry returns same issue");
    var rotated = await service.ExchangeAsync(Ticket("ABCDE1"), "content-1", "op-2", CancellationToken.None);
    Require(rotated.StreamId == first.StreamId && rotated.Generation == first.Generation + 1 && rotated.AccessToken != first.AccessToken, "rotation retains stream and increments generation");
    await RejectUse(service, first.AccessToken, "old token revoked");
    var context = service.UseAccess(rotated.AccessToken, value => value);
    Require(context.Caller.AccountId == "ABCDE1" && context.Caller.SessionId == rotated.StreamId && context.SessionId == rotated.SessionId, "access creates stable command caller and physical session context");
    await Rejects(service, Ticket("ABCDE1"), "content-1", "op-1", "operation_replayed", "old operation replay denied");
    Require(!rotated.ToString().Contains(rotated.AccessToken, StringComparison.Ordinal), "issue ToString redacts token");
}

async Task DeadlineCapacityAndCancellationAsync()
{
    var service = Service("ABCDE1", clock.GetUtcNow().AddSeconds(1));
    await Rejects(service, Ticket("ABCDE1"), "content-1", "short", "expired", "minimum issue deadline");
    using var capacityAdapter = Adapter(request => Task.FromResult(Result(request.SessionTicket.EndsWith("FGHIJ2", StringComparison.Ordinal) ? "FGHIJ2" : "ABCDE1")));
    var capacity = ServiceWithAdapter(capacityAdapter, capacity: 1, accounts: ("ABCDE1", "FGHIJ2"));
    await capacity.ExchangeAsync(Ticket("ABCDE1"), "content-1", "a", CancellationToken.None);
    await Rejects(capacity, Ticket("FGHIJ2"), "content-1", "b", "capacity", "family capacity");
    CancellationTokenSource? cancellationAfterVerify = null;
    using var cancelledAdapter = new PlayFabIdentityAdapter(new PlayFabIdentityOptions("B16D9", "injected-test-only"), _ => Task.FromResult(Result("ABCDE1")), () => { cancellationAfterVerify?.Cancel(); return clock.GetUtcNow(); });
    var cancelled = ServiceWithAdapter(cancelledAdapter);
    using var source = new CancellationTokenSource(); cancellationAfterVerify = source;
    try { await cancelled.ExchangeAsync(Ticket("ABCDE1"), "content-1", "cancel", source.Token); } catch (OperationCanceledException) { checks++; }
    cancellationAfterVerify = null;
    Require(await cancelled.ExchangeAsync(Ticket("ABCDE1"), "content-1", "after-cancel", CancellationToken.None) is not null, "cancelled verification creates no issue");
}

async Task ReconnectAndGenerationAsync()
{
    using var adapter = Adapter(_ => Task.FromResult(Result("ABCDE1")));
    var service = ServiceWithAdapter(adapter, history: 128);
    var first = await service.ExchangeAsync(Ticket("ABCDE1"), "content-1", "lost-reply", CancellationToken.None);
    clock.Advance(TimeSpan.FromSeconds(31));
    var renewed = await service.ExchangeAsync(Ticket("ABCDE1"), "content-1", "lost-reply", CancellationToken.None);
    Require(renewed.StreamId == first.StreamId && renewed.Generation == first.Generation + 1 && renewed.AccessToken != first.AccessToken, "lost reply retry after access expiry keeps stream and reissues access");
    await service.ExchangeAsync(Ticket("ABCDE1"), "content-1", "new-operation", CancellationToken.None);
    await Rejects(service, Ticket("ABCDE1"), "content-1", "lost-reply", "operation_replayed", "old operation after newer exchange denied");

    using var nearAdapter = Adapter(_ => Task.FromResult(Result("ABCDE1")));
    var near = ServiceWithAdapter(nearAdapter);
    var nearFirst = await near.ExchangeAsync(Ticket("ABCDE1"), "content-1", "near-expiry", CancellationToken.None);
    clock.Advance(TimeSpan.FromSeconds(29));
    var nearRenewed = await near.ExchangeAsync(Ticket("ABCDE1"), "content-1", "near-expiry", CancellationToken.None);
    Require(nearRenewed.Generation == nearFirst.Generation + 1 && nearRenewed.ExpiresInSeconds >= 2, "near expiry retry reissues valid access");

    using var generationAdapter = Adapter(_ => Task.FromResult(Result("ABCDE1")));
    var generation = ServiceWithAdapter(generationAdapter, history: 128);
    for (var operation = 1; operation <= 128; operation++)
        await generation.ExchangeAsync(Ticket("ABCDE1"), "content-1", "generation-" + operation, CancellationToken.None);
    await Rejects(generation, Ticket("ABCDE1"), "content-1", "generation-129", "generation_exhausted", "generation limit");
}

async Task ConcurrencyAsync()
{
    using var adapter = Adapter(_ => Task.FromResult(Result("ABCDE1")));
    var service = ServiceWithAdapter(adapter);
    var exchanges = await Task.WhenAll(service.ExchangeAsync(Ticket("ABCDE1"), "content-1", "left", CancellationToken.None), service.ExchangeAsync(Ticket("ABCDE1"), "content-1", "right", CancellationToken.None));
    var valid = exchanges.Count(issue => CanUse(service, issue.AccessToken));
    Require(valid == 1, "concurrent exchanges leave one valid token");
}

MatchAdmissionService Service(string account, DateTimeOffset? until = null) => ServiceWithAdapter(Adapter(_ => Task.FromResult(Result(account))), until);
MatchAdmissionService ServiceWithAdapter(PlayFabIdentityAdapter adapter, DateTimeOffset? until = null, int capacity = 8, (string left, string right)? accounts = null, int history = 16)
{
    var pair = accounts ?? ("ABCDE1", "FGHIJ2");
    return new MatchAdmissionService(adapter, [new MatchAssignment("match-1", "content-1", pair.left, pair.right, until ?? clock.GetUtcNow().AddMinutes(5))], new AdmissionSettings(capacity, TimeSpan.FromSeconds(30), 10, history), clock);
}
PlayFabIdentityAdapter Adapter(Func<AuthenticateSessionTicketRequest, Task<PlayFabResult<AuthenticateSessionTicketResult>>> call) => new(new PlayFabIdentityOptions("B16D9", "injected-test-only"), call, () => clock.GetUtcNow());
string Ticket(string account) => "ticket-" + account;
PlayFabResult<AuthenticateSessionTicketResult> Result(string account) => new() { Result = new AuthenticateSessionTicketResult { IsSessionTicketExpired = false, UserInfo = new UserAccountInfo { PlayFabId = account } } };
bool CanUse(MatchAdmissionService service, string token) { try { return service.UseAccess(token, _ => true); } catch (AdmissionRejectedException) { return false; } }
async Task Rejects(MatchAdmissionService service, string ticket, string content, string operation, string code, string name) { try { await service.ExchangeAsync(ticket, content, operation, CancellationToken.None); } catch (AdmissionRejectedException error) when (error.Code == code) { checks++; return; } throw new InvalidOperationException(name); }
Task RejectUse(MatchAdmissionService service, string token, string name) { try { service.UseAccess(token, _ => true); } catch (AdmissionRejectedException) { checks++; return Task.CompletedTask; } throw new InvalidOperationException(name); }
void Require(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }

sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan amount) => now = now.Add(amount);
}
