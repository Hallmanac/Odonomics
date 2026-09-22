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
    public async Task RunAsync_EveryOutcomeKind_TalliesEachIntoItsOwnDroppedReason()
    {
        List<string> links = Links(5);
        DetailPageOutcome[] outcomes = [DetailPageOutcome.MissingFields, DetailPageOutcome.NoVin, DetailPageOutcome.NotMatching, DetailPageOutcome.Failed, DetailPageOutcome.Upserted];

        DetailWalkTally tally = await WalkDetailWalk.RunAsync(
            links,
            maxDetailPages: 5,
            (_, i, _) => Task.FromResult(outcomes[i]),
            _ => Task.CompletedTask,
            CancellationToken.None);

        Assert.Equal(5, tally.Visited);
        Assert.Equal(1, tally.Upserted);
        Assert.Equal(1, tally.Dropped.MissingFields);
        Assert.Equal(1, tally.Dropped.NoVin);
        Assert.Equal(1, tally.Dropped.NotMatching);
        Assert.Equal(1, tally.Dropped.Failed);
        Assert.Equal(4, tally.Dropped.Total);
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
