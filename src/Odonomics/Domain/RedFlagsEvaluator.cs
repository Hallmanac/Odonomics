using System.Text.RegularExpressions;

namespace Odonomics.Domain;

/// <summary>The slice of one VIN-history listing the red-flags evaluator needs, kept independent of
/// the Marketcheck client types the same way <see cref="VehicleForScoring"/> is kept independent of
/// the EF Core entities. <see cref="LastSeen"/> (falling back to <see cref="FirstSeen"/> when a
/// source never reported one) is what lets <see cref="RedFlagsEvaluator"/> tell a listing's whole
/// window apart from just its first sighting, which the seller-grouping rule needs.</summary>
public sealed record VinHistoryPoint(string? Dealer, DateTimeOffset? FirstSeen, DateTimeOffset? LastSeen, decimal? Price, int? Mileage);

/// <summary>The one fact the evaluator needs from an NHTSA recall campaign, kept independent of
/// <c>Odonomics.Nhtsa.RecallEntry</c> the same way <see cref="VinHistoryPoint"/> is kept independent
/// of the Marketcheck client types. A caller maps its recall data down to this before calling
/// <see cref="RedFlagsEvaluator.Evaluate"/>.</summary>
public sealed record RecallForFlagging(bool RemedyAvailable);

/// <summary>One red flag: a short, kebab-case <see cref="ShortTag"/> for a compact one-line-per-vehicle
/// summary (see the research command), and a full-sentence <see cref="Detail"/> for a per-vehicle
/// detail view (see `odo show`).</summary>
public sealed record RedFlag(string ShortTag, string Detail);

/// <summary><see cref="Flags"/> is what still belongs in a summary or a detail view as something to
/// worry about; <see cref="Notes"/> is a plain-English record of something the evaluator judged to
/// be data noise rather than a real red flag (currently just a same-seller mileage correction), for
/// a caller to persist onto the vehicle the way an operator's own `odo note` would.</summary>
public sealed record EvaluationResult(IReadOnlyList<RedFlag> Flags, IReadOnlyList<string> Notes);

/// <summary>One seller group's shape for display (see <see cref="RedFlagsEvaluator.GroupBySeller"/>):
/// every dealer name recorded for the group, in first-appearance order, and the group's overall
/// window, price range, and mileage range. A caller (currently `odo show`) decides how many names to
/// print and how to format the ranges; this type only carries the facts.</summary>
public sealed record SellerGroupSummary(
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    IReadOnlyList<string> DealerNames,
    decimal? MinPrice,
    decimal? MaxPrice,
    int? MinMileage,
    int? MaxMileage);

/// <summary>
/// Plain, testable rules over a VIN's research data: mileage that decreased between listings by more
/// than rounding or a data-entry blip, a listing history spanning several distinct sellers in a short
/// window, an open recall with no remedy published yet, a safety rating below four stars, or a
/// current price well above the listing-history price trajectory. Every threshold below is a
/// deliberate v0 simplification, not a value NHTSA or Marketcheck hand us.
/// </summary>
public static partial class RedFlagsEvaluator
{
    public const int DefaultDealerCountThreshold = 3;
    public const int DefaultMileageDropMinMiles = 500;
    public const decimal DefaultMileageDropMinPercent = 0.01m;

    private const int DealerHopWindowDays = 90;
    private const int MaxSellerNamesShown = 3;
    private const decimal PriceAboveTrajectoryFactor = 1.15m;
    private const int MinPriorListingsForTrajectory = 2;
    private const int LowSafetyRatingThreshold = 4;

    /// <summary>How many days apart two listing windows may sit and still count as "overlapping or
    /// touching" for the seller-grouping rule (see <see cref="BuildSellerGroups"/>): a dealer-group
    /// syndication feed's per-rooftop windows rarely line up to the exact day, but two rooftops
    /// selling the same physical car in the same stretch of weeks are never days-then-months apart
    /// either.</summary>
    private const int SellerGroupWindowTouchDays = 2;

    /// <summary>How many days apart a mileage-drop pair's windows may sit and still be treated as a
    /// same-seller correction rather than a real rollback (see <see cref="FindMileageDrop"/>): a
    /// same-dealer listing that jumps straight from one day's window into the next day's is the same
    /// continuous listing being re-scraped with a corrected odometer reading, not two different
    /// sightings of the car.</summary>
    private const int MileageNoteMaxGapDays = 1;

    /// <summary>A listing's mileage at or below this is treated as a placeholder/reset value
    /// (a blank field defaulted to 0, or a similarly bogus near-zero scrape) rather than a real
    /// odometer reading, since a used car that previously showed tens of thousands of miles never
    /// legitimately drops to fewer than these miles in a later listing.</summary>
    private const int PlaceholderMileageMax = 50;

    public static EvaluationResult Evaluate(
        IReadOnlyList<RecallForFlagging> recalls,
        int? safetyOverallRating,
        IReadOnlyList<VinHistoryPoint> priorListings,
        decimal? currentPrice,
        int dealerCountThreshold = DefaultDealerCountThreshold,
        int mileageDropMinMiles = DefaultMileageDropMinMiles,
        decimal mileageDropMinPercent = DefaultMileageDropMinPercent)
    {
        List<RedFlag> flags = [];

        int noRemedyCount = recalls.Count(r => !r.RemedyAvailable);
        if (noRemedyCount > 0)
        {
            flags.Add(new RedFlag(
                "no-remedy-recall",
                $"{noRemedyCount} open recall{(noRemedyCount == 1 ? "" : "s")} with no remedy available yet"));
        }

        if (safetyOverallRating is int stars && stars < LowSafetyRatingThreshold)
        {
            flags.Add(new RedFlag(
                "low-safety-rating",
                $"NHTSA overall safety rating is {stars} star{(stars == 1 ? "" : "s")}, below 4"));
        }

        List<VinHistoryPoint> ordered = [.. priorListings.Where(p => p.FirstSeen is not null).OrderBy(p => p.FirstSeen)];
        List<SellerGroup> sellerGroups = BuildSellerGroups(ordered);

        (RedFlag? mileageFlag, List<string> mileageNotes) = FindMileageDrop(ordered, sellerGroups, mileageDropMinMiles, mileageDropMinPercent);
        if (mileageFlag is not null)
        {
            flags.Add(mileageFlag);
        }

        RedFlag? dealerHopFlag = FindDealerHop(sellerGroups, dealerCountThreshold);
        if (dealerHopFlag is not null)
        {
            flags.Add(dealerHopFlag);
        }

        decimal[] priorPrices = [.. priorListings.Select(p => p.Price).Where(p => p is not null).Select(p => p!.Value)];
        if (currentPrice is decimal price && priorPrices.Length >= MinPriorListingsForTrajectory)
        {
            decimal average = priorPrices.Average();
            if (price > average * PriceAboveTrajectoryFactor)
            {
                flags.Add(new RedFlag(
                    "price-spike",
                    $"current price ${price:N0} is well above the ${average:N0} average of {priorPrices.Length} prior listings"));
            }
        }

        return new EvaluationResult(flags, mileageNotes);
    }

    /// <summary>The listing history grouped by seller (see <see cref="BuildSellerGroups"/>), for a
    /// caller like `odo show` that wants to render a syndicated 50-row history as a handful of
    /// readable rows instead of one line per raw sighting.</summary>
    public static IReadOnlyList<SellerGroupSummary> GroupBySeller(IReadOnlyList<VinHistoryPoint> priorListings)
    {
        List<VinHistoryPoint> ordered = [.. priorListings.Where(p => p.FirstSeen is not null).OrderBy(p => p.FirstSeen)];
        List<SellerGroup> groups = BuildSellerGroups(ordered);

        return [.. groups.Select(g =>
        {
            (decimal? minPrice, decimal? maxPrice) = MinMax(g.Points.Select(p => p.Price));
            (int? minMileage, int? maxMileage) = MinMax(g.Points
                .Select(p => p.Mileage is int mileage && mileage > PlaceholderMileageMax ? mileage : (int?)null));
            return new SellerGroupSummary(g.WindowStart, g.WindowEnd, g.DealerNames, minPrice, maxPrice, minMileage, maxMileage);
        })];
    }

    /// <summary>The first mileage decrease between consecutive listings, after two deliberate
    /// exclusions applied to the ordered history itself, not to each pair: a placeholder value (see
    /// <see cref="PlaceholderMileageMax"/>) is dropped from the sequence entirely rather than merely
    /// skipped as an endpoint, so it can never become the baseline for the next comparison and mask a
    /// real rollback that straddles it; and listings first seen on the same calendar day (almost
    /// always the same snapshot re-scraped, not two real odometer readings) collapse to that day's
    /// first reading, for the same reason. What survives still needs a drop of at least the larger of
    /// <paramref name="mileageDropMinMiles"/> and <paramref name="mileageDropMinPercent"/> of the
    /// prior mileage (rounding and minor re-entry noise, not a rolled-back odometer). A drop between
    /// two points in the same <see cref="SellerGroup"/> (see <paramref name="sellerGroups"/>) whose
    /// windows are on consecutive or overlapping days is a same-seller correction, not a real flag: it
    /// becomes a note instead, and scanning continues for a genuine flag-worthy drop elsewhere in the
    /// history.</summary>
    private static (RedFlag? Flag, List<string> Notes) FindMileageDrop(
        IReadOnlyList<VinHistoryPoint> ordered, List<SellerGroup> sellerGroups, int mileageDropMinMiles, decimal mileageDropMinPercent)
    {
        List<VinHistoryPoint> filtered = FilterMileagePoints(ordered);
        List<string> notes = [];

        for (int i = 1; i < filtered.Count; i++)
        {
            VinHistoryPoint previous = filtered[i - 1];
            VinHistoryPoint next = filtered[i];
            if (previous.Mileage is not int previousMileage || next.Mileage is not int nextMileage || nextMileage >= previousMileage)
            {
                continue;
            }

            int drop = previousMileage - nextMileage;
            decimal minimumDrop = Math.Max(mileageDropMinMiles, previousMileage * mileageDropMinPercent);
            if (drop < minimumDrop)
            {
                continue;
            }

            SellerGroup? previousGroup = FindContainingGroup(sellerGroups, previous);
            SellerGroup? nextGroup = FindContainingGroup(sellerGroups, next);
            bool sameSellerGroup = previousGroup is not null && ReferenceEquals(previousGroup, nextGroup);
            double gapDays = ((next.FirstSeen!.Value) - (previous.LastSeen ?? previous.FirstSeen!.Value)).TotalDays;

            if (sameSellerGroup && Math.Abs(gapDays) <= MileageNoteMaxGapDays)
            {
                notes.Add(
                    $"mileage corrected {previousMileage:N0} to {nextMileage:N0} at {nextGroup!.RepresentativeName} on {next.FirstSeen:MMM d}");
                continue;
            }

            return (new RedFlag(
                "mileage-drop",
                $"mileage dropped from {previousMileage:N0} to {nextMileage:N0} between listings " +
                $"({previous.FirstSeen:yyyy-MM-dd} to {next.FirstSeen:yyyy-MM-dd})"), notes);
        }

        return (null, notes);
    }

    private static SellerGroup? FindContainingGroup(List<SellerGroup> groups, VinHistoryPoint point) =>
        groups.FirstOrDefault(g => g.Points.Contains(point));

    /// <summary>Drops placeholder-mileage points (see <see cref="PlaceholderMileageMax"/>) entirely,
    /// and collapses a run of same-calendar-day points down to the first reading of that day, so
    /// neither kind of bogus reading can ever end up as the baseline or endpoint of a pair comparison
    /// in <see cref="FindMileageDrop"/>.</summary>
    private static List<VinHistoryPoint> FilterMileagePoints(IReadOnlyList<VinHistoryPoint> ordered)
    {
        List<VinHistoryPoint> filtered = [];
        foreach (VinHistoryPoint point in ordered)
        {
            if (point.Mileage is int mileage && mileage <= PlaceholderMileageMax)
            {
                continue;
            }

            if (filtered.Count > 0 && filtered[^1].FirstSeen!.Value.Date == point.FirstSeen!.Value.Date)
            {
                continue;
            }

            filtered.Add(point);
        }

        return filtered;
    }

    /// <summary>Finds the first (earliest-starting) 90-day window across the counted sellers (see
    /// <see cref="CountSellers"/>) that reaches <paramref name="dealerCountThreshold"/> distinct
    /// sellers, rather than requiring the *entire* history to fit in one short window: a VIN with a
    /// long, ordinary history plus one recent burst of genuine relistings still needs to trip this,
    /// even though its oldest and newest listings are years apart.</summary>
    private static RedFlag? FindDealerHop(List<SellerGroup> sellerGroups, int dealerCountThreshold)
    {
        List<SellerGroup> counted = CountSellers(sellerGroups);

        for (int start = 0; start < counted.Count; start++)
        {
            int end = start;
            while (end + 1 < counted.Count && (counted[end + 1].WindowStart - counted[start].WindowStart).TotalDays <= DealerHopWindowDays)
            {
                end++;
            }

            int count = end - start + 1;
            if (count >= dealerCountThreshold)
            {
                int days = (int)(counted[end].WindowStart - counted[start].WindowStart).TotalDays;
                List<string> names = [.. counted.Skip(start).Take(count).Select(g => g.RepresentativeName)];
                string shown = string.Join(", ", names.Take(MaxSellerNamesShown));
                int remaining = names.Count - Math.Min(MaxSellerNamesShown, names.Count);
                string suffix = remaining > 0
                    ? $" and {remaining} more"
                    : "";
                return new RedFlag(
                    $"{count}-sellers",
                    $"listed by {count} different sellers within {days} days: {shown}{suffix}");
            }
        }

        return null;
    }

    /// <summary>Walks the chronologically-ordered seller groups and keeps only the ones that mark a
    /// genuine seller change: a group with no known dealer name at all is skipped entirely, the same
    /// as the raw-dealer-name rule this replaced skipped a listing with no name, since there is no
    /// evidence at all to tell it apart from a repeat of the seller before or after it. Of what
    /// remains, the first group always counts, and every later group counts only when its window
    /// starts after the previous counted group's last window ended AND the mileage moved between
    /// them. A group that overlaps the previous one, or picks up again at the same mileage, is
    /// folded away rather than counted as a new seller, since neither shape distinguishes it from the
    /// same car sitting with the same owner; an absent or placeholder reading on either side is never
    /// treated as evidence the mileage stayed put; only two real, equal readings are (the same
    /// distrust <see cref="BuildSellerGroups"/> already applies to a placeholder reading two methods
    /// away), so a group counts as a change whenever the mileage comparison is inconclusive.</summary>
    private static List<SellerGroup> CountSellers(List<SellerGroup> sellerGroups)
    {
        List<SellerGroup> counted = [];
        foreach (SellerGroup group in sellerGroups)
        {
            if (group.DealerNames.Count == 0)
            {
                continue;
            }

            if (counted.Count == 0)
            {
                counted.Add(group);
                continue;
            }

            SellerGroup previous = counted[^1];
            bool sequential = group.WindowStart > previous.WindowEnd;
            bool mileageUnchanged = previous.ExitMileage is int previousMileage && group.EntryMileage is int nextMileage
                && previousMileage > PlaceholderMileageMax && nextMileage > PlaceholderMileageMax
                && previousMileage == nextMileage;
            if (sequential && !mileageUnchanged)
            {
                counted.Add(group);
            }
        }

        return counted;
    }

    /// <summary>Clusters a VIN's ordered listing history into seller groups: two points merge when
    /// their listing windows overlap or touch within <see cref="SellerGroupWindowTouchDays"/> days AND
    /// their mileage is identical and a real reading, not a placeholder (see
    /// <see cref="PlaceholderMileageMax"/>, since two unrelated dealers' placeholder-zero rows must
    /// never merge just because 0 equals 0). This catches a dealer-group syndication feed relisting
    /// the same physical car under many rooftop names at once, regardless of what any of those
    /// rooftops are called. Two points also merge, as a secondary rule, when their dealer names share
    /// a word-for-word stem (see <see cref="NamesShareStem"/>), case insensitively, for the same
    /// dealer spelled or franchised differently across sightings. Both rules feed one union-find over
    /// every pair of points, so a name that bridges two otherwise-unmerged groups (or a window/mileage
    /// match between two otherwise-unnamed groups) still merges everything it touches, transitively,
    /// regardless of scan order.</summary>
    private static List<SellerGroup> BuildSellerGroups(IReadOnlyList<VinHistoryPoint> ordered)
    {
        int n = ordered.Count;
        int[] parent = [.. Enumerable.Range(0, n)];

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        void Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA != rootB)
            {
                parent[rootA] = rootB;
            }
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                bool sameWindowAndMileage = WindowsOverlapOrTouch(ordered[i], ordered[j], SellerGroupWindowTouchDays)
                    && ordered[i].Mileage is int mileageI && mileageI > PlaceholderMileageMax
                    && ordered[j].Mileage is int mileageJ && mileageJ > PlaceholderMileageMax
                    && mileageI == mileageJ;
                if (sameWindowAndMileage || NamesShareStem(ordered[i].Dealer, ordered[j].Dealer))
                {
                    Union(i, j);
                }
            }
        }

        return [.. Enumerable.Range(0, n)
            .GroupBy(Find)
            .Select(g => new SellerGroup([.. g.Select(i => ordered[i])]))
            .OrderBy(g => g.WindowStart)];
    }

    private static bool WindowsOverlapOrTouch(VinHistoryPoint a, VinHistoryPoint b, int maxGapDays)
    {
        DateTimeOffset aStart = a.FirstSeen!.Value;
        DateTimeOffset aEnd = a.LastSeen ?? aStart;
        DateTimeOffset bStart = b.FirstSeen!.Value;
        DateTimeOffset bEnd = b.LastSeen ?? bStart;

        if (aStart <= bEnd && bStart <= aEnd)
        {
            return true;
        }

        double gapDays = aEnd < bStart ? (bStart - aEnd).TotalDays : (aStart - bEnd).TotalDays;
        return gapDays <= maxGapDays;
    }

    private static bool NamesShareStem(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        string[] wordsA = NormalizeToWords(a);
        string[] wordsB = NormalizeToWords(b);
        return wordsA.Length > 0 && wordsB.Length > 0 && IsWordPrefixOfEither(wordsA, wordsB);
    }

    private static bool IsWordPrefixOfEither(string[] a, string[] b)
    {
        int length = Math.Min(a.Length, b.Length);
        for (int i = 0; i < length; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    private static string[] NormalizeToWords(string value)
    {
        string withoutPunctuation = Punctuation().Replace(value, " ");
        string collapsed = WhitespaceRun().Replace(withoutPunctuation, " ").Trim().ToLowerInvariant();
        return collapsed.Length == 0
            ? []
            : collapsed.Split(' ');
    }

    private static (T? Min, T? Max) MinMax<T>(IEnumerable<T?> values) where T : struct, IComparable<T>
    {
        List<T> present = [.. values.Where(v => v is not null).Select(v => v!.Value)];
        return present.Count == 0 ? (null, null) : (present.Min(), present.Max());
    }

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    /// <summary>One physical seller across the listing history, either literally one dealer or several
    /// listings merged by <see cref="BuildSellerGroups"/>. <see cref="WindowEnd"/> falls back to
    /// <see cref="VinHistoryPoint.FirstSeen"/> per point when a point never reported a
    /// <see cref="VinHistoryPoint.LastSeen"/>. <see cref="EntryMileage"/> and
    /// <see cref="ExitMileage"/> are the mileage of the group's earliest and latest point by
    /// <see cref="VinHistoryPoint.FirstSeen"/> respectively (identical unless the group merged by
    /// dealer-name stem across a real mileage change, like a same-dealer relisting), used to decide
    /// whether mileage "moved" into the next seller (see <see cref="CountSellers"/>).</summary>
    private sealed class SellerGroup(List<VinHistoryPoint> points)
    {
        public List<VinHistoryPoint> Points { get; } = points;

        public DateTimeOffset WindowStart => Points.Min(p => p.FirstSeen!.Value);
        public DateTimeOffset WindowEnd => Points.Max(p => p.LastSeen ?? p.FirstSeen!.Value);
        public int? EntryMileage => Points.OrderBy(p => p.FirstSeen).First().Mileage;
        public int? ExitMileage => Points.OrderBy(p => p.FirstSeen).Last().Mileage;

        public IReadOnlyList<string> DealerNames => [.. Points
            .Select(p => p.Dealer)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

        public string RepresentativeName => Points
            .Select(p => p.Dealer)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => NormalizeToWords(name!).Length)
            .FirstOrDefault() ?? "(unknown)";
    }
}
