using System.Text.Json;
using System.Text.RegularExpressions;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the search-page pull hands back, for each detail link, the text of that link's own
/// result card. The page is a fake: a small tree of elements standing in for the DOM, answering the two
/// scripts the pull runs the way the browser does (see <see cref="SearchPageLinks.CardScript"/>), so
/// nothing here opens a browser.</summary>
public class SearchPageLinksTests
{
    private sealed class FakeElement(string tag, string ownText, string? href, params FakeElement[] children)
    {
        public string Tag { get; } = tag;
        public string? Href { get; } = href;
        public FakeElement? Parent { get; private set; }
        public IReadOnlyList<FakeElement> Children { get; } = children;

        public string Text => string.Join("\n", new[] { ownText }.Concat(Children.Select(c => c.Text)).Where(t => t.Length > 0));

        public FakeElement Adopt()
        {
            foreach (FakeElement child in Children)
            {
                child.Parent = this;
                child.Adopt();
            }

            return this;
        }

        public IEnumerable<FakeElement> Anchors() =>
            (Href is null ? [] : new[] { this }).Concat(Children.SelectMany(c => c.Anchors()));
    }

    private static FakeElement Div(string ownText, params FakeElement[] children) => new("div", ownText, null, children);

    private static FakeElement Card(params FakeElement[] children) => new("card", "", null, children);

    private static FakeElement Anchor(string href, string text = "", params FakeElement[] children) => new("a", text, href, children);

    private sealed class FakePage(FakeElement body)
    {
        private readonly List<FakeElement> _anchors = [.. body.Adopt().Anchors()];

        public Task<string[][]> EvaluateAsync(string script, object? arg)
        {
            if (script == SearchPageLinks.AnchorScript)
            {
                return Task.FromResult(_anchors.Select(a => new[] { a.Href!, a.Text }).ToArray());
            }

            Assert.Equal(SearchPageLinks.CardScript, script);
            JsonElement options = JsonSerializer.SerializeToElement(arg);
            var hasAmount = new Regex(options.GetProperty("amount").GetString()!);
            string? container = options.GetProperty("container").GetString();
            string[][] rows =
            [
                .. options.GetProperty("indices").EnumerateArray().Select(i =>
                {
                    FakeElement anchor = _anchors[i.GetInt32()];
                    FakeElement? card = container is not null
                        ? Ancestors(anchor).FirstOrDefault(e => e.Tag == container)
                        : Ancestors(anchor).FirstOrDefault(e => hasAmount.IsMatch(e.Text));
                    return new[] { anchor.Href!, card?.Text ?? "" };
                })
            ];
            return Task.FromResult(rows);
        }

        private IEnumerable<FakeElement> Ancestors(FakeElement anchor)
        {
            for (FakeElement? element = anchor; element is not null && element != body; element = element.Parent)
            {
                yield return element;
            }
        }
    }

    private const string CarsComCard1 = "$21,202";
    private const string CarsComCard2 = "$22,075";

    private static FakeElement CarsComResults() =>
        Div("Used cars for sale",
            Card(
                Div("", Anchor("https://www.cars.com/vehicledetail/aaa/?sid=1")),
                Div(CarsComCard1 + "\n$349\n48,278 mi.\nEst. $385/mo", Anchor("https://www.cars.com/vehicledetail/aaa/?sid=1", "Used 2023 Toyota Corolla Hybrid LE"))),
            Card(
                Div("", Anchor("https://www.cars.com/vehicledetail/bbb/?sid=1")),
                Div(CarsComCard2 + "\n45,129 mi.\nEst. $401/mo", Anchor("https://www.cars.com/vehicledetail/bbb/?sid=1", "Used 2021 Toyota Corolla Hybrid SE"))),
            Div("Filters", Anchor("https://www.cars.com/shopping/results/?page=2", "Next page")));

    [Fact]
    public async Task ReadAsync_GivesEachDetailLinkTheTextOfTheCardItSitsIn()
    {
        var page = new FakePage(CarsComResults());

        IReadOnlyList<PageLink> links = await SearchPageLinks.ReadAsync(page.EvaluateAsync, WalkSites.CarsCom);

        PageLink second = links.Single(l => l.Href.Contains("/bbb/") && l.Text.StartsWith("Used"));
        Assert.Equal("$22,075\n45,129 mi.\nEst. $401/mo\nUsed 2021 Toyota Corolla Hybrid SE", second.CardText);
        PageLink first = links.Single(l => l.Href.Contains("/aaa/") && l.Text.StartsWith("Used"));
        Assert.StartsWith("$21,202\n$349\n48,278 mi.", first.CardText);
        Assert.DoesNotContain("$22,075", first.CardText);
    }

    [Fact]
    public async Task ReadAsync_AnAnchorWithNoTextOfItsOwnStillGetsItsCard()
    {
        var page = new FakePage(CarsComResults());

        IReadOnlyList<PageLink> links = await SearchPageLinks.ReadAsync(page.EvaluateAsync, WalkSites.CarsCom);

        PageLink photoLink = links.First(l => l.Href.Contains("/aaa/"));
        Assert.Equal("", photoLink.Text);
        Assert.Contains("$21,202", photoLink.CardText);
    }

    [Fact]
    public async Task ReadAsync_AnAnchorThatWrapsTheWholeCardIsItsOwnCard()
    {
        var page = new FakePage(Div("",
            Anchor("https://www.carvana.com/vehicle/1", "", Div("2024 Toyota Corolla Hybrid\nCurrent price:\n$24,590")),
            Anchor("https://www.carvana.com/vehicle/2", "", Div("2020 Toyota Corolla Hybrid\nCurrent price:\n$22,590"))));

        IReadOnlyList<PageLink> links = await SearchPageLinks.ReadAsync(page.EvaluateAsync, WalkSites.Carvana);

        Assert.Equal(["$24,590", "$22,590"], links.Select(l => Regex.Match(l.CardText, @"\$[\d,]+").Value));
        Assert.DoesNotContain("$22,590", links[0].CardText);
    }

    [Fact]
    public async Task ReadAsync_AnAnchorThatIsNotADetailLinkGetsNoCard()
    {
        var page = new FakePage(CarsComResults());

        IReadOnlyList<PageLink> links = await SearchPageLinks.ReadAsync(page.EvaluateAsync, WalkSites.CarsCom);

        PageLink next = links.Single(l => l.Text == "Next page");
        Assert.Equal("", next.CardText);
    }

    [Fact]
    public async Task ReadAsync_ADetailLinkWithNoDollarAmountAnywhereAboveItGetsNoCard()
    {
        var page = new FakePage(Div("Autotrader", Card(Div("Used 2022 Toyota Prius\n296K mi", Anchor("https://www.autotrader.com/cars-for-sale/vehicle/1", "2022 Toyota Prius")))));

        IReadOnlyList<PageLink> links = await SearchPageLinks.ReadAsync(page.EvaluateAsync, WalkSites.Autotrader);

        Assert.Equal("", Assert.Single(links).CardText);
    }

    // Cut from walks/autotrader/20260927-162013/insight/search.txt (lines 37-105) and cards.json: three
    // real listing cards, each linked by a title anchor (clickType=listing) and a "No Accidents"
    // purchaseConfidence anchor, and the "Consider Buying New" recommendation card that follows them.
    // Every real card's own dollar-ancestor test used to fail here, since its price is bare digits
    // ("19,394") with no dollar sign; only the recommendation card's "$265" ever matched.
    private static string AutotraderInsightHref(string id, string clickType) =>
        $"https://www.autotrader.com/cars-for-sale/vehicle/{id}?listingType=USED&makeCode=HONDA&maxMileage=100000&modelCode=INSIGHT&sortBy=relevance&startYear=2019&zip=32833&clickType={clickType}";

    private static string AutotraderInsightPurchaseConfidenceHref(string id) =>
        $"https://www.autotrader.com/cars-for-sale/vehicle/{id}?listingType=USED&makeCode=HONDA&maxMileage=100000&modelCode=INSIGHT&sortBy=relevance&startYear=2019&zip=32833#purchaseConfidence";

    private static FakeElement AutotraderInsightListingCard(string id, string title, string restOfTitleBlock, string priceBlock, string dealerBlock) =>
        Card(
            Div("Used"),
            Anchor(AutotraderInsightHref(id, "listing"), title),
            Div(restOfTitleBlock + "\n" + priceBlock),
            Anchor(AutotraderInsightPurchaseConfidenceHref(id), "No Accidents"),
            Div(dealerBlock));

    private static FakeElement AutotraderInsightResults() =>
        Div("3 Matches",
            AutotraderInsightListingCard(
                "790827297", "2021 Honda Insight", "EX\n69K mi\n Hybrid",
                "19,394\nSee payment\nGreat Price\nDealer Fees Included",
                "Driver's Mart Sanford\n25.69 mi. away\n(407) 663-0156\nCheck Availability"),
            AutotraderInsightListingCard(
                "790295486", "2021 Honda Insight", "Touring\n32K mi\n Hybrid",
                "25,286\nSee payment\nGreat Price\nDealer Fees Included",
                "Dealer\n38.24 mi. away\nGet seller's phone number\nCheck Availability"),
            AutotraderInsightListingCard(
                "792107365", "2019 Honda Insight", "Touring\n49K mi\n Hybrid",
                "19,201\nSee payment\nGreat Price\nDealer Fees Included",
                "Dealer\n40.08 mi. away\nGet seller's phone number\nCheck Availability"),
            Card(
                Div("Consider Buying New"),
                Anchor(AutotraderInsightHref("791238424", "ncb"), "2026 Honda Civic"),
                Div("Sport\n8 mi\n$265\n/mo.\nSee details\n30,232")));

    [Fact]
    public async Task ReadAsync_RecordedAutotraderInsightSearchPage_EveryRealListingCardHasItsOwnNonEmptyText()
    {
        var page = new FakePage(AutotraderInsightResults());

        IReadOnlyList<PageLink> links = await SearchPageLinks.ReadAsync(page.EvaluateAsync, WalkSites.Autotrader);

        PageLink[] realListingCards = [.. links.Where(l => l.Href.Contains("clickType=listing"))];
        Assert.Equal(3, realListingCards.Length);
        Assert.All(realListingCards, l => Assert.NotEmpty(l.CardText));
        PageLink first = realListingCards.Single(l => l.Href.Contains("790827297"));
        Assert.Contains("19,394", first.CardText);
        Assert.Contains("Great Price", first.CardText);
        Assert.DoesNotContain("25,286", first.CardText);
    }

    [Fact]
    public async Task ReadAsync_RecordedAutotraderInsightSearchPage_TheRecommendationCardsTextNeverMakesItACandidate()
    {
        var page = new FakePage(AutotraderInsightResults());

        IReadOnlyList<PageLink> links = await SearchPageLinks.ReadAsync(page.EvaluateAsync, WalkSites.Autotrader);
        IReadOnlyList<PageLink> candidates = WalkSites.Autotrader.CollectDetailCards(links, poolSize: 60, "3 Matches");

        PageLink recommendation = links.Single(l => l.Text == "2026 Honda Civic");
        Assert.NotEmpty(recommendation.CardText);
        Assert.DoesNotContain(candidates, c => c.Href == recommendation.Href);
        Assert.Equal(3, candidates.Count);
    }

    // Cut from walks/autotrader/20260926-121057/prius/search.txt (lines 373-400): a real listing card
    // with no price ("Contact Dealer For Price" / "See payment", no digits and no dollar sign anywhere
    // near it) sitting between two priced cards. A marker that required digits before "See payment"
    // never matched this card's own text, so the ancestor climb passed it by and landed on a container
    // holding all three cards, handing the no-price card a neighbor's price and badges.
    private static FakeElement AutotraderPriusNoPriceResults() =>
        Div("6 Matches",
            AutotraderInsightListingCard(
                "1", "2024 Toyota Prius", "LE\n44K mi\n Hybrid",
                "26,093\nSee payment\nGood Price\nDealer Fees Included",
                "Seminole Toyota\n24.79 mi. away\n(407) 853-2979\nCheck Availability"),
            AutotraderInsightListingCard(
                "2", "2022 Toyota Prius", "Limited\n296K mi\n Hybrid",
                "Contact Dealer For Price\nSee payment",
                "Seminole Toyota\n24.79 mi. away\n(407) 853-2979\nCheck Availability"),
            AutotraderInsightListingCard(
                "3", "2019 Toyota Prius", "LE\n24K mi\n Hybrid",
                "22,643\nSee payment\nGreat Price\nDealer Fees Included",
                "Harbor City Auto Sales Inc.\n38.28 mi. away\n(321) 373-8610\nCheck Availability"));

    [Fact]
    public async Task ReadAsync_ANoPriceCardBetweenPricedCardsGetsItsOwnCardAndNotANeighbors()
    {
        var page = new FakePage(AutotraderPriusNoPriceResults());

        IReadOnlyList<PageLink> links = await SearchPageLinks.ReadAsync(page.EvaluateAsync, WalkSites.Autotrader);

        PageLink noPriceCard = links.Single(l => l.Href.Contains("/vehicle/2?") && l.Href.Contains("clickType=listing"));
        Assert.Contains("Contact Dealer For Price", noPriceCard.CardText);
        Assert.DoesNotContain("26,093", noPriceCard.CardText);
        Assert.DoesNotContain("22,643", noPriceCard.CardText);
        Assert.Null(WalkSites.Autotrader.ReadCardPrice(noPriceCard.CardText));
    }

    [Fact]
    public async Task ReadAsync_ACardLongerThanOneCardCouldBeReadsAsNoCard()
    {
        var page = new FakePage(Div("", Anchor("https://www.carvana.com/vehicle/1", "", Div("Current price: $24,590 " + new string('x', SearchPageLinks.MaxCardTextLength)))));

        IReadOnlyList<PageLink> links = await SearchPageLinks.ReadAsync(page.EvaluateAsync, WalkSites.Carvana);

        Assert.Equal("", Assert.Single(links).CardText);
    }

    [Fact]
    public async Task ReadAsync_ASiteWithACardContainerSelectorUsesThatElementInsteadOfTheNearestPrice()
    {
        var site = WalkSites.CarsCom with { CardContainerSelector = "card" };
        var page = new FakePage(Div("",
            Card(
                Div("Dealer\nCheck Availability", Anchor("https://www.cars.com/vehicledetail/aaa/", "Used 2023 Toyota Corolla Hybrid LE")),
                Div("$21,202\n48,278 mi."))));

        IReadOnlyList<PageLink> links = await SearchPageLinks.ReadAsync(page.EvaluateAsync, site);

        Assert.Equal("Dealer\nCheck Availability\nUsed 2023 Toyota Corolla Hybrid LE\n$21,202\n48,278 mi.", Assert.Single(links).CardText);
    }

    [Fact]
    public async Task ReadAsync_APageWithNoDetailLinksAsksForNoCards()
    {
        var page = new FakePage(Div("", Anchor("https://www.cars.com/shopping/", "Shop")));
        int cardReads = 0;

        IReadOnlyList<PageLink> links = await SearchPageLinks.ReadAsync(
            (script, arg) =>
            {
                cardReads += script == SearchPageLinks.CardScript ? 1 : 0;
                return page.EvaluateAsync(script, arg);
            },
            WalkSites.CarsCom);

        Assert.Equal(0, cardReads);
        Assert.Single(links);
    }

    [Fact]
    public async Task CardsJson_ListsEachDetailLinkWithItsTextAndCardInPageOrder_AndLeavesOtherAnchorsOut()
    {
        var page = new FakePage(CarsComResults());
        IReadOnlyList<PageLink> links = await SearchPageLinks.ReadAsync(page.EvaluateAsync, WalkSites.CarsCom);

        using JsonDocument json = JsonDocument.Parse(SearchPageLinks.CardsJson(WalkSites.CarsCom, links));

        JsonElement[] entries = [.. json.RootElement.EnumerateArray()];
        Assert.Equal(4, entries.Length);
        Assert.Equal("https://www.cars.com/vehicledetail/aaa/?sid=1", entries[0].GetProperty("href").GetString());
        Assert.Equal("Used 2023 Toyota Corolla Hybrid LE", entries[1].GetProperty("text").GetString());
        Assert.StartsWith("$21,202", entries[1].GetProperty("card").GetString());
        Assert.DoesNotContain(entries, e => e.GetProperty("text").GetString() == "Next page");

    }

    [Fact]
    public void CardsJson_KeepsAmpersandsAndEllipsesReadable()
    {
        string text = SearchPageLinks.CardsJson(WalkSites.CarsCom, [new PageLink("https://www.cars.com/vehicledetail/x/?a=1&sid=2", "Used 2023 Toyota…", "$21,202")]);

        Assert.Contains("a=1&sid=2", text);
        Assert.Contains("Toyota…", text);
    }

    [Fact]
    public void CardsFileName_SitsBesideTheSearchFileOfTheSamePage()
    {
        Assert.Equal("cards.json", WalkPairSearches.CardsFileName(0, 1, 1));
        Assert.Equal("cards-2.json", WalkPairSearches.CardsFileName(0, 2, 1));
        Assert.Equal("cards-2-page-3.json", WalkPairSearches.CardsFileName(1, 3, 2));
        Assert.Equal("search-2-page-3.txt", WalkPairSearches.SearchFileName(1, 3, 2));
    }

    [Fact]
    public void UnrenderedFileName_SitsBesideTheCardsFileOfTheSamePage()
    {
        Assert.Equal("unrendered.json", WalkPairSearches.UnrenderedFileName(0, 1, 1));
        Assert.Equal("unrendered-2.json", WalkPairSearches.UnrenderedFileName(0, 2, 1));
        Assert.Equal("unrendered-2-page-3.json", WalkPairSearches.UnrenderedFileName(1, 3, 2));
    }

    [Fact]
    public void CollectDetailCards_KeepsTheFirstLinkPerListingWithTheFirstCardTextAnyOfItsLinksHas()
    {
        PageLink[] links =
        [
            new("https://www.cars.com/vehicledetail/aaa/?sid=1", "", ""),
            new("https://www.cars.com/vehicledetail/aaa/?sid=1", "Used 2023 Toyota Corolla Hybrid LE", "$21,202 card"),
            new("https://www.cars.com/vehicledetail/bbb/?sid=1", "Used 2021 Toyota Corolla Hybrid SE", "$22,075 card"),
            new("https://www.cars.com/vehicledetail/ccc/?sid=1", "New 2027 Toyota Corolla Hybrid LE", "$28,313 card"),
        ];

        IReadOnlyList<PageLink> cards = WalkSites.CarsCom.CollectDetailCards(links, int.MaxValue);

        Assert.Equal(["$21,202 card", "$22,075 card"], cards.Select(c => c.CardText));
        Assert.Equal(WalkSites.CarsCom.CollectDetailLinks(links, int.MaxValue), cards.Select(c => c.Href));
    }
}
