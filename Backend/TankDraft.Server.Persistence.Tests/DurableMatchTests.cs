using Microsoft.Data.Sqlite;
using TankDraft.Server.Persistence;
using TankDraft.Server.Security;
using Xunit;

namespace TankDraft.Server.Persistence.Tests;

public sealed class DurableMatchTests
{
    [Fact]
    public void Accepted_command_survives_reopen_and_exact_retry_has_one_effect()
    {
        using var fixture = new PersistenceFixture();
        CommandEnvelope command;
        CommandReply accepted;
        long head;
        using (var match = fixture.Open())
        {
            var caller = fixture.Caller(0);
            command = fixture.Choose(match, caller, "op-accepted");
            accepted = match.Execute(caller, command);
            Assert.True(accepted.Accepted);
            head = match.CommittedSequence;
        }
        using var reopened = fixture.Open();
        var retried = reopened.Execute(fixture.Caller(0), command);
        Assert.Equal(accepted, retried);
        Assert.Equal(head, reopened.CommittedSequence);
        var changed = command with { Payload = "{\"Token\":0,\"OfferIndex\":2}" };
        Assert.Equal("OperationConflict", reopened.Execute(fixture.Caller(0), changed).Code);
    }

    [Fact]
    public void Before_commit_failure_persists_neither_ack_nor_state()
    {
        using var fixture = new PersistenceFixture();
        using (var match = fixture.Open(storeHook: point =>
        {
            if (point == StoreFaultPoint.BeforeCommit) throw new InvalidOperationException("injected");
        }))
        {
            var caller = fixture.Caller(0);
            Assert.Throws<InvalidOperationException>(() => match.Execute(caller, fixture.Choose(match, caller, "op-before")));
            Assert.Throws<InvalidOperationException>(() => match.Pump());
            Assert.Throws<InvalidOperationException>(() => match.Capture(caller));
        }
        using var reopened = fixture.Open();
        Assert.Equal(0, reopened.CommittedSequence);
    }

    [Fact]
    public void After_apply_before_commit_failure_persists_nothing()
    {
        using var fixture = new PersistenceFixture();
        using (var match = fixture.Open(durableHook: point =>
        {
            if (point == DurableFaultPoint.AfterApplyBeforeCommit) throw new InvalidOperationException("injected");
        }))
        {
            var caller = fixture.Caller(0);
            Assert.Throws<InvalidOperationException>(() => match.Execute(caller, fixture.Choose(match, caller, "op-apply")));
        }
        using var reopened = fixture.Open();
        Assert.Equal(0, reopened.CommittedSequence);
    }

    [Fact]
    public void After_commit_failure_recovers_the_original_receipt()
    {
        using var fixture = new PersistenceFixture();
        CommandEnvelope command;
        using (var match = fixture.Open(storeHook: point =>
        {
            if (point == StoreFaultPoint.AfterCommit) throw new InvalidOperationException("injected");
        }))
        {
            var caller = fixture.Caller(0);
            command = fixture.Choose(match, caller, "op-after");
            Assert.Throws<InvalidOperationException>(() => match.Execute(caller, command));
        }
        using var reopened = fixture.Open();
        Assert.Equal("Accepted", reopened.Execute(fixture.Caller(0), command).Code);
        Assert.Equal(1, reopened.CommittedSequence);
    }

    [Fact]
    public void Expired_and_cross_account_callers_are_denied()
    {
        using var fixture = new PersistenceFixture();
        using var match = fixture.Open();
        var expired = fixture.Caller(0, TimeSpan.FromSeconds(1));
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("Unauthorized", match.Execute(expired, new CommandEnvelope(fixture.Recipe.MatchId, "1", fixture.Recipe.ContentVersion, "expired", 1, "Choose", "{}" )).Code);
        var hostile = fixture.Sessions.Create("account-attacker", fixture.Recipe.MatchId, "0", TimeSpan.FromMinutes(1));
        var caller = fixture.Sessions.Authenticate(hostile.Token) ?? throw new InvalidOperationException();
        Assert.Equal("Unauthorized", match.Execute(caller, new CommandEnvelope(fixture.Recipe.MatchId, "1", fixture.Recipe.ContentVersion, "cross", 1, "Choose", "{}" )).Code);
    }

    [Fact]
    public void Exclusive_writer_and_epoch_are_enforced()
    {
        using var fixture = new PersistenceFixture();
        using var first = fixture.OpenStore();
        var epoch = first.Epoch;
        Assert.ThrowsAny<IOException>(() => fixture.OpenStore());
        first.Dispose();
        using var second = fixture.OpenStore();
        Assert.True(second.Epoch > epoch);
        Assert.Throws<InvalidOperationException>(() => second.Commit(0, epoch, new StoreCommit(0, "Pump", null, null, null, false, new string('0', 64), [], null)));
    }

    [Fact]
    public void Corrupted_recipe_and_log_are_rejected_without_reset()
    {
        using var fixture = new PersistenceFixture();
        using (var match = fixture.Open())
        {
            var caller = fixture.Caller(0);
            Assert.True(match.Execute(caller, fixture.Choose(match, caller, "op-corrupt")).Accepted);
        }
        using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var update = connection.CreateCommand();
            update.CommandText = "UPDATE Log SET Hash = '00' WHERE Sequence = 1;";
            update.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => fixture.Open());
        Assert.True(File.Exists(fixture.DatabasePath));
    }

    [Fact]
    public void Existing_database_rejects_recipe_or_metadata_tampering()
    {
        using var fixture = new PersistenceFixture();
        using (fixture.Open()) { }
        var changed = fixture.Recipe with { BuildVersion = "tampered-build" };
        Assert.Throws<InvalidDataException>(() => new SqliteMatchStore(fixture.DatabasePath, changed.Encode(), fixture.Clock.GetUtcNow()));
        using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Meta;";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => fixture.OpenStore());
    }
}
