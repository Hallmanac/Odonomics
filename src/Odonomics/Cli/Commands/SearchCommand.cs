using Microsoft.EntityFrameworkCore;
using Odonomics.Domain;
using Odonomics.Ledger;
using Odonomics.Secrets;
using Odonomics.Sources;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

public static class SearchCommand
{
    public static async Task<int> RunAsync(string scenarioPath, CancellationToken cancellationToken)
    {
        Scenario scenario = ScenarioLoader.Load(scenarioPath);
        var secrets = new SecretResolver();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        using OdonomicsDbContext db = LedgerFactory.Open();

        var currentRun = new RunEntity { Command = "search", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(currentRun);
        await db.SaveChangesAsync(cancellationToken);

        IReadOnlyList<ListingQuery> queries = ListingQuery.FromScenario(scenario);

        IListingSource[] sources =
        [
            new AutoDevSource(secrets.AutoDevApiKey, http),
            new MarketcheckSource(secrets.MarketcheckApiKey, http),
        ];

        var upsertService = new LedgerUpsertService(db);
        var sourcesCovered = new List<string>();
        int upserted = 0;
        foreach (IListingSource source in sources)
        {
            SourceResult result = await source.RunAsync(queries, cancellationToken);
            if (result.CouldNotRun)
            {
                AnsiConsole.MarkupLineInterpolated($"[yellow]{source.Name}: could not run ({result.CouldNotRunReason})[/]");
                continue;
            }

            sourcesCovered.AddRange(result.ModelsCovered.Select(model => RunSources.Key(source.Name, model)));
            AnsiConsole.MarkupLineInterpolated($"{source.Name}: {result.Candidates.Count} candidates, {result.Rejections.Count} rejected");
            foreach (ListingCandidate candidate in result.Candidates)
            {
                await upsertService.UpsertAsync(candidate, currentRun, cancellationToken);
                upserted++;
            }
        }

        currentRun.Sources = RunSources.Join(sourcesCovered);
        currentRun.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        AnsiConsole.MarkupLineInterpolated($"upserted {upserted} candidate sighting(s) from this run");

        var diffService = new LedgerDiffService(db);
        SearchDiff diff = await diffService.ComputeAsync(currentRun, cancellationToken);
        DiffRenderer.Render(diff);

        return 0;
    }
}
