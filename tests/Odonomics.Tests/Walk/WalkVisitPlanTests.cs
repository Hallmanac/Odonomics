using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the console lines that let an operator judge a walk's length: the run's opening
/// lines, and the line before each pair's first detail page, in the order they reach the console.</summary>
public class WalkVisitPlanTests
{
    private static readonly WalkVisitPlan.PairKnown[] Pairs =
    [
        new("cars.com", "Toyota Camry Hybrid", 42),
        new("carvana", "Toyota Camry Hybrid", 0),
    ];

    [Fact]
    public void RunStartLines_WithNoCap_SaysSoAndListsEachPairsKnownPostings()
    {
        IReadOnlyList<string> lines = WalkVisitPlan.RunStartLines(Pairs, maxDetailPages: null, revisit: false);

        Assert.Equal(3, lines.Count);
        Assert.Contains("2 pair(s)", lines[0]);
        Assert.Contains("no per-pair cap", lines[0]);
        Assert.Contains("about 1 minute(s)", lines[0]);
        Assert.Contains("kept current from their search cards", lines[0]);
        Assert.Equal("  cars.com / Toyota Camry Hybrid: the ledger holds 42 posting(s)", lines[1]);
        Assert.Equal("  carvana / Toyota Camry Hybrid: the ledger holds 0 posting(s)", lines[2]);
    }

    [Fact]
    public void RunStartLines_WithACap_NamesIt()
    {
        IReadOnlyList<string> lines = WalkVisitPlan.RunStartLines(Pairs, maxDetailPages: 30, revisit: false);

        Assert.Contains("at most 30 matching detail page(s) per pair", lines[0]);
        Assert.DoesNotContain("no per-pair cap", lines[0]);
    }

    [Fact]
    public void RunStartLines_WithRevisit_SaysTheLedgersPostingsAreVisitedAgain()
    {
        IReadOnlyList<string> lines = WalkVisitPlan.RunStartLines(Pairs, maxDetailPages: null, revisit: true);

        Assert.Contains("visited again (--revisit)", lines[0]);
    }

    [Fact]
    public void PairLine_GivesTheCountAndAboutAMinutePerPage()
    {
        Assert.Equal(
            "carvana / Toyota Camry Hybrid: about to visit 68 detail page(s), roughly 68 minute(s)",
            WalkVisitPlan.PairLine("carvana", "Toyota", "Camry Hybrid", 68, startingWith: false));
    }

    [Fact]
    public void PairLine_ForACappedPairWithLaterSearches_SaysItIsOnlyTheStart()
    {
        string line = WalkVisitPlan.PairLine("cars.com", "Toyota", "Camry Hybrid", 15, startingWith: true);

        Assert.Contains("15 detail page(s) to start with", line);
        Assert.Contains("later searches", line);
    }

    [Fact]
    public void PairLine_WithNothingToVisit_SaysNothingNewToOpen()
    {
        Assert.Equal(
            "carvana / Toyota Camry Hybrid: about to visit 0 detail pages, nothing new to open",
            WalkVisitPlan.PairLine("carvana", "Toyota", "Camry Hybrid", 0, startingWith: false));
    }

    [Fact]
    public async Task ThePairLineReachesTheConsoleBeforeTheFirstDetailPageAndTheSummaryLineAfterTheLast()
    {
        List<string> console = [];
        List<string> anchors = [.. Enumerable.Range(0, 4).Select(i => $"https://www.carvana.com/vehicle/{i}")];

        DetailWalkTally tally = await WalkPairSearches.RunAsync(
            WalkSites.Carvana,
            ["https://www.carvana.com/cars/filters?zip=32114"],
            maxDetailPages: null,
            (_, _, _, _) => Task.FromResult<IReadOnlyList<string>>(anchors),
            (link, i, _) =>
            {
                console.Add($"detail {i + 1}");
                return Task.FromResult(DetailPageOutcome.Upserted);
            },
            _ => Task.CompletedTask,
            CancellationToken.None,
            (pages, startingWith) => console.Add(WalkVisitPlan.PairLine("carvana", "Toyota", "Camry Hybrid", pages, startingWith)));
        console.Add(WalkPairSummaryLine.Format("carvana", "Toyota", "Camry Hybrid", tally.Visited, 3, tally.Upserted, tally.Dropped));

        Assert.Equal(
            [
                "carvana / Toyota Camry Hybrid: about to visit 4 detail page(s), roughly 4 minute(s)",
                "detail 1",
                "detail 2",
                "detail 3",
                "detail 4",
                "carvana / Toyota Camry Hybrid: 4 pages, 3 known from cards, 4 saved, 0 dropped",
            ],
            console);
    }
}
