namespace Odonomics.Domain;

/// <summary>The slice of a ledger vehicle the scorer needs; kept independent of the EF Core
/// entities so Domain has no dependency on Ledger.</summary>
public sealed record VehicleForScoring
{
    public required string Vin { get; init; }
    public required int Year { get; init; }
    public required string Make { get; init; }
    public required string Model { get; init; }
    public required int Mileage { get; init; }
    public required decimal? LowestCurrentPrice { get; init; }

    public string MakeModel => $"{Make} {Model}";
}
