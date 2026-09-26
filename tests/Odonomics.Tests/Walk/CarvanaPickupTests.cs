using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the pickup option a carvana walk stores, from the "Pickup and Delivery" block
/// recorded live on 2026-09-26 for vehicle/4746813 (a 990-dollar shipping car), cut to the block and
/// its neighbours, and from the block on a full recorded page (camry-hybrid, 2026-09-23). Both print
/// the pickup line and its city with no fee, and the shipping fee under the delivery option. The
/// variants (a fee printed under pickup, an extra "change pickup location" line, no block at all) are
/// those pages with lines edited, since no recorded page had them all.</summary>
public class CarvanaPickupTests
{
    private static string FixtureNamed(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    private static string LiveBlock() => FixtureNamed("carvana-detail-4746813-pickup-block.txt");

    private static string RecordedPage() => FixtureNamed("carvana-detail-no-dealer.txt");

    private static string WithoutTheBlock(string page)
    {
        string[] lines = page.Split('\n');
        int start = Array.FindIndex(lines, l => l.Trim() == "Pickup and Delivery");
        int end = Array.FindIndex(lines, start, l => l.Trim() == "Need it sooner?");
        return string.Join('\n', lines.Take(start).Concat(lines.Skip(end)));
    }

    [Fact]
    public void Read_LiveBlock_ReturnsTheOrlandoLocationWithNoFee()
    {
        Assert.Equal(new PickupOption("Orlando, FL", 0m), CarvanaPickup.Read(LiveBlock()));
    }

    [Fact]
    public void Read_LiveBlock_LeavesTheShippingFeeUnderDeliveryToTheShippingReader()
    {
        Assert.Equal(990m, CarvanaShipping.Read(LiveBlock()));
    }

    [Fact]
    public void Read_BlockOnAFullRecordedPage_ReturnsTheSameOption()
    {
        Assert.Equal(new PickupOption("Orlando, FL", 0m), CarvanaPickup.Read(RecordedPage()));
        Assert.Equal(1290m, CarvanaShipping.Read(RecordedPage()));
    }

    [Fact]
    public void Read_PageWhoseBlockDidNotRender_ReturnsNullAndKeepsTheHeaderShippingFee()
    {
        string page = "2025 Toyota Prius LE\n$17,990\n$1,590 shipping | Get it Tuesday\nGet Started\nAsk Something Else\nMore to Love\n";

        Assert.Null(CarvanaPickup.Read(page));
        Assert.Equal(1590m, CarvanaShipping.Read(page));
    }

    [Fact]
    public void Read_RecordedPageWithTheBlockCutOut_ReturnsNull()
    {
        Assert.Null(CarvanaPickup.Read(WithoutTheBlock(RecordedPage())));
    }

    [Fact]
    public void Read_PickupLineWithoutTheBlockHeading_IsNotTaken()
    {
        Assert.Null(CarvanaPickup.Read("Need it sooner?\nPick it up from our Orlando location\nOrlando, FL\n"));
    }

    [Fact]
    public void Read_BlockThatOffersOnlyDelivery_ReturnsNull()
    {
        Assert.Null(CarvanaPickup.Read("Pickup and Delivery\n\nDelivery Tuesday\n\nDelivered to you within 3 days\nOrlando, FL\n$990 Shipping\nMore to Love\n"));
    }

    [Fact]
    public void Read_FeePrintedUnderThePickupOption_ReturnsTheFee()
    {
        string page = LiveBlock().Replace("Orlando, FL\nor", "Orlando, FL\n$49\nor");

        Assert.Equal(new PickupOption("Orlando, FL", 49m), CarvanaPickup.Read(page));
        Assert.Equal(990m, CarvanaShipping.Read(page));
    }

    [Fact]
    public void Read_FreePrintedUnderThePickupOption_ReturnsZero()
    {
        string page = LiveBlock().Replace("Orlando, FL\nor", "Orlando, FL\nFree\nor");

        Assert.Equal(new PickupOption("Orlando, FL", 0m), CarvanaPickup.Read(page));
    }

    [Fact]
    public void Read_FeeOnlyUnderTheDeliveryOption_IsNeverThePickupFee()
    {
        Assert.Equal(0m, CarvanaPickup.Read(LiveBlock())?.Fee);
    }

    [Fact]
    public void Read_ChangePickupLocationLineBetweenThePickupLineAndTheCity_StillFindsTheCity()
    {
        string page = LiveBlock().Replace("Pick it up from our Orlando location\n", "Pick it up from our Orlando location\nin Pickup Wednesday, change pickup location\n");

        Assert.Equal(new PickupOption("Orlando, FL", 0m), CarvanaPickup.Read(page));
    }

    [Fact]
    public void Read_PickupLineWithNoCityLine_FallsBackToTheNameInThePickupLine()
    {
        string page = LiveBlock().Replace("Orlando, FL\nor", "or");

        Assert.Equal(new PickupOption("Orlando", 0m), CarvanaPickup.Read(page));
    }

    [Fact]
    public void ReadPickup_CarvanaSite_ReadsTheOptionAndCarsComStoresNone()
    {
        Assert.Equal(new PickupOption("Orlando, FL", 0m), WalkSites.Carvana.ReadPickup(LiveBlock()));
        Assert.Null(WalkSites.CarsCom.ReadPickup(LiveBlock()));
    }

    [Fact]
    public void LazyDetailBlockMarker_OnlyCarvanaNamesOne_AndItIsTheBlockHeading()
    {
        Assert.Equal("Pickup and Delivery", WalkSites.Carvana.LazyDetailBlockMarker);
        Assert.Null(WalkSites.CarsCom.LazyDetailBlockMarker);
        Assert.Null(WalkSites.Autotrader.LazyDetailBlockMarker);
    }
}
