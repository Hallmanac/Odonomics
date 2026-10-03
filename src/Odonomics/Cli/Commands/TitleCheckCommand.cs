using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Odonomics.Auctions;
using Odonomics.Domain;
using Odonomics.Ledger;
using Odonomics.Walk;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

/// <summary>`odo title check`: looks each of the top-ranked VINs (or the VINs given) up in the public
/// Copart and IAA salvage-auction archives over the same operator-launched browser connection
/// `odo dealer grade` uses (see <see cref="CdpConnection"/>), and stores the result on the vehicle (see
/// <see cref="AuctionCheckEntity"/>); a found sale raises the <c>salvage-auction</c> red flag. It makes
/// no Marketcheck or other paid API call. Each VIN is searched on html.duckduckgo.com, and at most
/// <see cref="AuctionSearchPageReader.MaxArchiveResults"/> archive results are opened. VINs are handled one at a
/// time with a pause between them. A captcha or block, or a page the parser does not recognise, is
/// stored as could-not-read with its reason and the run moves on; it is never retried within the run,
/// and a later run looks that VIN up again. Archive sites change and may block automated reads, so
/// this is a best-effort lookup (see docs/title-check.md).</summary>
public static class TitleCheckCommand
{
    public const int DefaultTop = 20;

    public static async Task<int> RunAsync(string scenarioPath, IReadOnlyList<string> vins, int? top, CancellationToken cancellationToken)
    {
        if (vins.Count > 0 && top is not null)
        {
            AnsiConsole.MarkupLine("[red]pass either VINs or --top, not both[/]");
            return 1;
        }

        if (top is < 1)
        {
            AnsiConsole.MarkupLine("[red]--top must be at least 1[/]");
            return 1;
        }

        if (!await CdpConnection.IsAvailableAsync(cancellationToken))
        {
            CdpConnection.PrintUnavailableMessage();
            return 1;
        }

        using OdonomicsDbContext db = LedgerFactory.Open();

        List<VehicleEntity> vehicles = vins.Count > 0
            ? await ExplicitVehiclesAsync(db, vins, cancellationToken)
            : await TopRankedVehiclesAsync(db, scenarioPath, top ?? DefaultTop, cancellationToken);

        if (vins.Count > 0 && vehicles.Count == 0)
        {
            return 1;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (VehicleEntity vehicle in vehicles)
        {
            if (vehicle.AuctionCheck is AuctionCheckEntity stored && !AuctionChecks.NeedsCheck(stored, now))
            {
                AnsiConsole.MarkupLineInterpolated($"{vehicle.Vin}: already checked, {Describe(stored)}");
            }
        }

        List<VehicleEntity> targets = [.. vehicles.Where(v => AuctionChecks.NeedsCheck(v.AuctionCheck, now))];
        if (targets.Count == 0)
        {
            AnsiConsole.MarkupLine("no VINs to check");
            return 0;
        }

        AnsiConsole.MarkupLineInterpolated($"checking {targets.Count} VIN(s) in the public salvage-auction archives");

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.ConnectOverCDPAsync($"http://localhost:{ChromeLaunchLine.DebugPort}");
        ICDPSession browserCdp = await browser.NewBrowserCDPSessionAsync();
        IBrowserContext context = browser.Contexts.FirstOrDefault()
            ?? throw new InvalidOperationException("the CDP connection exposed no browser context to attach to; is the browser still running?");

        var recorder = new WalkRecorder(DataDirectory.Resolve(), "auctions", "titles", DateTimeOffset.UtcNow);
        var pacing = new WalkPacing(Random.Shared);

        int found = 0;
        int notFound = 0;
        int couldNotRead = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            VehicleEntity vehicle = targets[i];
            if (i > 0)
            {
                await Task.Delay(pacing.RandomDetailGap(), cancellationToken);
            }

            AuctionLookupResult result = await LookUpAsync(vehicle.Vin, browserCdp, context, recorder, pacing, cancellationToken);
            AuctionCheckEntity check = AuctionChecks.Apply(vehicle, result, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(cancellationToken);

            switch (result.Outcome)
            {
                case AuctionCheckOutcome.Found:
                    found++;
                    break;
                case AuctionCheckOutcome.NotFound:
                    notFound++;
                    break;
                default:
                    couldNotRead++;
                    break;
            }

            string line = result.Outcome == AuctionCheckOutcome.CouldNotRead
                ? $"could not read the archives: {result.Reason}{(check.Outcome == AuctionCheckOutcome.Found ? $"; keeping the stored sale ({Describe(check)})" : "")}"
                : Describe(check);
            Color color = result.Outcome switch
            {
                AuctionCheckOutcome.Found => Color.Red,
                AuctionCheckOutcome.CouldNotRead => Color.Yellow,
                _ => Color.Default,
            };
            AnsiConsole.Write(new Text($"{vehicle.Vin}: {line}{Environment.NewLine}", new Style(color)));
        }

        AnsiConsole.MarkupLineInterpolated($"found {found} auction sale(s), {notFound} not found, {couldNotRead} could not read");
        return couldNotRead == targets.Count ? 1 : 0;
    }

    /// <summary>One line saying what a stored lookup holds; a found sale reads as its red-flag detail
    /// followed by the auction and lot.</summary>
    public static string Describe(AuctionCheckEntity check) => check.Outcome switch
    {
        AuctionCheckOutcome.Found => $"{RedFlagsEvaluator.SalvageAuction(check.SaleDocument, check.PrimaryDamage, check.SecondaryDamage, check.SaleDate).Detail}{LotSuffix(check)}",
        AuctionCheckOutcome.NotFound => "no auction sale found in the public archives",
        _ => $"could not read the archives: {check.CouldNotReadReason}",
    };

    private static string LotSuffix(AuctionCheckEntity check)
    {
        string lot = string.Join(" ", new[] { check.Auction, check.LotNumber is null ? null : $"lot {check.LotNumber}" }.OfType<string>());
        return lot.Length == 0
            ? ""
            : $", {lot}";
    }

    /// <summary>Looks one VIN up, once: a DuckDuckGo search, then at most a few archive results. Whatever
    /// goes wrong on the way (a navigation failure, a block) becomes a could-not-read result and is not
    /// retried, so a blocked site costs one page load and not a loop.</summary>
    private static async Task<AuctionLookupResult> LookUpAsync(
        string vin, ICDPSession browserCdp, IBrowserContext context, WalkRecorder recorder, WalkPacing pacing, CancellationToken cancellationToken)
    {
        IPage page = await BackgroundTabs.OpenAsync(browserCdp, context);
        try
        {
            await page.GotoAsync(AuctionSearchPageReader.BuildSearchUrl(vin), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Task.Delay(pacing.RandomScrollPause(), cancellationToken);
            string searchHtml = await page.ContentAsync();
            await recorder.WriteAsync($"search-{vin}.html", searchHtml, cancellationToken);

            AuctionSearchPage search = AuctionSearchPageReader.Read(searchHtml);
            if (AuctionLookup.FromSearch(search) is AuctionLookupResult settled)
            {
                return settled;
            }

            List<AuctionPageReading> readings = [];
            for (int i = 0; i < search.ArchiveUrls.Count; i++)
            {
                string url = search.ArchiveUrls[i];
                if (i > 0)
                {
                    await Task.Delay(pacing.RandomScrollPause(), cancellationToken);
                }

                AuctionPageReading reading = await ReadArchivePageAsync(page, vin, url, recorder, i, pacing, cancellationToken);
                readings.Add(reading);
                if (reading.Status == AuctionPageStatus.Found)
                {
                    break;
                }
            }

            return AuctionLookup.Decide(readings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AuctionLookupResult(AuctionCheckOutcome.CouldNotRead, null, $"the lookup failed ({ex.Message})");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private static async Task<AuctionPageReading> ReadArchivePageAsync(
        IPage page, string vin, string url, WalkRecorder recorder, int index, WalkPacing pacing, CancellationToken cancellationToken)
    {
        try
        {
            await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Task.Delay(pacing.RandomScrollPause(), cancellationToken);
            string title = await page.TitleAsync();
            string text = await page.EvaluateAsync<string>("() => document.body.innerText");
            await recorder.WriteAsync($"archive-{vin}-{index + 1}.txt", text, cancellationToken);
            return AuctionPageParser.Parse(vin, url, title, text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new AuctionPageReading(AuctionPageStatus.CouldNotRead, null, $"could not load {new Uri(url).Host} ({ex.Message})");
        }
    }

    private static async Task<List<VehicleEntity>> ExplicitVehiclesAsync(OdonomicsDbContext db, IReadOnlyList<string> vins, CancellationToken cancellationToken)
    {
        List<VehicleEntity> vehicles = [];
        foreach (string vin in vins.Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string upperVin = vin.Trim().ToUpperInvariant();
            VehicleEntity? vehicle = await db.Vehicles.Include(v => v.AuctionCheck).FirstOrDefaultAsync(v => v.Vin.ToUpper() == upperVin, cancellationToken);
            if (vehicle is null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]no vehicle with VIN {vin} in the ledger[/]");
                continue;
            }

            vehicles.Add(vehicle);
        }

        return vehicles;
    }

    /// <summary>The first <paramref name="top"/> vehicles of the current rank (see
    /// <see cref="RankRenderer.RankedVehicles"/>), scored exactly as `odo rank` scores them.</summary>
    private static async Task<List<VehicleEntity>> TopRankedVehiclesAsync(OdonomicsDbContext db, string scenarioPath, int top, CancellationToken cancellationToken)
    {
        Scenario scenario = ScenarioLoader.Load(scenarioPath);
        List<RunEntity> runs = await db.Runs.ToListAsync(cancellationToken);
        Dictionary<string, DateTimeOffset> latestCoverageBySource = RunSources.LatestCoverageBySource(runs);

        List<VehicleEntity> all = await db.Vehicles
            .Include(v => v.Postings).ThenInclude(p => p.PriceObservations)
            .Include(v => v.Postings).ThenInclude(p => p.Dealer)
            .Include(v => v.Postings).ThenInclude(p => p.Attributes)
            .Include(v => v.AuctionCheck)
            .ToListAsync(cancellationToken);

        FactoryTrimTable trimTable = FactoryTrimTable.LoadShipped();
        List<Score> scores = [.. all.Select(v => Scorer.Score(RankCommand.ForScoring(v, latestCoverageBySource, scenario.Fulfillment, scenario.Zip, scenario.RadiusMiles, trimTable), scenario))];
        Dictionary<string, VehicleEntity> byVin = all.ToDictionary(v => v.Vin);
        return [.. RankRenderer.RankedVehicles(scores).Take(top).Select(s => byVin[s.Vehicle.Vin])];
    }
}
