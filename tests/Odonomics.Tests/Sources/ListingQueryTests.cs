using Odonomics.Sources;

namespace Odonomics.Tests.Sources;

public class ListingQueryTests
{
    private static ListingQuery Query(string make, string model, int? hybridOnlyFromModelYear = null) =>
        new(make, model, YearMin: 2019, "32114", 50, MaxMileage: 100000, hybridOnlyFromModelYear);

    [Fact]
    public void MatchesExtractedVehicle_SameBaseModel_NoHybridTargeted_Matches()
    {
        ListingQuery query = Query("Honda", "Insight");

        Assert.True(query.MatchesExtractedVehicle("Honda", "Insight", "EX", 2021));
    }

    [Fact]
    public void MatchesExtractedVehicle_HybridTargeted_ExtractedIsBaseGasModel_Rejected()
    {
        // The exact drift found on the Carvana Camry Hybrid walk: the search page mixes gas and
        // hybrid trims, and a plain "Camry" candidate must not pass a "Camry Hybrid" query when
        // the scenario names no hybrid-only-from-year rule for it.
        ListingQuery query = Query("Toyota", "Camry Hybrid");

        Assert.False(query.MatchesExtractedVehicle("Toyota", "Camry", "XLE", 2022));
    }

    [Fact]
    public void MatchesExtractedVehicle_HybridTargeted_ExtractedIsHybrid_Matches()
    {
        ListingQuery query = Query("Toyota", "Camry Hybrid");

        Assert.True(query.MatchesExtractedVehicle("Toyota", "Camry Hybrid", "LE", 2022));
    }

    [Fact]
    public void MatchesExtractedVehicle_HybridTargeted_HybridOnlyInTrim_Matches()
    {
        ListingQuery query = Query("Toyota", "Corolla Hybrid");

        Assert.True(query.MatchesExtractedVehicle("Toyota", "Corolla", "Hybrid LE", 2022));
    }

    [Fact]
    public void MatchesExtractedVehicle_DifferentMake_Rejected()
    {
        ListingQuery query = Query("Honda", "Insight");

        Assert.False(query.MatchesExtractedVehicle("Toyota", "Insight", null, 2021));
    }

    [Fact]
    public void MatchesExtractedVehicle_DifferentBaseModel_Rejected()
    {
        ListingQuery query = Query("Toyota", "Corolla Hybrid");

        Assert.False(query.MatchesExtractedVehicle("Toyota", "Camry Hybrid", "LE", 2022));
    }

    [Fact]
    public void MatchesExtractedVehicle_NullMakeOrModel_JudgedOnWhateverIsPresent()
    {
        ListingQuery query = Query("Honda", "Insight");

        Assert.True(query.MatchesExtractedVehicle(null, "Insight", null, 2021));
        Assert.False(query.MatchesExtractedVehicle(null, "Civic", null, 2021));
    }

    [Fact]
    public void MatchesExtractedVehicle_HybridOnlyFromModelYear_CandidateBelowYear_Rejected()
    {
        // The bug this rule fixes: Toyota dropped the gas-only Camry for model year 2025, so a
        // 2024 Camry SE with no "Hybrid" in its text is still a gas car and must stay rejected.
        ListingQuery query = Query("Toyota", "Camry Hybrid", hybridOnlyFromModelYear: 2025);

        Assert.False(query.MatchesExtractedVehicle("Toyota", "Camry", "SE", 2024));
    }

    [Fact]
    public void MatchesExtractedVehicle_HybridOnlyFromModelYear_CandidateAtYear_Matches()
    {
        ListingQuery query = Query("Toyota", "Camry Hybrid", hybridOnlyFromModelYear: 2025);

        Assert.True(query.MatchesExtractedVehicle("Toyota", "Camry", "SE", 2025));
    }

    [Fact]
    public void MatchesExtractedVehicle_NoHybridOnlyFromModelYearRule_BaseGasCandidateBelowFutureYear_StillRejected()
    {
        // A model with no rule at all behaves exactly as before this feature existed.
        ListingQuery query = Query("Toyota", "Corolla Hybrid");

        Assert.False(query.MatchesExtractedVehicle("Toyota", "Corolla", "SE", 2030));
    }

    [Fact]
    public void GasOnlyBeforeHybridYear_CandidateBelowYear_ReturnsTheYear()
    {
        ListingQuery query = Query("Toyota", "Camry Hybrid", hybridOnlyFromModelYear: 2025);

        Assert.Equal(2025, query.GasOnlyBeforeHybridYear("Toyota", "Camry", "SE", 2024));
    }

    [Fact]
    public void GasOnlyBeforeHybridYear_CandidateAtOrAboveYear_ReturnsNull()
    {
        ListingQuery query = Query("Toyota", "Camry Hybrid", hybridOnlyFromModelYear: 2025);

        Assert.Null(query.GasOnlyBeforeHybridYear("Toyota", "Camry", "SE", 2025));
    }

    [Fact]
    public void GasOnlyBeforeHybridYear_NoRuleForModel_ReturnsNull()
    {
        ListingQuery query = Query("Toyota", "Corolla Hybrid");

        Assert.Null(query.GasOnlyBeforeHybridYear("Toyota", "Corolla", "SE", 2020));
    }
}
