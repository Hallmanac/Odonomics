using Odonomics.Domain;

namespace Odonomics.Ledger;

/// <summary>The red flags a vehicle's own postings raise from the vehicle-history summaries their listings state
/// (see <see cref="PostingHistory"/>): frame damage, a reported accident, and rental or fleet use. They come from
/// the ledger and need no research fetch, so `odo rank` can show them for a vehicle nobody has researched yet. Only
/// frame damage excludes a car from rank (see <see cref="FrameDamageSource"/>); a reported accident or rental use
/// stays a warning, and a summary that states zero accidents earns a marker (see <see cref="NoAccidentsStated"/>).</summary>
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
        List<PostingEntity> newestFirst = NewestFirst(vehicle);

        RedFlag?[] flags =
        [
            RedFlagsEvaluator.FrameDamageListed(Statements(newestFirst, p => p.HistoryFrameDamageStatement)),
            RedFlagsEvaluator.AccidentReported(AccidentCounts(newestFirst)),
            RedFlagsEvaluator.RentalHistory(Statements(newestFirst, p => p.HistoryUseStatement)),
        ];

        return [.. flags.OfType<RedFlag>()];
    }

    /// <summary>The marker `odo rank` and `odo show` print for a vehicle whose summaries state zero accidents (see
    /// <see cref="NoAccidentsStated"/>).</summary>
    public const string NoAccidentsMarker = "no accidents";

    /// <summary>The sources whose postings state frame damage, named as "CarGurus AutoCheck summary" and joined with
    /// a comma, for the reason `odo rank` gives when it excludes the vehicle; null when no posting states it. It
    /// reads the same postings as the frame-damage flag, gone ones included, so a car is excluded exactly when
    /// `odo show` raises that flag. Frame damage is the only history statement that excludes: a reported accident
    /// or rental use stays a warning.</summary>
    public static string? FrameDamageSource(VehicleEntity vehicle)
    {
        List<string> sources = [.. Statements(NewestFirst(vehicle), p => p.HistoryFrameDamageStatement)
            .Select(s => SourceLabel(s.Source))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        return sources.Count == 0
            ? null
            : string.Join(", ", sources);
    }

    /// <summary>Whether the vehicle's listings state zero accidents and none states one or more. Each source counts
    /// once, by the count its newest stating posting printed (as the accident-reported flag does), so the marker
    /// shows only when at least one source states a count and every count stated is zero. A summary that states
    /// no count says nothing, so it never earns the marker.</summary>
    public static bool NoAccidentsStated(VehicleEntity vehicle)
    {
        List<int> counts = [.. AccidentCounts(NewestFirst(vehicle)).Select(c => c.Count)];
        return counts.Count > 0 && counts.All(count => count <= 0);
    }

    private static string SourceLabel(string source) => source switch
    {
        "cargurus" => "CarGurus AutoCheck summary",
        _ => $"{source} history summary",
    };

    private static List<PostingEntity> NewestFirst(VehicleEntity vehicle) => [.. vehicle.Postings.OrderByDescending(p => p.LastSeen)];

    private static IEnumerable<(string Source, int Count)> AccidentCounts(IEnumerable<PostingEntity> newestFirst) =>
        newestFirst
            .Where(p => p.HistoryAccidentCount is not null)
            .GroupBy(p => p.Source)
            .Select(g => (g.Key, g.First().HistoryAccidentCount.GetValueOrDefault()));

    private static IEnumerable<(string Source, string Statement)> Statements(IEnumerable<PostingEntity> newestFirst, Func<PostingEntity, string?> statement) =>
        newestFirst
            .Select(p => (p.Source, Statement: statement(p)?.Trim() ?? ""))
            .Where(s => s.Statement.Length > 0);
}
