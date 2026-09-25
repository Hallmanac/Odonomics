using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class WalkDetailWalkTests
{
    private static List<string> Links(int count) => [.. Enumerable.Range(1, count).Select(i => $"https://example.com/detail/{i}")];

    [Fact]
    public async Task RunAsync_EveryLinkMatches_StopsAtTheCap()
    {
        List<string> links = Links(10);
        var visitedLinks = new List<string>();

        DetailWalkTally tally = await WalkDetailWalk.RunAsync(
            links,
            maxDetailPages: 3,
            (link, _, _) =>
            {
                visitedLinks.Add(link);
                return Task.FromResult(DetailPageOutcome.Upserted);
            },
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(3, tally.Visited);
        Assert.Equal(3, tally.Upserted);
        Assert.Equal(links[..3], visitedLinks);
    }

    [Fact]
    public async Task RunAsync_NonMatchingPagesDoNotCountAgainstTheCap()
    {
        // The first five links are some other model mixed into the search results (the exact
        // shape of the bug this fixes: cars.com/carvana returning gas trims on a hybrid walk),
        // the sixth through eighth are the real matches. A cap of 3 must still be met by real
        // matches, skipping past every non-matching link along the way rather than stopping once
        // 3 links total have been visited.
        List<string> links = Links(10);

        DetailWalkTally tally = await WalkDetailWalk.RunAsync(
            links,
            maxDetailPages: 3,
            (link, i, _) => Task.FromResult(i < 5 ? DetailPageOutcome.NotMatching : DetailPageOutcome.Upserted),
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(8, tally.Visited);
        Assert.Equal(3, tally.Upserted);
    }

    [Fact]
    public async Task RunAsync_PoolExhaustedBeforeCapMet_StopsWithFewerThanTheCap()
    {
        List<string> links = Links(4);

        DetailWalkTally tally = await WalkDetailWalk.RunAsync(
            links,
            maxDetailPages: 10,
            (_, _, _) => Task.FromResult(DetailPageOutcome.NotMatching),
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(4, tally.Visited);
        Assert.Equal(0, tally.Upserted);
    }

    [Fact]
    public async Task RunAsync_NoVinDrop_CountsAgainstTheCapButNotAsUpserted()
    {
        List<string> links = Links(3);

        DetailWalkTally tally = await WalkDetailWalk.RunAsync(
            links,
            maxDetailPages: 3,
            (_, _, _) => Task.FromResult(DetailPageOutcome.NoVin),
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(3, tally.Visited);
        Assert.Equal(0, tally.Upserted);
        Assert.Equal(3, tally.Dropped.NoVin);
        Assert.Equal(3, tally.Dropped.Total);
    }

    [Fact]
    public async Task RunAsync_RepeatVinDoesNotCountAgainstTheCap()
    {
        // The first three links resolve to a VIN this pair already saved (a search page linking
        // the same listing under two URLs, or two dealers cross-listing the same car); the fourth
        // through sixth are real, distinct matches. A cap of 3 must still be met by the distinct
        // matches, skipping past every repeat along the way rather than stopping once 3 links total
        // have been visited.
        List<string> links = Links(10);

        DetailWalkTally tally = await WalkDetailWalk.RunAsync(
            links,
            maxDetailPages: 3,
            (link, i, _) => Task.FromResult(i < 3 ? DetailPageOutcome.Repeat : DetailPageOutcome.Upserted),
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(6, tally.Visited);
        Assert.Equal(3, tally.Upserted);
        Assert.Equal(3, tally.Dropped.Repeat);
        Assert.Equal(tally.Visited, tally.Upserted + tally.Dropped.Total);
    }

    [Fact]
    public async Task RunAsync_EveryOutcomeKind_TalliesEachIntoItsOwnDroppedReason()
    {
        List<string> links = Links(8);
        DetailPageOutcome[] outcomes =
        [
            DetailPageOutcome.NewCar,
            DetailPageOutcome.MissingFields,
            DetailPageOutcome.NoVin,
            DetailPageOutcome.NotMatching,
            DetailPageOutcome.Failed,
            DetailPageOutcome.ExtractionFailed,
            DetailPageOutcome.Repeat,
            DetailPageOutcome.Upserted,
        ];

        DetailWalkTally tally = await WalkDetailWalk.RunAsync(
            links,
            maxDetailPages: 8,
            (_, i, _) => Task.FromResult(outcomes[i]),
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(8, tally.Visited);
        Assert.Equal(1, tally.Upserted);
        Assert.Equal(1, tally.Dropped.NewCar);
        Assert.Equal(1, tally.Dropped.MissingFields);
        Assert.Equal(1, tally.Dropped.NoVin);
        Assert.Equal(1, tally.Dropped.NotMatching);
        Assert.Equal(1, tally.Dropped.Failed);
        Assert.Equal(1, tally.Dropped.ExtractionFailed);
        Assert.Equal(1, tally.Dropped.Repeat);
        Assert.Equal(7, tally.Dropped.Total);
        Assert.Equal(tally.Visited, tally.Upserted + tally.Dropped.Total);
    }

    [Fact]
    public async Task RunAsync_NewCarPages_NeverSpendTheCap()
    {
        List<string> links = Links(8);

        DetailWalkTally tally = await WalkDetailWalk.RunAsync(
            links,
            maxDetailPages: 3,
            (_, i, _) => Task.FromResult(i < 5 ? DetailPageOutcome.NewCar : DetailPageOutcome.Upserted),
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(8, tally.Visited);
        Assert.Equal(3, tally.Upserted);
        Assert.Equal(5, tally.Dropped.NewCar);
        Assert.Equal(tally.Visited, tally.Upserted + tally.Dropped.Total);
    }

    [Fact]
    public async Task RunAsync_GapDelegate_RunsBetweenLinksButNotBeforeTheFirst()
    {
        List<string> links = Links(3);
        int gapCalls = 0;

        await WalkDetailWalk.RunAsync(
            links,
            maxDetailPages: 3,
            (_, _, _) => Task.FromResult(DetailPageOutcome.Upserted),
            _ =>
            {
                gapCalls++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(2, gapCalls);
    }
}
