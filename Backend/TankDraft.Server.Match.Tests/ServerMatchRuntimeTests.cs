using System.Text.Json;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Domain;
using TankDraft.Server.Security;
using Xunit;

namespace TankDraft.Server.Match.Tests;

public sealed class ServerMatchRuntimeTests
{
    [Fact]
    public void Deadline_is_monotonic_and_auto_choices_run_once_at_the_exact_boundary()
    {
        using var harness = MatchHarness.Create();
        harness.Clock.AdvanceMonotonic(TimeSpan.FromMilliseconds(999));
        Assert.True(harness.Runtime.Pump());
        Assert.Empty(harness.Runtime.InspectDecisions());

        harness.Clock.AdvanceMonotonic(TimeSpan.FromMilliseconds(1));
        Assert.True(harness.Runtime.Pump());
        var exact = harness.Runtime.InspectDecisions();
        Assert.Equal(2, exact.Count);
        Assert.All(exact, value => Assert.True(value.Automatic));

        harness.Clock.AdvanceMonotonic(TimeSpan.FromMilliseconds(1));
        Assert.True(harness.Runtime.Pump());
        Assert.Equal(2, harness.Runtime.InspectDecisions().Count);
    }

    [Fact]
    public async Task Concurrent_pumps_at_deadline_do_not_duplicate_auto_choices()
    {
        using var harness = MatchHarness.Create();
        harness.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => harness.Runtime.Pump())));

        var decisions = harness.Runtime.InspectDecisions();
        Assert.Equal(2, decisions.Count);
        Assert.Equal(new[] { 0, 1 }, decisions.Select(value => value.Side).OrderBy(value => value).ToArray());
        Assert.All(decisions, value => Assert.True(value.Automatic));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void Choice_is_admitted_only_before_the_deadline_tick(long offsetTicks, bool expectedAccepted)
    {
        using var harness = MatchHarness.Create();
        var snapshot = harness.Capture(0);
        harness.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1).Add(TimeSpan.FromTicks(offsetTicks)));
        if (offsetTicks >= 0) harness.Runtime.Pump();

        var reply = harness.Runtime.Execute(harness.Caller(0), harness.ChooseEnvelope(snapshot, "boundary", 1));
        Assert.Equal(expectedAccepted, reply.Accepted);
        if (expectedAccepted)
        {
            Assert.Single(harness.Runtime.InspectDecisions());
            return;
        }

        var decisions = harness.Runtime.InspectDecisions();
        Assert.Equal(2, decisions.Count);
        Assert.All(decisions, value => Assert.True(value.Automatic));
    }

    [Fact]
    public async Task Concurrent_execute_and_pump_at_deadline_admit_no_late_choice_and_auto_once_per_side()
    {
        using var harness = MatchHarness.Create();
        var snapshot = harness.Capture(0);
        harness.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        var command = Task.Run(() => harness.Runtime.Execute(harness.Caller(0), harness.ChooseEnvelope(snapshot, "at-deadline", 1)));
        var pump = Task.Run(() => harness.Runtime.Pump());
        await Task.WhenAll(command, pump);

        Assert.False((await command).Accepted);
        var decisions = harness.Runtime.InspectDecisions();
        Assert.Equal(2, decisions.Count);
        Assert.Equal(new[] { 0, 1 }, decisions.Select(value => value.Side).OrderBy(value => value).ToArray());
        Assert.All(decisions, value => Assert.True(value.Automatic));
    }

    [Fact]
    public void One_committed_choice_keeps_deadline_and_only_missing_side_is_automatic()
    {
        using var harness = MatchHarness.Create();
        var before = harness.Capture(0);
        var reply = harness.Choose(0, before, 0, "manual-0", 1);
        Assert.True(reply.Accepted);
        Assert.Equal(before.DeadlineAt, harness.Capture(0).DeadlineAt);

        harness.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        harness.Runtime.Pump();
        var decisions = harness.Runtime.InspectDecisions();
        Assert.Equal(2, decisions.Count);
        Assert.Contains(decisions, value => value.Side == 0 && !value.Automatic);
        Assert.Contains(decisions, value => value.Side == 1 && value.Automatic);
    }

    [Fact]
    public void Round_result_continuation_immediately_republishes_full_draft_formations_with_reproducible_ids()
    {
        using var left = MatchHarness.Create(maxStepsPerPump: 128);
        using var right = MatchHarness.Create(maxStepsPerPump: 128);
        left.AdvanceToRoundResult();
        right.AdvanceToRoundResult();

        left.Clock.AdvanceMonotonic(TimeSpan.FromMilliseconds(100));
        right.Clock.AdvanceMonotonic(TimeSpan.FromMilliseconds(100));
        left.CatchUp();
        right.CatchUp();

        var left0 = left.Capture(0);
        var left1 = left.Capture(1);
        var right0 = right.Capture(0);
        Assert.Equal(nameof(MatchPhase.Draft), left0.Phase);
        Assert.Equal(left0.Army.Sum(value => value.Count), left0.Entities.Count(value => value.Side == 0));
        Assert.Equal(left1.Army.Sum(value => value.Count), left0.Entities.Count(value => value.Side == 1));
        Assert.Equal(EntitySignature(left0.Entities), EntitySignature(right0.Entities));
    }

    [Fact]
    public void Accepted_draft_choice_immediately_publishes_the_complete_partial_formation_to_both_sides()
    {
        using var harness = MatchHarness.Create();
        var initial = harness.Capture(0);
        var offer = initial.Offers.Select((value, index) => (value, index))
            .First(value => value.value.UnitId != "unit.mines");
        var command = harness.ChooseEnvelope(initial, "immediate-public-formation", 1, offer.index);

        var accepted = harness.Runtime.Execute(harness.Caller(0), command);
        Assert.True(accepted.Accepted);

        var owner = harness.Capture(0);
        var opponent = harness.Capture(1);
        Assert.True(owner.Committed);
        Assert.NotEmpty(owner.Army);
        Assert.NotEmpty(owner.Entities);
        Assert.Contains(owner.Entities, entity => entity.Side == 0 && entity.DefinitionId == offer.value.UnitId);
        Assert.DoesNotContain(owner.Entities, entity => entity.Side == 1);
        Assert.Equal(EntitySignature(owner.Entities), EntitySignature(opponent.Entities));

        var replay = harness.Runtime.Execute(harness.Caller(0), command);
        Assert.Equal(accepted, replay);
        Assert.Single(harness.Runtime.InspectDecisions());
        Assert.Equal(EntitySignature(owner.Entities), EntitySignature(harness.Capture(1).Entities));

        harness.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        harness.Runtime.Pump();
        Assert.Equal(2, harness.Runtime.InspectDecisions().Count);
    }

    [Fact]
    public void Replay_preserves_ack_and_does_not_reset_the_authoritative_deadline()
    {
        using var harness = MatchHarness.Create();
        harness.Clock.AdvanceMonotonic(TimeSpan.FromMilliseconds(500));
        var snapshot = harness.Capture(1);
        var envelope = harness.ChooseEnvelope(snapshot, "replay", 1);
        var first = harness.Runtime.Execute(harness.Caller(0), envelope);
        var deadline = harness.Capture(0).DeadlineAt;
        var replay = harness.Runtime.Execute(harness.Caller(0), envelope);

        Assert.True(first.Accepted);
        Assert.Equal(first, replay);
        Assert.Equal(deadline, harness.Capture(0).DeadlineAt);
        Assert.Single(harness.Runtime.InspectDecisions());
    }

    [Fact]
    public void Accepted_replay_after_deadline_returns_original_ack_without_applying_twice()
    {
        using var harness = MatchHarness.Create();
        var snapshot = harness.Capture(0);
        var envelope = harness.ChooseEnvelope(snapshot, "late-replay", 1);
        var first = harness.Runtime.Execute(harness.Caller(0), envelope);
        Assert.True(first.Accepted);
        harness.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        harness.Runtime.Pump();
        var stateBeforeReplay = harness.Capture(0);
        var replay = harness.Runtime.Execute(harness.Caller(0), envelope);

        Assert.Equal(first, replay);
        Assert.Equal(2, harness.Runtime.InspectDecisions().Count);
        var stateAfterReplay = harness.Capture(0);
        Assert.Equal(stateBeforeReplay.Revision, stateAfterReplay.Revision);
        Assert.Equal(stateBeforeReplay.ChoiceToken, stateAfterReplay.ChoiceToken);
    }

    [Fact]
    public void Returning_session_observes_current_state_and_old_choice_token_cannot_mutate_it()
    {
        using var harness = MatchHarness.Create();
        var old = harness.Capture(0);
        harness.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        harness.Runtime.Pump();
        var current = harness.Capture(0);
        Assert.NotEqual(old.ChoiceToken, current.ChoiceToken);

        var stale = harness.Runtime.Execute(harness.Caller(0), harness.ChooseEnvelope(current, "stale", 1, token: old.ChoiceToken));
        Assert.False(stale.Accepted);
        Assert.Equal("stale-token", stale.Code);

        var returned = harness.IssueReturningCaller(0);
        var returnedSnapshot = harness.Runtime.Capture(returned);
        Assert.Equal(current.MatchId, returnedSnapshot.MatchId);
        Assert.Equal(current.Round, returnedSnapshot.Round);
        Assert.Equal(current.Phase, returnedSnapshot.Phase);
        Assert.Equal(current.ChoiceToken, returnedSnapshot.ChoiceToken);
        Assert.Empty(returnedSnapshot.Events);
    }

    [Fact]
    public void Utc_clock_jumps_do_not_change_monotonic_deadline_or_trigger_auto_choice()
    {
        using var harness = MatchHarness.Create();
        var before = harness.Capture(0);
        harness.Clock.JumpUtc(TimeSpan.FromSeconds(30));
        Assert.True(harness.Runtime.Pump());
        var after = harness.Capture(0);

        Assert.Equal(before.DeadlineAt, after.DeadlineAt);
        Assert.Empty(harness.Runtime.InspectDecisions());
    }

    [Fact]
    public void Journal_capacity_faults_before_later_automatic_mutation()
    {
        using var harness = MatchHarness.Create(journalCapacity: 1);
        harness.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        harness.Runtime.Pump();

        var snapshot = harness.Capture(1);
        Assert.Equal("Fault", snapshot.Fault);
        Assert.Single(harness.Runtime.InspectDecisions());
        Assert.False(snapshot.Committed);
        var revision = snapshot.Revision;
        Assert.True(harness.Runtime.Pump());
        var rejected = harness.Runtime.Execute(harness.Caller(1), harness.ChooseEnvelope(snapshot, "after-fault", 1));
        Assert.False(rejected.Accepted);
        Assert.Equal("Fault", rejected.Code);
        Assert.Equal(revision, harness.Capture(1).Revision);
    }

    [Fact]
    public void Expired_caller_is_rejected_before_deadline_pump_and_cannot_capture()
    {
        using var harness = MatchHarness.Create();
        var snapshot = harness.Capture(0);
        var expired = harness.Caller(0);
        harness.Clock.JumpUtc(TimeSpan.FromMinutes(11));

        var reply = harness.Runtime.Execute(expired, harness.ChooseEnvelope(snapshot, "expired", 1));
        Assert.False(reply.Accepted);
        Assert.Empty(harness.Runtime.InspectDecisions());
        Assert.Throws<UnauthorizedAccessException>(() => harness.Runtime.Capture(expired));
    }

    [Fact]
    public void Event_ring_requires_resync_after_cursor_falls_behind()
    {
        using var harness = MatchHarness.Create(eventCapacity: 2, maxStepsPerPump: 128);
        harness.AdvanceToBattle();
        var seenEvent = false;
        var seenEffectEntity = false;
        for (var index = 0; index < 160; index++)
        {
            harness.Clock.AdvanceMonotonic(TimeSpan.FromMilliseconds(34));
            harness.Runtime.Pump();
            var current = harness.Capture(0, 0);
            seenEvent |= current.EventSequence > 0;
            seenEffectEntity |= current.Entities.Any(value => value.Kind is BattleEntityKind.Projectile or BattleEntityKind.Zone);
            if (current.Phase != nameof(MatchPhase.Battle)) break;
        }

        var snapshot = harness.Capture(0, 0);
        Assert.True(seenEvent);
        Assert.True(seenEffectEntity);
        Assert.True(snapshot.ResyncRequired);
        Assert.InRange(snapshot.Events.Count, 0, 2);
    }

    [Fact]
    public void Event_cursor_is_consumed_once_and_a_future_cursor_requires_resync()
    {
        using var harness = MatchHarness.Create(eventCapacity: 8192, maxStepsPerPump: 128);
        harness.AdvanceToBattle();
        ServerMatchSnapshot state;
        do
        {
            harness.Clock.AdvanceMonotonic(TimeSpan.FromMilliseconds(34));
            harness.Runtime.Pump();
            state = harness.Capture(0, 0);
        } while (state.EventSequence == 0 && state.Phase == nameof(MatchPhase.Battle));
        Assert.True(state.EventSequence > 0);

        var one = harness.Capture(0, state.EventSequence - 1);
        var acknowledged = harness.Capture(0, state.EventSequence);
        var future = harness.Capture(0, state.EventSequence + 1);
        Assert.False(one.ResyncRequired);
        Assert.Single(one.Events);
        Assert.False(acknowledged.ResyncRequired);
        Assert.Empty(acknowledged.Events);
        Assert.True(future.ResyncRequired);
    }

    [Fact]
    public void Loser_gets_one_comeback_choice_then_three_normal_choices_resume()
    {
        using var harness = MatchHarness.Create(maxStepsPerPump: 128);
        harness.AdvanceToRoundResult();
        var result = harness.Capture(0);
        var loser = 1 - result.LastWinner;
        var winner = result.LastWinner;

        harness.Clock.AdvanceMonotonic(TimeSpan.FromMilliseconds(100));
        harness.Runtime.Pump();
        var bonus = harness.Capture(loser);
        var blockedWinner = harness.Capture(winner);
        Assert.True(bonus.IsComeback);
        Assert.Equal(loser, bonus.BonusSide);
        Assert.Equal(3, bonus.Offers.Count);
        Assert.Empty(blockedWinner.Offers);

        harness.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        harness.Runtime.Pump();
        Assert.Equal(1, harness.Capture(loser).ChoiceNumber);
        harness.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        harness.Runtime.Pump();
        Assert.Equal(2, harness.Capture(loser).ChoiceNumber);
        harness.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        harness.Runtime.Pump();
        Assert.Equal(3, harness.Capture(loser).ChoiceNumber);
        harness.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        harness.Runtime.Pump();
        Assert.Equal(nameof(MatchPhase.Battle), harness.Capture(loser).Phase);
    }

    [Fact]
    public void Fine_and_coarse_authoritative_pumping_produce_the_same_authored_timeline_at_the_same_time()
    {
        using var fine = MatchHarness.Create(maxStepsPerPump: 128);
        using var coarse = MatchHarness.Create(maxStepsPerPump: 128);
        fine.AdvanceFor(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(34));
        coarse.AdvanceFor(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1));

        var fineSnapshot = fine.Capture(0);
        var coarseSnapshot = coarse.Capture(0);
        Assert.Equal(fineSnapshot.Wins0, coarseSnapshot.Wins0);
        Assert.Equal(fineSnapshot.Wins1, coarseSnapshot.Wins1);
        Assert.Equal(fineSnapshot.LastWinner, coarseSnapshot.LastWinner);
        Assert.Equal(fineSnapshot.Phase, coarseSnapshot.Phase);
        Assert.Equal(fineSnapshot.Round, coarseSnapshot.Round);
        Assert.Equal(fineSnapshot.ChoiceToken, coarseSnapshot.ChoiceToken);
        Assert.Equal(fineSnapshot.SimulationTick, coarseSnapshot.SimulationTick);
        Assert.Equal(EntitySignature(fineSnapshot.Entities), EntitySignature(coarseSnapshot.Entities));
        var fineEvents = fine.Capture(0, 0);
        var coarseEvents = coarse.Capture(0, 0);
        Assert.Equal(EventSignature(fineEvents.Events), EventSignature(coarseEvents.Events));
        Assert.Equal(
            fine.InspectAutomaticDecisionSignature(),
            coarse.InspectAutomaticDecisionSignature());
        Assert.Equal(fine.Runtime.InspectDecisions(), coarse.Runtime.InspectDecisions());
    }

    [Fact]
    public void Snapshots_are_detached_and_assigned_identity_is_enforced()
    {
        using var harness = MatchHarness.Create();
        var initial = harness.Capture(0);
        Assert.Empty(initial.Army);
        Assert.True(harness.Choose(0, initial, 0, "choose-0", 1).Accepted);
        Assert.True(harness.Choose(1, harness.Capture(1), 0, "choose-1", 1).Accepted);
        var committed = harness.Capture(0);
        var originalArmy = committed.Army.Select(value => (value.UnitId, value.Count, value.UpgradeLevel)).ToArray();
        Assert.NotEmpty(originalArmy);
        Assert.True(harness.Choose(0, committed, 0, "choose-2-0", 2).Accepted);
        Assert.True(harness.Choose(1, harness.Capture(1), 0, "choose-2-1", 2).Accepted);
        Assert.Equal(originalArmy, committed.Army.Select(value => (value.UnitId, value.Count, value.UpgradeLevel)).ToArray());

        var intruder = harness.Registry.Create("intruder", "match-1", "0", TimeSpan.FromMinutes(2));
        var caller = harness.Registry.Authenticate(intruder.Token)!;
        var denied = harness.Runtime.Execute(caller, harness.ChooseEnvelope(harness.Capture(0), "intruder", 1));
        Assert.False(denied.Accepted);
        Assert.Equal("Unauthorized", denied.Code);
    }

    [Fact]
    public void Legal_order_consumes_one_charge_and_timeout_uses_only_offers()
    {
        using var harness = MatchHarness.Create();
        var first0 = harness.Capture(0);
        var mobileOffer = first0.Offers.Select((offer, index) => (offer, index))
            .First(value => value.offer.UnitId != "unit.mines").index;
        Assert.True(harness.Choose(0, first0, mobileOffer, "mobile", 1).Accepted);
        Assert.True(harness.Choose(1, harness.Capture(1), 0, "other", 1).Accepted);

        var draft = harness.Capture(0);
        Assert.True(draft.CanUseOrder);
        var charges = draft.OrderCharges;
        var order = harness.OrderEnvelope(draft, "armor", 2);
        var reply = harness.Runtime.Execute(harness.Caller(0), order);
        Assert.True(reply.Accepted);
        var committed = harness.Capture(0);
        Assert.True(committed.Committed);
        Assert.Equal("Order", committed.CommittedChoice!.Kind);
        Assert.Equal(charges - 1, committed.OrderCharges);

        harness.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        harness.Runtime.Pump();
        Assert.Equal(charges - 1, harness.Capture(0).OrderCharges);
        Assert.DoesNotContain(harness.Runtime.InspectDecisions(), value => value.Automatic && value.Kind == "Order");
    }

    [Fact]
    public void Catchup_refuses_command_without_consuming_its_sequence_then_accepts_same_sequence()
    {
        using var harness = MatchHarness.Create(maxStepsPerPump: 1);
        harness.AdvanceToBattle();
        harness.Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
        var current = harness.Capture(0);
        var command = harness.ChooseEnvelope(current, "catchup", 1);
        var blocked = harness.Runtime.Execute(harness.Caller(0), command);
        Assert.False(blocked.Accepted);
        Assert.Equal("CatchingUp", blocked.Code);

        harness.CatchUp();
        var after = harness.Runtime.Execute(harness.Caller(0), command);
        Assert.NotEqual("UnexpectedSequence", after.Code);
    }

    [Fact]
    public void Offline_match_resolves_real_battles_until_four_wins_and_dispose_stops_access()
    {
        using var harness = MatchHarness.Create(maxStepsPerPump: 128);
        harness.RunOfflineUntilMatchResult(TimeSpan.FromMilliseconds(34));
        var final = harness.Capture(0);
        Assert.Equal(nameof(MatchPhase.MatchResult), final.Phase);
        Assert.True(final.Wins0 == 4 || final.Wins1 == 4);
        Assert.NotEmpty(final.Results);

        harness.Runtime.Dispose();
        Assert.Throws<ObjectDisposedException>(() => harness.Runtime.Capture(harness.Caller(0)));
    }

    [Fact]
    public void Fine_and_coarse_offline_full_matches_have_identical_authoritative_results()
    {
        using var fine = MatchHarness.Create(maxStepsPerPump: 128);
        using var coarse = MatchHarness.Create(maxStepsPerPump: 128);
        fine.RunOfflineUntilMatchResult(TimeSpan.FromMilliseconds(34));
        coarse.RunOfflineUntilMatchResult(TimeSpan.FromSeconds(1));

        var fineFinal = fine.Capture(0);
        var coarseFinal = coarse.Capture(0);
        Assert.Equal((fineFinal.Wins0, fineFinal.Wins1, fineFinal.LastWinner),
            (coarseFinal.Wins0, coarseFinal.Wins1, coarseFinal.LastWinner));
        Assert.Equal(fineFinal.Results.Select(value => (value.Round, value.Winner, value.UsedRandomTieBreak,
            value.TieBreakSeed, value.TerminalHits.Count)), coarseFinal.Results.Select(value => (value.Round,
            value.Winner, value.UsedRandomTieBreak, value.TieBreakSeed, value.TerminalHits.Count)));
        Assert.Equal(fine.Runtime.InspectDecisions(), coarse.Runtime.InspectDecisions());
    }

    [Fact]
    public void Persistent_bonuses_are_side_scoped_copied_and_absent_by_default()
    {
        var content = AuthoredMatchFixture.Load();
        var baseline = new MatchService(content.MatchRules, content.MatchUnits, content.Deck, content.Deck, content.OrderId, content.Seed, content.OrderId);
        var supplied = content.MatchUnits.ToDictionary(x => x.Id, _ => new PersistentBattleBonus(100, 50, 25), StringComparer.Ordinal);
        var boosted = new MatchService(content.MatchRules, content.MatchUnits, content.Deck, content.Deck, content.OrderId, content.Seed, content.OrderId, supplied, null);
        supplied.Clear();
        AdvanceDraft(baseline);
        AdvanceDraft(boosted);
        var ordinary = baseline.CreateScenario().Stacks.ToDictionary(x => (x.Side, x.UnitId));
        var withPersistent = boosted.CreateScenario().Stacks.ToDictionary(x => (x.Side, x.UnitId));
        foreach (var pair in ordinary)
        {
            var actual = withPersistent[pair.Key];
            if (pair.Key.Side == 0)
            {
                Assert.Equal(pair.Value.HpBonusPercent + 100, actual.HpBonusPercent);
                Assert.Equal(pair.Value.DamageBonusPercent + 50, actual.DamageBonusPercent);
                Assert.Equal(25, actual.AttackSpeedBonusPercent);
            }
            else
            {
                Assert.Equal(pair.Value.HpBonusPercent, actual.HpBonusPercent);
                Assert.Equal(pair.Value.DamageBonusPercent, actual.DamageBonusPercent);
                Assert.Equal(0, actual.AttackSpeedBonusPercent);
            }
        }
    }

    [Fact]
    public void Persistent_bonuses_and_attack_speed_stack_reject_values_above_bound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PersistentBattleBonus(10001, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PersistentBattleBonus(0, 10001, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PersistentBattleBonus(0, 0, 10001));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleArmyStack(0, "unit.test", 1, attackSpeedBonusPercent: 10001));
    }

    private static void AdvanceDraft(MatchService match)
    {
        for (var choice = 0; choice < 3; choice++)
        {
            var token = match.ChoiceToken;
            Assert.True(match.TryChoose(0, token, 0, out _));
            Assert.True(match.TryChoose(1, token, 0, out _));
        }
        Assert.Equal(MatchPhase.Battle, match.Phase);
    }

    private sealed class MatchHarness : IDisposable
    {
        private readonly AuthoredMatchFixture _content;
        private readonly LocalSessionIssue[] _issues;
        private long[] _sequence = new long[2];

        private MatchHarness(AuthoredMatchFixture content, ManualTimeProvider clock, LocalSessionRegistry registry,
            LocalSessionIssue[] issues, ServerMatchRuntime runtime)
        {
            _content = content;
            Clock = clock;
            Registry = registry;
            _issues = issues;
            Runtime = runtime;
        }

        public ManualTimeProvider Clock { get; }
        public LocalSessionRegistry Registry { get; }
        public ServerMatchRuntime Runtime { get; }

        public static MatchHarness Create(int maxStepsPerPump = 64, int eventCapacity = 64, int journalCapacity = 512)
        {
            var content = AuthoredMatchFixture.Load();
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));
            var registry = new LocalSessionRegistry(8, clock);
            var issues = new[]
            {
                registry.Create("account-0", "match-1", "0", TimeSpan.FromMinutes(10)),
                registry.Create("account-1", "match-1", "1", TimeSpan.FromMinutes(10))
            };
            var match = new MatchService(content.MatchRules, content.MatchUnits, content.Deck, content.Deck, content.OrderId, content.Seed, content.OrderId);
            var settings = new ServerMatchSettings(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100),
                maxStepsPerPump, eventCapacity, journalCapacity, 512);
            return new MatchHarness(content, clock, registry, issues,
                new ServerMatchRuntime("match-1", "content-1", match, content.Definitions, content.BattleRules,
                    settings, clock, 12345, new[] { "account-0", "account-1" }));
        }

        public CallerIdentity Caller(int side) => Registry.Authenticate(_issues[side].Token)!;
        public CallerIdentity IssueReturningCaller(int side) => Registry.Authenticate(Registry.Create("account-" + side, "match-1", side.ToString(), TimeSpan.FromMinutes(5)).Token)!;
        public ServerMatchSnapshot Capture(int side, long? cursor = null) => Runtime.Capture(Caller(side), cursor);
        public CommandReply Choose(int side, ServerMatchSnapshot snapshot, int offerIndex, string operationId, long sequence) => Runtime.Execute(Caller(side), ChooseEnvelope(snapshot, operationId, sequence, offerIndex));
        public CommandEnvelope ChooseEnvelope(ServerMatchSnapshot snapshot, string operationId, long sequence, int offerIndex = 0, long? token = null) =>
            new("match-1", snapshot.Round.ToString(System.Globalization.CultureInfo.InvariantCulture), "content-1", operationId,
                sequence, "Choose", JsonSerializer.Serialize(new ChoosePayload { Token = token ?? snapshot.ChoiceToken, OfferIndex = offerIndex }));
        public CommandEnvelope OrderEnvelope(ServerMatchSnapshot snapshot, string operationId, long sequence) =>
            new("match-1", snapshot.Round.ToString(System.Globalization.CultureInfo.InvariantCulture), "content-1", operationId,
                sequence, "Order", JsonSerializer.Serialize(new OrderPayload { Token = snapshot.ChoiceToken }));

        public void AdvanceToBattle()
        {
            for (var index = 0; index < 12 && Capture(0).Phase == nameof(MatchPhase.Draft); index++)
            {
                Clock.AdvanceMonotonic(TimeSpan.FromSeconds(1));
                Runtime.Pump();
            }
            Assert.Equal(nameof(MatchPhase.Battle), Capture(0).Phase);
        }

        public void CatchUp()
        {
            for (var index = 0; index < 256; index++)
                if (Runtime.Pump()) return;
            throw new Xunit.Sdk.XunitException("Server did not catch up within deterministic test bound.");
        }

        public void RunOfflineUntilMatchResult(TimeSpan battleIncrement)
        {
            for (var index = 0; index < 24000; index++)
            {
                var snapshot = Capture(0);
                if (snapshot.Phase == nameof(MatchPhase.MatchResult)) return;
                Clock.AdvanceMonotonic(snapshot.Phase == nameof(MatchPhase.Battle)
                    ? battleIncrement
                    : TimeSpan.FromSeconds(1));
                CatchUp();
            }
            throw new Xunit.Sdk.XunitException("Offline match did not reach four wins within deterministic test bound.");
        }

        public void AdvanceToRoundResult()
        {
            AdvanceToBattle();
            for (var index = 0; index < 12000; index++)
            {
                if (Capture(0).Phase == nameof(MatchPhase.RoundResult)) return;
                Clock.AdvanceMonotonic(TimeSpan.FromMilliseconds(34));
                CatchUp();
            }
            throw new Xunit.Sdk.XunitException("Real authored battle did not complete within deterministic test bound.");
        }

        public void AdvanceFor(TimeSpan total, TimeSpan increment)
        {
            var elapsed = TimeSpan.Zero;
            while (elapsed < total)
            {
                var step = total - elapsed < increment ? total - elapsed : increment;
                Clock.AdvanceMonotonic(step);
                CatchUp();
                elapsed += step;
            }
        }

        public IReadOnlyList<(int Round, int Side, long Token, int OfferIndex, string OfferId)> InspectAutomaticDecisionSignature() =>
            Runtime.InspectDecisions().Where(value => value.Automatic)
                .Select(value => (value.Round, value.Side, value.ChoiceToken, value.OfferIndex, value.OfferId)).ToArray();

        public void Dispose() => Runtime.Dispose();
    }

    private static IReadOnlyList<(int Id, int Side, BattleEntityKind Kind, string Definition, int Hp, int MaxHp,
        int Target, float X, float Y, float PreviousX, float PreviousY, float FacingX, float FacingY, float Radius, float Progress)> EntitySignature(IReadOnlyList<BattleEntityState> values) =>
        values.Select(value => (value.Id, value.Side, value.Kind, value.DefinitionId, value.Hp, value.MaxHp,
            value.TargetId, value.Position.X, value.Position.Y, value.PreviousPosition.X, value.PreviousPosition.Y,
            value.Facing.X, value.Facing.Y, value.Radius, value.Progress)).ToArray();

    private static IReadOnlyList<(long Sequence, int Round, long Tick, BattleEventKind Kind, int Entity, int Side,
        int Amount, float X, float Y, float Radius, float TimeWithinTick)> EventSignature(IReadOnlyList<ServerBattleEvent> values) =>
        values.Select(value => (value.Sequence, value.Round, value.Value.Tick, value.Value.Kind, value.Value.EntityId,
            value.Value.Side, value.Value.Amount, value.Value.Position.X, value.Value.Position.Y, value.Value.Radius,
            value.Value.TimeWithinTick)).ToArray();
}
