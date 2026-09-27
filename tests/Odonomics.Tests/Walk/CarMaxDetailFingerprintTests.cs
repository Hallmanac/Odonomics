using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the fingerprint <see cref="CarMaxBackfill"/> uses to match a recorded CarMax
/// detail page to a posting is read correctly off two of walk run 20260927-192443's own recorded
/// pages, one "Only at" and one "Reserved at" (see CarMaxStoresTests, which reads the store line off
/// these same two pages).</summary>
public class CarMaxDetailFingerprintTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    [Fact]
    public void Read_OnlyAtLaurelFixture_ReturnsItsYearMakeModelTrimMileageAndPrice()
    {
        CarMaxDetailFingerprint? fingerprint = CarMaxDetailFingerprints.Read(Fixture("carmax-backfill-detail-laurel.txt"));

        Assert.Equal(new CarMaxDetailFingerprint(2019, "Honda", "Insight", "EX", 42000, 22998m), fingerprint);
    }

    [Fact]
    public void Read_ReservedAtNorthHoustonFixture_ReturnsItsYearMakeModelTrimMileageAndPrice()
    {
        CarMaxDetailFingerprint? fingerprint = CarMaxDetailFingerprints.Read(Fixture("carmax-backfill-detail-north-houston.txt"));

        Assert.Equal(new CarMaxDetailFingerprint(2022, "Honda", "Insight", "EX", 65000, 21998m), fingerprint);
    }

    [Fact]
    public void Read_PageWithNoTitleLine_ReturnsNull()
    {
        Assert.Null(CarMaxDetailFingerprints.Read("Shop\nSell/Trade\nFinance\n"));
    }

    [Theory]
    [InlineData("2k miles", 2000)]
    [InlineData("630 miles", 630)]
    [InlineData("33k miles", 33000)]
    public void Read_MileageLine_RoundsAKSuffixToThousands(string mileageLine, int expectedMileage)
    {
        string pageText = $"2022 Toyota Corolla Hybrid\nLE\n{mileageLine}\n\n$26,998\n";

        CarMaxDetailFingerprint? fingerprint = CarMaxDetailFingerprints.Read(pageText);

        Assert.Equal(expectedMileage, fingerprint?.Mileage);
    }

    [Fact]
    public void Read_ShippingFeeLine_IsNeverMistakenForTheBarePriceLine()
    {
        string pageText = "2022 Toyota Corolla Hybrid\nLE\n33k miles\n\n$22,998\n\n$649 shipping\n";

        CarMaxDetailFingerprint? fingerprint = CarMaxDetailFingerprints.Read(pageText);

        Assert.Equal(22998m, fingerprint?.Price);
    }
}
