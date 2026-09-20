using Microsoft.EntityFrameworkCore;
using Odonomics.Domain;
using Odonomics.Ledger;

namespace Odonomics.Cli.Commands;

public static class RankCommand
{
    public static async Task<int> RunAsync(string scenarioPath, decimal? budget, int? term, CancellationToken cancellationToken)
    {
        Scenario scenario = ScenarioLoader.Load(scenarioPath);
        if (term is int overrideTerm)
        {
            scenario = scenario with { TermMonths = overrideTerm };
        }

        using OdonomicsDbContext db = LedgerFactory.Open();

        List<RunEntity> runs = await db.Runs.ToListAsync(cancellationToken);
        Dictionary<string, DateTimeOffset> latestCoverageBySource = RunSources.LatestCoverageBySource(runs);

        List<VehicleEntity> vehicles = await db.Vehicles
            .Include(v => v.Postings).ThenInclude(p => p.PriceObservations)
            .ToListAsync(cancellationToken);

        var scores = new List<Score>();
        foreach (VehicleEntity vehicle in vehicles)
        {
            decimal? lowestCurrentPrice = LowestCurrentPrice(vehicle, latestCoverageBySource);
            var forScoring = new VehicleForScoring
            {
                Vin = vehicle.Vin,
                Year = vehicle.Year,
                Make = vehicle.Make,
                Model = vehicle.Model,
                Mileage = vehicle.Mileage,
                LowestCurrentPrice = lowestCurrentPrice,
            };
            scores.Add(Scorer.Score(forScoring, scenario));
        }

        RankRenderer.Render(scores, budget);
        return 0;
    }

    /// <summary>The lowest latest price among this vehicle's postings that were still active as of
    /// the most recent run that covered that posting's own source (LastSeen equals that run's own
    /// timestamp; see LedgerUpsertService). A posting whose source no run has ever covered, or
    /// whose most recent covering run was also the one that saw it, still counts; only a posting
    /// whose source was checked more recently without seeing it again drops out.</summary>
    private static decimal? LowestCurrentPrice(VehicleEntity vehicle, IReadOnlyDictionary<string, DateTimeOffset> latestCoverageBySource)
    {
        IEnumerable<PostingEntity> activePostings = vehicle.Postings.Where(p =>
            !latestCoverageBySource.TryGetValue(p.Source, out DateTimeOffset latestCoverage) || p.LastSeen == latestCoverage);

        decimal?[] latestPrices =
        [
            .. activePostings.Select(p => p.PriceObservations
                .OrderByDescending(o => o.ObservedAt)
                .Select(o => (decimal?)o.Price)
                .FirstOrDefault()),
        ];

        decimal?[] known = [.. latestPrices.Where(p => p is not null)];
        return known.Length == 0 ? null : known.Min();
    }
}
