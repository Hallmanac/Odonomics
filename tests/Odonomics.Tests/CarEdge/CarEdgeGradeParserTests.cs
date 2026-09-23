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

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Daytona Toyota");

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

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Seminole Toyota");

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

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "CarMax Sanford");

        Assert.Equal(CarEdgeGradeStatus.NotFound, result.Status);
        Assert.Null(result.Grade);
    }

    [Fact]
    public async Task Parse_MultiResultPageNoCardNamesTheSearchedDealer_ReturnsNotFound()
    {
        string pageText = await ReadFixtureAsync("dealers-q-holler-drivers-mart-sanford.txt");

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Driver's Mart");

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

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Hyundai");

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

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Toyota of Orlando");

        Assert.Equal(CarEdgeGradeStatus.Graded, result.Status);
        Assert.Equal("C", result.Grade);
        Assert.Equal(70, result.Score);
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

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Hyundai");

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

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Tesla");

        Assert.Equal(CarEdgeGradeStatus.NotFound, result.Status);
        Assert.Null(result.Grade);
    }

    [Fact]
    public async Task Parse_CarEdgeOwn404Page_ReturnsSearchUrlInvalid()
    {
        string pageText = await ReadFixtureAsync("search-url-invalid-404.txt");

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Any Dealer");

        Assert.Equal(CarEdgeGradeStatus.CarEdgeSearchUrlInvalid, result.Status);
        Assert.Null(result.Grade);
    }

    [Fact]
    public void Parse_PageMatchesNoKnownLayoutAtAll_ReturnsUnrecognized()
    {
        CarEdgeGradeResult result = CarEdgeGradeParser.Parse("Just a moment... checking your browser.", "Holler Honda");

        Assert.Equal(CarEdgeGradeStatus.Unrecognized, result.Status);
    }
}
