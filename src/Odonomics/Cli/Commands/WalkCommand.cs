using System.CommandLine;
using System.Web;
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
/// hand, never launches one itself. With no site argument it walks cars.com, then carvana, then autotrader; a
/// site argument narrows it to that one site. With no --model it walks every model in the
/// scenario's allowed list, in order, on whichever site(s) it's covering; --model narrows it to
/// that one model exactly, on whichever site(s) it's covering. The scenario's facets are the only
/// filter by default: with no --max the walk visits every car a pair's searches return. --max is an
/// opt-in limit on matching detail pages visited per site-and-model pair, not per run or raw page
/// visits: a page rejected for not matching the model, or one whose VIN this pair already saved
/// through a different link, doesn't spend the cap, so the walk can open more candidate links than
/// --max to fill it. Before each pair's first detail page the walk prints how many it is about to
/// visit (see <see cref="WalkVisitPlan"/>). A link the ledger already holds is not
/// opened at all: it is kept current from its search card (see <see cref="KnownCardTouches"/>), so a
/// repeat walk visits only new cars, and --revisit forces a detail visit for every link as before.
/// See the brief for the full pacing spec; this command implements it as literally as an automated
/// agent can, since the actual bot-defense behavior can only be proven by the operator running it
/// against a real browser.
/// </summary>
public static class WalkCommand
{
    public static Option<int?> CreateMaxOption() => new("--max")
    {
        Description = "limit the matching detail pages visited per site-and-model pair; without it the walk visits every car a pair's searches return that the ledger does not already hold. A page rejected for not matching the model doesn't count against the limit, so the walk may open more candidate links than this to reach it",
    };

    public static Option<bool> CreateRevisitOption() => new("--revisit")
    {
        Description = "open a detail page for every link, including the ones the ledger already holds; without it those are kept current from their search cards and only new cars are visited",
    };

    public static async Task<int> RunAsync(string scenarioPath, string? siteName, string? modelOverride, int? maxDetailPages, bool revisit, CancellationToken cancellationToken)
    {
        List<WalkSite> sites;
        if (siteName is null)
        {
            sites = [WalkSites.CarsCom, WalkSites.Carvana, WalkSites.Autotrader];
        }
        else
        {
            WalkSite? site = WalkSites.Find(siteName);
            if (site is null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]unknown walk target \"{siteName}\"; expected cars.com, carvana, or autotrader[/]");
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
        var currentRun = new RunEntity { Command = BuildCommandLabel(siteName, modelOverride), Sources = "", StartedAt = DateTimeOffset.UtcNow, Zip = scenario.Zip, RadiusMiles = scenario.RadiusMiles };
        db.Runs.Add(currentRun);
        await db.SaveChangesAsync(cancellationToken);

        string dataDirectory = DataDirectory.Resolve();
        var pacing = new WalkPacing(Random.Shared);
        var upsertService = new LedgerUpsertService(db);

        Dictionary<(string Source, string Model), int> knownCounts = await upsertService.KnownPostingCountsAsync(cancellationToken);
        foreach (string line in WalkVisitPlan.RunStartLines(
            [.. sites.SelectMany(site => models.Select(makeModel => new WalkVisitPlan.PairKnown(
                site.Name,
                makeModel,
                knownCounts.GetValueOrDefault((site.Name, MakeModel.Split(makeModel).Model)))))],
            maxDetailPages,
            revisit))
        {
            AnsiConsole.MarkupLineInterpolated($"{line}");
        }

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
            (site, makeModel, ct) => WalkPairAsync(site, makeModel, page, browserCdp, context, scenario, extraction, upsertService, currentRun, dataDirectory, pacing, maxDetailPages, revisit, ct),
            (site, makeModel) => AnsiConsole.MarkupLineInterpolated($"walking {site.Name} for {makeModel}"),
            (site, makeModel, ex) => AnsiConsole.MarkupLineInterpolated($"[yellow]{site.Name} / {makeModel}: walk failed ({ex.Message})[/]"),
            ct => Task.Delay(pacing.RandomPairGap(), ct),
            ct => db.SaveChangesAsync(ct),
            cancellationToken);

        currentRun.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        RenderSummary(summaries);

        var diffService = new LedgerDiffService(db);
        SearchDiff diff = await diffService.ComputeAsync(currentRun, scenario, cancellationToken);
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
        int? maxDetailPages,
        bool revisit,
        CancellationToken cancellationToken)
    {
        (string make, string model) = MakeModel.Split(makeModel);
        ListingQuery query = ListingQuery.For(scenario, makeModel);
        var recorder = new WalkRecorder(dataDirectory, site.Name, model, currentRun.StartedAt);

        IReadOnlyList<string> searchUrls = site.BuildSearchUrls(query);
        KnownCardTouches knownTouches = await KnownCardTouches.LoadAsync(upsertService, site.Name, currentRun, revisit, cancellationToken);

        // Set when a search's link collection stopped with the site's results not all read because
        // the pool was full (see WalkSearchPages); the pair's coverage is then recorded as partial.
        bool linkCollectionCapped = false;

        // The first result page after the first that failed to load, in any of the pair's searches; the
        // pages after it were never read, so the pair's coverage is recorded as partial for that reason.
        int? failedResultPage = null;

        async Task<SearchPageContent> LoadSearchPageAsync(string pageUrl, string searchLabel, int searchIndex, int pageNumber, CancellationToken ct)
        {
            // A pair with several searches always names the page too, so the line says which search page it is.
            string pageLabel = pageNumber > 1 || searchUrls.Count > 1 ? $"{searchLabel}, page {pageNumber}" : searchLabel;
            AnsiConsole.MarkupLineInterpolated($"opening {pageLabel} for {make} {model} on {site.Name}");
            await page.GotoAsync(pageUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await CdpConnection.HandleChallengeIfPresentAsync(page, ct);

            await ScrollInStepsAsync(page, pacing, ct);
            TimeSpan dwell = pacing.RandomDwell();
            AnsiConsole.MarkupLineInterpolated($"dwelling {dwell.TotalSeconds:0}s on the search page");
            await Task.Delay(dwell, ct);

            string searchBodyText = await page.EvaluateAsync<string>("() => document.body.innerText");
            await recorder.WriteAsync(WalkPairSearches.SearchFileName(searchIndex, pageNumber, searchUrls.Count), searchBodyText, ct);

            IReadOnlyList<PageLink> links = await SearchPageLinks.ReadAsync((script, arg) => page.EvaluateAsync<string[][]>(script, arg), site);
            await recorder.WriteAsync(WalkPairSearches.CardsFileName(searchIndex, pageNumber, searchUrls.Count), SearchPageLinks.CardsJson(site, links), ct);
            return new SearchPageContent(links, searchBodyText);
        }

        async Task<IReadOnlyList<string>> CollectLinksAsync(string searchUrl, int searchIndex, int linkPoolSize, CancellationToken ct)
        {
            string searchLabel = searchUrls.Count > 1
                ? $"search {searchIndex + 1} of {searchUrls.Count}, {HttpUtility.ParseQueryString(new Uri(searchUrl).Query).Get("models[]")} facet"
                : "search page";
            IReadOnlyList<string> links = await WalkSearchPages.CollectLinksAsync(
                site,
                searchUrl,
                linkPoolSize,
                knownTouches.TryTouchAsync,
                (pageUrl, pageNumber, pageCt) => LoadSearchPageAsync(pageUrl, searchLabel, searchIndex, pageNumber, pageCt),
                (pageNumber, ex) =>
                {
                    failedResultPage ??= pageNumber;
                    AnsiConsole.MarkupLineInterpolated($"[yellow]{searchLabel}, page {pageNumber} failed to load, so paging stops there ({ex.Message})[/]");
                },
                pageNumber => AnsiConsole.MarkupLineInterpolated($"{searchLabel}, page {pageNumber}: the search ran out of exact matches, so paging stops there"),
                () => linkCollectionCapped = true,
                ct,
                revisit);
            string capText = linkPoolSize == WalkPairSearches.UnboundedPool
                ? "no cap"
                : $"cap {linkPoolSize / site.DetailLinkOverfetchMultiplier} matching candidate(s)";
            AnsiConsole.MarkupLineInterpolated($"found {links.Count} new detail link(s) to consider ({capText}), {knownTouches.Count} known from cards so far");
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
                string bodyText = await DetailPageScroll.ReadAsync(
                    () => detailPage.EvaluateAsync("() => window.scrollBy(0, window.innerHeight)"),
                    () => detailPage.EvaluateAsync<string>("() => document.body.innerText"),
                    pacing.RandomScrollPause,
                    site.LazyDetailBlockMarker,
                    pacing.ScrollSteps - 1,
                    ct);
                await recorder.WriteAsync($"detail-{i + 1}.txt", bodyText, ct);

                if (site.ReadsAsSold(bodyText))
                {
                    int marked = await upsertService.MarkSoldAsync(site.Name, WalkSites.CanonicalDetailUrl(detailUrl), currentRun, ct);
                    string ledgerNote = marked > 0 ? ", marked sold in the ledger" : "";
                    AnsiConsole.MarkupLineInterpolated($"[grey]detail {i + 1}: dropped, {WalkOutcomeWording.DroppedReason(DetailPageOutcome.Sold)}{ledgerNote}[/]");
                    return DetailPageOutcome.Sold;
                }

                if (site.ReadsAsNoPrice(bodyText))
                {
                    AnsiConsole.MarkupLineInterpolated($"[grey]detail {i + 1}: dropped, {WalkOutcomeWording.DroppedReason(DetailPageOutcome.NoPriceListed)}[/]");
                    return DetailPageOutcome.NoPriceListed;
                }

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

                AnsiConsole.MarkupLineInterpolated($"[grey]detail {i + 1}: read {outcome.Result.Year} {outcome.Result.Make} {outcome.Result.Model} {outcome.Result.Trim}, fuel type {outcome.Result.FuelType ?? "not stated"}[/]");

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

                if (!query.MatchesWalkedPage(outcome.Result.Make, outcome.Result.Model, outcome.Result.Trim, outcome.Result.Year, bodyText))
                {
                    int? gasOnlyBeforeYear = query.GasOnlyBeforeHybridYear(outcome.Result.Make, outcome.Result.Model, outcome.Result.Trim, outcome.Result.Year);
                    string detail = gasOnlyBeforeYear is int hybridYear
                        ? string.IsNullOrWhiteSpace(outcome.Result.Trim)
                            ? $"{outcome.Result.Year} {outcome.Result.Model}, gas-only before {hybridYear}"
                            : $"{outcome.Result.Year} {outcome.Result.Model} {outcome.Result.Trim}, gas-only before {hybridYear}"
                        : $"doesn't match {make} {model}: {outcome.Result.Year} {outcome.Result.Make} {outcome.Result.Model} {outcome.Result.Trim}"
                            + (query.IsHybridVariant ? "; no Hybrid in title, trim, or spec line" : "");
                    AnsiConsole.MarkupLineInterpolated($"[grey]detail {i + 1}: dropped, {WalkOutcomeWording.DroppedReason(DetailPageOutcome.NotMatching)} ({detail})[/]");
                    return DetailPageOutcome.NotMatching;
                }

                if (outcome.Result.Year is null || outcome.Result.Price is null || outcome.Result.Mileage is null)
                {
                    AnsiConsole.MarkupLineInterpolated($"[yellow]detail {i + 1}: dropped, {WalkOutcomeWording.DroppedReason(DetailPageOutcome.MissingFields)} (year/price/mileage; {outcome.Result.Vin})[/]");
                    return DetailPageOutcome.MissingFields;
                }

                PickupOption? pickup = site.ReadPickup(bodyText);
                ResolvedDealer dealer = site.ResolveDealer(outcome.Result.DealerName, outcome.Result.DealerLocation, bodyText);
                string canonicalUrl = WalkSites.CanonicalDetailUrl(detailUrl);
                FeeStatement? feeStatement = site.ReadFeeStatement(bodyText)?.ForAskingPrice(outcome.Result.Price);
                var candidate = new ListingCandidate
                {
                    Vin = outcome.Result.Vin,
                    Source = site.Name,
                    // Not the raw detailUrl: cars.com appends a per-search-session "sid" query
                    // parameter that's different on every run, and LedgerUpsertService keys a
                    // posting on (Vin, Source, Url), so storing the raw URL would mint a new
                    // posting every run instead of recognizing the one already in the ledger.
                    Url = canonicalUrl,
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
                    ShippingFee = site.ReadShippingFee(bodyText),
                    Attributes = knownTouches.BadgesOfNewLink(canonicalUrl),
                    PickupFee = pickup?.Fee,
                    PickupLocation = pickup?.Location,
                    FeePosture = feeStatement?.Posture,
                    ItemizedFeesTotal = feeStatement?.ItemizedTotal,
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
            cancellationToken,
            (pages, startingWith) => AnsiConsole.MarkupLineInterpolated($"{WalkVisitPlan.PairLine(site.Name, make, model, pages, startingWith)}"),
            revisit);

        await knownTouches.CommitAsync(cancellationToken);

        bool capped = linkCollectionCapped || tally.Capped;
        AnsiConsole.MarkupLineInterpolated($"{WalkPairSummaryLine.Format(site.Name, make, model, tally.Visited, knownTouches.Count, tally.Upserted, tally.Dropped, AnsiConsole.Profile.Width, capped, failedResultPage)}");

        return new WalkPairOutcome(tally.Visited, tally.Upserted, tally.Dropped, knownTouches.Count, capped, failedResultPage);
    }

    private static void RenderSummary(List<WalkPairSummary> summaries)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[bold]Walk summary[/]");
        AnsiConsole.Write(BuildSummaryTable(summaries));
    }

    // Column widths are chosen so that, added to Border.Minimal's per-column padding and
    // separators, the table never needs more than 80 columns. A column with the default padding costs
    // 3 chars (a space each side and a separator) and the table's own edges cost 1; the three count
    // columns (Pages, Known, Saved) drop their padding, so each costs only its separator and its
    // numbers sit right-aligned against it: 10 + 21 + 5 + 5 + 5 + 12 + 6 = 64 for the cells, plus
    // (3 * 4 + 1 * 3 + 1) = 16, is 80. Site and Model are also truncated to their column's width before
    // they reach the table, since Spectre wraps a cell that overflows its declared width onto a
    // second line rather than cropping it, which would split one pair's row across two lines of the
    // table. Dropped is the one column that's allowed to wrap: its own reason breakdown can run
    // longer than 12 columns, and none of the reason words themselves are wider than that (the
    // longest, "(extraction", is 11), so Spectre folds it onto a continuation line under the same row
    // at a space rather than mid-word.
    private const int SiteColumnWidth = 10;
    private const int ModelColumnWidth = 21;
    private const int PagesColumnWidth = 5;
    private const int KnownColumnWidth = 5;
    private const int SavedColumnWidth = 5;
    private const int DroppedColumnWidth = 12;
    private const int StatusColumnWidth = 6;

    private static TableColumn CountColumn(string header, int width) =>
        new(header) { Width = width, NoWrap = true, Alignment = Justify.Right, Padding = new Padding(0, 0) };

    /// <summary>Builds the end-of-run table without writing it, so a rendering test can capture
    /// it against a fixed-width console instead of the real one. Pages counts the detail pages
    /// visited and Known the links kept current from their search cards without a visit.</summary>
    public static Table BuildSummaryTable(IReadOnlyList<WalkPairSummary> summaries)
    {
        var table = new Table { Border = TableBorder.Minimal };
        table.Width(80);
        table.AddColumn(new TableColumn("Site") { Width = SiteColumnWidth, NoWrap = true });
        table.AddColumn(new TableColumn("Model") { Width = ModelColumnWidth, NoWrap = true });
        table.AddColumn(CountColumn("Pages", PagesColumnWidth));
        table.AddColumn(CountColumn("Known", KnownColumnWidth));
        table.AddColumn(CountColumn("Saved", SavedColumnWidth));
        table.AddColumn(new TableColumn("Dropped") { Width = DroppedColumnWidth });
        table.AddColumn(new TableColumn("Status") { Width = StatusColumnWidth, NoWrap = true });
        foreach (WalkPairSummary summary in summaries)
        {
            table.AddRow(
                Format.Cell(Format.Truncate(summary.Site, SiteColumnWidth)),
                Format.Cell(Format.Truncate(summary.Model, ModelColumnWidth)),
                summary.DetailPagesVisited.ToString(),
                summary.KnownFromCards.ToString(),
                summary.Upserted.ToString(),
                Format.Cell(WalkPairSummaryLine.TableCell(summary.Dropped)),
                StatusCell(summary));
        }

        return table;
    }

    private static string StatusCell(WalkPairSummary summary) => summary switch
    {
        { Completed: false } => "[yellow]failed[/]",
        { FailedPage: not null } => "unread",
        { Capped: true } => "capped",
        _ => "ok",
    };

    private static async Task ScrollInStepsAsync(IPage page, WalkPacing pacing, CancellationToken cancellationToken)
    {
        for (int step = 0; step < pacing.ScrollSteps; step++)
        {
            await page.EvaluateAsync("() => window.scrollBy(0, window.innerHeight * 0.8)");
            await Task.Delay(pacing.RandomScrollPause(), cancellationToken);
        }
    }
}
