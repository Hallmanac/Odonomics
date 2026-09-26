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

    [Fact]
    public void Split_LeavesOutThePartialCoverageMarkers()
    {
        RunEntity run = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "carvana:Prius,capped:carvana:Prius,cars.com:Prius");

        Assert.Equal(["carvana:Prius", "cars.com:Prius"], RunSources.Split(run));
        Assert.Equal(["carvana:Prius"], RunSources.PartialCoverage(run));
    }

    [Fact]
    public void Split_LeavesOutTheUnreadCoverageMarkersAndUnreadCoverageNamesThem()
    {
        RunEntity run = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), $"carvana:Prius,{RunSources.UnreadKey("carvana:Prius")},cars.com:Prius,{RunSources.PartialKey("cars.com:Prius")}");

        Assert.Equal(["carvana:Prius", "cars.com:Prius"], RunSources.Split(run));
        Assert.Equal(["carvana:Prius"], RunSources.UnreadCoverage(run));
        Assert.Equal(["cars.com:Prius"], RunSources.PartialCoverage(run));
        Assert.True(RunSources.IsPartial(run, "carvana:Prius"));
        Assert.True(RunSources.IsPartial(run, "cars.com:Prius"));
        Assert.False(RunSources.IsPartial(run, "carvana:Insight"));
    }

    [Fact]
    public void LatestCoverageBySource_APartialMarker_IsNotACoverageEntryOfItsOwn()
    {
        RunEntity run = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), $"carvana:Prius,{RunSources.PartialKey("carvana:Prius")}");

        Dictionary<string, DateTimeOffset> latest = RunSources.LatestCoverageBySource([run]);

        Assert.Equal(["carvana:Prius"], latest.Keys);
    }

    [Fact]
    public void LatestCoverageBySource_ACappedRun_DoesNotSupersedeTheFullCoverageBeforeIt()
    {
        string prius = RunSources.Key("cars.com", "Prius");
        RunEntity full = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), prius);
        RunEntity capped = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), $"{prius},{RunSources.PartialKey(prius)}");

        Dictionary<string, DateTimeOffset> latest = RunSources.LatestCoverageBySource([capped, full]);

        Assert.Equal(full.StartedAt, latest[prius]);
    }

    [Fact]
    public void LatestCoverageBySource_AnUnreadRun_DoesNotSupersedeTheFullCoverageBeforeIt()
    {
        string prius = RunSources.Key("carvana", "Prius");
        RunEntity full = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), prius);
        RunEntity unread = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), $"{prius},{RunSources.UnreadKey(prius)}");

        Dictionary<string, DateTimeOffset> latest = RunSources.LatestCoverageBySource([unread, full]);

        Assert.Equal(full.StartedAt, latest[prius]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LatestCoverageBySource_ACappedRunAndAnUnreadRunInEitherOrder_BothAreSteppedPastToTheFullCoverage(bool unreadIsNewer)
    {
        string prius = RunSources.Key("cars.com", "Prius");
        RunEntity full = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), prius);
        RunEntity older = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), $"{prius},{(unreadIsNewer ? RunSources.PartialKey(prius) : RunSources.UnreadKey(prius))}");
        RunEntity newer = Run(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero), $"{prius},{(unreadIsNewer ? RunSources.UnreadKey(prius) : RunSources.PartialKey(prius))}");

        List<RunEntity> chain = RunSources.CoverageChain(prius, [full, older, newer]);
        Dictionary<string, DateTimeOffset> latest = RunSources.LatestCoverageBySource([full, older, newer]);

        Assert.Equal([newer, older, full], chain);
        Assert.Equal(full.StartedAt, latest[prius]);
    }

    [Fact]
    public void LatestCoverageBySource_ACappedRunOverOnePair_LeavesTheRunsOtherPairAlone()
    {
        string prius = RunSources.Key("cars.com", "Prius");
        string insight = RunSources.Key("cars.com", "Insight");
        RunEntity earlier = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), $"{prius},{insight}");
        RunEntity later = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), $"{prius},{RunSources.PartialKey(prius)},{insight}");

        Dictionary<string, DateTimeOffset> latest = RunSources.LatestCoverageBySource([earlier, later]);

        Assert.Equal(earlier.StartedAt, latest[prius]);
        Assert.Equal(later.StartedAt, latest[insight]);
    }

    [Fact]
    public void LatestCoverageBySource_APairOnlyEverCoveredPartially_ReportsTheFirstPartialRun()
    {
        string prius = RunSources.Key("cars.com", "Prius");
        RunEntity first = Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), $"{prius},{RunSources.PartialKey(prius)}");
        RunEntity second = Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), $"{prius},{RunSources.PartialKey(prius)}");

        Dictionary<string, DateTimeOffset> latest = RunSources.LatestCoverageBySource([first, second]);

        Assert.Equal(first.StartedAt, latest[prius]);
    }

    [Fact]
    public void Key_RoundTripsThroughSplitKey()
    {
        string token = RunSources.Key("cars.com", "Insight");

        (string source, string model) = RunSources.SplitKey(token);

        Assert.Equal("cars.com:Insight", token);
        Assert.Equal("cars.com", source);
        Assert.Equal("Insight", model);
    }

    [Fact]
    public void Key_DifferentModelsOnTheSameSource_AreDifferentTokens()
    {
        string insightWalk = RunSources.Key("cars.com", "Insight");
        string priusWalk = RunSources.Key("cars.com", "Prius");

        Dictionary<string, DateTimeOffset> latest = RunSources.LatestCoverageBySource(
        [
            Run(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), insightWalk),
            Run(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), priusWalk),
        ]);

        Assert.Equal(2, latest.Count);
        Assert.NotEqual(latest[insightWalk], latest[priusWalk]);
    }
}
