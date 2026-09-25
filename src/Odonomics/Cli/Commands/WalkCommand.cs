using System.CommandLine;
using System.Web;
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
/// hand, never launches one itself. With no site argument it walks cars.com and then carvana; a
/// site argument narrows it to that one site. With no --model it walks every model in the
/// scenario's allowed list, in order, on whichever site(s) it's covering; --model narrows it to
/// that one model exactly, on whichever site(s) it's covering. --max caps matching detail pages
/// visited per site-and-model pair, not per run or raw page visits: a page rejected for not
/// matching the model, or one whose VIN this pair already saved through a different link, doesn't
/// spend the cap, so the walk can open more candidate links than --max to fill it. --max defaults
/// to 30 (<see cref="WalkPacing.DefaultMaxDetailPages"/>). See the brief for the full pacing spec;
/// this command implements it as literally as an automated agent can, since the actual bot-defense behavior can
/// only be proven by the operator running it against a real browser.
/// </summary>
public static class WalkCommand
{
    public static Option<int> CreateMaxOption() => new("--max")
    {
        Description = "maximum number of matching detail pages to visit per site-and-model pair; a page rejected for not matching the model doesn't count against it, so the walk may open more candidate links than this to reach it",
        DefaultValueFactory = _ => WalkPacing.DefaultMaxDetailPages,
    };

    public static async Task<int> RunAsync(string scenarioPath, string? siteName, string? modelOverride, int maxDetailPages, CancellationToken cancellationToken)
    {
        List<WalkSite> sites;
        if (siteName is null)
        {
            sites = [WalkSites.CarsCom, WalkSites.Carvana];
        }
        else
        {
            WalkSite? site = WalkSites.Find(siteName);
            if (site is null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]unknown walk target \"{siteName}\"; expected cars.com or carvana[/]");
                return 1;
            }

            sites = [site];
        }

        if (!await CdpConnection.IsAvailableAsync(cancellationToken))
        {
            CdpConnection.PrintUnavailableMessage();
            return 1;
        }

        Scenario scenario = ScenarioLoader.Load(scenarioPath);
        List<string> models = ResolveModels(scenario, modelOverride);
        if (models.Count == 0)
        {
            throw new InvalidOperationException("the scenario has no allowed models to walk");
        }

        foreach (string model in models)
        {
            try
            {
                MakeModel.Split(model);
            }
            catch (InvalidOperationException ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]{ex.Message}[/]");
                return 1;
            }
        }

        var secrets = new SecretResolver();
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        ExtractionClient extraction = ExtractionClient.FromAppDirectory(secrets.AnthropicApiKey, http);

        using OdonomicsDbContext db = LedgerFactory.Open();
        var currentRun = new RunEntity { Command = BuildCommandLabel(siteName, modelOverride), Sources = "", StartedAt = DateTimeOffset.UtcNow };
        db.Runs.Add(currentRun);
        await db.SaveChangesAsync(cancellationToken);

        string dataDirectory = DataDirectory.Resolve();
        var pacing = new WalkPacing(Random.Shared);
        var upsertService = new LedgerUpsertService(db);

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.ConnectOverCDPAsync($"http://localhost:{ChromeLaunchLine.DebugPort}");
        ICDPSession browserCdp = await browser.NewBrowserCDPSessionAsync();
        IBrowserContext context = browser.Contexts.FirstOrDefault()
            ?? throw new InvalidOperationException("the CDP connection exposed no browser context to attach to; is the browser still running?");
        IPage page = context.Pages.FirstOrDefault() ?? await BackgroundTabs.OpenAsync(browserCdp, context);

        List<WalkPairSummary> summaries = await WalkCoverage.RunAsync(
            currentRun,
            sites,
            models,
            (site, makeModel, ct) => WalkPairAsync(site, makeModel, page, browserCdp, context, scenario, extraction, upsertService, currentRun, dataDirectory, pacing, maxDetailPages, ct),
            (site, makeModel) => AnsiConsole.MarkupLineInterpolated($"walking {site.Name} for {makeModel}"),
            (site, makeModel, ex) => AnsiConsole.MarkupLineInterpolated($"[yellow]{site.Name} / {makeModel}: walk failed ({ex.Message})[/]"),
            ct => Task.Delay(pacing.RandomPairGap(), ct),
            ct => db.SaveChangesAsync(ct),
            cancellationToken);

        currentRun.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        RenderSummary(summaries);

        var diffService = new LedgerDiffService(db);
        SearchDiff diff = await diffService.ComputeAsync(currentRun, cancellationToken);
        DiffRenderer.Render(diff);

        return summaries.Any(s => s.Completed) ? 0 : 1;
    }

    /// <summary>Resolves which "Make Model" values to walk. --model narrows to exactly one,
    /// normalized to the scenario's own casing when it matches one of the allowed models
    /// case-insensitively (an operator typing "honda insight" is still asking for the scenario's
    /// "Honda Insight"): every other model value in the ledger — a search run's, or this run's own
    /// coverage token and Vehicle.Model — comes from the scenario's own casing, and a walk that
    /// stored a differently-cased Model would stamp a "source:model" token nothing else ever
    /// matches, silently breaking the rank view's and diff's coverage lookups for that VIN from
    /// then on. With no --model, every allowed model is walked, in the scenario's own order.</summary>
    private static List<string> ResolveModels(Scenario scenario, string? modelOverride)
    {
        if (modelOverride is null)
        {
            return [.. scenario.Filters.AllowedModels];
        }

        string normalized = scenario.Filters.AllowedModels
            .FirstOrDefault(m => string.Equals(m, modelOverride, StringComparison.OrdinalIgnoreCase))
            ?? modelOverride;
        return [normalized];
    }

    private static string BuildCommandLabel(string? siteName, string? modelOverride)
    {
        string label = siteName is null ? "walk" : $"walk {siteName}";
        return modelOverride is null ? label : $"{label} --model \"{modelOverride}\"";
    }

    private static async Task<WalkPairOutcome> WalkPairAsync(
        WalkSite site,
        string makeModel,
        IPage page,
        ICDPSession browserCdp,
        IBrowserContext context,
        Scenario scenario,
        ExtractionClient extraction,
        LedgerUpsertService upsertService,
        RunEntity currentRun,
        string dataDirectory,
        WalkPacing pacing,
        int maxDetailPages,
        CancellationToken cancellationToken)
    {
        (string make, string model) = MakeModel.Split(makeModel);
        ListingQuery query = ListingQuery.For(scenario, makeModel);
        var recorder = new WalkRecorder(dataDirectory, site.Name, model, currentRun.StartedAt);

        IReadOnlyList<string> searchUrls = site.BuildSearchUrls(query);

        async Task<IReadOnlyList<string>> CollectLinksAsync(string searchUrl, int searchIndex, int linkPoolSize, CancellationToken ct)
        {
            string searchLabel = searchUrls.Count > 1
                ? $"search {searchIndex + 1} of {searchUrls.Count}, {HttpUtility.ParseQueryString(new Uri(searchUrl).Query).Get("models[]")} facet"
                : "search page";
            AnsiConsole.MarkupLineInterpolated($"opening {searchLabel} for {make} {model} on {site.Name}");
            await page.GotoAsync(searchUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await CdpConnection.HandleChallengeIfPresentAsync(page, ct);

            await ScrollInStepsAsync(page, pacing, ct);
            TimeSpan dwell = pacing.RandomDwell();
            AnsiConsole.MarkupLineInterpolated($"dwelling {dwell.TotalSeconds:0}s on the search page");
            await Task.Delay(dwell, ct);

            string searchBodyText = await page.EvaluateAsync<string>("() => document.body.innerText");
            await recorder.WriteAsync(WalkPairSearches.SearchFileName(searchIndex), searchBodyText, ct);

            string[][] anchors = await page.EvaluateAsync<string[][]>("() => Array.from(document.querySelectorAll('a')).map(a => [a.href, a.innerText || ''])");
            IReadOnlyList<string> links = site.CollectDetailLinks([.. anchors.Select(a => new PageLink(a[0], a[1]))], linkPoolSize);
            AnsiConsole.MarkupLineInterpolated($"found {links.Count} detail link(s) to consider (cap {linkPoolSize / site.DetailLinkOverfetchMultiplier} matching candidate(s))");
            return links;
        }

        // Distinct detail links can still resolve to the same VIN within one pair (two dealers
        // cross-listing the same car, or a search page that links one listing twice under
        // different query strings that CanonicalDetailUrl doesn't fold together); tracked so a
        // second sighting is recorded as a repeat instead of spending another slot of the cap.
        var savedVinsThisPair = new HashSet<string>();

        async Task<DetailPageOutcome> VisitLinkAsync(string detailUrl, int i, CancellationToken ct)
        {
            IPage? detailPage = null;
            try
            {
                detailPage = await BackgroundTabs.OpenAsync(browserCdp, context);
                await detailPage.GotoAsync(detailUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                await CdpConnection.HandleChallengeIfPresentAsync(detailPage, ct);
                await detailPage.EvaluateAsync("() => window.scrollBy(0, window.innerHeight)");
                await Task.Delay(pacing.RandomScrollPause(), ct);

                string bodyText = await detailPage.EvaluateAsync<string>("() => document.body.innerText");
                await recorder.WriteAsync($"detail-{i + 1}.txt", bodyText, ct);

                if (site.SkippedCardTitlePattern is not null && NewCarPage.Reads(bodyText))
                {
                    AnsiConsole.MarkupLineInterpolated($"[grey]detail {i + 1}: dropped, {WalkOutcomeWording.DroppedReason(DetailPageOutcome.NewCar)}[/]");
                    return DetailPageOutcome.NewCar;
                }

                ExtractionOutcome outcome = await extraction.ExtractAsync(bodyText, ct);
                if (outcome.Error is not null || outcome.Result is null)
                {
                    AnsiConsole.MarkupLineInterpolated($"[yellow]detail {i + 1}: dropped, {WalkOutcomeWording.DroppedReason(DetailPageOutcome.ExtractionFailed)} ({outcome.Error})[/]");
                    return DetailPageOutcome.ExtractionFailed;
                }

                if (string.IsNullOrWhiteSpace(outcome.Result.Vin))
                {
                    AnsiConsole.MarkupLineInterpolated($"[grey]detail {i + 1}: dropped, {WalkOutcomeWording.DroppedReason(DetailPageOutcome.NoVin)} found on the page[/]");
                    return DetailPageOutcome.NoVin;
                }

                if (savedVinsThisPair.Contains(outcome.Result.Vin))
                {
                    AnsiConsole.MarkupLineInterpolated($"[grey]detail {i + 1}: dropped, {WalkOutcomeWording.DroppedReason(DetailPageOutcome.Repeat)} ({outcome.Result.Vin} already saved this pair)[/]");
                    return DetailPageOutcome.Repeat;
                }

                if (!query.MatchesExtractedVehicle(outcome.Result.Make, outcome.Result.Model, outcome.Result.Trim, outcome.Result.Year))
                {
                    int? gasOnlyBeforeYear = query.GasOnlyBeforeHybridYear(outcome.Result.Make, outcome.Result.Model, outcome.Result.Trim, outcome.Result.Year);
                    string detail = gasOnlyBeforeYear is int hybridYear
                        ? string.IsNullOrWhiteSpace(outcome.Result.Trim)
                            ? $"{outcome.Result.Year} {outcome.Result.Model}, gas-only before {hybridYear}"
                            : $"{outcome.Result.Year} {outcome.Result.Model} {outcome.Result.Trim}, gas-only before {hybridYear}"
                        : $"doesn't match {make} {model}: {outcome.Result.Year} {outcome.Result.Make} {outcome.Result.Model} {outcome.Result.Trim}";
                    AnsiConsole.MarkupLineInterpolated($"[grey]detail {i + 1}: dropped, {WalkOutcomeWording.DroppedReason(DetailPageOutcome.NotMatching)} ({detail})[/]");
                    return DetailPageOutcome.NotMatching;
                }

                if (outcome.Result.Year is null || outcome.Result.Price is null || outcome.Result.Mileage is null)
                {
                    AnsiConsole.MarkupLineInterpolated($"[yellow]detail {i + 1}: dropped, {WalkOutcomeWording.DroppedReason(DetailPageOutcome.MissingFields)} (year/price/mileage; {outcome.Result.Vin})[/]");
                    return DetailPageOutcome.MissingFields;
                }

                ResolvedDealer dealer = site.ResolveDealer(outcome.Result.DealerName, outcome.Result.DealerLocation);
                var candidate = new ListingCandidate
                {
                    Vin = outcome.Result.Vin,
                    Source = site.Name,
                    // Not the raw detailUrl: cars.com appends a per-search-session "sid" query
                    // parameter that's different on every run, and LedgerUpsertService keys a
                    // posting on (Vin, Source, Url), so storing the raw URL would mint a new
                    // posting every run instead of recognizing the one already in the ledger.
                    Url = WalkSites.CanonicalDetailUrl(detailUrl),
                    Year = outcome.Result.Year.Value,
                    Make = outcome.Result.Make ?? make,
                    // The canonical model this pair was walked for, not the extraction's own
                    // free-text model field: VehicleEntity.Model has to match the model half of the
                    // "source:model" token this pair stamps on the run (RunSources.Key), and the
                    // MatchesExtractedVehicle check above has already dropped any candidate that
                    // isn't actually this pair's model (e.g. a gas Camry on a Camry Hybrid walk),
                    // so stamping the canonical model here never mislabels a vehicle the page
                    // showed for a different reason (its own text reading as a trimmed variant like
                    // "Insight EX").
                    Model = model,
                    Trim = outcome.Result.Trim,
                    Price = outcome.Result.Price.Value,
                    Mileage = outcome.Result.Mileage.Value,
                    DealerName = dealer.Name,
                    DealerLocation = dealer.Location,
                    DealerNameIsFallback = dealer.IsFallback,
                };
                await upsertService.UpsertAsync(candidate, currentRun, ct);
                savedVinsThisPair.Add(candidate.Vin);
                return DetailPageOutcome.Upserted;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AnsiConsole.MarkupLineInterpolated($"[yellow]detail {i + 1}: dropped, {WalkOutcomeWording.DroppedReason(DetailPageOutcome.Failed)} ({ex.Message})[/]");
                return DetailPageOutcome.Failed;
            }
            finally
            {
                if (detailPage is not null)
                {
                    try
                    {
                        await detailPage.CloseAsync();
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        AnsiConsole.MarkupLineInterpolated($"[grey]detail {i + 1}: failed to close tab ({ex.Message})[/]");
                    }
                }
            }
        }

        DetailWalkTally tally = await WalkPairSearches.RunAsync(
            site,
            searchUrls,
            maxDetailPages,
            CollectLinksAsync,
            VisitLinkAsync,
            ct => Task.Delay(pacing.RandomDetailGap(), ct),
            cancellationToken);

        AnsiConsole.MarkupLineInterpolated($"{WalkPairSummaryLine.Format(site.Name, make, model, tally.Visited, tally.Upserted, tally.Dropped, AnsiConsole.Profile.Width)}");

        return new WalkPairOutcome(tally.Visited, tally.Upserted, tally.Dropped);
    }

    private static void RenderSummary(List<WalkPairSummary> summaries)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Walk summary[/]");
        AnsiConsole.Write(BuildSummaryTable(summaries));
    }

    // Column widths are chosen so that, added to Border.Minimal's per-column padding and
    // separators (3 chars per column plus 1 for the table's own edges), the table never needs
    // more than 80 columns: 8 + 22 + 5 + 5 + 15 + 6 + (3 * 6 + 1) = 80. Site and Model are also
    // truncated to their column's width before they reach the table, since Spectre wraps a cell
    // that overflows its declared width onto a second line rather than cropping it, which would
    // split one pair's row across two lines of the table. Dropped is the one column that's
    // allowed to wrap: its own reason breakdown can run longer than 15 columns, and none of the
    // reason words themselves are wider than that, so Spectre folds it onto a continuation line
    // under the same row at a space rather than mid-word.
    private const int SiteColumnWidth = 8;
    private const int ModelColumnWidth = 22;
    private const int PagesColumnWidth = 5;
    private const int SavedColumnWidth = 5;
    private const int DroppedColumnWidth = 15;
    private const int StatusColumnWidth = 6;

    /// <summary>Builds the end-of-run table without writing it, so a rendering test can capture
    /// it against a fixed-width console instead of the real one.</summary>
    public static Table BuildSummaryTable(IReadOnlyList<WalkPairSummary> summaries)
    {
        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn(new TableColumn("Site") { Width = SiteColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Model") { Width = ModelColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Pages") { Width = PagesColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Saved") { Width = SavedColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Dropped") { Width = DroppedColumnWidth });
        table.AddColumn(new TableColumn("Status") { Width = StatusColumnWidth, NoWrap = true });
        foreach (WalkPairSummary summary in summaries)
        {
            table.AddRow(
                Format.Cell(Format.Truncate(summary.Site, SiteColumnWidth)),
                Format.Cell(Format.Truncate(summary.Model, ModelColumnWidth)),
                summary.DetailPagesVisited.ToString(),
                summary.Upserted.ToString(),
                Format.Cell(WalkPairSummaryLine.TableCell(summary.Dropped)),
                summary.Completed ? "ok" : "[yellow]failed[/]");
        }

        return table;
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
