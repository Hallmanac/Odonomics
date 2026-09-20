namespace Odonomics.Domain;

/// <summary>
/// One cost-model input: either a single, pinned value, or a loose range the engine must
/// treat as a band rather than a point. See <see cref="Band"/> and <see cref="BandCalculator"/>.
/// </summary>
public abstract record Parameter
{
    public abstract decimal Expected { get; }
    public abstract decimal Low { get; }
    public abstract decimal High { get; }
    public abstract bool IsLoose { get; }

    public static Parameter Pinned(decimal value) => new PinnedParameter(value);

    public static Parameter Loose(decimal min, decimal max) => new LooseParameter(min, max);
}

public sealed record PinnedParameter(decimal Value) : Parameter
{
    public override decimal Expected => Value;
    public override decimal Low => Value;
    public override decimal High => Value;
    public override bool IsLoose => false;
}

public sealed record LooseParameter : Parameter
{
    public LooseParameter(decimal min, decimal max)
    {
        if (max < min)
        {
            throw new ArgumentException($"loose parameter max ({max}) must not be below min ({min})");
        }

        Min = min;
        Max = max;
    }

    public decimal Min { get; }
    public decimal Max { get; }

    public override decimal Expected => (Min + Max) / 2m;
    public override decimal Low => Min;
    public override decimal High => Max;
    public override bool IsLoose => true;
}

/// <summary>A cost figure that may be a single point (Low == Expected == High) or a range
/// produced when one or more of its inputs were <see cref="LooseParameter"/>.</summary>
public readonly record struct Band(decimal Low, decimal Expected, decimal High)
{
    public bool IsRange => Low != High;

    public static Band Point(decimal value) => new(value, value, value);
}
