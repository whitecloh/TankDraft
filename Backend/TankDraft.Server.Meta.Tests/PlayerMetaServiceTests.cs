using System.Collections.Immutable;
using TankDraft.Server.Meta;
using Xunit;

namespace TankDraft.Server.Meta.Tests;

public sealed class PlayerMetaServiceTests
{
    [Fact]
    public async Task Get_initializes_a_profile_with_CAS_and_reloads_it()
    {
        var store = new InMemoryStore(Inventory("u1", "u2", "u3", "u4", "o1"));
        var service = Service(store);

        var snapshot = await service.GetAsync(Actor, default);
        var again = await service.GetAsync(Actor, default);

        Assert.Equal(1, snapshot.ProfileVersion);
        Assert.Equal(snapshot.Profile, again.Profile);
        Assert.Equal(1, store.WriteCount);
    }

    [Fact]
    public async Task Get_initializes_missing_named_object_at_the_actual_entity_profile_version()
    {
        var store = new InMemoryStore(Inventory("u1", "u2", "u3", "u4", "o1"), new StoredProfile(7, null));

        var snapshot = await Service(store).GetAsync(Actor, default);

        Assert.Equal(8, snapshot.ProfileVersion);
        Assert.Equal(new[] { 7 }, store.ExpectedWriteVersions);
    }

    [Fact]
    public async Task Initialization_conflict_never_overwrites_another_named_object_write()
    {
        var other = Profile() with { Name = "Other" };
        var store = new InMemoryStore(Inventory("u1", "u2", "u3", "u4", "o1"), new StoredProfile(7, null));
        store.ConflictNextWriteWith(new StoredProfile(8, other));

        var snapshot = await Service(store).GetAsync(Actor, default);

        Assert.Equal("Other", snapshot.Profile.Name);
        Assert.Equal(8, snapshot.ProfileVersion);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal(new[] { 7 }, store.ExpectedWriteVersions);
    }

    [Fact]
    public async Task Get_never_grants_missing_starter_content()
    {
        var store = new InMemoryStore(Inventory("u1", "u2", "u3", "u4"));
        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => Service(store).GetAsync(Actor, default));

        Assert.Equal("not_owned", failure.Code);
        Assert.Equal(0, store.WriteCount);
    }

    [Theory]
    [InlineData("u5", "o1", "not_owned")]
    [InlineData("o1", "o1", "invalid_loadout")]
    public async Task Save_rejects_nonowned_or_wrong_kind_content(string changedUnit, string order, string code)
    {
        var store = new InMemoryStore(Inventory("u1", "u2", "u3", "u4", "u5", "o1"));
        var service = Service(store);
        var initial = await service.GetAsync(Actor, default);
        if (code == "not_owned") store.SetInventory(Inventory("u1", "u2", "u3", "u4", "o1"));

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => service.SaveLoadoutAsync(
            Actor, "content-1", initial.ProfileVersion, Guid.NewGuid(), new[] { "u1", "u2", "u3", changedUnit }, new[] { order }, default));

        Assert.Equal(code, failure.Code);
    }

    [Fact]
    public async Task Save_rejects_duplicates_wrong_slots_and_locked_arena()
    {
        var store = new InMemoryStore(Inventory("u1", "u2", "u3", "u4", "u5", "o1"));
        var service = Service(store);
        var initial = await service.GetAsync(Actor, default);

        var duplicate = await Assert.ThrowsAsync<MetaFailureException>(() => service.SaveLoadoutAsync(Actor, "content-1", initial.ProfileVersion, Guid.NewGuid(), new[] { "u1", "u1", "u2", "u3" }, new[] { "o1" }, default));
        var slots = await Assert.ThrowsAsync<MetaFailureException>(() => service.SaveLoadoutAsync(Actor, "content-1", initial.ProfileVersion, Guid.NewGuid(), new[] { "u1", "u2", "u3" }, new[] { "o1" }, default));
        var locked = await Assert.ThrowsAsync<MetaFailureException>(() => service.SaveLoadoutAsync(Actor, "content-1", initial.ProfileVersion, Guid.NewGuid(), new[] { "u1", "u2", "u3", "u5" }, new[] { "o1" }, default));
        var lockedOrderSlot = await Assert.ThrowsAsync<MetaFailureException>(() => service.SaveLoadoutAsync(Actor, "content-1", initial.ProfileVersion, Guid.NewGuid(), Units, new[] { "o1", "o2", "" }, default));

        Assert.Equal("invalid_loadout", duplicate.Code);
        Assert.Equal("invalid_loadout", slots.Code);
        Assert.Equal("locked", locked.Code);
        Assert.Equal("locked", lockedOrderSlot.Code);
    }

    [Fact]
    public async Task Save_rejects_stale_content_and_profile_versions()
    {
        var store = new InMemoryStore(Inventory("u1", "u2", "u3", "u4", "o1"));
        var service = Service(store);
        var snapshot = await service.GetAsync(Actor, default);

        var content = await Assert.ThrowsAsync<MetaFailureException>(() => service.SaveLoadoutAsync(Actor, "old", snapshot.ProfileVersion, Guid.NewGuid(), Units, Orders, default));
        var version = await Assert.ThrowsAsync<MetaFailureException>(() => service.SaveLoadoutAsync(Actor, "content-1", snapshot.ProfileVersion + 1, Guid.NewGuid(), Units, Orders, default));

        Assert.Equal("content_version_mismatch", content.Code);
        Assert.Equal("version_conflict", version.Code);
    }

    [Fact]
    public async Task Exact_operation_retry_is_idempotent_across_a_new_service()
    {
        var store = new InMemoryStore(Inventory("u1", "u2", "u3", "u4", "o1"));
        var first = Service(store);
        var current = await first.GetAsync(Actor, default);
        var operation = Guid.NewGuid();
        var saved = await first.SaveLoadoutAsync(Actor, "content-1", current.ProfileVersion, operation, Units, Orders, default);

        var retry = await Service(store).SaveLoadoutAsync(Actor, "content-1", current.ProfileVersion, operation, Units, Orders, default);

        Assert.Equal(saved.ProfileVersion, retry.ProfileVersion);
        Assert.Equal(saved.Profile.LastOperationId, retry.Profile.LastOperationId);
        Assert.Equal(saved.Profile.LastOperationFingerprint, retry.Profile.LastOperationFingerprint);
        Assert.Equal(saved.Profile.UnitIds, retry.Profile.UnitIds);
        Assert.Equal(saved.Profile.OrderIds, retry.Profile.OrderIds);
        Assert.Equal(2, store.WriteCount); // profile creation plus one accepted operation
    }

    [Fact]
    public async Task Same_operation_with_changed_payload_is_rejected()
    {
        var store = new InMemoryStore(Inventory("u1", "u2", "u3", "u4", "o1"));
        var service = Service(store);
        var current = await service.GetAsync(Actor, default);
        var operation = Guid.NewGuid();
        await service.SaveLoadoutAsync(Actor, "content-1", current.ProfileVersion, operation, Units, Orders, default);

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => service.SaveLoadoutAsync(Actor, "content-1", current.ProfileVersion, operation, new[] { "u4", "u3", "u2", "u1" }, Orders, default));

        Assert.Equal("operation_conflict", failure.Code);
    }

    [Theory]
    [InlineData("http", "provider_unavailable")]
    [InlineData("provider", "provider_unavailable")]
    [InlineData("invalid_profile", "invalid_profile")]
    public async Task Get_failures_never_initialize_or_reset_an_existing_profile_and_recovery_returns_it(string failureKind, string code)
    {
        var original = Profile() with { Name = "Existing", ArenaProgress = 17 };
        var inner = new InMemoryStore(Inventory("u1", "u2", "u3", "u4", "o1"), new StoredProfile(7, original));
        var store = new FaultingStore(inner, Failure(failureKind), readFailures: 1);

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => Service(store).GetAsync(Actor, default));
        var recovered = await Service(inner).GetAsync(Actor, default);

        Assert.Equal(code, failure.Code);
        Assert.Equal(original, recovered.Profile);
        Assert.Equal(7, recovered.ProfileVersion);
        Assert.Equal(0, inner.WriteCount);
    }

    [Theory]
    [InlineData("http", "provider_unavailable")]
    [InlineData("provider", "provider_unavailable")]
    [InlineData("invalid_profile", "invalid_profile")]
    public async Task Save_failures_never_reset_an_existing_profile_and_recovery_returns_it(string failureKind, string code)
    {
        var inner = new InMemoryStore(Inventory("u1", "u2", "u3", "u4", "o1"));
        var established = await Service(inner).GetAsync(Actor, default);
        var store = new FaultingStore(inner, Failure(failureKind), writeFailures: 1);

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => Service(store).SaveLoadoutAsync(
            Actor, "content-1", established.ProfileVersion, Guid.NewGuid(), Units, Orders, default));
        var recovered = await Service(inner).GetAsync(Actor, default);

        Assert.Equal(code, failure.Code);
        Assert.Equal(established.Profile, recovered.Profile);
        Assert.Equal(established.ProfileVersion, recovered.ProfileVersion);
        Assert.Equal(1, inner.WriteCount); // initial profile only
    }

    [Fact]
    public async Task Lost_save_response_recovers_exact_same_operation_without_a_second_write()
    {
        var inner = new InMemoryStore(Inventory("u1", "u2", "u3", "u4", "o1"));
        var established = await Service(inner).GetAsync(Actor, default);
        var operation = Guid.NewGuid();
        var store = new PersistThenLoseResponseStore(inner);

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => Service(store).SaveLoadoutAsync(
            Actor, "content-1", established.ProfileVersion, operation, Units, Orders, default));
        var retry = await Service(store).SaveLoadoutAsync(
            Actor, "content-1", established.ProfileVersion, operation, Units, Orders, default);

        Assert.Equal("provider_unavailable", failure.Code);
        Assert.Equal(established.ProfileVersion + 1, retry.ProfileVersion);
        Assert.Equal(operation.ToString("N"), retry.Profile.LastOperationId);
        Assert.Equal(2, inner.WriteCount); // initial profile plus the persisted operation
        Assert.Equal(1, store.WriteAttempts);
    }

    [Fact]
    public async Task Concurrent_saves_are_resolved_by_CAS()
    {
        var store = new InMemoryStore(Inventory("u1", "u2", "u3", "u4", "o1"));
        var service = Service(store);
        var current = await service.GetAsync(Actor, default);
        store.RequireConcurrentWrites(2);

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            try
            {
                await service.SaveLoadoutAsync(Actor, "content-1", current.ProfileVersion, Guid.NewGuid(), Units, Orders, default);
                return "accepted";
            }
            catch (MetaFailureException exception) { return exception.Code; }
        })));

        Assert.Equal(1, results.Count(result => result == "accepted"));
        Assert.Equal(1, results.Count(result => result == "version_conflict"));
    }

    [Fact]
    public async Task Stored_malformed_and_future_schema_profiles_are_rejected()
    {
        var inventory = Inventory("u1", "u2", "u3", "u4", "o1");
        var malformed = new InMemoryStore(inventory, new StoredProfile(-1, null));
        var futureProfile = Profile() with { SchemaVersion = 2 };
        var future = new InMemoryStore(inventory, new StoredProfile(1, futureProfile));

        var malformedFailure = await Assert.ThrowsAsync<MetaFailureException>(() => Service(malformed).GetAsync(Actor, default));
        var futureFailure = await Assert.ThrowsAsync<MetaFailureException>(() => Service(future).GetAsync(Actor, default));

        Assert.Equal("provider_unavailable", malformedFailure.Code);
        Assert.Equal("invalid_profile", futureFailure.Code);
    }

    [Fact]
    public async Task Revalidate_for_match_returns_immutable_validated_snapshot()
    {
        var store = new InMemoryStore(Inventory("u1", "u2", "u3", "u4", "o1"));
        var service = Service(store);
        await service.GetAsync(Actor, default);

        var snapshot = await service.RevalidateForMatchAsync(Actor, "content-1", default);

        Assert.Equal(1, snapshot.ProfileVersion);
        Assert.Equal(Units, snapshot.Profile.UnitIds);
        Assert.Equal("inventory-v1", snapshot.Inventory.SourceVersion);
    }

    [Fact]
    public async Task Inflight_capacity_rejects_without_unbounded_waiting()
    {
        var inner = new InMemoryStore(Inventory("u1", "u2", "u3", "u4", "o1"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = new BlockingReadStore(inner, entered, release);
        var service = new PlayerMetaService(blocking, Rules(), maximumInflight: 1);
        var first = service.GetAsync(Actor, default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var busy = await Assert.ThrowsAsync<MetaFailureException>(() => service.GetAsync(Actor, default));
        release.SetResult();
        await first;

        Assert.Equal("busy", busy.Code);
    }

    private static readonly PlayerEntity Actor = new("account-1", "entity-1");
    private static readonly string[] Units = ["u1", "u2", "u3", "u4"];
    private static readonly string[] Orders = ["o1", "", ""];

    private static PlayerMetaService Service(IPlayerDataStore store) => new(store, Rules());

    private static MetaRules Rules() => new(
        "content-1", 4, ImmutableArray.Create(1, 20, 35),
        ImmutableArray.Create(
            new MetaContentDefinition("u1", ContentKind.Unit, 1), new MetaContentDefinition("u2", ContentKind.Unit, 1),
            new MetaContentDefinition("u3", ContentKind.Unit, 1), new MetaContentDefinition("u4", ContentKind.Unit, 1),
            new MetaContentDefinition("u5", ContentKind.Unit, 2), new MetaContentDefinition("o1", ContentKind.Order, 1),
            new MetaContentDefinition("o2", ContentKind.Order, 1)),
        Profile());

    private static PlayerProfile Profile() => new(1, "Tester", 1, 1, 0, 0,
        Units.ToImmutableArray(), Orders.ToImmutableArray(), null, null);

    private static InventorySnapshot Inventory(params string[] owned) => new("inventory-v1", "etag-v1",
        owned.ToImmutableHashSet(StringComparer.Ordinal), ImmutableDictionary<string, long>.Empty.Add("soft", 0));

    private static Exception Failure(string kind) => kind switch
    {
        "http" => new HttpRequestException("lost connection"),
        "provider" => new MetaFailureException("provider_unavailable"),
        "invalid_profile" => new MetaFailureException("invalid_profile"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed class InMemoryStore : IPlayerDataStore
    {
        private readonly object _gate = new();
        private StoredProfile _profile;
        private InventorySnapshot _inventory;
        private Barrier? _writeBarrier;
        private StoredProfile? _conflictProfile;

        public InMemoryStore(InventorySnapshot inventory, StoredProfile? profile = null)
        {
            _inventory = inventory;
            _profile = profile ?? new StoredProfile(0, null);
        }

        public int WriteCount { get; private set; }
        public List<int> ExpectedWriteVersions { get; } = [];

        public Task<StoredProfile> ReadProfileAsync(PlayerEntity player, CancellationToken cancellationToken)
        {
            lock (_gate) return Task.FromResult(_profile);
        }

        public Task<InventorySnapshot> ReadInventoryAsync(PlayerEntity player, CancellationToken cancellationToken)
        {
            lock (_gate) return Task.FromResult(_inventory);
        }

        public Task<StoredProfile> WriteProfileAsync(PlayerEntity player, PlayerProfile profile, int expectedVersion, CancellationToken cancellationToken)
        {
            var barrier = _writeBarrier;
            if (barrier is not null && !barrier.SignalAndWait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Concurrent write test did not receive both writers.");
            lock (_gate)
            {
                ExpectedWriteVersions.Add(expectedVersion);
                if (_conflictProfile is not null)
                {
                    _profile = _conflictProfile;
                    _conflictProfile = null;
                    throw new MetaFailureException("version_conflict");
                }
                if (_profile.ProfileVersion != expectedVersion) throw new MetaFailureException("version_conflict");
                _profile = new StoredProfile(expectedVersion + 1, profile);
                WriteCount++;
                return Task.FromResult(_profile);
            }
        }

        public void SetInventory(InventorySnapshot inventory)
        {
            lock (_gate) _inventory = inventory;
        }

        public void RequireConcurrentWrites(int participants) => _writeBarrier = new Barrier(participants);

        public void ConflictNextWriteWith(StoredProfile profile)
        {
            lock (_gate) _conflictProfile = profile;
        }
    }

    private sealed class BlockingReadStore : IPlayerDataStore
    {
        private readonly InMemoryStore _inner;
        private readonly TaskCompletionSource _entered;
        private readonly TaskCompletionSource _release;

        public BlockingReadStore(InMemoryStore inner, TaskCompletionSource entered, TaskCompletionSource release)
        {
            _inner = inner;
            _entered = entered;
            _release = release;
        }

        public async Task<StoredProfile> ReadProfileAsync(PlayerEntity player, CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return await _inner.ReadProfileAsync(player, cancellationToken);
        }

        public Task<InventorySnapshot> ReadInventoryAsync(PlayerEntity player, CancellationToken cancellationToken)
            => _inner.ReadInventoryAsync(player, cancellationToken);

        public Task<StoredProfile> WriteProfileAsync(PlayerEntity player, PlayerProfile profile, int expectedVersion, CancellationToken cancellationToken)
            => _inner.WriteProfileAsync(player, profile, expectedVersion, cancellationToken);
    }

    private sealed class FaultingStore : IPlayerDataStore
    {
        private readonly InMemoryStore _inner;
        private readonly Exception _failure;
        private int _remainingReadFailures;
        private int _remainingWriteFailures;

        public FaultingStore(InMemoryStore inner, Exception failure, int readFailures = 0, int writeFailures = 0)
        {
            _inner = inner;
            _failure = failure;
            _remainingReadFailures = readFailures;
            _remainingWriteFailures = writeFailures;
        }

        public Task<StoredProfile> ReadProfileAsync(PlayerEntity player, CancellationToken cancellationToken)
        {
            if (Interlocked.Decrement(ref _remainingReadFailures) >= 0) return Task.FromException<StoredProfile>(_failure);
            return _inner.ReadProfileAsync(player, cancellationToken);
        }

        public Task<InventorySnapshot> ReadInventoryAsync(PlayerEntity player, CancellationToken cancellationToken)
            => _inner.ReadInventoryAsync(player, cancellationToken);

        public Task<StoredProfile> WriteProfileAsync(PlayerEntity player, PlayerProfile profile, int expectedVersion, CancellationToken cancellationToken)
        {
            if (Interlocked.Decrement(ref _remainingWriteFailures) >= 0) return Task.FromException<StoredProfile>(_failure);
            return _inner.WriteProfileAsync(player, profile, expectedVersion, cancellationToken);
        }
    }

    private sealed class PersistThenLoseResponseStore : IPlayerDataStore
    {
        private readonly InMemoryStore _inner;
        private bool _loseNextWriteResponse = true;

        public PersistThenLoseResponseStore(InMemoryStore inner) => _inner = inner;
        public int WriteAttempts { get; private set; }

        public Task<StoredProfile> ReadProfileAsync(PlayerEntity player, CancellationToken cancellationToken)
            => _inner.ReadProfileAsync(player, cancellationToken);

        public Task<InventorySnapshot> ReadInventoryAsync(PlayerEntity player, CancellationToken cancellationToken)
            => _inner.ReadInventoryAsync(player, cancellationToken);

        public async Task<StoredProfile> WriteProfileAsync(PlayerEntity player, PlayerProfile profile, int expectedVersion, CancellationToken cancellationToken)
        {
            WriteAttempts++;
            var written = await _inner.WriteProfileAsync(player, profile, expectedVersion, cancellationToken);
            if (_loseNextWriteResponse)
            {
                _loseNextWriteResponse = false;
                throw new HttpRequestException("response lost after persistence");
            }
            return written;
        }
    }
}
