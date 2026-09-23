using Microsoft.EntityFrameworkCore;
using Odonomics.Domain;
using Odonomics.Ledger;
using Spectre.Console;

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
            .Include(v => v.Postings).ThenInclude(p => p.Dealer)
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
                DealerGrade = DealerGradeSummary(vehicle),
                OnlyFGradedDealers = vehicle.Postings.Count > 0 && vehicle.Postings.All(p => p.Dealer?.Grade?.StartsWith('F') == true),
            };
            scores.Add(Scorer.Score(forScoring, scenario));
            research[vehicle.Vin] = ResearchStatusFor(vinRecordsByVin.GetValueOrDefault(vehicle.Vin), lowestCurrentPrice);
        }

        RankRenderer.Render(AnsiConsole.Console, scores, budget, research, scenario.TargetMonthlyBudgets);
        return 0;
    }

    private static ResearchStatus ResearchStatusFor(VinRecordEntity? record, decimal? currentPrice)
    {
        if (record?.ResearchedAt is not DateTimeOffset researchedAt)
        {
            return new ResearchStatus(Researched: false, ResearchedAt: null, HasRedFlag: false, RecallsKnown: false, RecallCount: 0);
        }

        IReadOnlyList<RedFlag> redFlags = VinResearchService.RedFlagsForCached(record, currentPrice);
        return new ResearchStatus(
            Researched: true,
            ResearchedAt: researchedAt,
            HasRedFlag: redFlags.Count > 0,
            RecallsKnown: record.RecallsRawJson is not null,
            RecallCount: record.OpenRecallCount);
    }

    private static string? DealerGradeSummary(VehicleEntity vehicle)
    {
        List<string> grades = [.. vehicle.Postings
            .Where(p => p.Dealer?.Grade is not null)
            .Select(p => p.Dealer!.Grade!)
            .Distinct()];
        return grades.Count == 0 ? null : string.Join("/", grades);
    }
}
