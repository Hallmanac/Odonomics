using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Odonomics.Domain;
using Odonomics.Extraction;
using Odonomics.Ledger;
using Odonomics.Secrets;
using Odonomics.Sources;
using Odonomics.Walk;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

/// <summary>
/// The assisted browser walk: connects over CDP to a browser the operator already launched by
/// hand, never launches one itself. One search page, a capped number of detail pages, paced like
/// a person, pausing for the operator on a challenge page. See the brief for the full pacing
/// spec; this command implements it as literally as an automated agent can, since the actual
/// bot-defense behavior can only be proven by the operator running it against a real browser.
/// </summary>
public static class WalkCommand
{
    public static async Task<int> RunAsync(string scenarioPath, string siteName, string? modelOverride, int maxDetailPages, CancellationToken cancellationToken)
    {
        WalkSite? site = WalkSites.Find(siteName);
        if (site is null)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]unknown walk target \"{siteName}\"; expected cars.com or carvana[/]");
            return 1;
        }

        if (!await CdpConnection.IsAvailableAsync(cancellationToken))
        {
            CdpConnection.PrintUnavailableMessage();
            return 1;
        }

        Scenario scenario = ScenarioLoader.Load(scenarioPath);
        string makeModel = modelOverride ?? scenario.Filters.AllowedModels.FirstOrDefault()
            ?? throw new InvalidOperationException("the scenario has no allowed models to walk");

        // Normalized to the scenario's own casing when the override matches one of its allowed
        // models case-insensitively (an operator typing "honda insight" is still asking for the
        // scenario's "Honda Insight"): every other model value in the ledger — a search run's, or
        // this run's own coverage token and Vehicle.Model — comes from the scenario's own casing,
        // and a walk that stored a differently-cased Model would stamp a "source:model" token
        // nothing else ever matches, silently breaking the rank view's and diff's coverage lookups
        // for that VIN from then on.
        makeModel = scenario.Filters.AllowedModels
            .FirstOrDefault(m => string.Equals(m, makeModel, StringComparison.OrdinalIgnoreCase))
            ?? makeModel;

        int spaceIndex = makeModel.IndexOf(' ');
        if (spaceIndex < 0)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]--model must be \"Make Model\" (e.g. \"Honda Insight\"), got \"{makeModel}\"[/]");
            return 1;
        }

        string make = makeModel[..spaceIndex];
        string model = makeModel[(spaceIndex + 1)..];
        var query = new ListingQuery(make, model, scenario.Filters.MinYearFor(makeModel), scenario.Zip, scenario.RadiusMiles, scenario.Filters.MaxMileage);

        var secrets = new SecretResolver();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        ExtractionClient extraction = ExtractionClient.FromAppDirectory(secrets.AnthropicApiKey, http);

        using OdonomicsDbContext db = LedgerFactory.Open();
        var currentRun = new RunEntity { Command = $"walk {site.Name}", Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(currentRun);
        await db.SaveChangesAsync(cancellationToken);

        var recorder = new WalkRecorder(DataDirectory.Resolve(), site.Name, currentRun.StartedAt);
        var pacing = new WalkPacing(Random.Shared);
        var upsertService = new LedgerUpsertService(db);

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.ConnectOverCDPAsync($"http://localhost:{ChromeLaunchLine.DebugPort}");
        ICDPSession browserCdp = await browser.NewBrowserCDPSessionAsync();
        IBrowserContext context = browser.Contexts.FirstOrDefault()
            ?? throw new InvalidOperationException("the CDP connection exposed no browser context to attach to; is the browser still running?");
        IPage page = context.Pages.FirstOrDefault() ?? await BackgroundTabs.OpenAsync(browserCdp, context);

        string searchUrl = site.BuildSearchUrl(make, model, scenario.Zip, scenario.RadiusMiles);
        AnsiConsole.MarkupLineInterpolated($"opening search page for {make} {model} on {site.Name}");
        await page.GotoAsync(searchUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await CdpConnection.HandleChallengeIfPresentAsync(page, cancellationToken);

        await ScrollInStepsAsync(page, pacing, cancellationToken);
        TimeSpan dwell = pacing.RandomDwell();
        AnsiConsole.MarkupLineInterpolated($"dwelling {dwell.TotalSeconds:0}s on the search page");
        await Task.Delay(dwell, cancellationToken);

        string searchBodyText = await page.EvaluateAsync<string>("() => document.body.innerText");
        await recorder.WriteAsync("search.txt", searchBodyText, cancellationToken);

        string[] hrefs = await page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('a')).map(a => a.href)");
        List<string> detailLinks = [.. hrefs.Where(h => site.DetailUrlPattern.IsMatch(h)).Distinct().Take(maxDetailPages)];
        AnsiConsole.MarkupLineInterpolated($"found {detailLinks.Count} detail link(s) to visit");

        int upserted = 0;
        int droppedNoVin = 0;
        for (int i = 0; i < detailLinks.Count; i++)
        {
            if (i > 0)
            {
                TimeSpan gap = pacing.RandomDetailGap();
                await Task.Delay(gap, cancellationToken);
            }

            string detailUrl = detailLinks[i];
            IPage detailPage = await BackgroundTabs.OpenAsync(browserCdp, context);
            try
            {
                await detailPage.GotoAsync(detailUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                await CdpConnection.HandleChallengeIfPresentAsync(detailPage, cancellationToken);
                await detailPage.EvaluateAsync("() => window.scrollBy(0, window.innerHeight)");
                await Task.Delay(pacing.RandomScrollPause(), cancellationToken);

                string bodyText = await detailPage.EvaluateAsync<string>("() => document.body.innerText");
                await recorder.WriteAsync($"detail-{i + 1}.txt", bodyText, cancellationToken);

                ExtractionOutcome outcome = await extraction.ExtractAsync(bodyText, cancellationToken);
                if (outcome.Error is not null || outcome.Result is null)
                {
                    AnsiConsole.MarkupLineInterpolated($"[yellow]detail {i + 1}: extraction failed ({outcome.Error})[/]");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(outcome.Result.Vin))
                {
                    droppedNoVin++;
                    AnsiConsole.MarkupLineInterpolated($"[grey]detail {i + 1}: dropped, no VIN found on the page[/]");
                    continue;
                }

                if (outcome.Result.Year is null || outcome.Result.Price is null || outcome.Result.Mileage is null)
                {
                    AnsiConsole.MarkupLineInterpolated($"[yellow]detail {i + 1}: dropped, missing year/price/mileage ({outcome.Result.Vin})[/]");
                    continue;
                }

                if (!query.MatchesExtractedVehicle(outcome.Result.Make, outcome.Result.Model, outcome.Result.Trim))
                {
                    AnsiConsole.MarkupLineInterpolated($"[grey]detail {i + 1}: dropped, doesn't match {make} {model} ({outcome.Result.Year} {outcome.Result.Make} {outcome.Result.Model} {outcome.Result.Trim})[/]");
                    continue;
                }

                var candidate = new ListingCandidate
                {
                    Vin = outcome.Result.Vin,
                    Source = site.Name,
                    Url = detailUrl,
                    Year = outcome.Result.Year.Value,
                    Make = outcome.Result.Make ?? make,
                    // The canonical model this walk was launched for, not the extraction's own
                    // free-text model field: VehicleEntity.Model has to match the model half of the
                    // "source:model" token this run stamps on itself (RunSources.Key below), and the
                    // MatchesExtractedVehicle check above has already dropped any candidate that
                    // isn't actually this walk's model (e.g. a gas Camry on a Camry Hybrid walk),
                    // so stamping the canonical model here never mislabels a vehicle the page
                    // showed for a different reason (its own text reading as a trimmed variant like
                    // "Insight EX").
                    Model = model,
                    Trim = outcome.Result.Trim,
                    Price = outcome.Result.Price.Value,
                    Mileage = outcome.Result.Mileage.Value,
                };
                await upsertService.UpsertAsync(candidate, currentRun, cancellationToken);
                upserted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AnsiConsole.MarkupLineInterpolated($"[yellow]detail {i + 1}: failed to load ({ex.Message})[/]");
            }
            finally
            {
                await detailPage.CloseAsync();
            }
        }

        // Only stamped once the walk actually reaches here without throwing or being cancelled:
        // this run covered exactly this one model on this one site, never the site as a whole, so
        // an earlier throw (a bad GotoAsync, a cancelled challenge wait) or a walk of a different
        // model must never leave this or any other model's postings looking checked when they
        // were not.
        currentRun.Sources = RunSources.Key(site.Name, model);
        currentRun.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        AnsiConsole.MarkupLineInterpolated($"upserted {upserted} vehicle(s), dropped {droppedNoVin} candidate(s) with no VIN");

        var diffService = new LedgerDiffService(db);
        SearchDiff diff = await diffService.ComputeAsync(currentRun, cancellationToken);
        DiffRenderer.Render(diff);

        return 0;
    }

    private static async Task ScrollInStepsAsync(IPage page, WalkPacing pacing, CancellationToken cancellationToken)
    {
        for (int step = 0; step < pacing.ScrollSteps; step++)
        {
            await page.EvaluateAsync("() => window.scrollBy(0, window.innerHeight * 0.8)");
            await Task.Delay(pacing.RandomScrollPause(), cancellationToken);
        }
    }
}
