using System.Text.Json;
using Odonomics.Domain;

namespace Odonomics.Tests.Domain;

public class FactoryTrimTableTests
{
    private const string CorollaRows = """
        {
          "entries": [
            { "make": "Toyota", "model": "Corolla Hybrid", "yearFrom": 2020, "yearTo": 2023, "trim": "LE",
              "smartKeyEntry": "present", "pushButtonStart": "present", "source": "test sheet" }
          ]
        }
        """;

    private static VehicleEquipment Sticker(EquipmentStatus smartKey, EquipmentStatus pushButton) => new(
        new EquipmentFact(smartKey, smartKey == EquipmentStatus.Unknown ? EquipmentSource.None : EquipmentSource.WindowSticker),
        new EquipmentFact(pushButton, pushButton == EquipmentStatus.Unknown ? EquipmentSource.None : EquipmentSource.WindowSticker));

    [Fact]
    public void LoadShipped_TheFileInTheRepo_LoadsAndValidates()
    {
        FactoryTrimTable table = FactoryTrimTable.LoadShipped();

        Assert.NotNull(table.Entries);
    }

    [Fact]
    public void LoadShipped_EveryRowKeepsAUrlSourceAndAConfidenceOfHighOrMedium()
    {
        FactoryTrimTable table = FactoryTrimTable.LoadShipped();

        Assert.NotEmpty(table.Entries);
        Assert.All(table.Entries, entry =>
        {
            Assert.Contains("https://", entry.Source);
            Assert.Matches("^(high|medium|entry high, start medium)$", entry.Confidence ?? "");
        });
    }

    [Theory]
    [InlineData("Honda", "Insight", 2020, "LX", EquipmentStatus.Absent, EquipmentStatus.Present)]
    [InlineData("Honda", "Insight", 2022, "Touring", EquipmentStatus.Present, EquipmentStatus.Present)]
    [InlineData("Toyota", "Prius", 2020, "LE", EquipmentStatus.Present, EquipmentStatus.Present)]
    [InlineData("Toyota", "Corolla Hybrid", 2022, "LE", EquipmentStatus.Present, EquipmentStatus.Present)]
    [InlineData("Toyota", "Corolla Hybrid", 2023, "LE", EquipmentStatus.Unknown, EquipmentStatus.Unknown)]
    [InlineData("Toyota", "Corolla Hybrid", 2023, "XLE", EquipmentStatus.Present, EquipmentStatus.Present)]
    [InlineData("Toyota", "Camry Hybrid", 2018, "SE", EquipmentStatus.Present, EquipmentStatus.Present)]
    [InlineData("Toyota", "Camry Hybrid", 2021, "XLE", EquipmentStatus.Unknown, EquipmentStatus.Unknown)]
    [InlineData("Toyota", "Camry Hybrid", 2025, "LE", EquipmentStatus.Unknown, EquipmentStatus.Present)]
    [InlineData("Toyota", "Prius", 2022, "LE", EquipmentStatus.Unknown, EquipmentStatus.Present)]
    [InlineData("Toyota", "Prius", 2024, "LE", EquipmentStatus.Unknown, EquipmentStatus.Unknown)]
    public void LoadShipped_PinnedCars_GetTheResearchedStatuses(
        string make, string model, int year, string trim, EquipmentStatus smartKey, EquipmentStatus pushButton)
    {
        FactoryTrimTable table = FactoryTrimTable.LoadShipped();

        VehicleEquipment equipment = table.Lookup(make, model, year, trim);

        Assert.Equal(smartKey, equipment.SmartKeyEntry.Status);
        Assert.Equal(pushButton, equipment.PushButtonStart.Status);
    }

    [Theory]
    [InlineData(2019)]
    [InlineData(2020)]
    [InlineData(2021)]
    public void LoadShipped_HondaInsightLx_HasNoProximityEntryButAFobSoKeylessEntryIsPresent(int year)
    {
        FactoryTrimTable table = FactoryTrimTable.LoadShipped();

        VehicleEquipment equipment = table.Lookup("Honda", "Insight", year, "LX");

        Assert.Equal(EquipmentStatus.Absent, equipment.SmartKeyEntry.Status);
        Assert.Equal(EquipmentStatus.Present, equipment.KeylessFobEntry.Status);
        Assert.Equal(new EquipmentFact(EquipmentStatus.Present, EquipmentSource.TrimTable), equipment.KeylessEntry);
    }

    [Fact]
    public void Lookup_RowThatStatesOnlyAFob_IsValidAndLeavesTheOtherFeaturesUnknown()
    {
        FactoryTrimTable table = FactoryTrimTable.Parse("""
            { "entries": [
              { "make": "Honda", "model": "Fit", "yearFrom": 2020, "yearTo": 2020, "trim": "LX",
                "keylessFobEntry": "present", "source": "test sheet" }
            ] }
            """);

        VehicleEquipment equipment = table.Lookup("Honda", "Fit", 2020, "LX");

        Assert.Equal(EquipmentStatus.Present, equipment.KeylessFobEntry.Status);
        Assert.Equal(EquipmentStatus.Unknown, equipment.SmartKeyEntry.Status);
        Assert.Equal(EquipmentStatus.Unknown, equipment.PushButtonStart.Status);
    }

    [Fact]
    public void Lookup_ProximityAbsentWithNoFobStatement_LeavesKeylessEntryUnknown()
    {
        FactoryTrimTable table = FactoryTrimTable.Parse("""
            { "entries": [
              { "make": "Honda", "model": "Fit", "yearFrom": 2020, "yearTo": 2020, "trim": "LX",
                "smartKeyEntry": "absent", "source": "test sheet" }
            ] }
            """);

        VehicleEquipment equipment = table.Lookup("Honda", "Fit", 2020, "LX");

        Assert.Equal(EquipmentStatus.Absent, equipment.SmartKeyEntry.Status);
        Assert.Equal(EquipmentStatus.Unknown, equipment.KeylessEntry.Status);
    }

    [Theory]
    [InlineData(2019, "L Eco")]
    [InlineData(2019, "LE")]
    [InlineData(2020, "LE")]
    [InlineData(2021, "LE")]
    public void LoadShipped_PriusWhereTheSmartKeyCoversOnlyTheDriversDoor_IsPresentWithThatNote(int year, string trim)
    {
        FactoryTrimTable table = FactoryTrimTable.LoadShipped();

        FactoryTrimEntry entry = Assert.Single(table.Entries, e =>
            e.Model == "Prius" && e.Trim == trim && e.YearFrom <= year && e.YearTo >= year);

        Assert.Equal("present", entry.SmartKeyEntry);
        Assert.Contains("driver's door only", entry.Note);
    }

    [Theory]
    [InlineData(2019, "Limited FWD", EquipmentStatus.Present, EquipmentStatus.Present)]
    [InlineData(2019, "limited fwd", EquipmentStatus.Present, EquipmentStatus.Present)]
    [InlineData(2019, "Limited AWD-e", EquipmentStatus.Present, EquipmentStatus.Present)]
    [InlineData(2019, "LE FWD", EquipmentStatus.Present, EquipmentStatus.Present)]
    [InlineData(2019, "L Eco FWD", EquipmentStatus.Present, EquipmentStatus.Present)]
    [InlineData(2019, "LE 4WD", EquipmentStatus.Present, EquipmentStatus.Present)]
    [InlineData(2019, "XSE Premium", EquipmentStatus.Unknown, EquipmentStatus.Unknown)]
    [InlineData(2019, "LEFWD", EquipmentStatus.Unknown, EquipmentStatus.Unknown)]
    [InlineData(2019, "FWD", EquipmentStatus.Unknown, EquipmentStatus.Unknown)]
    [InlineData(2022, "LE FWD", EquipmentStatus.Unknown, EquipmentStatus.Present)]
    public void LoadShipped_PriusWithADrivetrainSuffix_ReadsItsBaseTrimRow(
        int year, string trim, EquipmentStatus smartKey, EquipmentStatus pushButton)
    {
        FactoryTrimTable table = FactoryTrimTable.LoadShipped();

        VehicleEquipment equipment = table.Lookup("Toyota", "Prius", year, trim);

        Assert.Equal(smartKey, equipment.SmartKeyEntry.Status);
        Assert.Equal(pushButton, equipment.PushButtonStart.Status);
    }

    [Fact]
    public void Lookup_TrimThatMatchesARowExactly_WinsOverItsStrippedBaseTrim()
    {
        FactoryTrimTable table = FactoryTrimTable.Parse("""
            { "entries": [
              { "make": "Toyota", "model": "Prius", "yearFrom": 2019, "yearTo": 2019, "trim": "LE", "smartKeyEntry": "present", "source": "a" },
              { "make": "Toyota", "model": "Prius", "yearFrom": 2019, "yearTo": 2019, "trim": "LE AWD-e", "smartKeyEntry": "absent", "source": "b" }
            ] }
            """);

        Assert.Equal(EquipmentStatus.Absent, table.Lookup("Toyota", "Prius", 2019, "LE AWD-e").SmartKeyEntry.Status);
        Assert.Equal(EquipmentStatus.Present, table.Lookup("Toyota", "Prius", 2019, "LE FWD").SmartKeyEntry.Status);
    }

    [Fact]
    public void Parse_EmptyEntries_GivesNoAnswerForAnyCar()
    {
        FactoryTrimTable table = FactoryTrimTable.Parse("""{ "entries": [] }""");

        VehicleEquipment equipment = table.Lookup("Toyota", "Corolla Hybrid", 2023, "LE");

        Assert.Equal(VehicleEquipment.Unknown, equipment);
    }

    [Fact]
    public void Lookup_ModelYearAndTrimInARow_AnswersFromTheTable()
    {
        FactoryTrimTable table = FactoryTrimTable.Parse(CorollaRows);

        VehicleEquipment equipment = table.Lookup("toyota", "corolla hybrid", 2021, "le");

        Assert.Equal(new EquipmentFact(EquipmentStatus.Present, EquipmentSource.TrimTable), equipment.SmartKeyEntry);
        Assert.Equal(new EquipmentFact(EquipmentStatus.Present, EquipmentSource.TrimTable), equipment.PushButtonStart);
    }

    [Theory]
    [InlineData(2019, "LE")]
    [InlineData(2024, "LE")]
    [InlineData(2021, "XLE")]
    [InlineData(2021, null)]
    public void Lookup_YearOrTrimOutsideEveryRow_IsUnknown(int year, string? trim)
    {
        FactoryTrimTable table = FactoryTrimTable.Parse(CorollaRows);

        Assert.Equal(VehicleEquipment.Unknown, table.Lookup("Toyota", "Corolla Hybrid", year, trim));
    }

    [Fact]
    public void Lookup_RowsThatDisagreeAboutAFeature_GiveNoAnswerForIt()
    {
        FactoryTrimTable table = FactoryTrimTable.Parse("""
            { "entries": [
              { "make": "Toyota", "model": "Corolla Hybrid", "yearFrom": 2020, "yearTo": 2023, "trim": "LE", "smartKeyEntry": "present", "pushButtonStart": "present", "source": "a" },
              { "make": "Toyota", "model": "Corolla Hybrid", "yearFrom": 2022, "yearTo": 2023, "trim": "LE", "smartKeyEntry": "absent", "source": "b" }
            ] }
            """);

        VehicleEquipment equipment = table.Lookup("Toyota", "Corolla Hybrid", 2022, "LE");

        Assert.Equal(EquipmentStatus.Unknown, equipment.SmartKeyEntry.Status);
        Assert.Equal(EquipmentStatus.Present, equipment.PushButtonStart.Status);
    }

    [Fact]
    public void Fill_UnknownStickerStatus_TakesTheTablesAnswer()
    {
        FactoryTrimTable table = FactoryTrimTable.Parse(CorollaRows);

        VehicleEquipment filled = table.Fill(VehicleEquipment.Unknown, "Toyota", "Corolla Hybrid", 2023, "LE");

        Assert.Equal(EquipmentSource.TrimTable, filled.PushButtonStart.Source);
        Assert.Equal(EquipmentStatus.Present, filled.PushButtonStart.Status);
    }

    [Fact]
    public void Fill_StickerStatusAlreadyKnown_IsNeverReplacedByTheTable()
    {
        FactoryTrimTable table = FactoryTrimTable.Parse(CorollaRows);

        VehicleEquipment filled = table.Fill(Sticker(EquipmentStatus.Absent, EquipmentStatus.Absent), "Toyota", "Corolla Hybrid", 2023, "LE");

        Assert.Equal(new EquipmentFact(EquipmentStatus.Absent, EquipmentSource.WindowSticker), filled.SmartKeyEntry);
        Assert.Equal(new EquipmentFact(EquipmentStatus.Absent, EquipmentSource.WindowSticker), filled.PushButtonStart);
    }

    [Fact]
    public void Fill_EmptyTable_LeavesTheStickerStatusesAlone()
    {
        VehicleEquipment sticker = Sticker(EquipmentStatus.Present, EquipmentStatus.Unknown);

        Assert.Equal(sticker, FactoryTrimTable.Empty.Fill(sticker, "Toyota", "Corolla Hybrid", 2023, "LE"));
    }

    [Theory]
    [InlineData("""{ "entries": [ { "make": "Toyota", "model": "Corolla Hybrid", "yearFrom": 2020, "yearTo": 2023, "trim": "LE", "smartKeyEntry": "yes", "source": "s" } ] }""")]
    [InlineData("""{ "entries": [ { "make": "Toyota", "model": "Corolla Hybrid", "yearFrom": 2023, "yearTo": 2020, "trim": "LE", "smartKeyEntry": "present", "source": "s" } ] }""")]
    [InlineData("""{ "entries": [ { "make": "Toyota", "model": "Corolla Hybrid", "yearFrom": 2020, "yearTo": 2023, "trim": "LE", "smartKeyEntry": "present" } ] }""")]
    [InlineData("""{ "entries": [ { "make": "Toyota", "model": "Corolla Hybrid", "yearFrom": 2020, "yearTo": 2023, "trim": "LE", "source": "s" } ] }""")]
    [InlineData("""{ "entries": [ { "make": "Toyota", "model": "Corolla Hybrid", "yearFrom": 2020, "yearTo": 2023, "trim": " ", "smartKeyEntry": "present", "source": "s" } ] }""")]
    public void Parse_InvalidRow_Throws(string json)
    {
        Assert.ThrowsAny<JsonException>(() => FactoryTrimTable.Parse(json));
    }
}
