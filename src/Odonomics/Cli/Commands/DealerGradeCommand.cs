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
/// same-named cards its partial or absent location could not tell apart. The bare "Carvana" dealer
/// and the "Private seller" dealer are never looked up (see <see cref="SkipReason"/>); Carvana's hubs
/// are graded by their own names like any other dealer. Recording a grade also records the doc fee and
/// add-ons note the same card prints; `--refresh` looks up dealers that already have a grade again so
/// ones graded before the ledger kept those two can pick them up (see <see cref="NeedsCheck"/>).
/// Three consecutive CarEdge 404 pages mean the search URL itself is dead, not that three dealers in
/// a row are unrateable, so the run stops there and exits non-zero rather than burning through the
/// rest of the list against a URL that will keep failing; every dealer it never got to stays
/// ungraded and eligible for the next run.</summary>
public static class DealerGradeCommand
{
    public static async Task<int> RunAsync(bool all, string? vin, bool refresh, CancellationToken cancellationToken)
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
            targets = [.. (await db.Dealers.OrderBy(d => d.Id).ToListAsync(cancellationToken)).Where(d => NeedsCheck(d, refresh))];
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

            foreach (DealerEntity dealer in dealers.Where(d => !NeedsCheck(d, refresh)))
            {
                PrintGrade(dealer);
            }

            targets = [.. dealers.Where(d => NeedsCheck(d, refresh))];
        }

        if (targets.Count == 0)
        {
            AnsiConsole.MarkupLine(refresh ? "no dealers to check" : "no ungraded dealers to check");
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
        int kept = 0;
        int unmatched = 0;
        int failed = 0;
        var deadSearchUrlGate = new ConsecutiveDeadSearchUrlGate();
        bool visitedAny = false;
        for (int i = 0; i < targets.Count; i++)
        {
            DealerEntity dealer = targets[i];
            if (SkipReason(dealer) is string skipReason)
            {
                DealerGradeOutcome skipped = ApplySkip(dealer, skipReason, DateTimeOffset.UtcNow);
                ungraded++;
                AnsiConsole.Write(new Text(skipped.Line + Environment.NewLine, new Style(skipped.Color)));
                await db.SaveChangesAsync(cancellationToken);
                continue;
            }

            if (visitedAny)
            {
                await Task.Delay(pacing.RandomDetailGap(), cancellationToken);
            }

            visitedAny = true;
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
                    case DealerGradeTally.Kept:
                        kept++;
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

        AnsiConsole.MarkupLineInterpolated($"graded {graded}, recorded {ungraded} ungraded, kept {kept} stored, {unmatched} unmatched, {failed} failed");
        return deadSearchUrlGate.ShouldStop ? 1 : 0;
    }

    /// <summary>Whether a run looks this dealer up. By default only a dealer never checked, which is
    /// the checked-once rule. With <paramref name="refresh"/> a dealer that already has a grade is
    /// looked up again too, so one graded before the ledger kept the doc fee and add-ons note can
    /// pick them up; a dealer stamped checked with no grade (unrated, or skipped on purpose) stays
    /// checked, since a second look has nothing to add to it.</summary>
    public static bool NeedsCheck(DealerEntity dealer, bool refresh) =>
        dealer.GradeCheckedAt is null || (refresh && dealer.Grade is not null);

    /// <summary>Why a dealer is never looked up on CarEdge, or null when it is. The bare "Carvana"
    /// dealer is the chain, not a seller: CarEdge lists Carvana as a card per hub ("Carvana Winder"), so
    /// a search for the bare name returns hub cards, and taking one would grade the chain by an
    /// arbitrary hub. Every carvana posting a hub is known for is linked to that hub's own
    /// dealer, and those are graded by their hub name; the bare dealer keeps the postings no hub is
    /// known for, which therefore show no grade. The "Private seller" dealer is every private
    /// seller at once, and a person has no CarEdge Dealer Rating, so a search for that name could only
    /// match some unrelated business.</summary>
    public static string? SkipReason(DealerEntity dealer) => dealer switch
    {
        _ when CarvanaDealers.IsChain(dealer) => "Carvana is a chain and CarEdge lists it by hub; each Carvana hub is graded under its own name",
        _ when PrivateSellerDealers.IsPrivateSeller(dealer) => "a private seller is a person, not a dealer CarEdge rates",
        _ => null,
    };

    /// <summary>Records that a dealer was deliberately not looked up: stamps it checked, with
    /// <paramref name="reason"/> and no grade, so it is neither retried on every run nor mistaken for
    /// a dealer CarEdge reported unrated.</summary>
    public static DealerGradeOutcome ApplySkip(DealerEntity dealer, string reason, DateTimeOffset checkedAt)
    {
        dealer.Grade = null;
        dealer.DocFee = null;
        dealer.AddOnsNote = null;
        dealer.GradeReason = reason;
        dealer.GradeCheckedAt = checkedAt;
        return new DealerGradeOutcome($"{dealer.Name}: skipped, {reason}", Color.Grey, DealerGradeTally.Ungraded, Stamped: true);
    }

    /// <summary>Applies what the parser made of a dealer's CarEdge page to that dealer, and says how
    /// to report it. <see cref="DealerEntity.GradeCheckedAt"/> is stamped only for the outcomes
    /// that are final, a grade or CarEdge having none; every other outcome leaves the dealer
    /// untouched so a later run looks it up again. A dealer that already has a grade and now comes
    /// back with no card at all keeps that grade, doc fee, and add-ons note, since a missing card
    /// says nothing about the rating. A card marked "Not rated" is CarEdge positively saying there
    /// is none, so it clears the stored grade, doc fee, and add-ons note.</summary>
    public static DealerGradeOutcome ApplyResult(DealerEntity dealer, CarEdgeGradeResult result, DateTimeOffset checkedAt, string searchUrl)
    {
        switch (result.Status)
        {
            case CarEdgeGradeStatus.Graded:
                dealer.Grade = result.Grade;
                dealer.GradeReason = result.Reason;
                dealer.DocFee = result.DocFeeAmount;
                dealer.AddOnsNote = result.AddOnsNote;
                dealer.GradeCheckedAt = checkedAt;
                return new DealerGradeOutcome($"{dealer.Name}: {result.Grade}", Color.Default, DealerGradeTally.Graded, Stamped: true);
            case CarEdgeGradeStatus.NotFound when dealer.Grade is not null:
                dealer.GradeCheckedAt = checkedAt;
                return new DealerGradeOutcome(
                    $"{dealer.Name}: no longer on CarEdge, keeping the stored grade {dealer.Grade}",
                    Color.Grey,
                    DealerGradeTally.Kept,
                    Stamped: true);
            case CarEdgeGradeStatus.NotFound:
                dealer.GradeCheckedAt = checkedAt;
                return new DealerGradeOutcome($"{dealer.Name}: not on CarEdge", Color.Grey, DealerGradeTally.Ungraded, Stamped: true);
            case CarEdgeGradeStatus.NotRated:
                string line = dealer.Grade is null
                    ? $"{dealer.Name}: not rated on CarEdge"
                    : $"{dealer.Name}: CarEdge now shows this dealer as not rated, cleared the stored grade {dealer.Grade}";
                dealer.Grade = null;
                dealer.GradeReason = null;
                dealer.DocFee = null;
                dealer.AddOnsNote = null;
                dealer.GradeCheckedAt = checkedAt;
                return new DealerGradeOutcome(line, Color.Grey, DealerGradeTally.Ungraded, Stamped: true);
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
        if (dealer.Grade is null && dealer.GradeReason is not null)
        {
            AnsiConsole.MarkupLineInterpolated($"{dealer.Name}: skipped, {dealer.GradeReason}");
        }
        else if (dealer.Grade is null)
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
    Kept,
    Unmatched,
    Failed,
}

/// <summary>What <see cref="DealerGradeCommand.ApplyResult"/> decided: the line to print, its color,
/// which tally the dealer counts toward, and whether the dealer's row was stamped and so needs
/// saving.</summary>
public sealed record DealerGradeOutcome(string Line, Color Color, DealerGradeTally Tally, bool Stamped);
