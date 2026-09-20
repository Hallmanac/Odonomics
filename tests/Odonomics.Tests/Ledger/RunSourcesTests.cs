using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

public class RunSourcesTests
{
    private static RunEntity Run(DateTimeOffset startedAt, string sources) => new() { Command = "search", Sources = sources, StartedAt = startedAt };

    [Fact]
    public void LatestCoverageBySource_EachSourceGetsItsOwnMostRecentCoveringRun()
    {
        RunEntity search = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "auto.dev,marketcheck");
        RunEntity walk = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), "cars.com");

        Dictionary<string, DateTimeOffset> latest = RunSources.LatestCoverageBySource([search, walk]);

        Assert.Equal(search.StartedAt, latest["auto.dev"]);
        Assert.Equal(search.StartedAt, latest["marketcheck"]);
        Assert.Equal(walk.StartedAt, latest["cars.com"]);
        Assert.False(latest.ContainsKey("carvana"));
    }

    [Fact]
    public void LatestCoverageBySource_RunWithNoSources_ContributesNothing()
    {
        RunEntity partialSearch = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), sources: "");

        Dictionary<string, DateTimeOffset> latest = RunSources.LatestCoverageBySource([partialSearch]);

        Assert.Empty(latest);
    }

    [Fact]
    public void LatestCoverageBySource_TwoRunsOverTheSameSource_KeepsTheLaterOne()
    {
        RunEntity earlier = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "auto.dev");
        RunEntity later = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), "auto.dev");

        Dictionary<string, DateTimeOffset> latest = RunSources.LatestCoverageBySource([earlier, later]);

        Assert.Equal(later.StartedAt, latest["auto.dev"]);
    }
}
