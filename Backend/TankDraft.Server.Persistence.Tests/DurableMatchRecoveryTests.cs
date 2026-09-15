using Microsoft.Data.Sqlite;
using TankDraft.Contracts.Battle;
using TankDraft.Server.Persistence;
using TankDraft.Server.Security;
using Xunit;

namespace TankDraft.Server.Persistence.Tests;

public sealed class DurableMatchRecoveryTests
{
    [Fact]
    public void Active_projectile_or_zone_reopens_with_identical_authoritative_state()
    {
        using var fixture = new PersistenceFixture();
        var caller0 = fixture.Caller(0, TimeSpan.FromHours(1));
        var caller1 = fixture.Caller(1, TimeSpan.FromHours(1));
        string expectedHash;
        long expectedTick;
        IReadOnlyList<TankDraft.Contracts.Battle.BattleEntityState> expectedEntities;
        using (var live = fixture.Open())
        {
            var reachedActiveEffect = false;
            for (var step = 0; step < 2_000; step++)
            {
                fixture.Clock.Advance(TimeSpan.FromMilliseconds(34));
                _ = live.Pump();
                var state = live.Capture(caller0);
                if (state.Phase == "Battle" && state.Entities.Any(entity => entity.Kind is BattleEntityKind.Projectile or BattleEntityKind.Zone))
                {
                    reachedActiveEffect = true;
                    expectedHash = live.GetStateHash();
                    expectedTick = state.SimulationTick;
                    expectedEntities = state.Entities.ToArray();
                    goto Reopen;
                }
            }
            throw new Xunit.Sdk.XunitException("QA authored battle never reached an active effect state.");

        Reopen:
            Assert.True(reachedActiveEffect);
        }
        using var recovered = fixture.Open();
        var actual = recovered.Capture(caller0);
        Assert.Equal(expectedHash, recovered.GetStateHash());
        Assert.Equal(expectedTick, actual.SimulationTick);
        Assert.Equal(expectedEntities, actual.Entities);
        Assert.NotEmpty(recovered.Capture(caller1).Entities);
    }

    [Fact]
    public void Reopening_after_every_committed_step_matches_uninterrupted_match()
    {
        using var controlFixture = new PersistenceFixture();
        using var recoveryFixture = new PersistenceFixture();
        var controlCaller = controlFixture.Caller(0, TimeSpan.FromHours(1));
        var recoveryCaller = recoveryFixture.Caller(0, TimeSpan.FromHours(1));
        using var control = controlFixture.Open();
        DurableMatch? recovered = recoveryFixture.Open();
        try
        {
            for (var step = 0; step < 2_000; step++)
            {
                var expected = control.Capture(controlCaller);
                var actual = recovered.Capture(recoveryCaller);
                Assert.Equal(expected.Phase, actual.Phase);
                Assert.Equal(expected.Round, actual.Round);
                Assert.Equal(control.GetStateHash(), recovered.GetStateHash());
                if (expected.Phase == "MatchResult")
                {
                    Assert.Equal(expected.Wins0, actual.Wins0);
                    Assert.Equal(expected.Wins1, actual.Wins1);
                    return;
                }
                controlFixture.Clock.Advance(TimeSpan.FromSeconds(1));
                recoveryFixture.Clock.Advance(TimeSpan.FromSeconds(1));
                _ = control.Pump();
                _ = recovered.Pump();
                recovered.Dispose();
                recovered = recoveryFixture.Open();
            }
            throw new Xunit.Sdk.XunitException("Bounded recovery match did not reach a result.");
        }
        finally { recovered?.Dispose(); }
    }

    [Fact]
    public void Completion_outbox_retries_with_one_external_application()
    {
        using var fixture = new PersistenceFixture();
        var caller = fixture.Caller(0, TimeSpan.FromHours(1));
        using (var match = fixture.Open()) fixture.DriveToResult(match, caller);
        var sinkPath = Path.Combine(Path.GetDirectoryName(fixture.DatabasePath)!, "sink.db");
        var deliveries = new List<string>();
        var faultedOnce = false;
        using (var match = fixture.Open(durableHook: point =>
        {
            if (point == DurableFaultPoint.AfterDeliveryBeforeMark && !faultedOnce)
            {
                faultedOnce = true;
                throw new InvalidOperationException("after external application");
            }
        }))
        {
            Assert.Throws<InvalidOperationException>(() => match.DeliverOutbox(message => ApplyToSink(sinkPath, message, deliveries)));
        }
        using var recovered = fixture.Open();
        Assert.Equal(1, recovered.DeliverOutbox(message => ApplyToSink(sinkPath, message, deliveries)));
        Assert.Equal(0, recovered.DeliverOutbox(message => ApplyToSink(sinkPath, message, deliveries)));
        Assert.Equal(2, deliveries.Count);
        Assert.Equal(deliveries[0], deliveries[1]);
        Assert.Equal(1, SinkCount(sinkPath));
    }

    private static bool ApplyToSink(string sinkPath, MatchCompletedEvent message, List<string> deliveries)
    {
        deliveries.Add(message.ResultId);
        using var connection = new SqliteConnection($"Data Source={sinkPath};Pooling=False");
        connection.Open();
        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = "CREATE TABLE IF NOT EXISTS Applied(ResultId TEXT PRIMARY KEY, PayloadHash TEXT NOT NULL);";
            schema.ExecuteNonQuery();
        }
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT OR IGNORE INTO Applied(ResultId, PayloadHash) VALUES($id, $hash);";
        insert.Parameters.AddWithValue("$id", message.ResultId);
        insert.Parameters.AddWithValue("$hash", PersistenceJson.Hash(PersistenceJson.Encode(message)));
        insert.ExecuteNonQuery();
        return true;
    }

    private static long SinkCount(string sinkPath)
    {
        using var connection = new SqliteConnection($"Data Source={sinkPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Applied;";
        return (long)(command.ExecuteScalar() ?? throw new InvalidOperationException());
    }
}
