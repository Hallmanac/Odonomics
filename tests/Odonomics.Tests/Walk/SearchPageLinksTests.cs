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
    public void CardsFileName_IsCardsJsonForTheFirstSearchPageThenNumberedLikeSearchTxt()
    {
        Assert.Equal("cards.json", WalkPairSearches.CardsFileName(0));
        Assert.Equal("cards-2.json", WalkPairSearches.CardsFileName(1));
        Assert.Equal("search.txt", WalkPairSearches.SearchFileName(0));
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
