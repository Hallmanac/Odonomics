using Odonomics.Cli;
using Odonomics.Domain;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Odonomics.Tests.Cli;

[Collection(NoColorEnvironmentCollection.Name)]
public class ShowRendererHistoryTableRenderingTests
{
    [Fact]
    public void BuildGroupedHistoryTable_FiftyRowSyndicatedHistory_RendersUnderTenLinesWithinEightyColumns()
    {
        // 50 rows across 16 real ALM-group and affiliated rooftops (see the recorded
        // 4T1DAACK7SU000408 history), all overlapping the same window at the same mileage: the
        // grouped table collapses this to its one real seller group, so it stays readable instead
        // of scrolling 50 raw rows.
        string[] rooftops =
        [
            "Carrollton Hyundai", "Alm Hyundai Florence", "ALM Chevrolet South", "Alm Kia Perry",
            "ALM Mazda Macon", "Alm Hyundai Athens", "ALM Mazda South", "Alm Cdjr Macon",
            "ALM Ford Marietta", "Alm Chrysler Dodge Jeep Ram Perry", "Alm Kia South", "Alm Nissan Newnan",
            "Genesis of Macon", "Alm Hyundai West", "Five Star Hyundai of Macon", "Five Star Hyundai of Warner Robins",
        ];
        DateTimeOffset windowStart = new(2026, 1, 24, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset windowEnd = new(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> points = [.. Enumerable.Range(0, 50)
            .Select(i => new VinHistoryPoint(rooftops[i % rooftops.Length], windowStart, windowEnd, 27995m, 36005))];

        IReadOnlyList<SellerGroupSummary> groups = RedFlagsEvaluator.GroupBySeller(points);
        Assert.Single(groups);

        string[] lines = Render(groups);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        string[] contentLines = [.. lines.Where(line => !string.IsNullOrWhiteSpace(line))];
        Assert.True(contentLines.Length < 10, $"grouped history table ran to {contentLines.Length} lines, expected under 10");
    }

    [Fact]
    public void BuildGroupedHistoryTable_WithNoColorSet_HasNoAnsiCodes()
    {
        List<VinHistoryPoint> points =
        [
            new("Dealer A", new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 20000m, 40000),
            new("Dealer B", new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero), 21000m, 45000),
        ];

        IReadOnlyList<SellerGroupSummary> groups = RedFlagsEvaluator.GroupBySeller(points);
        Assert.Equal(2, groups.Count);

        string[] lines = Render(groups);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.DoesNotContain(lines, line => line.Contains('\u001b'));
    }

    private static string[] Render(IReadOnlyList<SellerGroupSummary> groups)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Profile.Capabilities.Ansi = false;

        console.Write(ShowRenderer.BuildGroupedHistoryTable(groups));

        return console.Output.Replace("\r\n", "\n").Split('\n');
    }
}
