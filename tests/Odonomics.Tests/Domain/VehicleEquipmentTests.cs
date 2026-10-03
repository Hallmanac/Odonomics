using Odonomics.Domain;

namespace Odonomics.Tests.Domain;

public class VehicleEquipmentTests
{
    private static EquipmentFact Fact(EquipmentStatus status, EquipmentSource source = EquipmentSource.WindowSticker) =>
        status == EquipmentStatus.Unknown
            ? EquipmentFact.Unknown
            : new EquipmentFact(status, source);

    [Theory]
    [InlineData(EquipmentStatus.Present, EquipmentStatus.Unknown, EquipmentStatus.Present)]
    [InlineData(EquipmentStatus.Present, EquipmentStatus.Absent, EquipmentStatus.Present)]
    [InlineData(EquipmentStatus.Unknown, EquipmentStatus.Present, EquipmentStatus.Present)]
    [InlineData(EquipmentStatus.Absent, EquipmentStatus.Present, EquipmentStatus.Present)]
    [InlineData(EquipmentStatus.Absent, EquipmentStatus.Absent, EquipmentStatus.Absent)]
    [InlineData(EquipmentStatus.Absent, EquipmentStatus.Unknown, EquipmentStatus.Unknown)]
    [InlineData(EquipmentStatus.Unknown, EquipmentStatus.Absent, EquipmentStatus.Unknown)]
    [InlineData(EquipmentStatus.Unknown, EquipmentStatus.Unknown, EquipmentStatus.Unknown)]
    public void KeylessEntry_IsPresentWithEitherAbsentOnlyWithBothAbsentAndUnknownOtherwise(
        EquipmentStatus smartKey, EquipmentStatus fob, EquipmentStatus expected)
    {
        var equipment = new VehicleEquipment(Fact(smartKey), EquipmentFact.Unknown, Fact(fob));

        Assert.Equal(expected, equipment.KeylessEntry.Status);
        Assert.Equal(expected, equipment.For(EquipmentFeatures.KeylessEntry).Status);
    }

    [Fact]
    public void SmartKeyEntry_KeepsItsStricterMeaningWhenAFobIsPresent()
    {
        var equipment = new VehicleEquipment(Fact(EquipmentStatus.Absent), EquipmentFact.Unknown, Fact(EquipmentStatus.Present));

        Assert.Equal(EquipmentStatus.Absent, equipment.For(EquipmentFeatures.SmartKeyEntry).Status);
    }

    [Fact]
    public void KeylessEntry_WhenBothFactsAreKnown_TakesTheWindowStickerOverTheTrimTable()
    {
        var equipment = new VehicleEquipment(
            Fact(EquipmentStatus.Absent, EquipmentSource.TrimTable),
            EquipmentFact.Unknown,
            Fact(EquipmentStatus.Present, EquipmentSource.WindowSticker));

        Assert.Equal(new EquipmentFact(EquipmentStatus.Present, EquipmentSource.WindowSticker), equipment.KeylessEntry);
    }

    [Fact]
    public void KeylessEntry_PresentFromProximityEntryAloneUsesThatSource()
    {
        var equipment = new VehicleEquipment(Fact(EquipmentStatus.Present, EquipmentSource.TrimTable), EquipmentFact.Unknown);

        Assert.Equal(new EquipmentFact(EquipmentStatus.Present, EquipmentSource.TrimTable), equipment.KeylessEntry);
    }

    [Theory]
    [InlineData("Keyless Entry", "keyless entry")]
    [InlineData("smart-key entry", "smart-key entry")]
    public void Canonical_AcceptsBothEntryValues(string name, string expected)
    {
        Assert.Equal(expected, EquipmentFeatures.Canonical(name));
    }

    [Fact]
    public void Canonical_DoesNotAcceptTheStoredFobFactAsARequirement()
    {
        Assert.Null(EquipmentFeatures.Canonical(EquipmentFeatures.KeylessFobEntry));
    }
}
