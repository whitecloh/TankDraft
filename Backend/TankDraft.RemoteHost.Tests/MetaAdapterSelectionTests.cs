using TankDraft.RemoteHost;
using TankDraft.Server.Meta;
using Xunit;

namespace TankDraft.RemoteHost.Tests;

public sealed class MetaAdapterSelectionTests
{
    static PlayFabMetaBootstrap.Settings Legacy(bool verified = true) => new(
        "PlayFabLegacyClosedQa", verified, CatalogVersion: "tankdraft-qa-v1",
        LegacyBindings: [new("unit.mines", "unit.mines")], Currencies: [new("CO", "coins")]);

    [Fact]
    public void LegacyRequiresExplicitAdapterAndVerifiedPlayerWritePolicy()
    {
        PlayFabMetaBootstrap.ValidateSettings(Legacy());
        Assert.Throws<InvalidDataException>(() => PlayFabMetaBootstrap.ValidateSettings(Legacy(false)));
        Assert.Throws<InvalidDataException>(() => PlayFabMetaBootstrap.ValidateSettings(Legacy() with { Mode = "Legacy" }));
    }

    [Fact]
    public void MixedEconomiesNeverSilentlyFallback()
    {
        Assert.Throws<InvalidDataException>(() => PlayFabMetaBootstrap.ValidateSettings(Legacy() with { CollectionId = "default" }));
        Assert.Throws<InvalidDataException>(() => PlayFabMetaBootstrap.ValidateSettings(Legacy() with { Bindings = [] }));
        Assert.Throws<InvalidDataException>(() => PlayFabMetaBootstrap.ValidateSettings(Legacy() with { CatalogVersion = null }));
        Assert.Throws<InvalidDataException>(() => PlayFabMetaBootstrap.ValidateSettings(Legacy() with { LegacyBindings = [] }));
    }

    [Fact]
    public void ExistingV2RemainsAnExplicitSeparateOption()
    {
        var v2 = new PlayFabMetaBootstrap.Settings("PlayFabClosedQa", true, "default",
            [new InventoryContentBinding(Guid.NewGuid().ToString(), "unit.mines", false)]);
        PlayFabMetaBootstrap.ValidateSettings(v2);
        Assert.Throws<InvalidDataException>(() => PlayFabMetaBootstrap.ValidateSettings(v2 with { Currencies = [] }));
    }
}
