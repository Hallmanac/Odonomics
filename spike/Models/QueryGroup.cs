namespace Spike.Models;

public sealed record QueryGroup(string Make, string Model, int YearMin, int? YearMax)
{
    public int MaxMileage => 100_000;
    public int RadiusMiles => 50;
    public string Zip => "32114";

    public static readonly IReadOnlyList<QueryGroup> All =
    [
        new QueryGroup("Honda", "Insight", 2019, 2022),
        new QueryGroup("Toyota", "Corolla Hybrid", 2020, 2021),
        new QueryGroup("Toyota", "Camry Hybrid", 2018, 2019),
        new QueryGroup("Toyota", "Prius", 2020, null),
    ];

    public bool MatchesYear(int? year) =>
        year is not null && year >= YearMin && (YearMax is null || year <= YearMax);

    public bool MatchesMileage(int? mileage) => mileage is null || mileage <= MaxMileage;

    private bool IsHybridVariant => Model.Contains("Hybrid", StringComparison.OrdinalIgnoreCase);

    private string BaseModelName => IsHybridVariant
        ? Model[..Model.IndexOf(" Hybrid", StringComparison.OrdinalIgnoreCase)]
        : Model;

    /// <summary>
    /// Whether an extracted candidate actually is what this group asked a site for. Aggregators
    /// and retailers were found on day one to silently ignore an unrecognized make/model facet
    /// (e.g. a guessed "corolla_hybrid" slug) and fall back to showing generic inventory for the
    /// make, so every page-walk candidate is re-checked here against the query rather than
    /// trusted just because it came back from a request built for this group.
    /// </summary>
    public bool MatchesExtractedVehicle(string? make, string? model, string? trim, int? year, int? mileage)
    {
        if (!MatchesYear(year) || !MatchesMileage(mileage))
        {
            return false;
        }

        if (make is not null && !make.Contains(Make, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var modelAndTrim = $"{model} {trim}";
        if (!modelAndTrim.Contains(BaseModelName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsHybridVariant && !modelAndTrim.Contains("Hybrid", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
