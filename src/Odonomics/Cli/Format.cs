using Odonomics.Domain;
using Spectre.Console;

namespace Odonomics.Cli;

/// <summary>Locale-independent formatting for money and bands, so output is identical on every
/// machine regardless of the host's own culture settings (the project ships InvariantGlobalization).</summary>
public static class Format
{
    public static string Money(decimal value) => $"${value:N0}";

    public static string MoneyCents(decimal value) => $"${value:N2}";

    /// <summary>A point band renders as one figure; a range renders "$low-$high" so it still fits
    /// a narrow column at 80 total columns.</summary>
    public static string Band(Band band) =>
        band.IsRange ? $"{Money(band.Low)}-{Money(band.High)}" : Money(band.Expected);

    /// <summary>Escapes a value bound for Table.AddRow, which parses every string it's given as
    /// Spectre markup: a make, model, trim, or note that came from a scraped page or an operator
    /// keystroke can carry a literal '[' (e.g. a dealer's own "[SOLD]" prefix), which would
    /// otherwise be read as the start of a markup tag.</summary>
    public static string Cell(string value) => Markup.Escape(value);

    /// <summary>Bounds a value to at most <paramref name="maxWidth"/> characters, replacing the
    /// tail with an ellipsis when it's cut short. A fixed-width table column never wraps a row
    /// onto a second line no matter how long a scraped or scenario-file value turns out to be,
    /// only Spectre's own word-wrapping does that, so the value has to already fit before it
    /// reaches the table.</summary>
    public static string Truncate(string value, int maxWidth)
    {
        if (value.Length <= maxWidth)
        {
            return value;
        }

        return maxWidth <= 1 ? new string('…', Math.Max(maxWidth, 0)) : $"{value[..(maxWidth - 1)]}…";
    }

    /// <summary>Rounds each part to whole dollars so the parts add up to the total's own rounding,
    /// which independent rounding does not guarantee: the whole dollars the total is short of the
    /// parts' floors go to the parts with the largest fractional remainders.</summary>
    public static decimal[] RoundedToTotal(decimal[] parts, decimal total)
    {
        decimal[] floors = [.. parts.Select(Math.Floor)];
        int shortfall = WholeDollars(total) - (int)floors.Sum();
        HashSet<int> roundedUp = TopByRemainder(parts, floors, [.. Enumerable.Range(0, parts.Length)], shortfall);

        return [.. floors.Select((floor, i) => roundedUp.Contains(i) ? floor + 1m : floor)];
    }

    /// <summary>The loan payment and each running cost of a vehicle's during-loan monthly cost, in the
    /// order they are printed, rounded to whole dollars so each side's lines add up to
    /// <see cref="CostBreakdown.DuringLoanMonthly"/>'s own rounded low and high (see
    /// <see cref="RoundedToTotals"/>). Both `odo show` and `odo rank --detail` print their figures
    /// from here, so the two commands cannot show different payments for one vehicle.</summary>
    public static IReadOnlyList<RoundedLine> DuringLoanLines(CostBreakdown cost)
    {
        Domain.Band[] parts = [cost.Payment, Domain.Band.Point(cost.InsuranceMonthly), cost.Fuel, cost.Maintenance, cost.Reserve];
        string[] labels = ["Loan payment", "Insurance", "Fuel", "Maintenance", "Reserve"];
        (decimal[] lows, decimal[] highs) = RoundedToTotals(parts, cost.DuringLoanMonthly);

        return [.. parts.Select((part, i) => new RoundedLine(labels[i], lows[i], highs[i], part.IsRange))];
    }

    /// <summary>Rounds each part band to whole dollars so the low ends add up to the total's rounded
    /// low and the high ends add up to its rounded high, each side apportioned against its own
    /// unrounded figures. A part that is not a range prints one figure on both sides, so it takes
    /// the same rounding on both; how many of those parts round up is the count closest to the
    /// fixed parts' own remainders that still leaves both sides a workable split of round-ups
    /// among the ranged parts. A ranged part whose floors match on both ends has to round up on the
    /// high end whenever it rounds up on the low end, or it would print inverted; the split picks
    /// how many such parts the low side may round up so that always holds. This assumes each
    /// total's low and high are the sums of the parts' lows and highs, which holds for a
    /// <see cref="CostBreakdown"/>; a total that is not can only be matched as nearly as the
    /// parts' round-ups reach.</summary>
    public static (decimal[] Lows, decimal[] Highs) RoundedToTotals(Domain.Band[] parts, Domain.Band total)
    {
        decimal[] lowParts = [.. parts.Select(part => part.Low)];
        decimal[] highParts = [.. parts.Select(part => part.High)];
        decimal[] lowFloors = [.. lowParts.Select(Math.Floor)];
        decimal[] highFloors = [.. highParts.Select(Math.Floor)];
        int lowRoundUps = WholeDollars(total.Low) - (int)lowFloors.Sum();
        int highRoundUps = WholeDollars(total.High) - (int)highFloors.Sum();

        int[] fixedParts = [.. Enumerable.Range(0, parts.Length).Where(i => !parts[i].IsRange)];
        int[] ranged = [.. Enumerable.Range(0, parts.Length).Where(i => parts[i].IsRange)];
        int[] sharedFloor = [.. ranged.Where(i => lowFloors[i] == highFloors[i])];
        int spreadCount = ranged.Length - sharedFloor.Length;

        // A split of round-ups is workable when neither side needs more (or fewer) than its ranged
        // parts can take, and the low side's parts that share a floor with their high end fit within
        // the high side's round-ups.
        bool Workable(int fixedCount)
        {
            int low = lowRoundUps - fixedCount;
            int high = highRoundUps - fixedCount;
            return low >= 0 && high >= 0 && low <= ranged.Length && high <= ranged.Length && low <= high + spreadCount;
        }

        int fixedWanted = WholeDollars(fixedParts.Sum(i => lowParts[i] - lowFloors[i]));
        int fixedMin = Math.Max(0, Math.Max(lowRoundUps, highRoundUps) - ranged.Length);
        int fixedMax = Math.Max(fixedMin, Math.Min(fixedParts.Length, Math.Min(lowRoundUps, highRoundUps)));
        int fixedRoundUps = Enumerable.Range(0, fixedParts.Length + 1)
            .Where(Workable)
            .OrderBy(count => Math.Abs(count - fixedWanted))
            .Select(count => (int?)count)
            .FirstOrDefault() ?? Math.Clamp(fixedWanted, fixedMin, fixedMax);

        int lowCount = Math.Clamp(lowRoundUps - fixedRoundUps, 0, ranged.Length);
        int highCount = Math.Clamp(highRoundUps - fixedRoundUps, 0, ranged.Length);

        // Of the low side's round-ups, how many go to parts that share a floor: as many as the
        // remainders favor, held to what the high side can repeat and to what the spread parts
        // cannot absorb.
        int naturalShared = TopByRemainder(lowParts, lowFloors, ranged, lowCount).Count(sharedFloor.Contains);
        int mostShared = Math.Min(sharedFloor.Length, Math.Min(lowCount, highCount));
        int fewestShared = Math.Min(mostShared, Math.Max(0, lowCount - spreadCount));
        int lowShared = Math.Clamp(naturalShared, fewestShared, mostShared);

        HashSet<int> fixedUp = TopByRemainder(lowParts, lowFloors, fixedParts, fixedRoundUps);
        HashSet<int> lowUp = [
            .. TopByRemainder(lowParts, lowFloors, sharedFloor, lowShared),
            .. TopByRemainder(lowParts, lowFloors, [.. ranged.Except(sharedFloor)], lowCount - lowShared)];
        HashSet<int> highUp = [
            .. lowUp.Where(sharedFloor.Contains),
            .. TopByRemainder(highParts, highFloors, [.. ranged.Where(i => !lowUp.Contains(i) || !sharedFloor.Contains(i))], highCount - lowShared)];

        decimal[] lows = [.. lowFloors.Select((floor, i) => floor + (fixedUp.Contains(i) || lowUp.Contains(i) ? 1m : 0m))];
        decimal[] highs = [.. highFloors.Select((floor, i) => floor + (fixedUp.Contains(i) || highUp.Contains(i) ? 1m : 0m))];
        return (lows, highs);
    }

    private static int WholeDollars(decimal value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>The <paramref name="count"/> candidates with the largest fractional remainders.</summary>
    private static HashSet<int> TopByRemainder(decimal[] parts, decimal[] floors, int[] candidates, int count) =>
        [.. candidates
            .OrderByDescending(i => parts[i] - floors[i])
            .Take(Math.Max(count, 0))];
}

/// <summary>One whole-dollar line of a reconciled cost: a range prints "$low-$high", anything else
/// the one figure.</summary>
public readonly record struct RoundedLine(string Label, decimal Low, decimal High, bool IsRange)
{
    public string Figure => IsRange
        ? $"{Format.Money(Low)}-{Format.Money(High)}"
        : Format.Money(Low);
}
