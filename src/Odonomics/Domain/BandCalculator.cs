namespace Odonomics.Domain;

/// <summary>
/// Evaluates a cost function once at every parameter's expected value for the band's midpoint,
/// then once at every corner of the loose parameters' min/max box for the band's low and high.
/// With a single loose parameter this reduces to evaluating the low and high ends of that
/// parameter's own range, which is what the money-definitions contract calls a "band whose ends
/// equal the pinned results" at each end. With more than one loose parameter at once it is a
/// bounding box over the corners rather than a true global min/max, which is an accepted
/// approximation for v0: nothing in this scenario has enough simultaneously loose parameters for
/// the corner count (2^loose-count) to matter.
/// </summary>
public static class BandCalculator
{
    public static Band Compute(IReadOnlyList<Parameter> parameters, Func<IReadOnlyList<decimal>, decimal> evaluate)
    {
        decimal[] expectedValues = [.. parameters.Select(p => p.Expected)];
        decimal expected = evaluate(expectedValues);

        int[] looseIndexes = [.. parameters.Select((p, i) => (p, i)).Where(x => x.p.IsLoose).Select(x => x.i)];
        if (looseIndexes.Length == 0)
        {
            return Band.Point(expected);
        }

        decimal low = decimal.MaxValue;
        decimal high = decimal.MinValue;
        int cornerCount = 1 << looseIndexes.Length;
        for (int mask = 0; mask < cornerCount; mask++)
        {
            decimal[] values = [.. expectedValues];
            for (int bit = 0; bit < looseIndexes.Length; bit++)
            {
                int paramIndex = looseIndexes[bit];
                bool useHigh = (mask & (1 << bit)) != 0;
                values[paramIndex] = useHigh ? parameters[paramIndex].High : parameters[paramIndex].Low;
            }

            decimal result = evaluate(values);
            low = Math.Min(low, result);
            high = Math.Max(high, result);
        }

        return new Band(low, expected, high);
    }
}
