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

        RunEntity? latestRun = await db.Runs.OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);

        List<VehicleEntity> vehicles = await db.Vehicles
            .Include(v => v.Postings).ThenInclude(p => p.PriceObservations)
            .ToListAsync(cancellationToken);

        var scores = new List<Score>();
        foreach (VehicleEntity vehicle in vehicles)
        {
            decimal? lowestCurrentPrice = LowestCurrentPrice(vehicle, latestRun);
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

    /// <summary>The lowest latest price among this vehicle's postings that were still active as
    /// of the most recent run (LastSeen equals that run's own timestamp; see
    /// LedgerUpsertService). With no runs yet every posting counts, since there is nothing to
    /// compare against.</summary>
    private static decimal? LowestCurrentPrice(VehicleEntity vehicle, RunEntity? latestRun)
    {
        IEnumerable<PostingEntity> activePostings = latestRun is null
            ? vehicle.Postings
            : vehicle.Postings.Where(p => p.LastSeen == latestRun.StartedAt);

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
