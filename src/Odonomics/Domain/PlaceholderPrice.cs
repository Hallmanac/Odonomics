namespace Odonomics.Domain;

/// <summary>The asking price below which a listing's price is a placeholder rather than a real
/// asking price (auto.dev has returned 0 for a car with no price posted). The listing sources
/// reject a candidate below it, and the ledger readers ignore an already-stored observation below
/// it, so a placeholder can never rank a car first or set a budget figure.</summary>
public static class PlaceholderPrice
{
    public const decimal Floor = 1000m;

    public static bool IsBelowFloor(decimal price) => price < Floor;
}
