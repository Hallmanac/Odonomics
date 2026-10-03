namespace Odonomics.Domain;

/// <summary>One ledger vehicle as the similar-price comparison reads it: the fields that decide who
/// counts as similar, and its asking price (null when it has no current one).</summary>
public sealed record PriceComparable(string Vin, int Year, string Make, string Model, int Mileage, decimal? Price);

/// <summary>The soft note `odo show` and `odo rank` print for a car priced well below similar ledger
/// cars: a price that far under the market is sometimes a salvage or rebuilt title the listing never
/// says, so the note asks the buyer to confirm the title. A car's similar cars are the other ledger
/// vehicles of the same make and model, ignoring case, with a model year within
/// <see cref="YearWindow"/> and a mileage within <see cref="MileageWindowMiles"/>, that have a price. With
/// fewer than <see cref="MinimumSimilarCars"/> of them there is no note, because a median of a few cars
/// says little. The note fires when the car's price is at most <see cref="Threshold"/> of that median.
/// It is a note and not a red flag: it is not a <see cref="RedFlag"/>, never changes a score, and
/// does not mark a vehicle's research as flagged.</summary>
public static class SimilarPriceNote
{
    public const int YearWindow = 1;
    public const int MileageWindowMiles = 15_000;
    public const int MinimumSimilarCars = 5;
    public const decimal Threshold = 0.80m;

    /// <summary>The note for each car in <paramref name="cars"/> that earns one, keyed by VIN. A car with
    /// no price, too few similar cars, or a price above the threshold has no entry.</summary>
    public static IReadOnlyDictionary<string, string> For(IReadOnlyList<PriceComparable> cars)
    {
        var notes = new Dictionary<string, string>();
        foreach (IGrouping<(string Make, string Model), PriceComparable> sameModel in cars
            .Where(c => c.Price is not null)
            .GroupBy(c => (c.Make.ToUpperInvariant(), c.Model.ToUpperInvariant())))
        {
            foreach (PriceComparable car in sameModel)
            {
                if (NoteFor(car, sameModel) is string note)
                {
                    notes[car.Vin] = note;
                }
            }
        }

        return notes;
    }

    private static string? NoteFor(PriceComparable car, IEnumerable<PriceComparable> sameModel)
    {
        if (car.Price is not decimal price)
        {
            return null;
        }

        List<decimal> similarPrices = [.. sameModel
            .Where(other => other.Vin != car.Vin
                && Math.Abs(other.Year - car.Year) <= YearWindow
                && Math.Abs(other.Mileage - car.Mileage) <= MileageWindowMiles)
            .Select(other => other.Price)
            .OfType<decimal>()
            .Order()];
        if (similarPrices.Count < MinimumSimilarCars)
        {
            return null;
        }

        decimal median = Median(similarPrices);
        if (median <= 0m || price > median * Threshold)
        {
            return null;
        }

        decimal percentBelow = Math.Round((1m - (price / median)) * 100m, MidpointRounding.AwayFromZero);
        return $"priced {percentBelow:0}% below {similarPrices.Count} similar cars (median ${median:N0}); confirm the title";
    }

    private static decimal Median(IReadOnlyList<decimal> sorted) => sorted.Count % 2 == 1
        ? sorted[sorted.Count / 2]
        : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2m;
}
