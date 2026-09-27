using Odonomics.Ledger;
using Odonomics.Walk;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

/// <summary>`odo walk --backfill-carmax`: fills a bare CarMax dealer's store, and any CarMax posting's
/// reserved-or-in-transit availability, from detail pages `odo walk carmax` has already recorded on
/// disk (see <see cref="CarMaxBackfill"/>). Opens no browser and visits no page, so it needs none of
/// the CDP setup the rest of the walk command does.</summary>
public static class CarMaxBackfillCommand
{
    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using OdonomicsDbContext db = LedgerFactory.Open();
        var upsertService = new LedgerUpsertService(db);

        var run = new RunEntity { Command = "walk --backfill-carmax", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        List<PostingEntity> postings = await upsertService.CarMaxPostingsAsync(cancellationToken);
        CarMaxBackfillTally tally = await CarMaxBackfill.RunAsync(DataDirectory.Resolve(), postings, upsertService, run, cancellationToken);

        run.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        AnsiConsole.MarkupLineInterpolated(
            $"CarMax backfill: {tally.Filled} filled, {tally.AlreadySet} already set, {tally.CouldNotMatch} could not match");
        return 0;
    }
}
