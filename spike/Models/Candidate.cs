namespace Spike.Models;

public sealed record Candidate
{
    public required string Source { get; init; }
    public string? Vin { get; init; }
    public int? Year { get; init; }
    public string? Make { get; init; }
    public string? Model { get; init; }
    public string? Trim { get; init; }
    public decimal? Price { get; init; }
    public int? Mileage { get; init; }
    public string? Url { get; init; }

    /// <summary>Path (relative to repo root) of the raw recorded response this candidate was read from.</summary>
    public string? RawRecordPath { get; init; }

    /// <summary>True when the candidate went through the model extraction step (a page-walk candidate).</summary>
    public bool WasExtracted { get; init; }

    /// <summary>
    /// True when the extracted vehicle actually matches the query group it was fetched for. A
    /// page-walk candidate can be false here (site ignored the model filter) while still being a
    /// legitimate data point for the extraction-accuracy hand-check: extraction can be judged
    /// correct or wrong independently of whether the site returned the right car.
    /// </summary>
    public bool MatchesQuery { get; init; } = true;
}
