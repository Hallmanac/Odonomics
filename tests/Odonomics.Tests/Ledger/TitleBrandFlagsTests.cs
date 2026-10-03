using Odonomics.Domain;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

public class TitleBrandFlagsTests
{
    private static readonly DateTimeOffset Seen = new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero);

    private static PostingEntity Posting(string source, string? titleBrandPhrase, int daysAgo = 0) => new()
    {
        VehicleVin = "JTDBCMFEXS3070309",
        Source = source,
        Url = $"https://example.com/{source}/{daysAgo}",
        FirstSeen = Seen.AddDays(-daysAgo),
        LastSeen = Seen.AddDays(-daysAgo),
        TitleBrandPhrase = titleBrandPhrase,
    };

    private static VehicleEntity Vehicle(params PostingEntity[] postings) => new()
    {
        Vin = "JTDBCMFEXS3070309",
        Year = 2025,
        Make = "Toyota",
        Model = "Corolla Hybrid",
        Mileage = 83682,
        FirstSeen = Seen,
        LastSeen = Seen,
        Postings = [.. postings],
    };

    [Fact]
    public void For_PostingStatingABrand_NamesThePhraseAndTheSource()
    {
        RedFlag flag = Assert.Single(TitleBrandFlags.For(Vehicle(Posting("cars.com", "rebuilt title"))));

        Assert.Equal("title-brand-listed", flag.ShortTag);
        Assert.Equal("the listing text says so: cars.com states \"rebuilt title\"; confirm the title status before buying", flag.Detail);
    }

    [Fact]
    public void For_NoPostingStatingABrand_RaisesNothing() =>
        Assert.Empty(TitleBrandFlags.For(Vehicle(Posting("cars.com", null), Posting("autotrader", "  "))));

    [Fact]
    public void For_SeveralPostingsStatingBrands_NamesEachSourceOnceNewestFirst()
    {
        VehicleEntity vehicle = Vehicle(
            Posting("autotrader", "Salvage Title", daysAgo: 5),
            Posting("cars.com", "rebuilt title", daysAgo: 1),
            Posting("cars.com", "Rebuilt Title", daysAgo: 3));

        RedFlag flag = Assert.Single(TitleBrandFlags.For(vehicle));

        Assert.Equal("the listing text says so: cars.com states \"rebuilt title\"; autotrader states \"Salvage Title\"; confirm the title status before buying", flag.Detail);
    }
}
