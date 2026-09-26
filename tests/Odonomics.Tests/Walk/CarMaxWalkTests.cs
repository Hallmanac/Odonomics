using System.Text.Json;
using Odonomics.Sources;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the carmax walk target: its search URLs, detail-link pattern, match count, and link
/// collection. The search fixtures are cut to the shape of the page probed over CDP on 2026-09-26
/// (Prius, zip 32833, 2019 and newer, under 100,000 miles, nationwide): the body text with the filtered
/// "526 matches" line first, the "Show 25 matches" control, and the site-wide totals further down, and
/// the cards file the walk's recorder writes (one entry per detail link, with its card's text). The
/// text is written from the strings that probe recorded, not copied from a saved capture.</summary>
public class CarMaxWalkTests
{
    private static ListingQuery Query(string make, string model, int yearMin = 2019, int? hybridOnlyFromModelYear = null) =>
        new(make, model, yearMin, "32833", 50, 100000, hybridOnlyFromModelYear);

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    private sealed record CardEntry(string Href, string Text, string Card);

    private static List<PageLink> Cards(string name) =>
        [.. (JsonSerializer.Deserialize<List<CardEntry>>(Fixture(name), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [])
            .Select(c => new PageLink(c.Href, c.Text, c.Card))];

    [Fact]
    public void CarMaxSearchUrl_Prius_SearchesNationwideFromTheMinimumYearToNextYear()
    {
        string url = WalkSites.CarMaxSearchUrl(Query("Toyota", "Prius"), currentYear: 2026);

        Assert.Equal("https://www.carmax.com/cars/toyota/prius/2019-2027?zip=32833&distance=nationwide&mileage=100000", url);
    }

    [Fact]
    public void CarMaxSearchUrl_CorollaHybrid_BuildsTheSlashFormDirectly()
    {
        string url = WalkSites.CarMaxSearchUrl(Query("Toyota", "Corolla Hybrid"), currentYear: 2026);

        Assert.Equal("https://www.carmax.com/cars/toyota/corolla/hybrid/2019-2027?zip=32833&distance=nationwide&mileage=100000", url);
    }

    [Fact]
    public void CarMaxSearchUrl_CamryHybrid_UsesItsOwnMinimumYear()
    {
        string url = WalkSites.CarMaxSearchUrl(Query("Toyota", "Camry Hybrid", yearMin: 2018, hybridOnlyFromModelYear: 2025), currentYear: 2026);

        Assert.Equal("https://www.carmax.com/cars/toyota/camry/hybrid/2018-2027?zip=32833&distance=nationwide&mileage=100000", url);
    }

    [Fact]
    public void CarMaxSearchUrl_Insight_UsesTheMakeAndModelPaths()
    {
        string url = WalkSites.CarMaxSearchUrl(Query("Honda", "Insight"), currentYear: 2026);

        Assert.Equal("https://www.carmax.com/cars/honda/insight/2019-2027?zip=32833&distance=nationwide&mileage=100000", url);
    }

    [Fact]
    public void CarMaxSearchUrl_IgnoresTheScenarioRadiusAndReadsZipYearAndMileage()
    {
        string narrow = WalkSites.CarMaxSearchUrl(new ListingQuery("Toyota", "Prius", 2021, "32114", 10, 60000), currentYear: 2026);
        string wide = WalkSites.CarMaxSearchUrl(new ListingQuery("Toyota", "Prius", 2021, "32114", 500, 60000), currentYear: 2026);

        Assert.Equal(narrow, wide);
        Assert.Equal("https://www.carmax.com/cars/toyota/prius/2021-2027?zip=32114&distance=nationwide&mileage=60000", narrow);
    }

    [Fact]
    public void BuildSearchUrls_IsOneSearchEvenForAHybridOnlyFromYearModel()
    {
        IReadOnlyList<string> urls = WalkSites.CarMax.BuildSearchUrls(Query("Toyota", "Camry Hybrid", yearMin: 2018, hybridOnlyFromModelYear: 2025));

        string url = Assert.Single(urls);
        Assert.Matches(@"^https://www\.carmax\.com/cars/toyota/camry/hybrid/2018-\d{4}\?zip=32833&distance=nationwide&mileage=100000$", url);
    }

    [Theory]
    [InlineData("https://www.carmax.com/car/29085801", true)]
    [InlineData("https://www.carmax.com/car/29085801?sc_cid=abc", true)]
    [InlineData("https://www.carmax.com/cars/toyota/prius/2019-2027?zip=32833", false)]
    [InlineData("https://www.carmax.com/cars/toyota/prius", false)]
    [InlineData("https://www.carmax.com/car/", false)]
    [InlineData("https://www.cars.com/car/29085801", false)]
    public void DetailUrlPattern_MatchesOnlyCarLinks(string href, bool expected)
    {
        Assert.Equal(expected, WalkSites.CarMax.DetailUrlPattern.IsMatch(href));
    }

    [Fact]
    public void CanonicalDetailUrl_DropsTheQueryString()
    {
        Assert.Equal("https://www.carmax.com/car/29085801", WalkSites.CanonicalDetailUrl("https://www.carmax.com/car/29085801?sc_cid=abc#photos"));
    }

    [Fact]
    public void MatchCountIn_RecordedSearchPage_IsTheFirstCountLineAndNotTheSiteWideTotals()
    {
        string pageText = Fixture("carmax-prius-search.txt");
        Assert.Contains("86,317 matches", pageText);
        Assert.Contains("Show 25 matches", pageText);

        Assert.Equal(526, WalkSites.CarMax.MatchCountIn(pageText));
    }

    [Theory]
    [InlineData("Used Toyota Prius for sale\n4 matches\nShow 25 matches\n86,337 matches", 4)]
    [InlineData("1 match\nShow 25 matches", 1)]
    [InlineData("0 matches\n86,337 matches", 0)]
    [InlineData("Show 25 matches\n12 matches", 12)]
    [InlineData("1,204 matches", 1204)]
    public void MatchCountIn_ReadsTheFirstCountAndSkipsTheShowControl(string pageText, int expected)
    {
        Assert.Equal(expected, WalkSites.CarMax.MatchCountIn(pageText));
    }

    [Fact]
    public void MatchCountIn_PageWithOnlyTheShowControl_IsNoCount()
    {
        Assert.Null(WalkSites.CarMax.MatchCountIn("Sort by\nShow 25 matches"));
    }

    [Theory]
    [InlineData("Show 25 matches", true)]
    [InlineData("  Show 25 matches ", true)]
    [InlineData("Show 5 matches", true)]
    [InlineData("526 matches", false)]
    [InlineData("Show all filters", false)]
    public void LoadMoreControlPattern_MatchesOnlyTheShowControlLabel(string label, bool expected)
    {
        Assert.Equal(expected, WalkSites.CarMax.LoadMoreControlPattern?.IsMatch(label));
    }

    [Fact]
    public void CollectDetailLinks_RecordedSearchPage_YieldsOneLinkPerListing()
    {
        string pageText = Fixture("carmax-prius-search.txt");
        List<PageLink> anchors = Cards("carmax-prius-search-cards.json");
        Assert.Equal(5, anchors.Count);

        IReadOnlyList<string> links = WalkSites.CarMax.CollectDetailLinks(anchors, poolSize: 60, pageText);

        Assert.Equal(
            [
                "https://www.carmax.com/car/29085801",
                "https://www.carmax.com/car/28870112",
                "https://www.carmax.com/car/29104455",
                "https://www.carmax.com/car/29001233",
            ],
            links);
    }

    [Fact]
    public void CollectDetailLinks_StatedCountBelowTheCardsOnThePage_TrustsTheCount()
    {
        string pageText = Fixture("carmax-prius-search.txt").Replace("526 matches", "2 matches");

        IReadOnlyList<string> links = WalkSites.CarMax.CollectDetailLinks(Cards("carmax-prius-search-cards.json"), poolSize: 60, pageText);

        Assert.Equal(2, links.Count);
    }

    [Fact]
    public void CollectDetailLinks_ZeroMatches_YieldsNoLinks()
    {
        string pageText = Fixture("carmax-prius-search.txt").Replace("526 matches", "0 matches");

        Assert.Empty(WalkSites.CarMax.CollectDetailLinks(Cards("carmax-prius-search-cards.json"), poolSize: 60, pageText));
    }

    [Fact]
    public void CollectDetailCards_KeepsTheFirstCardTextAListingsLinksCarry()
    {
        IReadOnlyList<PageLink> cards = WalkSites.CarMax.CollectDetailCards(Cards("carmax-prius-search-cards.json"), poolSize: 60, Fixture("carmax-prius-search.txt"));

        // The first anchor for 29085801 is the photo, with no text; the card text comes from its title anchor.
        Assert.Contains("$49 shipping·Get it by Monday", cards[0].CardText);
    }

    [Fact]
    public void CollectDetailCards_ReadCardFee_GivesEachRecordedCardItsOwnFee()
    {
        IReadOnlyList<PageLink> cards = WalkSites.CarMax.CollectDetailCards(Cards("carmax-prius-search-cards.json"), poolSize: 60, Fixture("carmax-prius-search.txt"));

        Assert.Equal(
            [new CardFee(49m), new CardFee(0m, "Orlando"), new CardFee(149m), new CardFee(1999m)],
            cards.Select(c => WalkSites.CarMax.ReadCardFee(c.CardText)).Select(f => f!.Value));
    }

    [Fact]
    public void Find_CarMax_ReturnsTheSite()
    {
        Assert.Same(WalkSites.CarMax, WalkSites.Find("CarMax"));
        Assert.Equal("carmax", WalkSites.CarMax.Name);
    }

    [Fact]
    public void ResolveDealer_PageNamingAStore_KeepsTheStoreAndItsLocation()
    {
        ResolvedDealer dealer = WalkSites.CarMax.ResolveDealer("CarMax Orlando", "Orlando, FL");

        Assert.Equal(new ResolvedDealer("CarMax Orlando", "Orlando, FL", IsFallback: false), dealer);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("CarMax")]
    public void ResolveDealer_PageNamingNoStore_FallsBackToTheChain(string? extractedName)
    {
        ResolvedDealer dealer = WalkSites.CarMax.ResolveDealer(extractedName, "Orlando, FL");

        Assert.Equal(new ResolvedDealer("CarMax", null, IsFallback: true), dealer);
    }
}
