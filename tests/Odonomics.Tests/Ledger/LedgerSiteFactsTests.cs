using Microsoft.EntityFrameworkCore;
using Odonomics.Ledger;

namespace Odonomics.Tests.Ledger;

/// <summary>Proves the ledger's storage for site-specific facts: the typed fee columns on a posting
/// and the doc fee columns on a dealer come back exactly as written and are null when nothing wrote
/// them, and a posting's display-only attributes keep one row per name, with a later observation
/// replacing the earlier one.</summary>
public class LedgerSiteFactsTests
{
    private const string Vin = "1HGCM82633A004352";
    private const string Url = "https://www.carvana.com/vehicle/4636332";
    private static readonly DateTimeOffset FirstRunAt = new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SecondRunAt = new(2026, 9, 27, 0, 0, 0, TimeSpan.Zero);

    private static ListingCandidate Candidate() => new()
    {
        Vin = Vin,
        Source = "carvana",
        Url = Url,
        Year = 2020,
        Make = "Toyota",
        Model = "Prius",
        Trim = "LE",
        Price = 18000m,
        Mileage = 40000,
    };

    private static async Task<RunEntity> StartRunAsync(OdonomicsDbContext db, DateTimeOffset startedAt)
    {
        var run = new RunEntity { Command = "walk", Sources = "carvana:Prius", StartedAt = startedAt };
        db.Runs.Add(run);
        await db.SaveChangesAsync(CancellationToken.None);
        return run;
    }

    private static async Task<(LedgerUpsertService Service, PostingEntity Posting, RunEntity Run)> SeededAsync(OdonomicsDbContext db)
    {
        var service = new LedgerUpsertService(db);
        RunEntity run = await StartRunAsync(db, FirstRunAt);
        await service.UpsertAsync(Candidate(), run, CancellationToken.None);
        return (service, await db.Postings.SingleAsync(), run);
    }

    private static async Task<Dictionary<string, (string Value, int RunId)>> AttributesOfAsync(OdonomicsDbContext db, int postingId) =>
        (await db.PostingAttributes.Where(a => a.PostingId == postingId).ToListAsync())
        .ToDictionary(a => a.Name, a => (a.Value, a.ObservedRunId));

    [Fact]
    public async Task NewPosting_HasNoFeeFactsUntilARunReadsThem()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        await SeededAsync(db);

        using OdonomicsDbContext reread = testDb.CreateContext();
        PostingEntity posting = await reread.Postings.SingleAsync();
        Assert.Null(posting.FeePosture);
        Assert.Null(posting.ItemizedFeesTotal);
        Assert.Null(posting.PickupFee);
        Assert.Null(posting.PickupLocation);
    }

    [Fact]
    public async Task PostingFeeColumns_RoundTripThroughTheLedger()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (_, PostingEntity posting, _) = await SeededAsync(db);

        posting.FeePosture = FeePostures.Itemized;
        posting.ItemizedFeesTotal = 1234.50m;
        posting.PickupFee = 0m;
        posting.PickupLocation = "Carvana Winder, GA";
        await db.SaveChangesAsync(CancellationToken.None);

        using OdonomicsDbContext reread = testDb.CreateContext();
        PostingEntity stored = await reread.Postings.SingleAsync();
        Assert.Equal("itemized", stored.FeePosture);
        Assert.Equal(1234.50m, stored.ItemizedFeesTotal);
        Assert.Equal(0m, stored.PickupFee);
        Assert.Equal("Carvana Winder, GA", stored.PickupLocation);
    }

    [Fact]
    public void FeePostures_AreTheThreeStringsTheLedgerStores()
    {
        Assert.Equal(["all-in", "itemized", "unknown"], [FeePostures.AllIn, FeePostures.Itemized, FeePostures.Unknown]);
    }

    [Fact]
    public async Task DealerDocFeeAndAddOnsNote_RoundTripAndStartNull()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        db.Dealers.Add(new DealerEntity { Name = "Holler Honda", NormalizedName = "HOLLER HONDA", NormalizedLocation = "" });
        await db.SaveChangesAsync(CancellationToken.None);
        DealerEntity dealer = await db.Dealers.SingleAsync();
        Assert.Null(dealer.DocFee);
        Assert.Null(dealer.AddOnsNote);

        dealer.DocFee = 1199m;
        dealer.AddOnsNote = "$358 add-ons";
        await db.SaveChangesAsync(CancellationToken.None);

        using OdonomicsDbContext reread = testDb.CreateContext();
        DealerEntity stored = await reread.Dealers.SingleAsync();
        Assert.Equal(1199m, stored.DocFee);
        Assert.Equal("$358 add-ons", stored.AddOnsNote);
    }

    [Fact]
    public async Task SetPostingAttributesAsync_StoresEachNameWithTheRunThatObservedIt()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, PostingEntity posting, RunEntity run) = await SeededAsync(db);

        await service.SetPostingAttributesAsync(
            posting.Id,
            new Dictionary<string, string> { ["Badge"] = "Certified", ["Rating"] = "4.8" },
            run,
            CancellationToken.None);

        using OdonomicsDbContext reread = testDb.CreateContext();
        Dictionary<string, (string Value, int RunId)> stored = await AttributesOfAsync(reread, posting.Id);
        Assert.Equal(2, stored.Count);
        Assert.Equal(("Certified", run.Id), stored["Badge"]);
        Assert.Equal(("4.8", run.Id), stored["Rating"]);
    }

    [Fact]
    public async Task SetPostingAttributesAsync_ALaterObservationReplacesTheEarlierOneAndLeavesOtherNamesAlone()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, PostingEntity posting, RunEntity first) = await SeededAsync(db);
        RunEntity second = await StartRunAsync(db, SecondRunAt);
        await service.SetPostingAttributesAsync(
            posting.Id,
            new Dictionary<string, string> { ["Badge"] = "Certified", ["Rating"] = "4.8" },
            first,
            CancellationToken.None);

        await service.SetPostingAttributesAsync(
            posting.Id,
            new Dictionary<string, string> { ["Rating"] = "4.6", ["Inspection"] = "150 points" },
            second,
            CancellationToken.None);

        using OdonomicsDbContext reread = testDb.CreateContext();
        Dictionary<string, (string Value, int RunId)> stored = await AttributesOfAsync(reread, posting.Id);
        Assert.Equal(3, stored.Count);
        Assert.Equal(("Certified", first.Id), stored["Badge"]);
        Assert.Equal(("4.6", second.Id), stored["Rating"]);
        Assert.Equal(("150 points", second.Id), stored["Inspection"]);
    }

    [Fact]
    public async Task SetPostingAttributesAsync_TrimsAndSkipsBlankNamesAndValues()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, PostingEntity posting, RunEntity run) = await SeededAsync(db);

        await service.SetPostingAttributesAsync(
            posting.Id,
            new Dictionary<string, string> { [" Badge "] = " Certified ", ["  "] = "orphan", ["Rating"] = "" },
            run,
            CancellationToken.None);

        using OdonomicsDbContext reread = testDb.CreateContext();
        Dictionary<string, (string Value, int RunId)> stored = await AttributesOfAsync(reread, posting.Id);
        KeyValuePair<string, (string Value, int RunId)> only = Assert.Single(stored);
        Assert.Equal("Badge", only.Key);
        Assert.Equal("Certified", only.Value.Value);
    }

    [Fact]
    public async Task PostingAttributes_TheLedgerRefusesTwoRowsForOneNameOnOnePosting()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (_, PostingEntity posting, RunEntity run) = await SeededAsync(db);
        db.PostingAttributes.AddRange(
            new PostingAttributeEntity { PostingId = posting.Id, Name = "Badge", Value = "Certified", ObservedRunId = run.Id },
            new PostingAttributeEntity { PostingId = posting.Id, Name = "Badge", Value = "Other", ObservedRunId = run.Id });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UpsertAsync_StoresTheCandidatesAttributesWithTheRunThatSawThem()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity run = await StartRunAsync(db, FirstRunAt);

        await service.UpsertAsync(
            Candidate() with { Attributes = new Dictionary<string, string> { [PostingAttributeNames.Deal] = "Great Deal", [PostingAttributeNames.PriceDrop] = "Price Drop" } },
            run,
            CancellationToken.None);

        using OdonomicsDbContext reread = testDb.CreateContext();
        PostingEntity posting = await reread.Postings.SingleAsync();
        Dictionary<string, (string Value, int RunId)> stored = await AttributesOfAsync(reread, posting.Id);
        Assert.Equal(2, stored.Count);
        Assert.Equal(("Great Deal", run.Id), stored["deal"]);
        Assert.Equal(("Price Drop", run.Id), stored["price-drop"]);
    }

    [Fact]
    public async Task UpsertAsync_ACandidateWithNoAttributesLeavesTheOnesAnEarlierRunStored()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        var service = new LedgerUpsertService(db);
        RunEntity first = await StartRunAsync(db, FirstRunAt);
        RunEntity second = await StartRunAsync(db, SecondRunAt);
        await service.UpsertAsync(
            Candidate() with { Attributes = new Dictionary<string, string> { [PostingAttributeNames.Deal] = "Good Deal" } },
            first,
            CancellationToken.None);

        await service.UpsertAsync(Candidate(), second, CancellationToken.None);

        using OdonomicsDbContext reread = testDb.CreateContext();
        PostingEntity posting = await reread.Postings.SingleAsync();
        Dictionary<string, (string Value, int RunId)> stored = await AttributesOfAsync(reread, posting.Id);
        Assert.Equal(("Good Deal", first.Id), Assert.Single(stored).Value);
    }

    [Fact]
    public async Task SetPostingAttributesByUrlAsync_RefreshesEachPostingAtItsUrlAndIgnoresAnUnknownUrl()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, PostingEntity posting, RunEntity first) = await SeededAsync(db);
        RunEntity second = await StartRunAsync(db, SecondRunAt);
        await service.SetPostingAttributesAsync(
            posting.Id,
            new Dictionary<string, string> { [PostingAttributeNames.Deal] = "Good Deal", [PostingAttributeNames.Demand] = "High Demand" },
            first,
            CancellationToken.None);

        await service.SetPostingAttributesByUrlAsync(
            "carvana",
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                [Url] = new Dictionary<string, string> { [PostingAttributeNames.Deal] = "Great Deal" },
                ["https://www.carvana.com/vehicle/1"] = new Dictionary<string, string> { [PostingAttributeNames.Deal] = "Good Deal" },
            },
            second,
            CancellationToken.None);

        using OdonomicsDbContext reread = testDb.CreateContext();
        Dictionary<string, (string Value, int RunId)> stored = await AttributesOfAsync(reread, posting.Id);
        Assert.Equal(("Great Deal", second.Id), stored["deal"]);
        Assert.Equal(("High Demand", first.Id), stored["demand"]);
        Assert.Equal(2, await reread.PostingAttributes.CountAsync());
    }

    [Fact]
    public async Task SetPostingAttributesByUrlAsync_OnlyTouchesPostingsOfTheGivenSource()
    {
        using var testDb = new LedgerTestDatabase();
        using OdonomicsDbContext db = testDb.CreateContext();
        (LedgerUpsertService service, _, RunEntity run) = await SeededAsync(db);

        await service.SetPostingAttributesByUrlAsync(
            "cars.com",
            new Dictionary<string, IReadOnlyDictionary<string, string>> { [Url] = new Dictionary<string, string> { [PostingAttributeNames.Deal] = "Great Deal" } },
            run,
            CancellationToken.None);

        using OdonomicsDbContext reread = testDb.CreateContext();
        Assert.Empty(await reread.PostingAttributes.ToListAsync());
    }
}
