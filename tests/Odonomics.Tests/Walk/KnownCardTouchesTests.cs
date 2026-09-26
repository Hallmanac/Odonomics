using Microsoft.EntityFrameworkCore;
using Odonomics.Domain;
using Odonomics.Ledger;
using Odonomics.Tests.Ledger;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves a repeat walk visits only listings the ledger has never seen: a link the ledger holds
/// is touched from its search card (so a lower card price is a Price drop and a listing no card shows is
/// Gone, through the diff as it already works), never enters the pool of detail visits, and never spends
/// the cap, while --revisit sends every link to the pool as before. The search pages are a fake that
/// serves canned cards; the ledger is a real one.</summary>
public class KnownCardTouchesTests
{
    private const string Search = "https://www.carvana.com/cars/filters?zip=32833&cvnaid=abc";
    private static readonly DateTimeOffset FirstRunAt = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondRunAt = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);

    private static Scenario DaughterScenario { get; } = ScenarioLoader.Load(Path.Combine(TestPaths.RepoRoot, "scenarios", "daughter.json"));

    private static string CardUrl(int id) => $"https://www.carvana.com/vehicle/{id}";

    private static readonly IReadOnlyDictionary<string, string> NoBadges = new Dictionary<string, string>();

    private static PageLink Card(int id, decimal? price, string badgeLines = "") => new(
        CardUrl(id),
        "",
        badgeLines + (price is null ? "2024 Toyota Prius\nLE\n46k miles" : $"2024 Toyota Prius\nLE\n46k miles\nCurrent price:\n${price:N0}\n$440/mo\nestimated"));

    private static ListingCandidate Candidate(int id, decimal price) => new()
    {
        Vin = $"1HGCM82633A{id:D6}",
        Source = "carvana",
        Url = CardUrl(id),
        Year = 2020,
        Make = "Toyota",
        Model = "Prius",
        Trim = "LE",
        Price = price,
        Mileage = 40000,
        DealerName = "Carvana",
        DealerNameIsFallback = true,
        ShippingFee = 690m,
    };

    private static Task<SearchPageContent> Serve(Dictionary<int, List<PageLink>> pages, int pageNumber) =>
        Task.FromResult(new SearchPageContent(pages.GetValueOrDefault(pageNumber, [])));

    private static async Task<RunEntity> StartRunAsync(OdonomicsDbContext db, DateTimeOffset startedAt)
    {
        var run = new RunEntity { Command = "walk", Sources = "carvana:Prius", StartedAt = startedAt };
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
        return run;
    }

    private static async Task<(LedgerUpsertService Service, RunEntity SecondRun)> LedgerWithFirstRunAsync(OdonomicsDbContext db, params (int Id, decimal Price)[] postings)
    {
        var service = new LedgerUpsertService(db);
        RunEntity first = await StartRunAsync(db, FirstRunAt);
        foreach ((int id, decimal price) in postings)
        {
            await service.UpsertAsync(Candidate(id, price), first, CancellationToken.None);
        }

        return (service, await StartRunAsync(db, SecondRunAt));
    }

    private static async Task<IReadOnlyList<string>> CollectAsync(KnownCardTouches touches, Dictionary<int, List<PageLink>> pages, int poolSize, List<int>? loadedPages = null)
    {
        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.Carvana,
            Search,
            poolSize,
            touches.TryTouchAsync,
            (_, pageNumber, _) =>
            {
                loadedPages?.Add(pageNumber);
                return Serve(pages, pageNumber);
            },
            (_, _) => { },
            _ => { },
            () => { },
            CancellationToken.None);
        await touches.CommitAsync(CancellationToken.None);
        return pool;
    }

    [Fact]
    public async Task ARepeatWalk_TouchesKnownCardsAndHandsOnlyNewLinksToTheDetailPool()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m), (2, 19000m), (3, 20000m));
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: false, CancellationToken.None);

        IReadOnlyList<string> pool = await CollectAsync(touches, new() { [1] = [Card(1, 17000m), Card(2, 19000m), Card(3, 20000m), Card(4, 21000m), Card(5, 22000m)] }, poolSize: 60);

        Assert.Equal([CardUrl(4), CardUrl(5)], pool);
        Assert.Equal(3, touches.Count);
        Assert.All(await db.Postings.ToListAsync(), p => Assert.Equal(SecondRunAt, p.LastSeen));
    }

    [Fact]
    public async Task ARepeatWalk_ASeededPostingSeenAgainAtALowerCardPriceIsAPriceDropAndNotGone()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m), (2, 19000m));
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: false, CancellationToken.None);

        await CollectAsync(touches, new() { [1] = [Card(1, 17000m), Card(2, 19000m)] }, poolSize: 60);
        SearchDiff diff = await new LedgerDiffService(db).ComputeAsync(second, DaughterScenario, CancellationToken.None);

        PriceDropEntry drop = Assert.Single(diff.PriceDrops);
        Assert.Equal((CardUrl(1), 18000m, 17000m), (drop.Url, drop.PreviousPrice, drop.CurrentPrice));
        Assert.Empty(diff.Gone);
    }

    [Fact]
    public async Task ARepeatWalk_ASeededPostingAbsentFromTheCardsIsGone()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m), (2, 19000m));
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: false, CancellationToken.None);

        await CollectAsync(touches, new() { [1] = [Card(2, 19000m)] }, poolSize: 60);
        SearchDiff diff = await new LedgerDiffService(db).ComputeAsync(second, DaughterScenario, CancellationToken.None);

        GonePostingEntry gone = Assert.Single(diff.Gone);
        Assert.Equal(CardUrl(1), gone.Url);
        Assert.Empty(diff.PriceDrops);
    }

    [Fact]
    public async Task ARepeatWalk_AKnownCardWhosePriceCouldNotBeReadIsStillTouched()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m));
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: false, CancellationToken.None);

        await CollectAsync(touches, new() { [1] = [Card(1, null)] }, poolSize: 60);
        SearchDiff diff = await new LedgerDiffService(db).ComputeAsync(second, DaughterScenario, CancellationToken.None);

        Assert.Equal(1, touches.Count);
        Assert.Empty(diff.Gone);
        Assert.Empty(diff.PriceDrops);
        Assert.Single(db.PriceObservations);
    }

    [Fact]
    public async Task Revisit_SendsEveryLinkToTheDetailPoolAndTouchesNone()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m), (2, 19000m));
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: true, CancellationToken.None);

        IReadOnlyList<string> pool = await CollectAsync(touches, new() { [1] = [Card(1, 17000m), Card(2, 19000m), Card(3, 21000m)] }, poolSize: 60);

        Assert.Equal([CardUrl(1), CardUrl(2), CardUrl(3)], pool);
        Assert.Equal(0, touches.Count);
        Assert.All(await db.Postings.ToListAsync(), p => Assert.Equal(FirstRunAt, p.LastSeen));
    }

    [Fact]
    public async Task KnownLinksNeverSpendThePool_SoNewLinksBehindThemStillFillIt()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m), (2, 18000m), (3, 18000m), (4, 18000m));
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: false, CancellationToken.None);
        List<int> loadedPages = [];

        IReadOnlyList<string> pool = await CollectAsync(
            touches,
            new()
            {
                [1] = [Card(1, 18000m), Card(2, 18000m)],
                [2] = [Card(3, 18000m), Card(4, 18000m)],
                [3] = [Card(5, 20000m), Card(6, 20000m), Card(7, 20000m)],
            },
            poolSize: 2,
            loadedPages);

        Assert.Equal([1, 2, 3], loadedPages);
        Assert.Equal([CardUrl(5), CardUrl(6)], pool);
        Assert.Equal(4, touches.Count);
    }

    [Fact]
    public async Task APoolThatIsFull_StillTouchesTheKnownLinksOnTheSamePage()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (3, 18000m));
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: false, CancellationToken.None);

        IReadOnlyList<string> pool = await CollectAsync(touches, new() { [1] = [Card(1, 20000m), Card(2, 20000m), Card(3, 18000m)] }, poolSize: 1);

        Assert.Equal([CardUrl(1)], pool);
        Assert.Equal(1, touches.Count);
    }

    [Fact]
    public async Task TheStatedCountBoundsKnownAndNewLinksTogether()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m), (9, 18000m));
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: false, CancellationToken.None);
        var page = new SearchPageContent([Card(1, 18000m), Card(2, 20000m), Card(3, 20000m), Card(9, 18000m)], "3 cars\nSort by Recommended");

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.Carvana,
            Search,
            60,
            touches.TryTouchAsync,
            (_, _, _) => Task.FromResult(page),
            (_, _) => { },
            _ => { },
            () => { },
            CancellationToken.None);
        await touches.CommitAsync(CancellationToken.None);

        Assert.Equal([CardUrl(2), CardUrl(3)], pool);
        Assert.Equal(1, touches.Count);
        Assert.Equal(FirstRunAt, (await db.Postings.SingleAsync(p => p.Url == CardUrl(9))).LastSeen);
    }

    [Fact]
    public async Task AListingBothOfAPairsSearchesShow_IsTouchedAndCountedOnce()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m));
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: false, CancellationToken.None);

        bool first = await touches.TryTouchAsync(CardUrl(1), 17000m, NoBadges, CancellationToken.None);
        bool again = await touches.TryTouchAsync(CardUrl(1), 16000m, NoBadges, CancellationToken.None);
        bool unknown = await touches.TryTouchAsync(CardUrl(2), 16000m, NoBadges, CancellationToken.None);

        Assert.True(first);
        Assert.True(again);
        Assert.False(unknown);
        Assert.Equal(1, touches.Count);
        await touches.CommitAsync(CancellationToken.None);
        Assert.Equal([18000m, 17000m], (await db.PriceObservations.ToListAsync()).OrderBy(o => o.ObservedAt).Select(o => o.Price));
    }

    [Fact]
    public async Task ATouchIsOnlyRemembered_UntilTheCallerCommitsAtTheEndOfAPair()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m));
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: false, CancellationToken.None);

        await touches.TryTouchAsync(CardUrl(1), 17000m, NoBadges, CancellationToken.None);

        Assert.Equal(1, touches.Count);
        Assert.Equal(FirstRunAt, (await db.Postings.SingleAsync()).LastSeen);
        Assert.Single(db.PriceObservations);

        await touches.CommitAsync(CancellationToken.None);

        Assert.Equal(SecondRunAt, (await db.Postings.SingleAsync()).LastSeen);
        Assert.Equal(2, await db.PriceObservations.CountAsync());
    }

    [Fact]
    public async Task AKnownLinkWhoseFirstCardPriceWasUnreadable_TakesThePriceOfALaterCard()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m));
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: false, CancellationToken.None);

        await touches.TryTouchAsync(CardUrl(1), null, NoBadges, CancellationToken.None);
        await touches.TryTouchAsync(CardUrl(1), 17000m, NoBadges, CancellationToken.None);
        await touches.CommitAsync(CancellationToken.None);

        Assert.Equal([18000m, 17000m], (await db.PriceObservations.ToListAsync()).OrderBy(o => o.ObservedAt).Select(o => o.Price));
    }

    [Fact]
    public async Task ASourceWithoutACardPriceReader_TouchesKnownLinksWithoutAPrice()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity first = await StartRunAsync(db, FirstRunAt);
        string url = "https://www.autotrader.com/cars-for-sale/vehicle/787014112";
        await service.UpsertAsync(Candidate(1, 18000m) with { Source = "autotrader", Url = url }, first, CancellationToken.None);
        RunEntity second = await StartRunAsync(db, SecondRunAt);
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "autotrader", second, revisit: false, CancellationToken.None);
        var page = new SearchPageContent([new PageLink(url + "?clickType=listing", "2022 Toyota Prius", "Used\n2022 Toyota Prius\n138K mi\n17,499\nSee payment")], "1 Match");

        IReadOnlyList<string> pool = await WalkSearchPages.CollectLinksAsync(
            WalkSites.Autotrader,
            "https://www.autotrader.com/cars-for-sale/used-cars/toyota/prius",
            60,
            touches.TryTouchAsync,
            (_, _, _) => Task.FromResult(page),
            (_, _) => { },
            _ => { },
            () => { },
            CancellationToken.None);
        await touches.CommitAsync(CancellationToken.None);

        Assert.Empty(pool);
        Assert.Equal(1, touches.Count);
        Assert.Single(db.PriceObservations);
        Assert.Equal(SecondRunAt, (await db.Postings.SingleAsync()).LastSeen);
    }

    private static async Task<Dictionary<string, (string Value, int RunId)>> AttributesAtAsync(OdonomicsDbContext db, string url) =>
        (await db.PostingAttributes.Where(a => a.Posting!.Url == url).ToListAsync())
        .ToDictionary(a => a.Name, a => (a.Value, a.ObservedRunId));

    [Fact]
    public async Task ARepeatWalk_AKnownCardsBadgesAreRefreshedOnItsPostingWithoutADetailVisit()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m));
        PostingEntity posting = await db.Postings.SingleAsync();
        RunEntity first = await db.Runs.SingleAsync(r => r.StartedAt == FirstRunAt);
        await service.SetPostingAttributesAsync(
            posting.Id,
            new Dictionary<string, string> { [PostingAttributeNames.Deal] = "Great Deal", [PostingAttributeNames.Shipping] = "Free shipping" },
            first,
            CancellationToken.None);
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: false, CancellationToken.None);

        IReadOnlyList<string> pool = await CollectAsync(touches, new() { [1] = [Card(1, 18000m, "Price Drop\nFree shipping\n")] }, poolSize: 60);

        Assert.Empty(pool);
        Dictionary<string, (string Value, int RunId)> stored = await AttributesAtAsync(db, CardUrl(1));
        Assert.Equal(("Price Drop", second.Id), stored["price-drop"]);
        Assert.Equal(("Free shipping", second.Id), stored["shipping"]);
        Assert.Equal(("Great Deal", first.Id), stored["deal"]);
    }

    [Fact]
    public async Task ARepeatWalk_AKnownCardWithNoBadgeRecordsNothingAndLeavesEarlierAttributes()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m), (2, 19000m));
        PostingEntity earlier = await db.Postings.SingleAsync(p => p.Url == CardUrl(1));
        RunEntity first = await db.Runs.SingleAsync(r => r.StartedAt == FirstRunAt);
        await service.SetPostingAttributesAsync(earlier.Id, new Dictionary<string, string> { [PostingAttributeNames.Deal] = "Great Deal" }, first, CancellationToken.None);
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: false, CancellationToken.None);

        await CollectAsync(touches, new() { [1] = [Card(1, 18000m), Card(2, 19000m)] }, poolSize: 60);

        Assert.Equal(("Great Deal", first.Id), Assert.Single(await AttributesAtAsync(db, CardUrl(1))).Value);
        Assert.Empty(await AttributesAtAsync(db, CardUrl(2)));
    }

    [Fact]
    public async Task ARepeatWalk_APostingIsNotWrittenBeforeThePairCommits()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m));
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: false, CancellationToken.None);

        await touches.TryTouchAsync(CardUrl(1), 18000m, new Dictionary<string, string> { [PostingAttributeNames.PriceDrop] = "Price Drop" }, CancellationToken.None);

        Assert.Empty(await db.PostingAttributes.ToListAsync());
        await touches.CommitAsync(CancellationToken.None);
        Assert.Single(await db.PostingAttributes.ToListAsync());
    }

    [Fact]
    public async Task ARepeatWalk_ANewLinksCardBadgesAreKeptForItsDetailVisit()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m));
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: false, CancellationToken.None);

        IReadOnlyList<string> pool = await CollectAsync(touches, new() { [1] = [Card(1, 18000m, "Price Drop\n"), Card(2, 19000m, "Great Deal\nFree shipping\n"), Card(3, 20000m)] }, poolSize: 60);

        Assert.Equal([CardUrl(2), CardUrl(3)], pool);
        Assert.Equal(
            new Dictionary<string, string> { [PostingAttributeNames.Deal] = "Great Deal", [PostingAttributeNames.Shipping] = "Free shipping" },
            touches.BadgesOfNewLink(CardUrl(2)));
        Assert.Empty(touches.BadgesOfNewLink(CardUrl(3)));
        Assert.Empty(touches.BadgesOfNewLink(CardUrl(1)));
        Assert.Empty(await AttributesAtAsync(db, CardUrl(2)));
    }

    [Fact]
    public async Task Revisit_EveryCardsBadgesAreKeptForItsDetailVisit()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, RunEntity second) = await LedgerWithFirstRunAsync(db, (1, 18000m));
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", second, revisit: true, CancellationToken.None);

        IReadOnlyList<string> pool = await CollectAsync(touches, new() { [1] = [Card(1, 18000m, "Price Drop\n")] }, poolSize: 60);

        Assert.Equal([CardUrl(1)], pool);
        Assert.Equal("Price Drop", touches.BadgesOfNewLink(CardUrl(1))[PostingAttributeNames.PriceDrop]);
    }

    [Fact]
    public async Task ADetailVisitedPostingStoresItsCardsBadgesThroughTheUpsert()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity first = await StartRunAsync(db, FirstRunAt);
        KnownCardTouches touches = await KnownCardTouches.LoadAsync(service, "carvana", first, revisit: false, CancellationToken.None);
        await CollectAsync(touches, new() { [1] = [Card(7, 21000m, "Price Drop\nGreat Deal\n")] }, poolSize: 60);

        await service.UpsertAsync(Candidate(7, 21000m) with { Attributes = touches.BadgesOfNewLink(CardUrl(7)) }, first, CancellationToken.None);

        Dictionary<string, (string Value, int RunId)> stored = await AttributesAtAsync(db, CardUrl(7));
        Assert.Equal(("Price Drop", first.Id), stored["price-drop"]);
        Assert.Equal(("Great Deal", first.Id), stored["deal"]);
    }
}
