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
/// later run; a page that merely failed to parse is left unstamped so a later run retries it, and
/// so is one whose name-matched cards were all in another city or state, or that matched several
/// same-named cards a partial location could not tell apart. Three consecutive CarEdge 404 pages
/// mean the search URL itself is dead, not that three dealers in a row are unrateable, so the run
/// stops there and exits non-zero rather than burning through the rest of the list against a URL
/// that will keep failing; every dealer it never got to stays ungraded and eligible for the next
/// run.</summary>
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
        int unmatched = 0;
        int failed = 0;
        var deadSearchUrlGate = new ConsecutiveDeadSearchUrlGate();
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

                CarEdgeGradeResult result = CarEdgeGradeParser.Parse(bodyText, dealer.Name, dealer.Location);
                DealerGradeOutcome outcome = ApplyResult(dealer, result, DateTimeOffset.UtcNow, url);
                switch (outcome.Tally)
                {
                    case DealerGradeTally.Graded:
                        graded++;
                        break;
                    case DealerGradeTally.Ungraded:
                        ungraded++;
                        break;
                    case DealerGradeTally.Unmatched:
                        unmatched++;
                        break;
                    case DealerGradeTally.Failed:
                        failed++;
                        break;
                }

                if (result.Status == CarEdgeGradeStatus.CarEdgeSearchUrlInvalid)
                {
                    deadSearchUrlGate.RecordDeadSearchUrl();
                }
                else
                {
                    deadSearchUrlGate.RecordOtherOutcome();
                }

                AnsiConsole.Write(new Text(outcome.Line + Environment.NewLine, new Style(outcome.Color)));
                if (outcome.Stamped)
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                deadSearchUrlGate.RecordOtherOutcome();
                AnsiConsole.MarkupLineInterpolated($"[yellow]{dealer.Name}: failed to check ({ex.Message})[/]");
            }
            finally
            {
                await page.CloseAsync();
            }

            if (deadSearchUrlGate.ShouldStop)
            {
                AnsiConsole.MarkupLine("[red]stopping: 3 consecutive CarEdge search pages came back dead[/]");
                break;
            }
        }

        AnsiConsole.MarkupLineInterpolated($"graded {graded}, recorded {ungraded} ungraded, {unmatched} unmatched, {failed} failed");
        return deadSearchUrlGate.ShouldStop ? 1 : 0;
    }

    /// <summary>Applies what the parser made of a dealer's CarEdge page to that dealer, and says how
    /// to report it. <see cref="DealerEntity.GradeCheckedAt"/> is stamped only for the two outcomes
    /// that are final, a grade or CarEdge positively having none; every other outcome leaves the
    /// dealer untouched so a later run looks it up again.</summary>
    public static DealerGradeOutcome ApplyResult(DealerEntity dealer, CarEdgeGradeResult result, DateTimeOffset checkedAt, string searchUrl)
    {
        switch (result.Status)
        {
            case CarEdgeGradeStatus.Graded:
                dealer.Grade = result.Grade;
                dealer.GradeReason = result.Reason;
                dealer.GradeCheckedAt = checkedAt;
                return new DealerGradeOutcome($"{dealer.Name}: {result.Grade}", Color.Default, DealerGradeTally.Graded, Stamped: true);
            case CarEdgeGradeStatus.NotFound:
                dealer.GradeCheckedAt = checkedAt;
                return new DealerGradeOutcome($"{dealer.Name}: not on CarEdge", Color.Grey, DealerGradeTally.Ungraded, Stamped: true);
            case CarEdgeGradeStatus.Ambiguous:
                return new DealerGradeOutcome(
                    $"{dealer.Name}: several CarEdge dealers share this name and the location on record is missing or too partial to pick one, left ungraded until a fuller location is known",
                    Color.Yellow,
                    DealerGradeTally.Unmatched,
                    Stamped: false);
            case CarEdgeGradeStatus.LocationMismatch:
                return new DealerGradeOutcome(
                    $"{dealer.Name}: name matched, location did not, will retry later",
                    Color.Yellow,
                    DealerGradeTally.Unmatched,
                    Stamped: false);
            case CarEdgeGradeStatus.CarEdgeSearchUrlInvalid:
                return new DealerGradeOutcome(
                    $"{dealer.Name}: CarEdge search returned its own 404, search URL is dead: {searchUrl}",
                    Color.Red,
                    DealerGradeTally.Failed,
                    Stamped: false);
            default:
                return new DealerGradeOutcome(
                    $"{dealer.Name}: page didn't match a known CarEdge layout, will retry later",
                    Color.Yellow,
                    DealerGradeTally.Failed,
                    Stamped: false);
        }
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

/// <summary>How a dealer's lookup counted toward the run's closing tally.</summary>
public enum DealerGradeTally
{
    Graded,
    Ungraded,
    Unmatched,
    Failed,
}

/// <summary>What <see cref="DealerGradeCommand.ApplyResult"/> decided: the line to print, its color,
/// which tally the dealer counts toward, and whether the dealer's row was stamped and so needs
/// saving.</summary>
public sealed record DealerGradeOutcome(string Line, Color Color, DealerGradeTally Tally, bool Stamped);
