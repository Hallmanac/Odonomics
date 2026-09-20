using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Odonomics.Domain;
using Odonomics.Extraction;
using Odonomics.Ledger;
using Odonomics.Secrets;
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

        if (!await CdpAvailability.IsListeningAsync(ChromeLaunchLine.DebugPort, cancellationToken))
        {
            AnsiConsole.MarkupLine("[yellow]nothing is listening on the CDP debugging port. Launch Chrome with this line, then run `odo walk` again:[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine(ChromeLaunchLine.Build());
            return 1;
        }

        Scenario scenario = ScenarioLoader.Load(scenarioPath);
        string makeModel = modelOverride ?? scenario.Filters.AllowedModels.FirstOrDefault()
            ?? throw new InvalidOperationException("the scenario has no allowed models to walk");
        int spaceIndex = makeModel.IndexOf(' ');
        string make = spaceIndex < 0 ? makeModel : makeModel[..spaceIndex];
        string model = spaceIndex < 0 ? "" : makeModel[(spaceIndex + 1)..];

        var secrets = new SecretResolver();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        ExtractionClient extraction = ExtractionClient.FromAppDirectory(secrets.AnthropicApiKey, http);

        using OdonomicsDbContext db = LedgerFactory.Open();
        RunEntity? previousRun = await db.Runs.OrderByDescending(r => r.Id).FirstOrDefaultAsync(cancellationToken);
        var currentRun = new RunEntity { Command = $"walk {site.Name}", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(currentRun);
        await db.SaveChangesAsync(cancellationToken);

        var recorder = new WalkRecorder(DataDirectory.Resolve(), site.Name, currentRun.StartedAt);
        var pacing = new WalkPacing(Random.Shared);
        var upsertService = new LedgerUpsertService(db);

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.ConnectOverCDPAsync($"http://localhost:{ChromeLaunchLine.DebugPort}");
        IBrowserContext context = browser.Contexts.FirstOrDefault() ?? await browser.NewContextAsync();
        IPage page = context.Pages.FirstOrDefault() ?? await context.NewPageAsync();

        string searchUrl = site.BuildSearchUrl(make, model, scenario.Zip, scenario.RadiusMiles);
        AnsiConsole.MarkupLineInterpolated($"opening search page for {make} {model} on {site.Name}");
        await page.GotoAsync(searchUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await HandleChallengeIfPresentAsync(page, cancellationToken);

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
            IPage detailPage = await context.NewPageAsync();
            try
            {
                await detailPage.GotoAsync(detailUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                await HandleChallengeIfPresentAsync(detailPage, cancellationToken);
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

                var candidate = new ListingCandidate
                {
                    Vin = outcome.Result.Vin,
                    Source = site.Name,
                    Url = detailUrl,
                    Year = outcome.Result.Year.Value,
                    Make = outcome.Result.Make ?? make,
                    Model = outcome.Result.Model ?? model,
                    Trim = outcome.Result.Trim,
                    Price = outcome.Result.Price.Value,
                    Mileage = outcome.Result.Mileage.Value,
                };
                await upsertService.UpsertAsync(candidate, currentRun, cancellationToken);
                upserted++;
            }
            finally
            {
                await detailPage.CloseAsync();
            }
        }

        currentRun.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        AnsiConsole.MarkupLineInterpolated($"upserted {upserted} vehicle(s), dropped {droppedNoVin} candidate(s) with no VIN");

        var diffService = new LedgerDiffService(db);
        SearchDiff diff = await diffService.ComputeAsync(currentRun, previousRun, cancellationToken);
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

    /// <summary>Beeps (a terminal bell, which works the same on every platform, unlike
    /// Console.Beep), prints the prompt, blocks on Enter, then reloads the page once. Settles for
    /// a few seconds before the first check, then a few more if it still looks blocked: a
    /// bot-defense JS challenge often resolves itself within a few seconds in a real Chrome tab
    /// (the same settle-then-recheck the spike's PageWalkEngine proved out), so checking the
    /// instant navigation completes risks a false positive on a page that just hasn't finished
    /// rendering yet.</summary>
    private static async Task HandleChallengeIfPresentAsync(IPage page, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
        string title = await page.TitleAsync();
        string bodyText = await page.EvaluateAsync<string>("() => document.body.innerText");
        if (ChallengeDetector.IsChallenge(title, bodyText))
        {
            await Task.Delay(TimeSpan.FromSeconds(8), cancellationToken);
            title = await page.TitleAsync();
            bodyText = await page.EvaluateAsync<string>("() => document.body.innerText");
        }

        if (!ChallengeDetector.IsChallenge(title, bodyText))
        {
            return;
        }

        Console.Write('\a');
        AnsiConsole.MarkupLineInterpolated($"[bold red]challenge on {page.Url}: solve it in the browser, then press Enter[/]");
        await Console.In.ReadLineAsync(cancellationToken);
        await page.ReloadAsync();
    }
}
