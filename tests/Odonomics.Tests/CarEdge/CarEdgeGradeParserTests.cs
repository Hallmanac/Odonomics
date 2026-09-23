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
