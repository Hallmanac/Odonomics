namespace Odonomics.Domain;

/// <summary>The slice of one VIN-history listing the red-flags evaluator needs, kept independent of
/// the Marketcheck client types the same way <see cref="VehicleForScoring"/> is kept independent of
/// the EF Core entities.</summary>
public sealed record VinHistoryPoint(string? Dealer, DateTimeOffset? FirstSeen, decimal? Price, int? Mileage);

/// <summary>
/// Plain, testable rules over a VIN's research data: mileage that decreased between listings, a
/// listing history spanning several dealers in a short window, an open recall, a safety rating
/// below four stars, or a current price well above the listing-history price trajectory. Every
/// threshold below is a deliberate v0 simplification, not a value NHTSA or Marketcheck hand us.
/// </summary>
public static class RedFlagsEvaluator
{
    private const int DealerHopMinDealers = 3;
    private const int DealerHopWindowDays = 90;
    private const decimal PriceAboveTrajectoryFactor = 1.15m;
    private const int MinPriorListingsForTrajectory = 2;
    private const int LowSafetyRatingThreshold = 4;

    public static IReadOnlyList<string> Evaluate(
        int openRecallCount,
        int? safetyOverallRating,
        IReadOnlyList<VinHistoryPoint> priorListings,
        decimal? currentPrice)
    {
        List<string> flags = [];

        if (openRecallCount > 0)
        {
            flags.Add($"{openRecallCount} open NHTSA recall{(openRecallCount == 1 ? "" : "s")}");
        }

        if (safetyOverallRating is int stars && stars < LowSafetyRatingThreshold)
        {
            flags.Add($"NHTSA overall safety rating is {stars} star{(stars == 1 ? "" : "s")}, below 4");
        }

        List<VinHistoryPoint> ordered = [.. priorListings.Where(p => p.FirstSeen is not null).OrderBy(p => p.FirstSeen)];

        for (int i = 1; i < ordered.Count; i++)
        {
            if (ordered[i - 1].Mileage is int previousMileage && ordered[i].Mileage is int nextMileage && nextMileage < previousMileage)
            {
                flags.Add($"mileage dropped from {previousMileage:N0} to {nextMileage:N0} between listings " +
                          $"({ordered[i - 1].FirstSeen:yyyy-MM-dd} to {ordered[i].FirstSeen:yyyy-MM-dd})");
            }
        }

        string? dealerHopFlag = FindDealerHop(ordered);
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
                flags.Add($"current price ${price:N0} is well above the ${average:N0} average of {priorPrices.Length} prior listings");
            }
        }

        return flags;
    }

    /// <summary>Finds the first (earliest-starting) 90-day window across the ordered history that
    /// touched three or more distinct dealers, rather than requiring the *entire* history to fit in
    /// one short window: a VIN with a long, ordinary history plus one recent burst of relistings
    /// still needs to trip this, even though its oldest and newest listings are years apart.</summary>
    private static string? FindDealerHop(IReadOnlyList<VinHistoryPoint> ordered)
    {
        for (int start = 0; start < ordered.Count; start++)
        {
            int end = start;
            while (end + 1 < ordered.Count && (ordered[end + 1].FirstSeen!.Value - ordered[start].FirstSeen!.Value).TotalDays <= DealerHopWindowDays)
            {
                end++;
            }

            List<string> dealersInWindow =
            [
                .. ordered.Skip(start).Take(end - start + 1)
                    .Select(p => p.Dealer)
                    .OfType<string>()
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Distinct(),
            ];

            if (dealersInWindow.Count >= DealerHopMinDealers)
            {
                int days = (int)(ordered[end].FirstSeen!.Value - ordered[start].FirstSeen!.Value).TotalDays;
                return $"listed by {dealersInWindow.Count} different dealers within {days} days: {string.Join(", ", dealersInWindow)}";
            }
        }

        return null;
    }
}
