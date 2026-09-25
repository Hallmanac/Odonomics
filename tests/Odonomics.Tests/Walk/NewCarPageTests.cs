using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves which cars.com detail pages read as a new car, over two recorded pages: a new
/// 2027 Corolla Hybrid (title "New 2027 …", an MSRP line, no Mileage line) and a used 2024 one.</summary>
public class NewCarPageTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    [Fact]
    public void Reads_RecordedNewCarPage_IsNewCar()
    {
        Assert.True(NewCarPage.Reads(Fixture("cars-com-new-car-detail.txt")));
    }

    [Fact]
    public void Reads_RecordedUsedCarPage_IsNotNewCar()
    {
        Assert.False(NewCarPage.Reads(Fixture("cars-com-used-detail.txt")));
    }

    [Fact]
    public void Reads_TitleSaysNewEvenWithMileageAndNoMsrp_IsNewCar()
    {
        Assert.True(NewCarPage.Reads("Home\nNew 2026 Toyota Corolla Hybrid SE\n$29,893\nMileage\n1 mi\n"));
    }

    [Fact]
    public void Reads_MsrpLineWithNoMileageLineAndNoTitle_IsNewCar()
    {
        Assert.True(NewCarPage.Reads("Home\nToyota Corolla Hybrid\n$28,313\nMSRP\n"));
    }

    [Fact]
    public void Reads_UsedCarMissingItsMileage_IsNotNewCar()
    {
        Assert.False(NewCarPage.Reads("Home\nUsed 2024 Toyota Corolla Hybrid LE\n$22,990\nEst. payment\n"));
    }

    [Fact]
    public void Reads_UsedTitleFollowedByANewCarSimilarVehicleCard_IsNotNewCar()
    {
        Assert.False(NewCarPage.Reads("Used 2024 Toyota Corolla Hybrid LE\n$22,990\nMileage\n53,933 mi\nSimilar vehicles\nNew 2027 Toyota Corolla Hybrid LE\n"));
    }

    [Fact]
    public void Reads_WindowsLineEndings_StillMatchesTheTitle()
    {
        Assert.True(NewCarPage.Reads("Home\r\nNew 2027 Toyota Corolla Hybrid LE\r\n$28,313\r\n"));
    }
}
