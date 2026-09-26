using System.Text.Json;
using Odonomics.Domain;
using Odonomics.Ledger;
using Odonomics.Sources;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the cargurus walk target: its search URLs and the ids in them, its detail-link pattern, match
/// count, sponsored-card skip and result paging, and the link collection over search pages recorded on 2026-09-26
/// (Toyota Corolla Hybrid, 96 vehicles, pages 1 and 5 of 5; Honda Insight, 21 vehicles, one page; zip 32833, 50
/// miles, 2019 and newer, under 100,000 miles). Each fixture pair is one load of one page: its body text, and the
/// cards file the walk's recorder writes (one entry per detail link, with its card's text).</summary>
public class CarGurusWalkTests
{
    private const string PagingHtmlToken = "eyJmaXJzdFBhZ2UiOjE4LCJwYWdlTiI6MjF9";

    private static ListingQuery Query(string make, string model, int yearMin = 2019) =>
        new(make, model, yearMin, "32833", 50, 100000);

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    private sealed record CardEntry(string Href, string Text, string Card);

    private static List<PageLink> Cards(string name) =>
        [.. (JsonSerializer.Deserialize<List<CardEntry>>(Fixture(name), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [])
            .Select(c => new PageLink(c.Href, c.Text, c.Card))];

    private static string ListingId(string href) => new Uri(href).AbsolutePath["/details/".Length..];

    [Theory]
    [InlineData("Toyota", "Prius", 2019, "m7%2Cm7%2Fd15")]
    [InlineData("Toyota", "Corolla Hybrid", 2019, "m7%2Cm7%2Fd2840")]
    [InlineData("Toyota", "Camry Hybrid", 2018, "m7%2Cm7%2Fd2908")]
    [InlineData("Honda", "Insight", 2019, "m6%2Cm6%2Fd591")]
    public void BuildSearchUrls_EachScenarioModel_IsOneSearchWithItsIdsAndTheScenariosFacets(string make, string model, int yearMin, string modelPath)
    {
        IReadOnlyList<string> urls = WalkSites.CarGurus.BuildSearchUrls(Query(make, model, yearMin));

        string url = Assert.Single(urls);
        Assert.Equal(
            $"https://www.cargurus.com/search?zip=32833&distance=50&makeModelTrimPaths={modelPath}&startYear={yearMin}&maxMileage=100000&sortDirection=ASC&sortType=BEST_MATCH",
            url);
    }

    [Fact]
    public void BuildSearchUrls_ReadsZipRadiusYearAndMileageFromTheQuery()
    {
        IReadOnlyList<string> urls = WalkSites.CarGurus.BuildSearchUrls(new ListingQuery("Honda", "Insight", 2021, "32114", 25, 60000));

        Assert.Equal(
            "https://www.cargurus.com/search?zip=32114&distance=25&makeModelTrimPaths=m6%2Cm6%2Fd591&startYear=2021&maxMileage=60000&sortDirection=ASC&sortType=BEST_MATCH",
            Assert.Single(urls));
    }

    [Fact]
    public void BuildSearchUrls_ModelWithNoId_IsAnErrorNamingTheModel()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => WalkSites.CarGurus.BuildSearchUrls(Query("Toyota", "RAV4 Hybrid")));

        Assert.Contains("Toyota RAV4 Hybrid", error.Message);
        Assert.Contains("CarGurusSearch", error.Message);
    }

    [Fact]
    public void ModelPath_EveryModelOfTheDaughterScenario_HasAnId()
    {
        Scenario scenario = ScenarioLoader.Load(Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json"));

        foreach (string makeModel in scenario.Filters.AllowedModels)
        {
            (string make, string model) = MakeModel.Split(makeModel);
            Assert.Matches("^m[0-9]+%2Cm[0-9]+%2Fd[0-9]+$", CarGurusSearch.ModelPath(make, model));
        }
    }

    [Theory]
    [InlineData("https://www.cargurus.com/details/458757260?resultSetId=0052d05c&sponsoredType=NONE", true)]
    [InlineData("https://www.cargurus.com/details/458757260", true)]
    [InlineData("https://cargurus.com/details/458757260", true)]
    [InlineData("https://www.cargurus.com/details/", false)]
    [InlineData("https://www.cargurus.com/search?zip=32833", false)]
    [InlineData("https://www.cargurus.com/Cars/inventorylisting/details/458757260", false)]
    [InlineData("https://www.cars.com/details/458757260", false)]
    public void DetailUrlPattern_MatchesOnlyDetailLinks(string href, bool expected)
    {
        Assert.Equal(expected, WalkSites.CarGurus.DetailUrlPattern.IsMatch(href));
    }

    [Fact]
    public void CanonicalDetailUrl_DropsTheSearchSessionQueryString()
    {
        string href = "https://www.cargurus.com/details/458757260?resultSetId=0052d05c-f1c9-43b7-b070-0f010e6c101c&searchUuid=7be08519&sponsoredType=NONE&listingIndex=1";

        Assert.Equal("https://www.cargurus.com/details/458757260", WalkSites.CanonicalDetailUrl(href));
    }

    [Fact]
    public void MatchCountIn_RecordedSearchPages_IsTheVehiclesFoundLine()
    {
        Assert.Equal(96, WalkSites.CarGurus.MatchCountIn(Fixture("cargurus-corolla-hybrid-search.txt")));
        Assert.Equal(21, WalkSites.CarGurus.MatchCountIn(Fixture("cargurus-insight-search.txt")));
    }

    [Theory]
    [InlineData("1 vehicle found", 1)]
    [InlineData("1,204 vehicles found\nSort by: Best match", 1204)]
    [InlineData("0 vehicles found", 0)]
    public void MatchCountIn_ReadsSingularPluralAndThousands(string pageText, int expected)
    {
        Assert.Equal(expected, WalkSites.CarGurus.MatchCountIn(pageText));
    }

    [Theory]
    [InlineData("https://www.cargurus.com/details/456552773?resultSetId=a&sponsoredType=PRIORITY&srpVariation=DEFAULT_SEARCH", true)]
    [InlineData("https://www.cargurus.com/details/456552773?resultSetId=a&sponsoredType=FEATURED&srpVariation=DEFAULT_SEARCH", true)]
    [InlineData("https://www.cargurus.com/details/450695684?resultSetId=a&sponsoredType=HIGHLIGHT&srpVariation=DEFAULT_SEARCH", true)]
    [InlineData("https://www.cargurus.com/details/456552773?resultSetId=a&sponsoredType=PRIORITY", true)]
    [InlineData("https://www.cargurus.com/details/456552773?resultSetId=a&sponsoredType=NONE&srpVariation=DEFAULT_SEARCH", false)]
    [InlineData("https://www.cargurus.com/details/456552773?resultSetId=a&sponsoredType=NONE", false)]
    [InlineData("https://www.cargurus.com/details/456552773", false)]
    public void SponsoredLinkPattern_MatchesEverySponsoredTypeButNone(string href, bool expected)
    {
        Assert.Equal(expected, WalkSites.CarGurus.SponsoredLinkPattern!.IsMatch(href));
    }

    [Fact]
    public void CollectDetailLinks_RecordedFirstPage_YieldsTheOrdinaryResultsOnly()
    {
        List<PageLink> anchors = Cards("cargurus-corolla-hybrid-search-cards.json");
        Assert.Equal(22, anchors.Count);
        Assert.Equal(1, anchors.Count(a => a.Href.Contains("sponsoredType=PRIORITY")));

        IReadOnlyList<string> links = WalkSites.CarGurus.CollectDetailLinks(anchors, poolSize: 200, Fixture("cargurus-corolla-hybrid-search.txt"));

        Assert.Equal(18, links.Count);
        Assert.All(links, link => Assert.Contains("sponsoredType=NONE", link));
        Assert.Equal(links.Count, links.Select(ListingId).Distinct().Count());
        Assert.DoesNotContain("450695684", links.Select(ListingId));
    }

    [Fact]
    public void CollectDetailLinks_AListingPromotedAndAlsoOrdinary_IsKeptOnceThroughItsOrdinaryCard()
    {
        // 458337776 is a FEATURED "Sponsored Listing" card at the top of the page and an ordinary result further down.
        IReadOnlyList<PageLink> cards = WalkSites.CarGurus.CollectDetailCards(Cards("cargurus-corolla-hybrid-search-cards.json"), poolSize: 200, Fixture("cargurus-corolla-hybrid-search.txt"));

        PageLink card = Assert.Single(cards, c => ListingId(c.Href) == "458337776");
        Assert.Contains("sponsoredType=NONE", card.Href);
        Assert.DoesNotContain("Sponsored Listing", card.CardText);
    }

    [Fact]
    public void CollectDetailLinks_APromotedListingWithNoOrdinaryCardOnThePage_IsLeftForThePageThatHasIt()
    {
        // 456552773 is only a FEATURED card on page 1; its ordinary result is on a later page.
        IReadOnlyList<string> links = WalkSites.CarGurus.CollectDetailLinks(Cards("cargurus-corolla-hybrid-search-cards.json"), poolSize: 200, Fixture("cargurus-corolla-hybrid-search.txt"));

        Assert.DoesNotContain("456552773", links.Select(ListingId));
    }

    [Fact]
    public void CollectDetailLinks_RecordedLastPage_YieldsItsFifteenResultsAndNotTheHighlightedAd()
    {
        List<PageLink> anchors = Cards("cargurus-corolla-hybrid-search-page-5-cards.json");
        Assert.Equal(1, anchors.Count(a => a.Href.Contains("sponsoredType=HIGHLIGHT")));

        IReadOnlyList<string> links = WalkSites.CarGurus.CollectDetailLinks(anchors, poolSize: 200, "96 vehicles found");

        Assert.Equal(15, links.Count);
        Assert.DoesNotContain("450695684", links.Select(ListingId));
    }

    [Fact]
    public void CollectDetailLinks_RecordedInsightPage_YieldsAllTwentyOneAndCountsAPromotedCopyOnce()
    {
        IReadOnlyList<string> links = WalkSites.CarGurus.CollectDetailLinks(Cards("cargurus-insight-search-cards.json"), poolSize: 200, Fixture("cargurus-insight-search.txt"));

        Assert.Equal(21, links.Count);
        Assert.Equal(21, links.Select(ListingId).Distinct().Count());
        Assert.Contains("458757260", links.Select(ListingId));
    }

    [Fact]
    public void CollectDetailCards_EachRecordedCardReadsItsPriceFeeAndBadge()
    {
        IReadOnlyList<PageLink> cards = WalkSites.CarGurus.CollectDetailCards(Cards("cargurus-insight-search-cards.json"), poolSize: 200, Fixture("cargurus-insight-search.txt"));

        PageLink transfer = Assert.Single(cards, c => ListingId(c.Href) == "459400072");
        Assert.Equal(27697m, WalkSites.CarGurus.ReadCardPrice(transfer.CardText));
        Assert.Equal(new CardFee(699m), WalkSites.CarGurus.ReadCardFee(transfer.CardText));
        Assert.Equal(FeePostures.AllIn, WalkSites.CarGurus.ReadCardFeeStatement(transfer.CardText)?.Posture);
        Assert.Equal("Fair Deal", WalkSites.CarGurus.ReadCardBadges(transfer.CardText)[PostingAttributeNames.Deal]);
        Assert.All(cards, c => Assert.NotNull(WalkSites.CarGurus.ReadCardPrice(c.CardText)));
        Assert.All(cards, c => Assert.Equal(FeePostures.AllIn, WalkSites.CarGurus.ReadCardFeeStatement(c.CardText)?.Posture));
    }

    [Fact]
    public void AskingPriceOf_RecordedTransferCard_IsTheCardsDeliveredPriceNotTheDetailPagesLotPrice()
    {
        // The card says $27,697 with $699 shipping in it; the car's detail page shows $26,998, the price at its lot.
        string card = Assert.Single(Cards("cargurus-insight-search-cards.json"), c => ListingId(c.Href) == "459400072" && c.Href.Contains("sponsoredType=NONE")).CardText;

        Assert.Equal(27697m, WalkSites.CarGurus.AskingPriceOf(26998m, card));
    }

    [Fact]
    public void AskingPriceOf_NoCardOrACardWithNoPrice_IsNullRatherThanTheDetailPagesPrice()
    {
        Assert.Null(WalkSites.CarGurus.AskingPriceOf(26998m, null));
        Assert.Null(WalkSites.CarGurus.AskingPriceOf(26998m, "Store transfer to Orlando, FL"));
    }

    [Fact]
    public void AskingPriceOf_ASiteThatStoresTheExtractedPrice_KeepsIt()
    {
        Assert.Equal(26998m, WalkSites.CarsCom.AskingPriceOf(26998m, "$27,697\nPrice includes fees"));
        Assert.Null(WalkSites.CarsCom.AskingPriceOf(null, "$27,697"));
    }

    [Fact]
    public void FeeStatementOf_CarGurus_IsTheCardsStatementWhateverTheDetailPageSays()
    {
        const string card = "Fair Deal\n$19,394\nPrice includes fees";
        const string detail = "The advertised price excludes an $999.00 Dealer Document Processing Fee";

        Assert.Equal(FeePostures.AllIn, WalkSites.CarGurus.FeeStatementOf(card, detail)?.Posture);
    }

    [Fact]
    public void FeeStatementOf_CarGurusCardWithNoFeeLine_IsUnknownAndNoCardIsNoStatementFromTheDetailPage()
    {
        Assert.Equal(FeePostures.Unknown, WalkSites.CarGurus.FeeStatementOf("Fair Deal\n$19,394", "detail text")?.Posture);
        Assert.Null(WalkSites.CarGurus.FeeStatementOf(null, "detail text"));
    }

    [Fact]
    public void FeeStatementOf_ASiteWithNoCardReader_ReadsTheDetailPage()
    {
        Assert.Equal(FeePostures.AllIn, WalkSites.CarsCom.FeeStatementOf("$27,697", "Seller has no extra fees")?.Posture);
    }

    [Fact]
    public void ReadPagingToken_RecordedFirstPage_IsThePageAlignmentInItsEmbeddedData()
    {
        Assert.Equal(PagingHtmlToken, WalkSites.CarGurus.ReadPagingToken(Fixture("cargurus-corolla-hybrid-search-paging.html")));
    }

    [Theory]
    [InlineData("<html><body>no embedded data</body></html>")]
    [InlineData("")]
    public void ReadPagingToken_PageWithNone_IsNull(string html)
    {
        Assert.Null(WalkSites.CarGurus.ReadPagingToken(html));
    }

    [Fact]
    public void PagedSearchUrl_CarriesThePageNumberAndTheFirstPagesAlignment()
    {
        string search = WalkSites.CarGurus.BuildSearchUrls(Query("Toyota", "Corolla Hybrid"))[0];

        Assert.Equal($"{search}&page=2&pageAlignment={PagingHtmlToken}", WalkSites.CarGurus.PagedSearchUrl!(search, 2, PagingHtmlToken));
    }

    [Fact]
    public void PagedSearchUrl_WithNoTokenAppendsOnlyThePageNumber()
    {
        string search = WalkSites.CarGurus.BuildSearchUrls(Query("Toyota", "Corolla Hybrid"))[0];

        Assert.Equal($"{search}&page=3", WalkSites.CarGurus.PagedSearchUrl!(search, 3, null));
    }

    [Fact]
    public void PagedSearchUrl_EscapesATokenThatEndsInPadding()
    {
        Assert.EndsWith("&pageAlignment=abc%3D%3D", WalkSites.CarGurus.PagedSearchUrl!("https://www.cargurus.com/search?zip=1", 2, "abc=="));
    }

    private static ValueTask<bool> NoneKnown(string canonicalUrl, decimal? cardPrice, IReadOnlyDictionary<string, string> cardBadges, CancellationToken cancellationToken) => ValueTask.FromResult(false);

    [Fact]
    public async Task CollectLinksAsync_FollowsPagesWithTheFirstPagesTokenUntilAPageAddsNothing()
    {
        string search = WalkSites.CarGurus.BuildSearchUrls(Query("Toyota", "Corolla Hybrid"))[0];
        List<(string Url, int PageNumber)> loads = [];
        string firstPageText = Fixture("cargurus-corolla-hybrid-search.txt");
        List<PageLink> firstPage = Cards("cargurus-corolla-hybrid-search-cards.json");
        List<PageLink> lastPage = Cards("cargurus-corolla-hybrid-search-page-5-cards.json");

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarGurus,
            search,
            WalkPairSearches.UnboundedPool,
            NoneKnown,
            (url, pageNumber, _) =>
            {
                loads.Add((url, pageNumber));
                return Task.FromResult(pageNumber == 1
                    ? new SearchPageContent(firstPage, firstPageText, PagingHtmlToken)
                    : new SearchPageContent(lastPage, "96 vehicles found"));
            },
            (_, _) => { },
            _ => { },
            () => { },
            CancellationToken.None);

        Assert.Equal(
            [search, $"{search}&page=2&pageAlignment={PagingHtmlToken}", $"{search}&page=3&pageAlignment={PagingHtmlToken}"],
            loads.Select(l => l.Url));
        Assert.Equal(18 + 15, pool.Count);
        Assert.Equal(pool.Count, pool.Select(ListingId).Distinct().Count());
    }

    [Fact]
    public async Task CollectLinksAsync_SearchThatFitsOnePage_IsLoadedOnceWhenTheCountIsCovered()
    {
        string search = WalkSites.CarGurus.BuildSearchUrls(Query("Honda", "Insight"))[0];
        List<int> loads = [];
        string text = Fixture("cargurus-insight-search.txt");
        List<PageLink> page = Cards("cargurus-insight-search-cards.json");

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.CarGurus,
            search,
            WalkPairSearches.UnboundedPool,
            NoneKnown,
            (_, pageNumber, _) =>
            {
                loads.Add(pageNumber);
                return Task.FromResult(new SearchPageContent(page, text, PagingHtmlToken));
            },
            (_, _) => { },
            _ => { },
            () => { },
            CancellationToken.None);

        Assert.Equal([1], loads);
        Assert.Equal(21, pool.Count);
    }

    [Fact]
    public void Find_CarGurus_ReturnsTheSite()
    {
        Assert.Same(WalkSites.CarGurus, WalkSites.Find("CarGurus"));
        Assert.Equal("cargurus", WalkSites.CarGurus.Name);
        Assert.True(WalkSites.CarGurus.AskingPriceFromCard);
    }

    [Fact]
    public void ResolveDealer_PageNamingADealer_KeepsItAndItsLocation()
    {
        Assert.Equal(new ResolvedDealer("Holler Driver's Mart Sanford", "Sanford, FL", IsFallback: false), WalkSites.CarGurus.ResolveDealer("Holler Driver's Mart Sanford", "Sanford, FL"));
    }

    [Fact]
    public void ResolveDealer_PageNamingNone_HasNoFallbackDealer()
    {
        Assert.Equal(new ResolvedDealer(null, "Sanford, FL", IsFallback: false), WalkSites.CarGurus.ResolveDealer(null, "Sanford, FL"));
    }
}
