using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Odonomics.CarEdge;
using Odonomics.Ledger;
using Odonomics.Walk;
using Spectre.Console;

namespace Odonomics.Cli.Commands;

/// <summary>`odo dealer grade`: looks up each ungraded dealer's CarEdge Dealer Rating over the
/// same operator-launched browser connection `odo walk` uses (see <see cref="CdpConnection"/>),
/// paced like the walk and pausing on a bot-defense challenge the same way. A dealer CarEdge
/// positively says it has no rating for is stamped as checked, so it is never looked up again on a
/// later run; a page that merely failed to parse is left unstamped so a later run retries it.</summary>
public static class DealerGradeCommand
{
    public static async Task<int> RunAsync(bool all, string? vin, CancellationToken cancellationToken)
    {
        // Exactly one of --all or a VIN: this is true whenever both are given or neither is.
        if (all == (vin is not null))
        {
            AnsiConsole.MarkupLine("[red]pass exactly one of --all or a VIN[/]");
            return 1;
        }

        if (!await CdpConnection.IsAvailableAsync(cancellationToken))
        {
            CdpConnection.PrintUnavailableMessage();
            return 1;
        }

        using OdonomicsDbContext db = LedgerFactory.Open();

        List<DealerEntity> targets;
        if (all)
        {
            targets = await db.Dealers.Where(d => d.GradeCheckedAt == null).OrderBy(d => d.Id).ToListAsync(cancellationToken);
        }
        else
        {
            VehicleEntity? vehicle = await db.Vehicles
                .Include(v => v.Postings).ThenInclude(p => p.Dealer)
                .FirstOrDefaultAsync(v => v.Vin == vin, cancellationToken);
            if (vehicle is null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]no vehicle with VIN {vin} in the ledger[/]");
                return 1;
            }

            List<DealerEntity> dealers = [.. vehicle.Postings.Select(p => p.Dealer).OfType<DealerEntity>().DistinctBy(d => d.Id)];
            if (dealers.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]no known dealer for this VIN yet[/]");
                return 0;
            }

            foreach (DealerEntity dealer in dealers.Where(d => d.GradeCheckedAt is not null))
            {
                PrintGrade(dealer);
            }

            targets = [.. dealers.Where(d => d.GradeCheckedAt is null)];
        }

        if (targets.Count == 0)
        {
            AnsiConsole.MarkupLine("no ungraded dealers to check");
            return 0;
        }

        AnsiConsole.MarkupLineInterpolated($"grading {targets.Count} dealer(s) on CarEdge");

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.ConnectOverCDPAsync($"http://localhost:{ChromeLaunchLine.DebugPort}");
        ICDPSession browserCdp = await browser.NewBrowserCDPSessionAsync();
        IBrowserContext context = browser.Contexts.FirstOrDefault()
            ?? throw new InvalidOperationException("the CDP connection exposed no browser context to attach to; is the browser still running?");

        var recorder = new WalkRecorder(DataDirectory.Resolve(), "caredge", "dealers", DateTimeOffset.UtcNow);
        var pacing = new WalkPacing(Random.Shared);

        int graded = 0;
        int ungraded = 0;
        int failed = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            if (i > 0)
            {
                await Task.Delay(pacing.RandomDetailGap(), cancellationToken);
            }

            DealerEntity dealer = targets[i];
            IPage page = await BackgroundTabs.OpenAsync(browserCdp, context);
            try
            {
                string url = CarEdgeSearch.BuildSearchUrl(dealer.Name, dealer.Location);
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                await CdpConnection.HandleChallengeIfPresentAsync(page, cancellationToken);
                await Task.Delay(pacing.RandomScrollPause(), cancellationToken);

                string bodyText = await page.EvaluateAsync<string>("() => document.body.innerText");
                await recorder.WriteAsync($"dealer-{dealer.Id}.txt", bodyText, cancellationToken);

                CarEdgeGradeResult result = CarEdgeGradeParser.Parse(bodyText, dealer.Name);
                switch (result.Status)
                {
                    case CarEdgeGradeStatus.Graded:
                        dealer.Grade = result.Grade;
                        dealer.GradeReason = result.Reason;
                        dealer.GradeCheckedAt = DateTimeOffset.UtcNow;
                        graded++;
                        AnsiConsole.MarkupLineInterpolated($"{dealer.Name}: {result.Grade}");
                        await db.SaveChangesAsync(cancellationToken);
                        break;
                    case CarEdgeGradeStatus.NotFound:
                        dealer.GradeCheckedAt = DateTimeOffset.UtcNow;
                        ungraded++;
                        AnsiConsole.MarkupLineInterpolated($"[grey]{dealer.Name}: not on CarEdge[/]");
                        await db.SaveChangesAsync(cancellationToken);
                        break;
                    case CarEdgeGradeStatus.Unrecognized:
                        failed++;
                        AnsiConsole.MarkupLineInterpolated($"[yellow]{dealer.Name}: page didn't match a known CarEdge layout, will retry later[/]");
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                AnsiConsole.MarkupLineInterpolated($"[yellow]{dealer.Name}: failed to check ({ex.Message})[/]");
            }
            finally
            {
                await page.CloseAsync();
            }
        }

        AnsiConsole.MarkupLineInterpolated($"graded {graded}, recorded {ungraded} ungraded, {failed} failed");
        return 0;
    }

    private static void PrintGrade(DealerEntity dealer)
    {
        if (dealer.Grade is null)
        {
            AnsiConsole.MarkupLineInterpolated($"{dealer.Name}: already checked, CarEdge has no rating for this dealer");
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated($"{dealer.Name}: {dealer.Grade}");
        }
    }
}
