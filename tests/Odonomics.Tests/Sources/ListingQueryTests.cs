using Odonomics.Sources;

namespace Odonomics.Tests.Sources;

public class ListingQueryTests
{
    private static ListingQuery Query(string make, string model) => new(make, model, YearMin: 2019, "32114", 50, MaxMileage: 100000);

    [Fact]
    public void MatchesExtractedVehicle_SameBaseModel_NoHybridTargeted_Matches()
    {
        ListingQuery query = Query("Honda", "Insight");

        Assert.True(query.MatchesExtractedVehicle("Honda", "Insight", "EX"));
    }

    [Fact]
    public void MatchesExtractedVehicle_HybridTargeted_ExtractedIsBaseGasModel_Rejected()
    {
        // The exact drift found on the Carvana Camry Hybrid walk: the search page mixes gas and
        // hybrid trims, and a plain "Camry" candidate must not pass a "Camry Hybrid" query.
        ListingQuery query = Query("Toyota", "Camry Hybrid");

        Assert.False(query.MatchesExtractedVehicle("Toyota", "Camry", "XLE"));
    }

    [Fact]
    public void MatchesExtractedVehicle_HybridTargeted_ExtractedIsHybrid_Matches()
    {
        ListingQuery query = Query("Toyota", "Camry Hybrid");

        Assert.True(query.MatchesExtractedVehicle("Toyota", "Camry Hybrid", "LE"));
    }

    [Fact]
    public void MatchesExtractedVehicle_HybridTargeted_HybridOnlyInTrim_Matches()
    {
        ListingQuery query = Query("Toyota", "Corolla Hybrid");

        Assert.True(query.MatchesExtractedVehicle("Toyota", "Corolla", "Hybrid LE"));
    }

    [Fact]
    public void MatchesExtractedVehicle_DifferentMake_Rejected()
    {
        ListingQuery query = Query("Honda", "Insight");

        Assert.False(query.MatchesExtractedVehicle("Toyota", "Insight", null));
    }

    [Fact]
    public void MatchesExtractedVehicle_DifferentBaseModel_Rejected()
    {
        ListingQuery query = Query("Toyota", "Corolla Hybrid");

        Assert.False(query.MatchesExtractedVehicle("Toyota", "Camry Hybrid", "LE"));
    }

    [Fact]
    public void MatchesExtractedVehicle_NullMakeOrModel_JudgedOnWhateverIsPresent()
    {
        ListingQuery query = Query("Honda", "Insight");

        Assert.True(query.MatchesExtractedVehicle(null, "Insight", null));
        Assert.False(query.MatchesExtractedVehicle(null, "Civic", null));
    }
}
