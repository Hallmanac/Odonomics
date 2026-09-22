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

        List<VinRecordEntity> vinRecords = await db.VinRecords.ToListAsync(cancellationToken);
        Dictionary<string, VinRecordEntity> vinRecordsByVin = vinRecords.ToDictionary(r => r.Vin);

        var scores = new List<Score>();
        var research = new Dictionary<string, ResearchStatus>();
        foreach (VehicleEntity vehicle in vehicles)
        {
            decimal? lowestCurrentPrice = VehiclePricing.LowestCurrentPrice(vehicle, latestCoverageBySource);
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
            research[vehicle.Vin] = ResearchStatusFor(vinRecordsByVin.GetValueOrDefault(vehicle.Vin), lowestCurrentPrice);
        }

        RankRenderer.Render(scores, budget, research);
        return 0;
    }

    private static ResearchStatus ResearchStatusFor(VinRecordEntity? record, decimal? currentPrice)
    {
        if (record?.ResearchedAt is not DateTimeOffset researchedAt || record.HistoryRawJson is null)
        {
            // Null HistoryRawJson means the history-based red flags were never evaluated, so this isn't "clean".
            return new ResearchStatus(Researched: false, ResearchedAt: null, HasRedFlag: false);
        }

        IReadOnlyList<string> redFlags = VinResearchService.RedFlagsForCached(record, currentPrice);
        return new ResearchStatus(Researched: true, ResearchedAt: researchedAt, HasRedFlag: redFlags.Count > 0);
    }
}
