using Odonomics.Domain;

namespace Odonomics.Tests.Domain;

public class SalvageSellerFlagTests
{
    private static DateTimeOffset Day(int year, int month, int day) => new(year, month, day, 0, 0, 0, TimeSpan.Zero);

    private static IReadOnlyList<RedFlag> Flags(IReadOnlyList<VinHistoryPoint> history) =>
        RedFlagsEvaluator.Evaluate(recalls: [], safetyOverallRating: 5, priorListings: history, currentPrice: null).Flags;

    /// <summary>The "all listings" rows of notes/show-JTDBCMFEXS3070309-2026-10-03.txt in the project
    /// home: a 2025 Corolla Hybrid XLE that was new at Toyota Sunnyvale in October 2024, passed through
    /// Erepairables.com in July 2026 at 83,630 miles, and is listed at Liz Auto Sales Llc from August
    /// 2026. `odo show` printed "Red flags (0)" for it before the salvage-seller rule existed.</summary>
    private static readonly VinHistoryPoint[] CorollaHybridHistory =
    [
        new("Price Family Dealerships", Day(2024, 10, 18), Day(2024, 10, 18), 30200m, 0),
        new("Toyota Sunnyvale", Day(2024, 10, 18), Day(2024, 10, 18), 30199m, 0),
        new("Toyota Sunnyvale", Day(2024, 10, 19), Day(2024, 10, 19), 30199m, 0),
        new("Price Family Dealerships", Day(2024, 10, 20), Day(2024, 11, 11), 30200m, 1),
        new("Toyota Sunnyvale", Day(2024, 10, 20), Day(2024, 11, 11), 30199m, 1),
        new("Erepairables.com", Day(2026, 7, 15), Day(2026, 7, 16), null, 83630),
        new("Liz Auto Sales Llc", Day(2026, 8, 18), Day(2026, 10, 3), 15995m, 83682),
    ];

    [Fact]
    public void Evaluate_JTDBCMFEXS3070309History_FlagsErepairablesWithItsDate()
    {
        RedFlag flag = Assert.Single(Flags(CorollaHybridHistory));

        Assert.Equal("salvage-seller", flag.ShortTag);
        Assert.Contains("Erepairables.com on 2026-07-15", flag.Detail);
        Assert.DoesNotContain("Liz Auto Sales", flag.Detail);
    }

    [Fact]
    public void Evaluate_OnlyOrdinaryDealers_RaisesNoSalvageFlag()
    {
        VinHistoryPoint[] ordinary = [.. CorollaHybridHistory.Where(p => p.Dealer != "Erepairables.com")];

        Assert.Empty(Flags(ordinary));
    }

    [Theory]
    [InlineData("Erepairables.com")]
    [InlineData("EREPAIRABLES")]
    [InlineData("Copart")]
    [InlineData("COPART INC")]
    [InlineData("IAA")]
    [InlineData("IAA Inc.")]
    [InlineData("Insurance Auto Auctions")]
    [InlineData("insurance auto auctions, inc")]
    [InlineData("SalvageBid")]
    [InlineData("Salvage Bid")]
    [InlineData("Salvage Reseller")]
    [InlineData("SalvageReseller.com")]
    [InlineData("A Better Bid")]
    [InlineData("a better bid llc")]
    [InlineData("AutoBidMaster")]
    [InlineData("Auto Bid Master")]
    public void IsSalvageSeller_KnownOutlet_Matches(string dealer)
    {
        Assert.True(SalvageSellers.IsSalvageSeller(dealer));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Toyota Sunnyvale")]
    [InlineData("Liz Auto Sales Llc")]
    [InlineData("Better Bid Motors")]
    [InlineData("Copartner Auto")]
    [InlineData("Maiaa Motors")]
    [InlineData("Auto Auctions of Dallas")]
    public void IsSalvageSeller_OrdinaryDealer_DoesNotMatch(string? dealer)
    {
        Assert.False(SalvageSellers.IsSalvageSeller(dealer));
    }

    [Fact]
    public void Evaluate_SeveralSalvageRowsFromOneSeller_NamesItOnceWithItsEarliestDate()
    {
        List<VinHistoryPoint> history =
        [
            new("Copart", Day(2026, 3, 10), Day(2026, 3, 12), null, 40000),
            new("COPART", Day(2026, 2, 1), Day(2026, 2, 2), null, 39000),
            new("Ordinary Motors", Day(2026, 4, 1), Day(2026, 5, 1), 20000m, 41000),
        ];

        RedFlag flag = Assert.Single(Flags(history));

        Assert.Contains("on 2026-02-01", flag.Detail);
        Assert.DoesNotContain("2026-03-10", flag.Detail);
    }

    [Fact]
    public void Evaluate_TwoDifferentSalvageSellers_NamesBothInDateOrder()
    {
        List<VinHistoryPoint> history =
        [
            new("SalvageBid", Day(2026, 5, 1), Day(2026, 5, 2), null, 40000),
            new("Copart", Day(2026, 2, 1), Day(2026, 2, 2), null, 39000),
        ];

        RedFlag flag = Assert.Single(Flags(history));

        Assert.True(flag.Detail.IndexOf("Copart on 2026-02-01", StringComparison.Ordinal) < flag.Detail.IndexOf("SalvageBid on 2026-05-01", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_SalvageRowWithNoDate_StillFlagsIt()
    {
        List<VinHistoryPoint> history = [new("IAA", null, null, null, 40000)];

        RedFlag flag = Assert.Single(Flags(history));

        Assert.Contains("IAA on an unknown date", flag.Detail);
    }
}
