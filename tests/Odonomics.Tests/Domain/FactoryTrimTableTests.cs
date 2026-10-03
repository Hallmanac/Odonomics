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
