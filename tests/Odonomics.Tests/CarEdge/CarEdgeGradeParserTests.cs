using Odonomics.CarEdge;

namespace Odonomics.Tests.CarEdge;

/// <summary>Proves the parser against recorded CarEdge dealer-page text
/// (tests/Odonomics.Tests/fixtures/caredge/), with no live network in the test run.</summary>
public class CarEdgeGradeParserTests
{
    private static string FixturePath(string fileName) =>
        Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "caredge", fileName);

    private static async Task<string> ReadFixtureAsync(string fileName) =>
        await File.ReadAllTextAsync(FixturePath(fileName));

    [Fact]
    public async Task Parse_GradedAPlusNoReason_ReturnsGradeWithNoReason()
    {
        string pageText = await ReadFixtureAsync("graded-a-plus.txt");

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Holler Honda");

        Assert.Equal(CarEdgeGradeStatus.Graded, result.Status);
        Assert.Equal("A+", result.Grade);
        Assert.Null(result.Reason);
    }

    [Fact]
    public async Task Parse_GradedFWithReason_ReturnsGradeAndReason()
    {
        string pageText = await ReadFixtureAsync("graded-f-with-reason.txt");

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Bayview Motors");

        Assert.Equal(CarEdgeGradeStatus.Graded, result.Status);
        Assert.Equal("F", result.Grade);
        Assert.NotNull(result.Reason);
        Assert.Contains("bait-and-switch", result.Reason);
        Assert.DoesNotContain("Based on 58 verified transactions", result.Reason);
    }

    [Fact]
    public async Task Parse_DealerNotFound_ReturnsNotFoundWithNoGrade()
    {
        string pageText = await ReadFixtureAsync("not-found.txt");

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Ace Motors");

        Assert.Equal(CarEdgeGradeStatus.NotFound, result.Status);
        Assert.Null(result.Grade);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Parse_NoGradeLineOnThePage_ReturnsUnrecognized()
    {
        CarEdgeGradeResult result = CarEdgeGradeParser.Parse("Just a moment... checking your browser.", "Holler Honda");

        Assert.Equal(CarEdgeGradeStatus.Unrecognized, result.Status);
    }

    [Fact]
    public async Task Parse_GradeLineBelongsToADifferentDealer_ReturnsUnrecognized()
    {
        string pageText = await ReadFixtureAsync("graded-a-plus.txt");

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, "Bayview Motors");

        Assert.Equal(CarEdgeGradeStatus.Unrecognized, result.Status);
        Assert.Null(result.Grade);
    }
}
