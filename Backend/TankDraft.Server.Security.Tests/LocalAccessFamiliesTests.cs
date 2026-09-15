using TankDraft.Server.Security;
using Xunit;

namespace TankDraft.Server.Security.Tests;

public sealed class LocalAccessFamiliesTests
{
    [Fact]
    public void Issue_is_idempotent_before_the_refresh_margin()
    {
        var clock = new ManualTimeProvider();
        var families = new LocalAccessFamilies(1, clock);
        var grant = families.Create("account", "match", "0", TimeSpan.FromMinutes(5));

        var first = Assert.IsType<LocalAccessIssue>(families.Issue(grant.Value, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15)));
        clock.Advance(TimeSpan.FromSeconds(10));
        var repeated = Assert.IsType<LocalAccessIssue>(families.Issue(grant.Value, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15)));

        Assert.Equal(first.AccessToken, repeated.AccessToken);
        Assert.Equal(first.SessionId, repeated.SessionId);
        Assert.Equal(first.StreamId, repeated.StreamId);
        Assert.Equal(1, repeated.Generation);
    }

    [Fact]
    public void Refresh_rotates_access_session_but_keeps_stream_and_rejects_old_access()
    {
        var clock = new ManualTimeProvider();
        var families = new LocalAccessFamilies(1, clock);
        var grant = families.Create("account", "match", "0", TimeSpan.FromMinutes(5));
        var first = Assert.IsType<LocalAccessIssue>(families.Issue(grant.Value, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15)));
        clock.Advance(TimeSpan.FromSeconds(45));

        var refreshed = Assert.IsType<LocalAccessIssue>(families.Issue(grant.Value, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15)));

        Assert.NotEqual(first.AccessToken, refreshed.AccessToken);
        Assert.NotEqual(first.SessionId, refreshed.SessionId);
        Assert.Equal(first.StreamId, refreshed.StreamId);
        Assert.Equal(2, refreshed.Generation);
        Assert.Throws<UnauthorizedAccessException>(() => families.UseAccess(first.AccessToken, context => context));
        Assert.Equal(refreshed.SessionId, families.UseAccess(refreshed.AccessToken, context => context).SessionId);
    }

    [Fact]
    public void Grant_revocation_invalidates_current_access()
    {
        var families = new LocalAccessFamilies(1, new ManualTimeProvider());
        var grant = families.Create("account", "match", "0", TimeSpan.FromMinutes(5));
        var access = Assert.IsType<LocalAccessIssue>(families.Issue(grant.Value, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15)));

        Assert.True(families.RevokeGrant(grant.Value));

        Assert.Null(families.Issue(grant.Value, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15)));
        Assert.Throws<UnauthorizedAccessException>(() => families.UseAccess(access.AccessToken, context => context));
    }

    [Fact]
    public void Grant_expiry_invalidates_current_access()
    {
        var clock = new ManualTimeProvider();
        var families = new LocalAccessFamilies(1, clock);
        var grant = families.Create("account", "match", "0", TimeSpan.FromSeconds(30));
        var access = Assert.IsType<LocalAccessIssue>(families.Issue(grant.Value, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15)));
        clock.Advance(TimeSpan.FromSeconds(30));

        Assert.Null(families.Issue(grant.Value, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15)));
        Assert.Throws<UnauthorizedAccessException>(() => families.UseAccess(access.AccessToken, context => context));
    }

    [Fact]
    public void Reused_issue_reports_remaining_access_lifetime_and_original_refresh_delay()
    {
        var clock = new ManualTimeProvider();
        var families = new LocalAccessFamilies(1, clock);
        var grant = families.Create("account", "match", "0", TimeSpan.FromMinutes(5));
        var first = Assert.IsType<LocalAccessIssue>(families.Issue(grant.Value, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15)));
        clock.Advance(TimeSpan.FromSeconds(10));

        var repeated = Assert.IsType<LocalAccessIssue>(families.Issue(grant.Value, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15)));

        Assert.Equal(60, first.ExpiresInSeconds);
        Assert.Equal(50, repeated.ExpiresInSeconds);
        Assert.Equal(45, first.RefreshAfterSeconds);
        Assert.Equal(35, repeated.RefreshAfterSeconds);
    }

    [Fact]
    public void Reused_issue_rounds_subsecond_refresh_delay_up_to_one_second()
    {
        var clock = new ManualTimeProvider();
        var families = new LocalAccessFamilies(1, clock);
        var grant = families.Create("account", "match", "0", TimeSpan.FromMinutes(5));
        var first = Assert.IsType<LocalAccessIssue>(families.Issue(grant.Value, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
        clock.Advance(TimeSpan.FromMilliseconds(24400));

        var repeated = Assert.IsType<LocalAccessIssue>(families.Issue(grant.Value, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));

        Assert.Equal(first.AccessToken, repeated.AccessToken);
        Assert.Equal(1, repeated.RefreshAfterSeconds);
    }

    [Fact]
    public async Task Concurrent_issue_and_revoke_leave_no_usable_access()
    {
        var families = new LocalAccessFamilies(1, new ManualTimeProvider());
        var grant = families.Create("account", "match", "0", TimeSpan.FromMinutes(5));
        using var start = new ManualResetEventSlim(false);
        var issuers = Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            start.Wait();
            return families.Issue(grant.Value, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15));
        })).ToArray();
        var revoke = Task.Run(() =>
        {
            start.Wait();
            return families.RevokeGrant(grant.Value);
        });

        start.Set();
        var issues = await Task.WhenAll(issuers);
        Assert.True(await revoke);

        Assert.Null(families.Issue(grant.Value, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15)));
        foreach (var issue in issues.Where(issue => issue is not null))
            Assert.Throws<UnauthorizedAccessException>(() => families.UseAccess(issue!.AccessToken, context => context));
    }
}
