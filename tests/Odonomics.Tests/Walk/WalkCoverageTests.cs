using Odonomics.Ledger;
using Odonomics.Tests.Ledger;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class WalkCoverageTests
{
    private static readonly WalkSite SiteA = new("site-a", _ => [""], new System.Text.RegularExpressions.Regex(".*"));
    private static readonly WalkSite SiteB = new("site-b", _ => [""], new System.Text.RegularExpressions.Regex(".*"));

    private static RunEntity Run(DateTimeOffset startedAt) => new() { Command = "walk", Sources = "", StartedAt = startedAt };

    [Fact]
    public async Task RunAsync_EveryPairSucceeds_StampsEveryPairsCoverageToken()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        List<WalkPairSummary> summaries = await WalkCoverage.RunAsync(
            run,
            [SiteA, SiteB],
            ["Honda Insight", "Toyota Prius"],
            (_, _, _) => Task.FromResult(new WalkPairOutcome(1, 1, new DroppedBreakdown(0, 0, 0, 0))),
            (_, _) => { },
            (_, _, _) => { },
            _ => Task.CompletedTask,
            ct => db.SaveChangesAsync(ct),
            CancellationToken.None);

        Assert.Equal(4, summaries.Count);
        Assert.All(summaries, s => Assert.True(s.Completed));
        Assert.Equal(
            "site-a:Insight,site-a:Prius,site-b:Insight,site-b:Prius",
            run.Sources);
    }

    [Fact]
    public async Task RunAsync_CarriesEachPairsKnownFromCardsCountIntoItsSummary()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        List<WalkPairSummary> summaries = await WalkCoverage.RunAsync(
            run,
            [SiteA],
            ["Honda Insight", "Toyota Prius"],
            (_, model, _) => Task.FromResult(new WalkPairOutcome(1, 1, new DroppedBreakdown(0, 0, 0, 0), KnownFromCards: model == "Honda Insight" ? 12 : 0)),
            (_, _) => { },
            (_, _, _) => { },
            _ => Task.CompletedTask,
            ct => db.SaveChangesAsync(ct),
            CancellationToken.None);

        Assert.Equal([12, 0], summaries.Select(s => s.KnownFromCards));
    }

    [Fact]
    public async Task RunAsync_InterruptedMidway_LeavesOnlyCompletedPairsStampedInTheDatabase()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        int callCount = 0;

        await Assert.ThrowsAsync<OperationCanceledException>(() => WalkCoverage.RunAsync(
            run,
            [SiteA, SiteB],
            ["Honda Insight", "Toyota Prius"],
            (_, _, ct) =>
            {
                callCount++;
                if (callCount == 3)
                {
                    throw new OperationCanceledException(ct);
                }

                return Task.FromResult(new WalkPairOutcome(1, 1, new DroppedBreakdown(0, 0, 0, 0)));
            },
            (_, _) => { },
            (_, _, _) => { },
            _ => Task.CompletedTask,
            ct => db.SaveChangesAsync(ct),
            CancellationToken.None));

        Assert.Equal("site-a:Insight,site-a:Prius", run.Sources);

        // Reload from a fresh context, the same as another process reading the ledger after this
        // one was interrupted, to prove the stamp actually made it to the database and not just
        // to the in-memory RunEntity.
        using OdonomicsDbContext verifyDb = testDb.CreateContext();
        RunEntity persisted = await verifyDb.Runs.FindAsync(run.Id) ?? throw new InvalidOperationException("run not found");
        Assert.Equal("site-a:Insight,site-a:Prius", persisted.Sources);
    }

    [Fact]
    public async Task RunAsync_OnePairFails_ReportsItAndContinuesWithTheRest()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        var failures = new List<(string Site, string Model, string Message)>();

        List<WalkPairSummary> summaries = await WalkCoverage.RunAsync(
            run,
            [SiteA],
            ["Honda Insight", "Toyota Prius"],
            (site, model, _) => model == "Honda Insight"
                ? throw new InvalidOperationException("the page never loaded")
                : Task.FromResult(new WalkPairOutcome(2, 1, new DroppedBreakdown(0, 1, 0, 0))),
            (_, _) => { },
            (site, model, ex) => failures.Add((site.Name, model, ex.Message)),
            _ => Task.CompletedTask,
            ct => db.SaveChangesAsync(ct),
            CancellationToken.None);

        Assert.Equal(2, summaries.Count);
        Assert.False(summaries[0].Completed);
        Assert.True(summaries[1].Completed);
        Assert.Equal("site-a:Prius", run.Sources);

        (string site, string model, string message) = Assert.Single(failures);
        Assert.Equal("site-a", site);
        Assert.Equal("Honda Insight", model);
        Assert.Equal("the page never loaded", message);
    }

    [Fact]
    public async Task RunAsync_PersistFailsForAPair_DoesNotLeaveThatPairsTokenOnTheRunAfterward()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        int saveCount = 0;

        List<WalkPairSummary> summaries = await WalkCoverage.RunAsync(
            run,
            [SiteA],
            ["Honda Insight", "Toyota Prius"],
            (_, _, _) => Task.FromResult(new WalkPairOutcome(1, 1, new DroppedBreakdown(0, 0, 0, 0))),
            (_, _) => { },
            (_, _, _) => { },
            _ => Task.CompletedTask,
            ct =>
            {
                saveCount++;
                if (saveCount == 1)
                {
                    throw new InvalidOperationException("database is locked");
                }

                return db.SaveChangesAsync(ct);
            },
            CancellationToken.None);

        Assert.False(summaries[0].Completed);
        Assert.True(summaries[1].Completed);

        // The first pair's save failed, so its token must not linger in memory to be swept up by
        // the second pair's successful save: the run's Sources should show only what actually
        // persisted.
        Assert.Equal("site-a:Prius", run.Sources);
    }

    [Fact]
    public async Task RunAsync_ACappedPair_StampsAPartialTokenBesideItsCoverageTokenAndSaysCappedInItsSummary()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        List<WalkPairSummary> summaries = await WalkCoverage.RunAsync(
            run,
            [SiteA],
            ["Honda Insight", "Toyota Prius"],
            (_, model, _) => Task.FromResult(new WalkPairOutcome(1, 1, new DroppedBreakdown(0, 0, 0, 0), Capped: model == "Honda Insight")),
            (_, _) => { },
            (_, _, _) => { },
            _ => Task.CompletedTask,
            ct => db.SaveChangesAsync(ct),
            CancellationToken.None);

        Assert.Equal([true, false], summaries.Select(s => s.Capped));
        Assert.Equal("site-a:Insight,capped:site-a:Insight,site-a:Prius", run.Sources);
        Assert.Equal(["site-a:Insight", "site-a:Prius"], RunSources.Split(run));
        Assert.Equal(["site-a:Insight"], RunSources.PartialCoverage(run));
    }

    [Fact]
    public async Task RunAsync_PersistFailsForACappedPair_LeavesNeitherItsCoverageNorItsPartialToken()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        RunEntity run = Run(DateTimeOffset.UtcNow);
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);

        int saveCount = 0;

        await WalkCoverage.RunAsync(
            run,
            [SiteA],
            ["Honda Insight", "Toyota Prius"],
            (_, _, _) => Task.FromResult(new WalkPairOutcome(1, 1, new DroppedBreakdown(0, 0, 0, 0), Capped: true)),
            (_, _) => { },
            (_, _, _) => { },
            _ => Task.CompletedTask,
            ct =>
            {
                saveCount++;
                return saveCount == 1
                    ? throw new InvalidOperationException("database is locked")
                    : db.SaveChangesAsync(ct);
            },
            CancellationToken.None);

        Assert.Equal("site-a:Prius,capped:site-a:Prius", run.Sources);
    }
}
