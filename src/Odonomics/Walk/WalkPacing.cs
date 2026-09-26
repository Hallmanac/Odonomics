namespace Odonomics.Walk;

/// <summary>The pacing the brief specifies: a few scroll steps with a pause between each, a dwell
/// on the search page, then a random gap between detail pages.</summary>
public sealed class WalkPacing(Random random)
{
    public int ScrollSteps { get; init; } = 4;
    public TimeSpan ScrollPauseMin { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan ScrollPauseMax { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan DwellMin { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan DwellMax { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan DetailGapMin { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan DetailGapMax { get; init; } = TimeSpan.FromSeconds(45);
    public TimeSpan PairGapMin { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan PairGapMax { get; init; } = TimeSpan.FromSeconds(45);

    public TimeSpan RandomScrollPause() => RandomBetween(ScrollPauseMin, ScrollPauseMax);

    public TimeSpan RandomDwell() => RandomBetween(DwellMin, DwellMax);

    public TimeSpan RandomDetailGap() => RandomBetween(DetailGapMin, DetailGapMax);

    /// <summary>The gap before opening the next (site, model) pair's search page, whether that
    /// next pair is another model on the same site or the first model of the next site.</summary>
    public TimeSpan RandomPairGap() => RandomBetween(PairGapMin, PairGapMax);

    private TimeSpan RandomBetween(TimeSpan min, TimeSpan max) =>
        min + (max - min) * random.NextDouble();
}
