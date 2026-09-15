using System.Text.Json;
using System.Text.Json.Serialization;
using TankDraft.Contracts.Battle;
using Xunit;

namespace TankDraft.Server.Match.Tests;

public sealed class AuthoredDefenseContentTests
{
    [Fact]
    public void Authored_defense_profiles_survive_export_and_strict_json_roundtrip()
    {
        var fixture = AuthoredMatchFixture.Load();
        var defs = fixture.Definitions;
        Assert.Equal(40, defs.Unit("unit.shield_vehicle").Shield.CapacityHpPercent);
        Assert.Equal(4f, defs.Unit("unit.shield_vehicle").Shield.IntervalSeconds);
        Assert.True(defs.Unit("unit.reactive_armor_tank").BlocksFirstHit);
        Assert.Equal(6, defs.Unit("unit.magazine_destroyer").Magazine.Shots);
        Assert.Equal(3f, defs.Unit("unit.magazine_destroyer").Magazine.ReloadSeconds);
        Assert.Equal(25, defs.Unit("unit.recovery_assault").LifeStealPercent);
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        foreach (var id in new[] { "unit.shield_vehicle", "unit.reactive_armor_tank", "unit.magazine_destroyer", "unit.recovery_assault" })
        {
            var original = defs.Unit(id);
            var json = JsonSerializer.Serialize(original, options);
            var restored = JsonSerializer.Deserialize<BattleUnitDefinition>(json, options);
            Assert.Equal(json, JsonSerializer.Serialize(restored, options));
            Assert.Contains(fixture.MatchUnits, value => value.Id == id && value.CanFightAlone);
        }
        foreach (var id in new[] { "unit.mines", "unit.heavy_tank", "unit.tank_destroyer", "unit.field_artillery" })
        {
            var d = defs.Unit(id);
            Assert.Null(d.Shield); Assert.False(d.BlocksFirstHit); Assert.Null(d.Magazine); Assert.Equal(0, d.LifeStealPercent);
        }
    }
}
