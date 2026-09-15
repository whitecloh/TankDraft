using System.IO;
using Newtonsoft.Json.Linq;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Domain;
using TankDraft.Match.Networking;
using TankDraft.Match.ServerClient;
using Xunit;

namespace TankDraft.ServerClient.Tests;

public sealed class DefenseWireTests
{
    [Fact]
    public void Binary_frame_round_trips_all_defense_fields_and_hashes_each_one()
    {
        var source = Frame(State());
        var decoded = NetCodec.DecodeFrame(NetCodec.EncodeFrame(source));
        var entity = Assert.Single(decoded.States);
        Assert.Equal(32, entity.ShieldHp);
        Assert.Equal(40, entity.ShieldMaxHp);
        Assert.Equal(1, entity.FirstHitBlocks);
        Assert.Equal(0, entity.Ammo);
        Assert.Equal(6, entity.MagazineSize);
        Assert.Equal(1.25f, entity.ReloadRemaining);
        Assert.Equal(3f, entity.ReloadDuration);

        Assert.Equal(1, entity.TransformationStage);
        var baseline = NetCodec.HashShared(Frame(State(ammo: 6, reloadRemaining: 0)));
        foreach (var changed in Variants())
            Assert.NotEqual(baseline, NetCodec.HashShared(Frame(changed)));
    }

    [Fact]
    public void Binary_frame_rejects_unknown_state_version()
    {
        var bytes = NetCodec.EncodeFrame(Frame(State()));
        BitConverter.GetBytes(BattleEntityState.WireVersion + 1).CopyTo(bytes, 0);
        Assert.Throws<InvalidDataException>(() => NetCodec.DecodeFrame(bytes));
    }

    [Fact]
    public void Server_frame_requires_version_and_every_defense_field()
    {
        var snapshot = Snapshot();
        AssertState(new ServerFrame((JObject)snapshot.DeepClone()));

        snapshot.Remove("BattleStateVersion");
        Assert.Throws<InvalidDataException>(() => new ServerFrame(snapshot));

        var wrongVersionType = Snapshot();
        wrongVersionType["BattleStateVersion"] = "2";
        Assert.Throws<InvalidDataException>(() => new ServerFrame(wrongVersionType));

        foreach (var field in DefenseFields)
        {
            var malformed = Snapshot();
            ((JObject)((JArray)malformed["Entities"]!)[0]).Remove(field);
            Assert.Throws<InvalidDataException>(() => new ServerFrame(malformed));
        }
    }

    [Fact]
    public void Json_projection_preserves_discrete_defense_state()
    {
        AssertState(new ServerFrame(Snapshot()));
    }

    [Theory]
    [InlineData("ShieldHp", -1)]
    [InlineData("ShieldMaxHp", 1)]
    [InlineData("FirstHitBlocks", 2)]
    [InlineData("TransformationStage", 2)]
    [InlineData("Ammo", 7)]
    [InlineData("ReloadRemaining", -1)]
    [InlineData("ReloadDuration", -1)]
    public void Server_frame_rejects_invalid_defense_state(string field, float value)
    {
        var malformed = Snapshot();
        ((JObject)((JArray)malformed["Entities"]!)[0])[field] = field.StartsWith("Reload") ? new JValue(value) : new JValue((int)value);
        Assert.ThrowsAny<Exception>(() => new ServerFrame(malformed));
    }

    [Fact]
    public void Server_frame_rejects_reload_beyond_duration_and_status_on_nonunit()
    {
        var beyond = Snapshot();
        ((JObject)((JArray)beyond["Entities"]!)[0])["ReloadRemaining"] = 4f;
        Assert.ThrowsAny<Exception>(() => new ServerFrame(beyond));

        var nonUnit = Snapshot();
        ((JObject)((JArray)nonUnit["Entities"]!)[0])["Kind"] = (int)BattleEntityKind.Projectile;
        Assert.ThrowsAny<Exception>(() => new ServerFrame(nonUnit));
    }

    [Theory]
    [InlineData("ReloadRemaining")]
    [InlineData("ReloadDuration")]
    public void Server_frame_rejects_non_finite_reload_values(string field)
    {
        foreach (var value in new[] { float.NaN, float.PositiveInfinity })
        {
            var malformed = Snapshot();
            ((JObject)((JArray)malformed["Entities"]!)[0])[field] = value;
            Assert.Throws<InvalidDataException>(() => new ServerFrame(malformed));
        }
    }

    static readonly string[] DefenseFields = { "ShieldHp", "ShieldMaxHp", "FirstHitBlocks", "Ammo", "MagazineSize", "ReloadRemaining", "ReloadDuration", "TransformationStage" };

    static BattleEntityState State(int shieldHp = 32, int shieldMaxHp = 40, int firstHitBlocks = 1, int ammo = 0, int magazineSize = 6, float reloadRemaining = 1.25f, float reloadDuration = 3f, int transformationStage = 1) =>
        new(1, 0, BattleEntityKind.Unit, "unit.defense", new BattleVec(1, 2), new BattleVec(1, 2), new BattleVec(1, 0), 80, 100, 1, 0, 0,
            shieldHp, shieldMaxHp, firstHitBlocks, ammo, magazineSize, reloadRemaining, reloadDuration, transformationStage);

    static IEnumerable<BattleEntityState> Variants()
    {
        yield return State(ammo: 6, reloadRemaining: 0, transformationStage: 0);
        yield return State(shieldHp: 31, ammo: 6, reloadRemaining: 0); yield return State(shieldMaxHp: 41, ammo: 6, reloadRemaining: 0); yield return State(firstHitBlocks: 0, ammo: 6, reloadRemaining: 0);
        yield return State(ammo: 5, reloadRemaining: 0); yield return State(magazineSize: 7, ammo: 6, reloadRemaining: 0); yield return State(ammo: 0, reloadRemaining: 1); yield return State(ammo: 0, reloadRemaining: 0, reloadDuration: 4);
    }

    static NetFrame Frame(BattleEntityState state) => new()
    {
        MatchId = "match-defense", ContentVersion = "content-defense", Round = 1, Side = 0, Revision = 1, Tick = 2,
        ChoiceToken = 3, EventWatermark = 0, PresentAt = 1, Phase = MatchPhase.Battle, States = new[] { state }
    };

    static JObject Snapshot() => new()
    {
        ["BattleStateVersion"] = BattleEntityState.WireVersion,
        ["MatchId"] = "match-defense", ["ContentVersion"] = "content-defense", ["Phase"] = "Battle", ["Fault"] = null,
        ["Revision"] = 1, ["ChoiceToken"] = 3, ["SimulationTick"] = 2L, ["EventSequence"] = 0L,
        ["Round"] = 1, ["ChoiceNumber"] = 0, ["BonusSide"] = -1, ["Wins0"] = 0, ["Wins1"] = 0, ["LastWinner"] = -1, ["OrderCharges"] = 0,
        ["IsComeback"] = false, ["Committed"] = false, ["CanUseOrder"] = false, ["CatchingUp"] = false, ["ResyncRequired"] = false,
        ["ServerNow"] = "1970-01-01T00:00:00.0000000+00:00", ["DeadlineAt"] = null,
        ["Entities"] = new JArray(new JObject
        {
            ["Id"] = 1, ["Side"] = 0, ["Kind"] = (int)BattleEntityKind.Unit, ["DefinitionId"] = "unit.defense",
            ["Position"] = Vec(1, 2), ["PreviousPosition"] = Vec(1, 2), ["Facing"] = Vec(1, 0),
            ["Hp"] = 80, ["MaxHp"] = 100, ["Radius"] = 1f, ["Progress"] = 0f, ["TargetId"] = 0,
            ["ShieldHp"] = 32, ["ShieldMaxHp"] = 40, ["FirstHitBlocks"] = 1, ["Ammo"] = 0, ["MagazineSize"] = 6, ["ReloadRemaining"] = 1.25f, ["ReloadDuration"] = 3f, ["TransformationStage"] = 1
        }),
        ["Events"] = new JArray(), ["Army"] = new JArray(), ["Offers"] = new JArray(), ["Results"] = new JArray()
    };

    static JObject Vec(float x, float y) => new() { ["X"] = x, ["Y"] = y };
    static void AssertState(ServerFrame frame)
    {
        var entity = Assert.Single(frame.Entities);
        Assert.Equal(1, entity.TransformationStage);
        Assert.Equal(32, entity.ShieldHp); Assert.Equal(40, entity.ShieldMaxHp); Assert.Equal(1, entity.FirstHitBlocks);
        Assert.Equal(0, entity.Ammo); Assert.Equal(6, entity.MagazineSize); Assert.Equal(1.25f, entity.ReloadRemaining); Assert.Equal(3f, entity.ReloadDuration);
    }
}
