using System.Text.Json;
using Odonomics.Sources;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the autotrader walk target: its search URLs, and its link collection and private
/// seller handling over pages recorded over CDP from Brian's Edge on 2026-09-25 (zip 32833, 50 miles,
/// 2019 and newer, under 100,000 miles). The search fixtures are the page's body text plus every
/// detail-link anchor on it as one JSON [href, text] pair per line; the private-seller detail page is
/// a recording with the seller's name replaced by "Sample S".</summary>
public class AutotraderWalkTests
{
    private static ListingQuery Query(string make, string model, int yearMin = 2019, int? hybridOnlyFromModelYear = null) =>
        new(make, model, yearMin, "32833", 50, 100000, hybridOnlyFromModelYear);

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    private static List<PageLink> Anchors(string name) =>
        [.. Fixture(name)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonSerializer.Deserialize<string[]>(line) ?? throw new InvalidOperationException("bad fixture line"))
            .Select(pair => new PageLink(pair[0], pair[1]))];

    private static string ListingId(string href) => WalkSites.CanonicalDetailUrl(href).Split('/')[^1];

    [Fact]
    public void BuildSearchUrls_Prius_HasNoHybridFacetBecauseThePriusIsHybridByName()
    {
        string url = WalkSites.Autotrader.BuildSearchUrls(Query("Toyota", "Prius")).Single();

        Assert.Equal(
            "https://www.autotrader.com/cars-for-sale/used-cars/toyota/prius?zip=32833&searchRadius=50&startYear=2019&maxMileage=100000&sortBy=relevance",
            url);
    }

    [Fact]
    public void BuildSearchUrls_CorollaHybrid_SearchesTheBaseModelWithTheHybridFuelGroup()
    {
        string url = WalkSites.Autotrader.BuildSearchUrls(Query("Toyota", "Corolla Hybrid")).Single();

        Assert.Equal(
            "https://www.autotrader.com/cars-for-sale/used-cars/toyota/corolla?zip=32833&searchRadius=50&startYear=2019&maxMileage=100000&sortBy=relevance&fuelTypeGroup=HYB",
            url);
    }

    [Fact]
    public void BuildSearchUrls_CamryHybrid_UsesItsOwnMinimumYearAndOneSearchEvenWithAHybridOnlyYear()
    {
        IReadOnlyList<string> urls = WalkSites.Autotrader.BuildSearchUrls(Query("Toyota", "Camry Hybrid", yearMin: 2018, hybridOnlyFromModelYear: 2025));

        // The fuel facet already reaches the base model's post-cutover hybrids, so the cars.com-style
        // second search for the base model would only repeat it.
        Assert.Equal(
            ["https://www.autotrader.com/cars-for-sale/used-cars/toyota/camry?zip=32833&searchRadius=50&startYear=2018&maxMileage=100000&sortBy=relevance&fuelTypeGroup=HYB"],
            urls);
    }

    [Fact]
    public void BuildSearchUrls_Insight_UsesTheMakeAndModelSlugsAndNoHybridFacet()
    {
        string url = WalkSites.Autotrader.BuildSearchUrls(Query("Honda", "Insight")).Single();

        Assert.Equal(
            "https://www.autotrader.com/cars-for-sale/used-cars/honda/insight?zip=32833&searchRadius=50&startYear=2019&maxMileage=100000&sortBy=relevance",
            url);
    }

    [Fact]
    public void BuildSearchUrls_NeverNarrowsBySellerType()
    {
        foreach (string model in new[] { "Prius", "Corolla Hybrid", "Camry Hybrid", "Insight" })
        {
            string url = WalkSites.Autotrader.BuildSearchUrls(Query("Toyota", model)).Single();

            Assert.DoesNotContain("sellerTypes", url);
        }
    }

    [Fact]
    public void BuildSearchUrls_ReadsZipRadiusYearAndMileageFromTheQuery()
    {
        string url = WalkSites.Autotrader.BuildSearchUrls(new ListingQuery("Toyota", "Prius", 2021, "32114", 75, 60000)).Single();

        Assert.Contains("zip=32114&searchRadius=75&startYear=2021&maxMileage=60000&", url);
    }

    [Theory]
    [InlineData("https://www.autotrader.com/cars-for-sale/vehicle/789050704", true)]
    [InlineData("https://www.autotrader.com/cars-for-sale/vehicle/789050704?listingType=USED&clickType=listing", true)]
    [InlineData("https://www.autotrader.com/cars-for-sale/vehicle/789050704#purchaseConfidence", true)]
    [InlineData("https://www.autotrader.com/cars-for-sale/toyota/prius/orlando-fl?zip=32833", false)]
    [InlineData("https://www.autotrader.com/cars-for-sale/vehicle/", false)]
    public void DetailUrlPattern_MatchesOnlyListingLinks(string href, bool expected)
    {
        Assert.Equal(expected, WalkSites.Autotrader.DetailUrlPattern.IsMatch(href));
    }

    [Theory]
    [InlineData("https://www.autotrader.com/cars-for-sale/vehicle/789050704?listingType=USED&makeCode=TOYOTA&clickType=listing")]
    [InlineData("https://www.autotrader.com/cars-for-sale/vehicle/789050704?listingType=USED&zip=32833#purchaseConfidence")]
    [InlineData("https://www.autotrader.com/cars-for-sale/vehicle/789050704#purchaseConfidence")]
    public void CanonicalDetailUrl_DropsTheQueryStringAndFragment(string href)
    {
        Assert.Equal("https://www.autotrader.com/cars-for-sale/vehicle/789050704", WalkSites.CanonicalDetailUrl(href));
    }

    [Fact]
    public void CollectDetailLinks_RecordedSearchPage_YieldsOneLinkPerMatchAndNoneOfTheFillerCards()
    {
        string pageText = Fixture("autotrader-prius-search.txt");
        List<PageLink> anchors = Anchors("autotrader-prius-search-links.jsonl");
        Assert.Contains("13 Matches", pageText);
        Assert.True(anchors.Select(a => ListingId(a.Href)).Distinct().Count() > 50, "the recording carries far more cards than matches");

        IReadOnlyList<string> links = WalkSites.Autotrader.CollectDetailLinks(anchors, poolSize: 60, pageText);

        Assert.Equal(13, links.Count);
        Assert.Equal(13, links.Select(ListingId).Distinct().Count());
        // The matches come first in page order: the same 13 listings the page's own clickType marks
        // as an ordinary result, a spotlight, or the sponsored top card, and none it marks as
        // "supplemental" (beyond the radius) or "similar" (other years, new cars).
        Assert.All(links, l => Assert.DoesNotMatch("clickType=(supplemental|similar)", l));
        Assert.Equal("787014112", ListingId(links[0]));
    }

    [Fact]
    public void CollectDetailLinks_RecordedSearchPage_StillHonorsASmallerPool()
    {
        IReadOnlyList<string> links = WalkSites.Autotrader.CollectDetailLinks(
            Anchors("autotrader-prius-search-links.jsonl"),
            poolSize: 5,
            Fixture("autotrader-prius-search.txt"));

        Assert.Equal(5, links.Count);
    }

    [Fact]
    public void CollectDetailLinks_ZeroMatchPageFilledWithOtherCards_YieldsNoLinks()
    {
        string pageText = Fixture("autotrader-zero-match-search.txt");
        List<PageLink> anchors = Anchors("autotrader-zero-match-search-links.jsonl");
        Assert.Contains("0 Matches", pageText);
        Assert.NotEmpty(anchors);

        IReadOnlyList<string> links = WalkSites.Autotrader.CollectDetailLinks(anchors, poolSize: 60, pageText);

        Assert.Empty(links);
    }

    [Fact]
    public void CollectDetailLinks_OneListingLinkedWithAndWithoutAFragment_IsOneCandidate()
    {
        IReadOnlyList<string> links = WalkSites.Autotrader.CollectDetailLinks(
            [
                new PageLink("https://www.autotrader.com/cars-for-sale/vehicle/789050704?clickType=listing", "2022 Toyota Prius"),
                new PageLink("https://www.autotrader.com/cars-for-sale/vehicle/789050704?zip=32833#purchaseConfidence", "No Accidents"),
            ],
            poolSize: 10,
            "1 Match");

        Assert.Single(links);
    }

    [Theory]
    [InlineData("1,204 Matches", 10)]
    [InlineData("Sort By:\n347 Matches\nRelevance", 10)]
    public void CollectDetailLinks_ACountAboveThePoolSize_ChangesNothing(string pageText, int expected)
    {
        List<PageLink> anchors = [.. Enumerable.Range(1, 30).Select(i => new PageLink($"https://www.autotrader.com/cars-for-sale/vehicle/{i}", ""))];

        Assert.Equal(expected, WalkSites.Autotrader.CollectDetailLinks(anchors, poolSize: 10, pageText).Count);
    }

    [Fact]
    public void CollectDetailLinks_PageThatStatesNoCount_IsTakenAsItComes()
    {
        List<PageLink> anchors = [.. Enumerable.Range(1, 30).Select(i => new PageLink($"https://www.autotrader.com/cars-for-sale/vehicle/{i}", ""))];

        Assert.Equal(10, WalkSites.Autotrader.CollectDetailLinks(anchors, poolSize: 10, "Used Toyota Prius for Sale").Count);
        Assert.Equal(10, WalkSites.Autotrader.CollectDetailLinks(anchors, poolSize: 10).Count);
    }

    [Fact]
    public void CollectDetailLinks_SitesWithNoMatchCountPattern_IgnoreTheSearchPageText()
    {
        IReadOnlyList<string> links = WalkSites.Carvana.CollectDetailLinks(
            [new PageLink("https://www.carvana.com/vehicle/123", "")],
            poolSize: 10,
            "0 Matches");

        Assert.Single(links);
    }

    [Fact]
    public void ResolveDealer_PrivateSellerPage_StoresPrivateSellerWithNoLocation()
    {
        ResolvedDealer dealer = WalkSites.Autotrader.ResolveDealer("Sample S", "Huntersville, NC", Fixture("autotrader-detail-private-seller.txt"));

        Assert.Equal(new ResolvedDealer("Private seller", null, IsFallback: false), dealer);
    }

    [Fact]
    public void ResolveDealer_PrivateSellerPageWhoseExtractionNamedNobody_StillStoresPrivateSeller()
    {
        ResolvedDealer dealer = WalkSites.Autotrader.ResolveDealer(null, null, Fixture("autotrader-detail-private-seller.txt"));

        Assert.Equal("Private seller", dealer.Name);
    }

    [Fact]
    public void ResolveDealer_DealerPage_KeepsTheDealerAndItsLocation()
    {
        Assert.DoesNotMatch(@"\(Private Seller\)", Fixture("autotrader-detail-dealer.txt"));

        ResolvedDealer dealer = WalkSites.Autotrader.ResolveDealer("City KIA of Greater Orlando", "Orlando, FL", Fixture("autotrader-detail-dealer.txt"));

        Assert.Equal(new ResolvedDealer("City KIA of Greater Orlando", "Orlando, FL", IsFallback: false), dealer);
    }

    [Fact]
    public void ResolveDealer_DealerNamedPrivateSellerExchange_IsNotAPrivateSeller()
    {
        // Autotrader's private listings are brokered by "Private Seller Exchange", a name that
        // also heads the search cards; only the detail page's own "(Private Seller)" line counts.
        ResolvedDealer dealer = WalkSites.Autotrader.ResolveDealer("Private Seller Exchange", null, "Private Seller Exchange\nBuy Online. Clean title. No Hassle\n");

        Assert.Equal("Private Seller Exchange", dealer.Name);
    }

    [Fact]
    public void ResolveDealer_SiteWithNoPrivateSellerPattern_IgnoresThePageText()
    {
        ResolvedDealer dealer = WalkSites.CarsCom.ResolveDealer("Holler Honda", "Orlando, FL", "Sample S (Private Seller)\n");

        Assert.Equal("Holler Honda", dealer.Name);
    }

    [Theory]
    [InlineData("Toyota", "Prius", "Toyota", "Prius", "XLE", 2023, true)]
    [InlineData("Toyota", "Prius", "Toyota", "Camry", "SE", 2023, false)]
    [InlineData("Toyota", "Corolla Hybrid", "Toyota", "Corolla", "Hybrid LE", 2023, true)]
    [InlineData("Toyota", "Corolla Hybrid", "Toyota", "Corolla", "LE", 2023, false)]
    [InlineData("Toyota", "Camry Hybrid", "Toyota", "Camry", "SE", 2024, false)]
    [InlineData("Honda", "Insight", "Honda", "Insight", "EX", 2019, true)]
    public void MatchesExtractedVehicle_AppliesToAutotraderCandidatesUnchanged(string queryMake, string queryModel, string make, string model, string trim, int year, bool expected)
    {
        // A private seller's listing gets no exemption: the page has to say it is this model.
        Assert.Equal(expected, Query(queryMake, queryModel).MatchesExtractedVehicle(make, model, trim, year));
    }

    [Fact]
    public void Find_Autotrader_ReturnsIt()
    {
        Assert.Same(WalkSites.Autotrader, WalkSites.Find("Autotrader"));
    }
}
