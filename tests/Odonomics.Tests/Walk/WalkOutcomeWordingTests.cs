using Odonomics.Extraction;
using Odonomics.Sources;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the parenthetical <see cref="WalkOutcomeWording.NotMatchingDetail"/> builds beside
/// "wrong model": what it says depends on whether the extraction (and the title-line fallback ahead of
/// it) actually named a model, and whether the extraction's own fuel type already said Hybrid.</summary>
public class WalkOutcomeWordingTests
{
    private static ListingQuery CamryHybrid(int? hybridOnlyFromModelYear = null) =>
        new("Toyota", "Camry Hybrid", YearMin: 2018, "32114", 50, 100000, hybridOnlyFromModelYear);

    private static readonly ExtractionResult Base = new(
        Vin: "4T1DAACK8TU240486", Year: 2026, Make: "Toyota", Model: null, Trim: "SE",
        Price: 33990m, Mileage: 12066, DealerName: null, DealerLocation: null, FuelType: "Hybrid");

    [Fact]
    public void NotMatchingDetail_BlankModel_NamesTheModelAsWhatsMissing()
    {
        string detail = WalkOutcomeWording.NotMatchingDetail(CamryHybrid(), Base with { Model = null });

        Assert.Equal("model missing from extraction", detail);
    }

    [Fact]
    public void NotMatchingDetail_WhitespaceModel_AlsoReadsAsMissing()
    {
        string detail = WalkOutcomeWording.NotMatchingDetail(CamryHybrid(), Base with { Model = "  " });

        Assert.Equal("model missing from extraction", detail);
    }

    [Fact]
    public void NotMatchingDetail_ModelPresentAndFuelTypeHybrid_OmitsTheNoHybridClaim()
    {
        // The page's own fuel type already said Hybrid, so the reason must not also claim the title,
        // trim, and spec line said nothing of the sort.
        string detail = WalkOutcomeWording.NotMatchingDetail(CamryHybrid(), Base with { Model = "Corolla" });

        Assert.Equal("doesn't match Toyota Camry Hybrid: 2026 Toyota Corolla SE", detail);
        Assert.DoesNotContain("no Hybrid", detail);
    }

    [Fact]
    public void NotMatchingDetail_ModelPresentAndFuelTypeNotHybrid_KeepsTodaysWording()
    {
        string detail = WalkOutcomeWording.NotMatchingDetail(CamryHybrid(), Base with { Model = "Corolla", FuelType = null });

        Assert.Equal("doesn't match Toyota Camry Hybrid: 2026 Toyota Corolla SE; no Hybrid in title, trim, or spec line", detail);
    }

    [Fact]
    public void NotMatchingDetail_NonHybridQuery_NeverAddsTheHybridClaim()
    {
        ListingQuery corolla = new("Toyota", "Corolla", YearMin: 2018, "32114", 50, 100000);

        string detail = WalkOutcomeWording.NotMatchingDetail(corolla, Base with { Model = "Camry", FuelType = null });

        Assert.Equal("doesn't match Toyota Corolla: 2026 Toyota Camry SE", detail);
    }

    [Fact]
    public void NotMatchingDetail_GasOnlyBeforeYear_StillWinsOverTheModelPresenceCheck()
    {
        string detail = WalkOutcomeWording.NotMatchingDetail(
            CamryHybrid(hybridOnlyFromModelYear: 2025),
            Base with { Model = "Camry", Year = 2024, FuelType = null });

        Assert.Equal("2024 Camry SE, gas-only before 2025", detail);
    }

    [Fact]
    public void MissingFieldNames_OnlyMileageBlank_NamesJustMileage()
    {
        Assert.Equal("mileage", WalkOutcomeWording.MissingFieldNames(2026, 33990m, null));
    }

    [Fact]
    public void MissingFieldNames_YearAndPriceBlank_NamesBothInOrder()
    {
        Assert.Equal("year, price", WalkOutcomeWording.MissingFieldNames(null, null, 12066));
    }

    [Fact]
    public void MissingFieldNames_AllThreeBlank_NamesAllThreeInOrder()
    {
        Assert.Equal("year, price, mileage", WalkOutcomeWording.MissingFieldNames(null, null, null));
    }

    [Fact]
    public void MissingFieldNames_NothingBlank_IsEmpty()
    {
        Assert.Equal("", WalkOutcomeWording.MissingFieldNames(2026, 33990m, 12066));
    }

    [Fact]
    public void StoreLineNotRecognised_NamesTheFallbackDealer()
    {
        Assert.Equal("store line not recognised, saved under bare CarMax", WalkOutcomeWording.StoreLineNotRecognised("CarMax"));
    }
}
