using Odonomics.CarEdge;

namespace Odonomics.Tests.CarEdge;

/// <summary>Proves the parser against recorded CarEdge `/dealers?q=` page text
/// (tests/Odonomics.Tests/fixtures/caredge/), with no live network in the test run. The graded
/// fixtures are copied from notes/caredge-fixtures/ in the project home, live pages captured
/// 2026-09-23; the 404 fixture is copied from a walk recording taken the same day against the
/// retired `/dealer-reviews?search=` URL.</summary>
public class CarEdgeGradeParserTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "caredge", fileName);

    private static async Task<string> ReadFixtureAsync(string fileName) =>
        await File.ReadAllTextAsync(FixturePath(fileName));

    [Fact]
    public async Task Parse_GradedWithNoAddOns_ReturnsFullCardAndIgnoresTheFooterGradeLink()
    {
        string pageText = await ReadFixtureAsync("dealers-q-daytona-toyota.txt");

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Daytona Toyota", null);

        Assert.Equal(CarEdgeGradeStatus.Graded, result.Status);
        // The page also carries a "Grade:" filter row and a "Top-Rated (Grade A) Dealers" footer
        // link; the dealer's own grade is B, proving the parser reads the card, not either of those.
        Assert.Equal("B", result.Grade);
        Assert.Equal(88, result.Score);
        Assert.Equal(11, result.VerifiedQuoteCount);
        Assert.Equal("$1,199", result.DocFee);
        Assert.Equal("No add-ons", result.AddOnsNote);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task Parse_GradedWithDollarAddOns_ReturnsAddOnsAmount()
    {
        string pageText = await ReadFixtureAsync("dealers-q-seminole-toyota.txt");

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Seminole Toyota", null);

        Assert.Equal(CarEdgeGradeStatus.Graded, result.Status);
        Assert.Equal("A", result.Grade);
        Assert.Equal(93, result.Score);
        Assert.Equal(9, result.VerifiedQuoteCount);
        Assert.Equal("$999", result.DocFee);
        Assert.Equal("$358 add-ons", result.AddOnsNote);
    }

    [Fact]
    public async Task Parse_SingleResultCardMatchesDealerButNotRated_ReturnsNotFound()
    {
        string pageText = await ReadFixtureAsync("dealers-q-carmax-sanford.txt");

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "CarMax Sanford", null);

        Assert.Equal(CarEdgeGradeStatus.NotFound, result.Status);
        Assert.Null(result.Grade);
    }

    [Fact]
    public async Task Parse_MultiResultPageNoCardNamesTheSearchedDealer_ReturnsNotFound()
    {
        string pageText = await ReadFixtureAsync("dealers-q-holler-drivers-mart-sanford.txt");

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Driver's Mart", null);

        Assert.Equal(CarEdgeGradeStatus.NotFound, result.Status);
        Assert.Null(result.Grade);
    }

    [Fact]
    public void Parse_MultiResultPageFirstCardIsGradedButNamesADifferentDealer_ReturnsNotFound()
    {
        // Every recorded results page echoes the search query back as `Search: "<name> <location>"`
        // above the results themselves, which always contains the searched dealer's own name. This
        // pins that the searched dealer's name check is scoped to the card bodies, not that echo, so
        // a fuzzy first result graded for some other dealer is never mistaken for the searched one.
        // The declared count matches the one card the page actually carries, so the "no card named
        // this dealer" fallthrough is trustworthy here rather than a partial parse.
        const string pageText = """
            Search: "Holler Hyundai Winter Park, FL"
            1 dealers found
            Sort:
            Highest ScoreLowest ScoreMost QuotesLowest Doc FeeHighest Doc FeeLowest MarkupHighest Markup
            Graded Motors
            Orlando, FL · 5 verified quotes
            $500
            doc fee
            No add-ons
            D
            62/100
            Below average
            See 5 verified quotes →
            """;

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Hyundai", null);

        Assert.Equal(CarEdgeGradeStatus.NotFound, result.Status);
        Assert.Null(result.Grade);
    }

    [Fact]
    public void Parse_FirstCardNameIsTheSearchedDealerNamePrefixExtended_SkipsToTheDealersOwnCard()
    {
        // A dealer whose name is a prefix of a differently named dealer's card must not have that
        // other card's grade attributed to it: "Toyota of Orlando South" contains "Toyota of
        // Orlando" as a boundary-clean substring once newlines normalize to spaces, so a plain
        // containment check on the card block wrongly accepts the first card here. The searched
        // dealer's own card, second and graded C, is the one that must win.
        const string pageText = """
            Search: "Toyota of Orlando"
            2 dealers found
            Sort:
            Highest ScoreLowest ScoreMost QuotesLowest Doc FeeHighest Doc FeeLowest MarkupHighest Markup
            Toyota of Orlando South
            Orlando, FL · 20 verified quotes
            $700
            doc fee
            No add-ons
            A
            95/100
            Highly transparent
            See 20 verified quotes →
            Toyota of Orlando
            Orlando, FL · 6 verified quotes
            $900
            doc fee
            No add-ons
            C
            70/100
            Below average
            See 6 verified quotes →
            """;

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Toyota of Orlando", null);

        Assert.Equal(CarEdgeGradeStatus.Graded, result.Status);
        Assert.Equal("C", result.Grade);
        Assert.Equal(70, result.Score);
    }

    [Fact]
    public void Parse_SoleCardNameExtendsTheSearchedDealerNameWithNoExactCardOnThePage_FallsBackToThatCard()
    {
        // CarEdge sometimes renders a dealer's full franchised name longer than the ledger's stem
        // ("Schaller Honda" in the ledger vs. "Schaller Honda Subaru Mitsubishi" on the card). When
        // no card on the page is an exact match, the boundary-anchored containment fallback must
        // still accept this card rather than falling through to a permanent "not on CarEdge".
        const string pageText = """
            Search: "Schaller Honda"
            1 dealers found
            Sort:
            Highest ScoreLowest ScoreMost QuotesLowest Doc FeeHighest Doc FeeLowest MarkupHighest Markup
            Schaller Honda Subaru Mitsubishi
            Orlando, FL · 12 verified quotes
            $600
            doc fee
            No add-ons
            B
            88/100
            Above average
            See 12 verified quotes →
            """;

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Schaller Honda", null);

        Assert.Equal(CarEdgeGradeStatus.Graded, result.Status);
        Assert.Equal("B", result.Grade);
        Assert.Equal(88, result.Score);
    }

    [Fact]
    public void Parse_DeclaredCountExceedsCardsThatParsed_ReturnsUnrecognized()
    {
        // The page claims two dealers but only one card matches a known card layout: the searched
        // dealer's own card may be a render variant the regexes don't cover, so this must come back
        // Unrecognized and be retried rather than a permanent "not on CarEdge" for a dealer CarEdge
        // may well have graded.
        const string pageText = """
            Search: "Holler Hyundai Winter Park, FL"
            2 dealers found
            Sort:
            Highest ScoreLowest ScoreMost QuotesLowest Doc FeeHighest Doc FeeLowest MarkupHighest Markup
            Graded Motors
            Orlando, FL · 5 verified quotes
            $500
            doc fee
            No add-ons
            D
            62/100
            Below average
            See 5 verified quotes →
            """;

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Hyundai", null);

        Assert.Equal(CarEdgeGradeStatus.Unrecognized, result.Status);
        Assert.Null(result.Grade);
    }

    [Fact]
    public void Parse_DealerNamedAfterAMakeInTheFilterChrome_DoesNotMatchTheFirstCard()
    {
        // The results page always carries a "All MakesAcura...Tesla...Volvo" filter row between the
        // results-found line and the first card. A dealer whose own name is a bare make (a listing
        // feed emits single-token names like "Tesla") must not match that chrome and be handed the
        // first card's grade.
        const string pageText = """
            Search: "Tesla Orlando, FL"
            1 dealers found
            Grade:
            AllABCDFCertified
            All MakesAcuraAlfa RomeoAudiBMWTeslaToyotaVolvo
            Sort:
            Highest ScoreLowest ScoreMost QuotesLowest Doc FeeHighest Doc FeeLowest MarkupHighest Markup
            Graded Motors
            Orlando, FL · 5 verified quotes
            $500
            doc fee
            No add-ons
            D
            62/100
            Below average
            See 5 verified quotes →
            """;

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Tesla", null);

        Assert.Equal(CarEdgeGradeStatus.NotFound, result.Status);
        Assert.Null(result.Grade);
    }

    [Fact]
    public void Parse_ExactNameMatchIsInADifferentCityThanTheSearchedDealer_ReturnsLocationMismatchRatherThanTheOtherStoresGrade()
    {
        // CarEdge's own search can return a same-named store in a city the ledger dealer was never
        // seen in (a chain, or a match CarEdge's own fuzzy search considered close enough). A card
        // whose name is an exact match must still be rejected when the dealer's own known location
        // disagrees with that card's "City, ST" line, rather than permanently attributing another
        // store's grade to this dealer.
        const string pageText = """
            Search: "Holler Honda Sanford, FL"
            1 dealers found
            Sort:
            Highest ScoreLowest ScoreMost QuotesLowest Doc FeeHighest Doc FeeLowest MarkupHighest Markup
            Holler Honda
            Columbia, SC · 8 verified quotes
            $600
            doc fee
            No add-ons
            A
            91/100
            Highly transparent
            See 8 verified quotes →
            """;

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Honda", "Sanford, FL");

        Assert.Equal(CarEdgeGradeStatus.LocationMismatch, result.Status);
        Assert.Null(result.Grade);
    }

    [Fact]
    public void Parse_ExactNameMatchIsInTheSameCityAsTheSearchedDealer_ReturnsThatCardsGrade()
    {
        // The counterpart to the cross-city rejection above: a card whose name and location both
        // agree with the searched dealer is accepted exactly as a name-only match always was.
        const string pageText = """
            Search: "Holler Honda Sanford, FL"
            1 dealers found
            Sort:
            Highest ScoreLowest ScoreMost QuotesLowest Doc FeeHighest Doc FeeLowest MarkupHighest Markup
            Holler Honda
            Sanford, FL · 8 verified quotes
            $600
            doc fee
            No add-ons
            A
            91/100
            Highly transparent
            See 8 verified quotes →
            """;

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Honda", "Sanford, FL");

        Assert.Equal(CarEdgeGradeStatus.Graded, result.Status);
        Assert.Equal("A", result.Grade);
        Assert.Equal(91, result.Score);
    }

    [Fact]
    public void Parse_DealerLocationIsCityOnly_StillMatchesACardCarryingCityAndState()
    {
        // An upstream source can report only a dealer's city (DealerLocationFormat.Build returns a
        // bare city when the API record has no state), while CarEdge's own card always renders
        // "City, ST". The city-only location must still match its own card rather than being
        // permanently rejected for lacking the state half the card always carries.
        const string pageText = """
            Search: "Holler Honda Sanford"
            1 dealers found
            Sort:
            Highest ScoreLowest ScoreMost QuotesLowest Doc FeeHighest Doc FeeLowest MarkupHighest Markup
            Holler Honda
            Sanford, FL · 8 verified quotes
            $600
            doc fee
            No add-ons
            A
            91/100
            Highly transparent
            See 8 verified quotes →
            """;

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Honda", "Sanford");

        Assert.Equal(CarEdgeGradeStatus.Graded, result.Status);
        Assert.Equal("A", result.Grade);
    }

    [Fact]
    public void Parse_DealerLocationIsStateOnly_StillMatchesACardCarryingCityAndState()
    {
        const string pageText = """
            Search: "Holler Honda FL"
            1 dealers found
            Sort:
            Highest ScoreLowest ScoreMost QuotesLowest Doc FeeHighest Doc FeeLowest MarkupHighest Markup
            Holler Honda
            Sanford, FL · 8 verified quotes
            $600
            doc fee
            No add-ons
            A
            91/100
            Highly transparent
            See 8 verified quotes →
            """;

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Honda", "FL");

        Assert.Equal(CarEdgeGradeStatus.Graded, result.Status);
        Assert.Equal("A", result.Grade);
    }

    [Fact]
    public void Parse_DealerLocationIsCityOnlyAndDisagreesWithTheCard_ReturnsLocationMismatch()
    {
        // The partial-location match must still reject a genuinely different city: a bare city
        // that isn't a whole-word match against the card's "City, ST" line is not this dealer.
        const string pageText = """
            Search: "Holler Honda Orlando"
            1 dealers found
            Sort:
            Highest ScoreLowest ScoreMost QuotesLowest Doc FeeHighest Doc FeeLowest MarkupHighest Markup
            Holler Honda
            Sanford, FL · 8 verified quotes
            $600
            doc fee
            No add-ons
            A
            91/100
            Highly transparent
            See 8 verified quotes →
            """;

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Honda", "Orlando");

        Assert.Equal(CarEdgeGradeStatus.LocationMismatch, result.Status);
        Assert.Null(result.Grade);
    }

    private static string CardText(string name, string location, string grade, int score) => $"""
        {name}
        {location} · 8 verified quotes
        $600
        doc fee
        No add-ons
        {grade}
        {score}/100
        Highly transparent
        See 8 verified quotes →
        """;

    private static string PageText(int count, params string[] cards) => $"""
        Search: "query"
        {count} dealers found
        Sort:
        Highest ScoreLowest ScoreMost QuotesLowest Doc FeeHighest Doc FeeLowest MarkupHighest Markup
        {string.Join("\n", cards)}
        """;

    [Theory]
    [InlineData("Winter Park, FL 32792", "Winter Park, FL")]
    [InlineData("Winter Park, FL 32792-1234", "Winter Park, FL")]
    [InlineData("Winter Park FL", "Winter Park, FL")]
    [InlineData("Ft. Lauderdale, FL", "Fort Lauderdale, FL")]
    [InlineData("Fort Lauderdale, FL", "Ft. Lauderdale, FL")]
    [InlineData("St. Petersburg, FL", "Saint Petersburg, FL")]
    [InlineData("Mt. Pleasant, SC", "Mount Pleasant, SC")]
    [InlineData("winter park, fl", "Winter Park, FL")]
    [InlineData("FL", "Orlando, FL")]
    [InlineData("MT", "Billings, MT")]
    [InlineData("Orlando", "Orlando, FL")]
    [InlineData("Orlando, FL", "Orlando, FL")]
    [InlineData("", "Orlando, FL")]
    [InlineData(null, "Orlando, FL")]
    public void Parse_DealerLocationAgreesWithTheCardComponentByComponent_ReturnsThatCardsGrade(string? dealerLocation, string cardLocation)
    {
        string pageText = PageText(1, CardText("Holler Honda", cardLocation, "A", 91));

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Honda", dealerLocation);

        Assert.Equal(CarEdgeGradeStatus.Graded, result.Status);
        Assert.Equal("A", result.Grade);
    }

    [Theory]
    [InlineData("MT", "Mt. Pleasant, SC")]
    [InlineData("Palm Beach", "West Palm Beach, FL")]
    [InlineData("York", "New York, NY")]
    [InlineData("Winter Park, FL", "Winter Park, CO")]
    [InlineData("Orlando, FL", "Orlando, KY")]
    [InlineData("GA", "Orlando, FL")]
    public void Parse_DealerLocationDisagreesWithTheCardInAnyComponent_ReturnsLocationMismatch(string dealerLocation, string cardLocation)
    {
        string pageText = PageText(1, CardText("Holler Honda", cardLocation, "A", 91));

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Honda", dealerLocation);

        Assert.Equal(CarEdgeGradeStatus.LocationMismatch, result.Status);
        Assert.Null(result.Grade);
    }

    [Fact]
    public void Parse_NoCardNamesTheDealerAtAll_StillReturnsNotFoundRatherThanLocationMismatch()
    {
        string pageText = PageText(1, CardText("Some Other Dealer", "Orlando, FL", "A", 91));

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Honda", "Sanford, FL");

        Assert.Equal(CarEdgeGradeStatus.NotFound, result.Status);
    }

    [Fact]
    public void Parse_StateOnlyDealerLocationMatchesTwoExactNameCardsInThatState_ReturnsAmbiguous()
    {
        string pageText = PageText(
            2,
            CardText("Holler Honda", "Sanford, FL", "A", 91),
            CardText("Holler Honda", "Tampa, FL", "C", 60));

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Honda", "FL");

        Assert.Equal(CarEdgeGradeStatus.Ambiguous, result.Status);
        Assert.Null(result.Grade);
    }

    [Fact]
    public void Parse_CityOnlyDealerLocationMatchesTwoExactNameCardsInThatCity_ReturnsAmbiguous()
    {
        string pageText = PageText(
            2,
            CardText("Holler Honda", "Springfield, IL", "A", 91),
            CardText("Holler Honda", "Springfield, MO", "C", 60));

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Honda", "Springfield");

        Assert.Equal(CarEdgeGradeStatus.Ambiguous, result.Status);
    }

    [Fact]
    public void Parse_DealerWithNoLocationMatchesSeveralStoresOfAChainByContainedName_ReturnsAmbiguousRatherThanTheFirstCard()
    {
        string pageText = PageText(
            2,
            CardText("Carvana Orlando", "Orlando, FL", "F", 20),
            CardText("Carvana Atlanta", "Atlanta, GA", "A", 95));

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Carvana", null);

        Assert.Equal(CarEdgeGradeStatus.Ambiguous, result.Status);
        Assert.Null(result.Grade);
    }

    [Fact]
    public void Parse_StateOnlyDealerLocationAndOnlyOneExactNameCardIsInThatState_ReturnsThatCard()
    {
        string pageText = PageText(
            2,
            CardText("Holler Honda", "Columbia, SC", "C", 60),
            CardText("Holler Honda", "Sanford, FL", "A", 91));

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Honda", "FL");

        Assert.Equal(CarEdgeGradeStatus.Graded, result.Status);
        Assert.Equal("A", result.Grade);
    }

    [Fact]
    public void Parse_FullDealerLocationPicksItsOwnCardAmongSeveralSameNamedCards()
    {
        string pageText = PageText(
            2,
            CardText("Holler Honda", "Tampa, FL", "C", 60),
            CardText("Holler Honda", "Sanford, FL", "A", 91));

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Honda", "Sanford, FL 32771");

        Assert.Equal(CarEdgeGradeStatus.Graded, result.Status);
        Assert.Equal("A", result.Grade);
    }

    [Fact]
    public async Task Parse_CarEdgeOwn404Page_ReturnsSearchUrlInvalid()
    {
        string pageText = await ReadFixtureAsync("search-url-invalid-404.txt");

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Any Dealer", null);

        Assert.Equal(CarEdgeGradeStatus.CarEdgeSearchUrlInvalid, result.Status);
        Assert.Null(result.Grade);
    }

    [Fact]
    public void Parse_PageMatchesNoKnownLayoutAtAll_ReturnsUnrecognized()
    {
        CarEdgeGradeResult result = CarEdgeGradeParser.Parse("Just a moment... checking your browser.", "Holler Honda", null);

        Assert.Equal(CarEdgeGradeStatus.Unrecognized, result.Status);
    }
}
