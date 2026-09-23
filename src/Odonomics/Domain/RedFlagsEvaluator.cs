using System.Text.RegularExpressions;

namespace Odonomics.Domain;

/// <summary>The slice of one VIN-history listing the red-flags evaluator needs, kept independent of
/// the Marketcheck client types the same way <see cref="VehicleForScoring"/> is kept independent of
/// the EF Core entities.</summary>
public sealed record VinHistoryPoint(string? Dealer, DateTimeOffset? FirstSeen, decimal? Price, int? Mileage);

/// <summary>The one fact the evaluator needs from an NHTSA recall campaign, kept independent of
/// <c>Odonomics.Nhtsa.RecallEntry</c> the same way <see cref="VinHistoryPoint"/> is kept independent
/// of the Marketcheck client types. A caller maps its recall data down to this before calling
/// <see cref="RedFlagsEvaluator.Evaluate"/>.</summary>
public sealed record RecallForFlagging(bool RemedyAvailable);

/// <summary>One red flag: a short, kebab-case <see cref="ShortTag"/> for a compact one-line-per-vehicle
/// summary (see the research command), and a full-sentence <see cref="Detail"/> for a per-vehicle
/// detail view (see `odo show`).</summary>
public sealed record RedFlag(string ShortTag, string Detail);

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

    /// <summary>A listing's mileage at or below this is treated as a placeholder/reset value
    /// (a blank field defaulted to 0, or a similarly bogus near-zero scrape) rather than a real
    /// odometer reading, since a used car that previously showed tens of thousands of miles never
    /// legitimately drops to fewer than these miles in a later listing.</summary>
    private const int PlaceholderMileageMax = 50;

    public static IReadOnlyList<RedFlag> Evaluate(
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

        RedFlag? mileageFlag = FindMileageDrop(ordered, mileageDropMinMiles, mileageDropMinPercent);
        if (mileageFlag is not null)
        {
            flags.Add(mileageFlag);
        }

        RedFlag? dealerHopFlag = FindDealerHop(ordered, dealerCountThreshold);
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

        return flags;
    }

    /// <summary>The first mileage decrease between consecutive listings, after two deliberate
    /// exclusions applied to the ordered history itself, not to each pair: a placeholder value (see
    /// <see cref="PlaceholderMileageMax"/>) is dropped from the sequence entirely rather than merely
    /// skipped as an endpoint, so it can never become the baseline for the next comparison and mask a
    /// real rollback that straddles it; and listings first seen on the same calendar day (almost
    /// always the same snapshot re-scraped, not two real odometer readings) collapse to that day's
    /// first reading, for the same reason. What survives still needs a drop of at least the larger of
    /// <paramref name="mileageDropMinMiles"/> and <paramref name="mileageDropMinPercent"/> of the
    /// prior mileage (rounding and minor re-entry noise, not a rolled-back odometer).</summary>
    private static RedFlag? FindMileageDrop(IReadOnlyList<VinHistoryPoint> ordered, int mileageDropMinMiles, decimal mileageDropMinPercent)
    {
        List<VinHistoryPoint> filtered = FilterMileagePoints(ordered);

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

            return new RedFlag(
                "mileage-drop",
                $"mileage dropped from {previousMileage:N0} to {nextMileage:N0} between listings " +
                $"({previous.FirstSeen:yyyy-MM-dd} to {next.FirstSeen:yyyy-MM-dd})");
        }

        return null;
    }

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

    /// <summary>Finds the first (earliest-starting) 90-day window across the ordered history that
    /// touched at least <paramref name="dealerCountThreshold"/> distinct sellers (after normalizing
    /// dealer names, see <see cref="DistinctSellers"/>), rather than requiring the *entire* history to
    /// fit in one short window: a VIN with a long, ordinary history plus one recent burst of
    /// relistings still needs to trip this, even though its oldest and newest listings are years
    /// apart.</summary>
    private static RedFlag? FindDealerHop(IReadOnlyList<VinHistoryPoint> ordered, int dealerCountThreshold)
    {
        for (int start = 0; start < ordered.Count; start++)
        {
            int end = start;
            while (end + 1 < ordered.Count && (ordered[end + 1].FirstSeen!.Value - ordered[start].FirstSeen!.Value).TotalDays <= DealerHopWindowDays)
            {
                end++;
            }

            List<string> sellers = DistinctSellers(ordered.Skip(start).Take(end - start + 1).Select(p => p.Dealer));

            if (sellers.Count >= dealerCountThreshold)
            {
                int days = (int)(ordered[end].FirstSeen!.Value - ordered[start].FirstSeen!.Value).TotalDays;
                string shown = string.Join(", ", sellers.Take(MaxSellerNamesShown));
                int remaining = sellers.Count - Math.Min(MaxSellerNamesShown, sellers.Count);
                string suffix = remaining > 0
                    ? $" and {remaining} more"
                    : "";
                return new RedFlag(
                    $"{sellers.Count}-sellers",
                    $"listed by {sellers.Count} different sellers within {days} days: {shown}{suffix}");
            }
        }

        return null;
    }

    /// <summary>Reduces a window's raw dealer names to distinct sellers: case and punctuation are
    /// normalized away, and a name that is a word-for-word leading prefix of another (e.g. "Schaller
    /// Honda" of "Schaller Honda Subaru Mitsubishi") is treated as the same seller listed under two
    /// spellings rather than two sellers, keeping the shortest spelling seen as the group's displayed
    /// name. A candidate is compared against every member already in a group, not only its current
    /// representative, and matching groups are merged together, so the result never depends on the
    /// order names first appear in and never holds two entries where one is a word-prefix of the
    /// other. Order of first group appearance is preserved so the printed list reads
    /// chronologically.</summary>
    private static List<string> DistinctSellers(IEnumerable<string?> dealerNames)
    {
        List<List<(string Original, string[] Words)>> groups = [];

        foreach (string? raw in dealerNames)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string[] words = NormalizeToWords(raw);
            if (words.Length == 0)
            {
                continue;
            }

            List<int> matchingGroupIndexes = [.. Enumerable.Range(0, groups.Count)
                .Where(i => groups[i].Any(member => IsWordPrefixOfEither(member.Words, words)))];

            if (matchingGroupIndexes.Count == 0)
            {
                groups.Add([(raw, words)]);
                continue;
            }

            List<(string Original, string[] Words)> mergedGroup = groups[matchingGroupIndexes[0]];
            for (int i = matchingGroupIndexes.Count - 1; i >= 1; i--)
            {
                mergedGroup.AddRange(groups[matchingGroupIndexes[i]]);
                groups.RemoveAt(matchingGroupIndexes[i]);
            }

            mergedGroup.Add((raw, words));
        }

        return [.. groups.Select(g => g.OrderBy(member => member.Words.Length).First().Original)];
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

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
