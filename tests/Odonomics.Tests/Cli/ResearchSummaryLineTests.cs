using Odonomics.Cli;

namespace Odonomics.Tests.Cli;

public class ResearchSummaryLineTests
{
    [Fact]
    public void Format_NoFlags_PrintsNone()
    {
        string line = ResearchSummaryLine.Format(2020, "Honda", "Insight", "1HGCM82633A004352", [], ResearchSource.Fetched);

        Assert.Equal("F 2020 Honda Insight, 1HGCM82633A004352: none", line);
    }

    [Fact]
    public void Format_WithFlags_ListsShortTags()
    {
        string line = ResearchSummaryLine.Format(2020, "Honda", "Insight", "1HGCM82633A004352", ["mileage-drop", "5-sellers"], ResearchSource.Fetched);

        Assert.Equal("F 2020 Honda Insight, 1HGCM82633A004352: mileage-drop, 5-sellers", line);
    }

    [Theory]
    [InlineData(ResearchSource.Fetched, 'F')]
    [InlineData(ResearchSource.Cached, 'C')]
    [InlineData(ResearchSource.Unreachable, 'U')]
    public void Format_MarksEachSourceWithItsOwnLetter(ResearchSource source, char marker)
    {
        string line = ResearchSummaryLine.Format(2020, "Honda", "Insight", "1HGCM82633A004352", [], source);

        Assert.StartsWith($"{marker} ", line);
    }

    [Fact]
    public void Format_ManyTags_CollapsesOverflowIntoPlusN()
    {
        string[] tags = [.. Enumerable.Range(0, 20).Select(i => $"flag-{i}")];

        string line = ResearchSummaryLine.Format(2020, "Honda", "Insight", "1HGCM82633A004352", tags, ResearchSource.Fetched);

        Assert.True(line.Length <= ResearchSummaryLine.MaxLineWidth, $"line exceeded 80 columns ({line.Length}): \"{line}\"");
        Assert.Contains(", +", line);
        Assert.DoesNotContain("flag-19", line);
    }

    [Fact]
    public void Format_NeverExceedsEightyColumns()
    {
        string line = ResearchSummaryLine.Format(
            2020, "Honda", "Insight", "1HGCM82633A004352", ["no-remedy-recall", "mileage-drop", "34-sellers", "low-safety-rating", "price-spike"], ResearchSource.Cached);

        Assert.True(line.Length <= ResearchSummaryLine.MaxLineWidth, $"line exceeded 80 columns ({line.Length}): \"{line}\"");
    }
}
