using Odonomics.Domain;

namespace Odonomics.Ledger;

/// <summary>The red flags a vehicle's own postings raise from the vehicle-history summaries their listings state
/// (see <see cref="PostingHistory"/>): frame damage, a reported accident, and rental or fleet use. They come from
/// the ledger and need no research fetch, so `odo rank` can show them for a vehicle nobody has researched yet. None
/// of them excludes a car from rank.</summary>
public static class HistoryFlags
{
    /// <summary>The frame-damage, accident-reported, and rental-history flags (see
    /// <see cref="RedFlagsEvaluator.FrameDamageListed"/>, <see cref="RedFlagsEvaluator.AccidentReported"/>, and
    /// <see cref="RedFlagsEvaluator.RentalHistory"/>), in that order, for each one some posting of
    /// <paramref name="vehicle"/> states, gone postings included: a listing that stated it is evidence about the car
    /// even after the listing is gone, and a later detail visit that finds it dropped replaces the summary on its own
    /// posting. Newest posting first. An accident count is the one a source's newest posting that states one printed,
    /// so a later reading replaces an earlier one from the same source instead of both being named.</summary>
    public static IReadOnlyList<RedFlag> For(VehicleEntity vehicle)
    {
        List<PostingEntity> newestFirst = [.. vehicle.Postings.OrderByDescending(p => p.LastSeen)];

        IEnumerable<(string Source, int Count)> accidentCounts = newestFirst
            .Where(p => p.HistoryAccidentCount is not null)
            .GroupBy(p => p.Source)
            .Select(g => (g.Key, g.First().HistoryAccidentCount.GetValueOrDefault()));

        RedFlag?[] flags =
        [
            RedFlagsEvaluator.FrameDamageListed(Statements(newestFirst, p => p.HistoryFrameDamageStatement)),
            RedFlagsEvaluator.AccidentReported(accidentCounts),
            RedFlagsEvaluator.RentalHistory(Statements(newestFirst, p => p.HistoryUseStatement)),
        ];

        return [.. flags.OfType<RedFlag>()];
    }

    private static IEnumerable<(string Source, string Statement)> Statements(IEnumerable<PostingEntity> newestFirst, Func<PostingEntity, string?> statement) =>
        newestFirst
            .Select(p => (p.Source, Statement: statement(p)?.Trim() ?? ""))
            .Where(s => s.Statement.Length > 0);
}
