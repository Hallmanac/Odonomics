using System.Reflection;
using Odonomics.Extraction;

namespace Odonomics.Tests.Extraction;

/// <summary>
/// Exercises ExtractionClient's untrusted-input grounding through reflection: the checks are
/// private implementation detail of a class whose public surface only makes sense wired to a real
/// or fixture subprocess, but the grounding logic itself is pure and worth pinning directly.
/// </summary>
public class ExtractionGroundingTests
{
    private static readonly MethodInfo GroundInPageTextMethod = typeof(ExtractionClient)
        .GetMethod("GroundInPageText", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ExtractionClient.GroundInPageText not found");

    private static ExtractionResult GroundInPageText(ExtractionResult extracted, string pageText) =>
        (ExtractionResult)GroundInPageTextMethod.Invoke(null, [extracted, pageText])!;

    [Fact]
    public void GroundInPageText_VinNotOnPage_DropsTheVinEvenThoughItHasVinShape()
    {
        var extracted = new ExtractionResult("1HGCM82633A004352", 2020, "Honda", "Accord", null, 18000m, 45000, null, null);
        string pageText = "2020 Honda Accord, one owner, $18,000, 45,000 miles. Call about financing.";

        ExtractionResult grounded = GroundInPageText(extracted, pageText);

        Assert.Null(grounded.Vin);
    }

    [Fact]
    public void GroundInPageText_VinOnPage_KeepsIt()
    {
        var extracted = new ExtractionResult("1HGCM82633A004352", 2020, "Honda", "Accord", null, 18000m, 45000, null, null);
        string pageText = "VIN: 1HGCM82633A004352. 2020 Honda Accord, $18,000, 45,000 miles.";

        ExtractionResult grounded = GroundInPageText(extracted, pageText);

        Assert.Equal("1HGCM82633A004352", grounded.Vin);
    }

    [Fact]
    public void GroundInPageText_PriceAndMileageOnSameLineSeparatedByASpace_KeepsBoth()
    {
        var extracted = new ExtractionResult(null, 2020, "Honda", "Insight", "EX", 18500m, 45231, null, null);
        string pageText = "2020 Honda Insight EX $18,500 45,231 miles";

        ExtractionResult grounded = GroundInPageText(extracted, pageText);

        Assert.Equal(18500m, grounded.Price);
        Assert.Equal(45231, grounded.Mileage);
    }

    [Fact]
    public void GroundInPageText_HyphenJoinedPhoneNumber_DoesNotGroundAFabricatedPriceAgainstIt()
    {
        var extracted = new ExtractionResult(null, 2020, "Honda", "Insight", null, 4567m, null, null, null);
        string pageText = "2020 Honda Insight. Call the dealer at (555) 123-4567 for details.";

        ExtractionResult grounded = GroundInPageText(extracted, pageText);

        Assert.Null(grounded.Price);
    }

    [Fact]
    public void GroundInPageText_HyphenJoinedZipPlusFour_DoesNotGroundAFabricatedMileageAgainstIt()
    {
        var extracted = new ExtractionResult(null, 2020, "Honda", "Insight", null, null, 1234, null, null);
        string pageText = "2020 Honda Insight, located at 90210-1234.";

        ExtractionResult grounded = GroundInPageText(extracted, pageText);

        Assert.Null(grounded.Mileage);
    }

    [Fact]
    public void GroundInPageText_DealerNameNotOnPage_DropsIt()
    {
        var extracted = new ExtractionResult(null, 2020, "Honda", "Insight", null, 18000m, 45000, "Holler Honda", null);
        string pageText = "2020 Honda Insight, $18,000, 45,000 miles.";

        ExtractionResult grounded = GroundInPageText(extracted, pageText);

        Assert.Null(grounded.DealerName);
    }

    [Fact]
    public void GroundInPageText_DealerNameAndLocationOnPage_KeepsBoth()
    {
        var extracted = new ExtractionResult(null, 2020, "Honda", "Insight", null, 18000m, 45000, "Holler Honda", "Winter Park, FL");
        string pageText = "2020 Honda Insight, $18,000, 45,000 miles. Sold by Holler Honda in Winter Park, FL.";

        ExtractionResult grounded = GroundInPageText(extracted, pageText);

        Assert.Equal("Holler Honda", grounded.DealerName);
        Assert.Equal("Winter Park, FL", grounded.DealerLocation);
    }

    [Fact]
    public void GroundInPageText_CarMaxDetailFixtureStatingMileageInThousands_KeepsTheFullMileage()
    {
        string pageText = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", "carmax-detail-29085801.txt"));
        var extracted = new ExtractionResult(null, 2022, "Toyota", "Prius", "LE", 24998m, 38000, "CarMax Orlando", null);

        ExtractionResult grounded = GroundInPageText(extracted, pageText);

        Assert.Equal(38000, grounded.Mileage);
        Assert.Equal(24998m, grounded.Price);
    }

    [Theory]
    [InlineData(38000, "38K miles", true)]
    [InlineData(38412, "38K miles", true)]
    [InlineData(37600, "38k mi", true)]
    [InlineData(38500, "38.5K miles", true)]
    [InlineData(38, "38K miles", false)]
    [InlineData(45000, "38K miles", false)]
    [InlineData(38000, "Save up to $38K on a new one", false)]
    [InlineData(38000, "Stock 38K", false)]
    public void GroundInPageText_ThousandsShorthandMileage_GroundsOnlyAMileageWithinItsRounding(int mileage, string statedOnPage, bool kept)
    {
        var extracted = new ExtractionResult(null, 2022, "Toyota", "Prius", null, null, mileage, null, null);

        ExtractionResult grounded = GroundInPageText(extracted, $"2022 Toyota Prius LE {statedOnPage}");

        Assert.Equal(kept ? mileage : null, grounded.Mileage);
    }
}
