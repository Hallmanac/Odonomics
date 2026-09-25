using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the shipping fee a carvana walk stores, from a recorded detail page (carvana,
/// camry-hybrid, 2026-09-23, the same capture <see cref="CarvanaDealerNameTests"/> uses). That page
/// prints "$1,290 shipping" beside its price, again in its delivery block as "$1,290 Shipping", and
/// lists other cars after "Need it sooner?". The free and absent pages are that page with the
/// shipping lines edited, since no recorded page had either.</summary>
public class CarvanaShippingTests
{
    private static string RecordedPage() =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", "carvana-detail-no-dealer.txt"));

    private static string WithoutShippingLines(string page) =>
        string.Join('\n', page.Split('\n').Where(line => !line.Contains("shipping", StringComparison.OrdinalIgnoreCase)));

    [Fact]
    public void Read_RecordedPageShowingADollarFee_ReturnsTheFee()
    {
        Assert.Equal(1290m, CarvanaShipping.Read(RecordedPage()));
    }

    [Fact]
    public void Read_PageShowingFreeShipping_ReturnsZero()
    {
        string page = RecordedPage().Replace("$1,290 shipping", "Free shipping").Replace("$1,290 Shipping", "Free shipping");

        Assert.Equal(0m, CarvanaShipping.Read(page));
    }

    [Fact]
    public void Read_PageShowingNoShippingLine_ReturnsNullRatherThanZero()
    {
        Assert.Null(CarvanaShipping.Read(WithoutShippingLines(RecordedPage())));
    }

    [Fact]
    public void Read_FeeFollowedByTheDeliveryPromiseOnOneLine_ReturnsTheFee()
    {
        Assert.Equal(1590m, CarvanaShipping.Read("2025 Toyota Prius LE\n$17,990\n$1,590 shipping | Get it Tuesday\nGet Started"));
    }

    [Fact]
    public void Read_ShippingLineOnlyOnASimilarVehicleCard_ReturnsNullRatherThanThatCarsFee()
    {
        string page = WithoutShippingLines(RecordedPage()) + "\nNeed it sooner?\n2017 Toyota Camry\n$19,590\n$290 shipping\n";

        Assert.Null(CarvanaShipping.Read(page));
    }

    [Fact]
    public void Read_ProseMentioningShippingMidSentence_IsNotTakenAsAFee()
    {
        Assert.Null(CarvanaShipping.Read("Ask about $500 shipping credits at pickup.\n"));
    }

    [Fact]
    public void ReadShippingFee_CarvanaSite_ReadsTheFeeAndCarsComStoresNone()
    {
        string page = RecordedPage();

        Assert.Equal(1290m, WalkSites.Carvana.ReadShippingFee(page));
        Assert.Null(WalkSites.CarsCom.ReadShippingFee(page));
    }
}
