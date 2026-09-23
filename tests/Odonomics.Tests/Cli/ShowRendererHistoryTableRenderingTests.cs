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
    public void BuildGroupedHistoryTable_MileageRangeWithTwoFiveDigitReadings_RendersBothInFull()
    {
        // A group merged by dealer-name stem across a real mileage change (a relisting) must not have
        // its mileage range's high end ellipsized away into a misleadingly smaller number.
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> points =
        [
            new("Dealer A", day1, day1, 19394m, 40000),
            new("Dealer A", day2, day2, 27995m, 69680),
        ];

        IReadOnlyList<SellerGroupSummary> groups = RedFlagsEvaluator.GroupBySeller(points);
        Assert.Single(groups);

        string[] lines = Render(groups);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.Contains(lines, line => line.Contains("40,000-69,680"));
    }

    [Fact]
    public void BuildGroupedHistoryTable_MileageRangeWithSixDigitHighEnd_RendersBothInFull()
    {
        // A dealer-name-stem-merged group spanning the 100k-mile mark (the README's own documented
        // case: "even when the mileage moved between them") must not have its six-digit high end
        // ellipsized away, the same defect shape as the five-digit case above, just one digit up.
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> points =
        [
            new("Dealer A", day1, day1, 19394m, 95000),
            new("Dealer A", day2, day2, 27995m, 150000),
        ];

        IReadOnlyList<SellerGroupSummary> groups = RedFlagsEvaluator.GroupBySeller(points);
        Assert.Single(groups);

        string[] lines = Render(groups);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.Contains(lines, line => line.Contains("95,000-150,000"));
    }

    [Fact]
    public void BuildGroupedHistoryTable_MileageRangeWithTwoSixDigitReadings_RendersBothInFullAndStillWrapsTheDealerCell()
    {
        string original = Environment.GetEnvironmentVariable("NO_COLOR") ?? "";
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", "1");

            DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset day2 = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
            const string longName = "Mercedes-Benz Of South Orlando Certified Pre-Owned";
            List<VinHistoryPoint> points =
            [
                new(longName, day1, day1, 19394m, 105000),
                new(longName, day2, day2, 27995m, 150000),
            ];

            IReadOnlyList<SellerGroupSummary> groups = RedFlagsEvaluator.GroupBySeller(points);
            Assert.Single(groups);

            string[] lines = Render(groups);
            string output = string.Join('\n', lines);

            Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
            Assert.Contains(lines, line => line.Contains("105,000-150,000"));
            Assert.DoesNotContain('…', output);
            Assert.Contains(longName, Unwrap(lines));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", original.Length == 0 ? null : original);
        }
    }

    [Fact]
    public void BuildGroupedHistoryTable_PriceRangeWithSixDigitHighEnd_RendersBothInFull()
    {
        // The same defect shape as the mileage six-digit case above, one field over: a price range
        // reaching six figures must not have its high end ellipsized away by Format.Truncate.
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> points =
        [
            new("Dealer A", day1, day1, 95000m, 40000),
            new("Dealer A", day2, day2, 150000m, 40000),
        ];

        IReadOnlyList<SellerGroupSummary> groups = RedFlagsEvaluator.GroupBySeller(points);
        Assert.Single(groups);

        string[] lines = Render(groups);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.Contains(lines, line => line.Contains("$95,000-$150,000"));
    }

    [Fact]
    public void BuildGroupedHistoryTable_PriceRangeWithTwoSixFigurePrices_RendersBothInFull()
    {
        // The widest price range the column is sized for: two six-figure prices, "$105,000-$150,000".
        DateTimeOffset day1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset day2 = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> points =
        [
            new("Dealer A", day1, day1, 105000m, 40000),
            new("Dealer A", day2, day2, 150000m, 40000),
        ];

        IReadOnlyList<SellerGroupSummary> groups = RedFlagsEvaluator.GroupBySeller(points);
        Assert.Single(groups);

        string[] lines = Render(groups);

        Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
        Assert.Contains(lines, line => line.Contains("$105,000-$150,000"));
    }

    [Fact]
    public void BuildGroupedHistoryTable_ManyRooftopGroup_LeadsTheWrappedDealerCellWithTheSellerCount()
    {
        // The dealer cell for a multi-seller group must lead with the seller count, so the one fact
        // the grouping exists to surface sits at the front of the cell even when the full
        // "name1, name2, name3 and N more" list wraps onto several lines.
        string[] rooftops =
        [
            "Carrollton Hyundai", "Alm Hyundai Florence", "ALM Chevrolet South", "Alm Kia Perry",
            "ALM Mazda Macon", "Alm Hyundai Athens", "ALM Mazda South", "Alm Cdjr Macon",
            "ALM Ford Marietta", "Alm Chrysler Dodge Jeep Ram Perry", "Alm Kia South", "Alm Nissan Newnan",
            "Genesis of Macon", "Alm Hyundai West", "Five Star Hyundai of Macon", "Five Star Hyundai of Warner Robins",
        ];
        DateTimeOffset windowStart = new(2026, 1, 24, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset windowEnd = new(2026, 3, 5, 0, 0, 0, TimeSpan.Zero);
        List<VinHistoryPoint> points = [.. rooftops
            .Select(name => new VinHistoryPoint(name, windowStart, windowEnd, 27995m, 36005))];

        IReadOnlyList<SellerGroupSummary> groups = RedFlagsEvaluator.GroupBySeller(points);
        Assert.Single(groups);

        string[] lines = Render(groups);

        Assert.Contains(lines, line => line.Contains("16 sellers"));
    }

    [Fact]
    public void BuildGroupedHistoryTable_ThirtySixSellerGroupAndThreeSellerGroupWithLongNames_NeverTruncatesTheDealerCell()
    {
        string original = Environment.GetEnvironmentVariable("NO_COLOR") ?? "";
        try
        {
            Environment.SetEnvironmentVariable("NO_COLOR", "1");

            DateTimeOffset start = new(2025, 10, 16, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset end = new(2026, 1, 5, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset laterStart = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset laterEnd = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
            List<VinHistoryPoint> points =
            [
                .. Enumerable.Range(1, 36).Select(i => new VinHistoryPoint(
                    i == 1 ? "Mercedes-Benz Of South Orlando" : $"Mercedes-Benz Of Rooftop {i}", start, end, 22489m, 85960)),
                new("Kahlig Auto Group", laterStart, laterEnd, 21990m, 86663),
                new("Kahlig Auto Group Hyundai", laterStart, laterEnd, 21990m, 86663),
                new("Kahlig Auto Group Kia", laterStart, laterEnd, 21990m, 86663),
            ];

            IReadOnlyList<SellerGroupSummary> groups = RedFlagsEvaluator.GroupBySeller(points);
            Assert.Equal(2, groups.Count);

            string[] lines = Render(groups);
            string output = string.Join('\n', lines);

            Assert.All(lines, line => Assert.True(line.Length <= 80, $"line exceeded 80 columns ({line.Length}): \"{line}\""));
            Assert.DoesNotContain('…', output);
            Assert.DoesNotContain('\u001b', output);
            Assert.Contains("36 sellers", output);
            Assert.Contains("and 33 more", output);
            Assert.Contains("3 sellers", output);
            Assert.Contains("Mercedes-Benz Of South Orlando", Unwrap(lines));
            Assert.Contains("Kahlig Auto Group", Unwrap(lines));
            Assert.Contains("$22,489", output);
            Assert.Contains("85,960", output);
            Assert.Contains("$21,990", output);
            Assert.Contains("86,663", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", original.Length == 0 ? null : original);
        }
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

    /// <summary>The dealer column's text with its wrapped continuation lines joined back into one
    /// string, so an assertion can look for a whole name that wrapping split across two lines.</summary>
    private static string Unwrap(string[] lines) =>
        string.Join(' ', lines.Select(line => line.Split('│')[0].Trim()).Where(cell => cell.Length > 0));

    private static string[] Render(IReadOnlyList<SellerGroupSummary> groups)
    {
        var console = new TestConsole();
        console.Profile.Width = 80;
        console.Profile.Capabilities.Ansi = false;

        console.Write(ShowRenderer.BuildGroupedHistoryTable(groups));

        return console.Output.Replace("\r\n", "\n").Split('\n');
    }
}
