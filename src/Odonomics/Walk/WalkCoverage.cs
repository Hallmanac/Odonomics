using Odonomics.Ledger;

namespace Odonomics.Walk;

/// <summary>What one (site, model) pair's walk actually visited, and how many of the ledger's known
/// links it kept current from their search cards without visiting them. <see cref="Capped"/> is true when
/// an explicit --max ended the pair before the site ran out of results (the link pool filled while more pages
/// remained, a search was never opened, or with --revisit the cap left collected links unvisited).
/// <see cref="FailedPage"/> is the number of the first result page after the first that failed to load
/// (null when none did), which ended that search's paging with the pages after it unread, so the pair's
/// coverage is partial for that reason too. <see cref="UnrenderedKnownUrls"/> are the canonical URLs of
/// known links whose card never rendered within the site's bounded render wait (see
/// <see cref="SearchPageCardRenderWait"/>) and so were never touched: each is exempted from the diff on
/// its own (see <see cref="Cli.Commands.WalkCommand"/>) whether or not the same render wait also makes the
/// whole pair's own coverage partial (see <see cref="RenderingDegraded"/> below). <see cref="AlsoCoveredModel"/> is the bare model of a second
/// vehicle this pair's own search always mixes in and saves under its own scenario model (a Prius Prime
/// candidate mixed into the "Toyota Prius" pair's own search, see PriusPrimeVariant), set whenever the
/// pair's own search ran, whether or not it happened to save one this run: a Prime already on the ledger
/// is kept current from its search card like any other known link and never revisited, so gating this on
/// a fresh save would leave every run after the first one that ever found a Prime unable to stamp its
/// coverage again. A "source:model" token gets stamped for it beside the pair's own (see
/// WalkCoverage.RunAsync), with the same partial-coverage markers, since a model with no walk pair of its
/// own otherwise has no run that ever covers it, leaving its postings unable to ever be reported gone.
/// <see cref="SearchFellShortOfStatedCount"/> is true when a multi-search pair (a hybrid-only-from-year
/// model's hybrid and base-model searches) had a search that fell short of its own stated count outside
/// the tolerance <see cref="WalkSearchCoverage"/> allows: the pages that would have closed that gap were
/// never read, so this is treated the same way a failed later page is, with the same "pages unread" reason
/// (see <see cref="FailedPage"/> and <see cref="Ledger.LedgerDiffService"/>). <see cref="RenderingDegraded"/>
/// is true when a known posting's card was among those its site's render wait still gave up on even after
/// a final re-check (see <see cref="SearchPageCardRenderWait.StillUnrenderedAsync"/>): unlike
/// <see cref="UnrenderedKnownUrls"/>'s own per-URL exemption, this cannot say which specific known posting
/// was affected, since a card the render wait never saw as a link at all (throttled off the page entirely,
/// not merely slow to render) is invisible to it the same way. This run did not actually measure everything
/// the pair's own search would ordinarily show, so it is treated the same way a failed later page is, with
/// the same "pages unread" reason. A cars.com search whose own paging stopped on a page holding only
/// CarMax, beyond-radius, or no-distance cards (see <see cref="WalkSearchPages.CollectLinksAsync"/>'s own
/// <c>onStoppedOnPaddingOnlyPage</c>) is not one of the reasons above at all: a CarMax or beyond-radius
/// card is never a candidate this walk would keep no matter what a later page holds, and a no-distance
/// card's own known posting, if it has one, is kept current from its card the moment it is reported (see
/// <see cref="WalkSearchPages.CollectLinksAsync"/>'s own <c>onNoDistance</c> touch), the same protective
/// reason a below-floor or over-mileage card's known posting is, so the pair's coverage stays full for that
/// stop regardless of whether any of those cards ever resolves.</summary>
public sealed record WalkPairOutcome(int DetailPagesVisited, int Upserted, DroppedBreakdown Dropped, int KnownFromCards = 0, bool Capped = false, int? FailedPage = null, int SkippedBeyondRadius = 0, int SkippedNoDistance = 0, int SkippedUnrendered = 0, IReadOnlyList<string>? UnrenderedKnownUrls = null, string? AlsoCoveredModel = null, bool SearchFellShortOfStatedCount = false, bool RenderingDegraded = false);

/// <summary>One (site, model) pair's result, for the end-of-run summary. <see cref="Completed"/>
/// is false when the pair's own walk threw (a page that never loaded, a site that errored); the
/// walk moves on to the next pair rather than aborting the whole run. <see cref="KnownFromCards"/> is
/// how many links the ledger already held that the pair touched from their cards. <see cref="Capped"/> is
/// true when the pair stopped short of the site's results (see <see cref="WalkPairOutcome.Capped"/>), and
/// <see cref="FailedPage"/> is the result page that failed to load, if one did. <see cref="UnrenderedKnownUrls"/>
/// carries forward <see cref="WalkPairOutcome.UnrenderedKnownUrls"/>, for the caller to hand every pair's
/// list to the diff once the whole run is done. <see cref="SearchFellShortOfStatedCount"/>,
/// and <see cref="RenderingDegraded"/> carry forward <see cref="WalkPairOutcome.SearchFellShortOfStatedCount"/>
/// and <see cref="WalkPairOutcome.RenderingDegraded"/>.</summary>
public sealed record WalkPairSummary(string Site, string Model, int DetailPagesVisited, int Upserted, DroppedBreakdown Dropped, bool Completed, int KnownFromCards = 0, bool Capped = false, int? FailedPage = null, int SkippedBeyondRadius = 0, int SkippedNoDistance = 0, int SkippedUnrendered = 0, IReadOnlyList<string>? UnrenderedKnownUrls = null, bool SearchFellShortOfStatedCount = false, bool RenderingDegraded = false);

/// <summary>
/// Walks every (site, model) pair in order, stamping the run's coverage token the moment each
/// pair's own walk completes, rather than once at the end of the whole run: a run cancelled or
/// interrupted partway through leaves only the pairs that actually finished looking covered, and
/// the rest untouched, so a later diff never treats an unwalked pair's stale postings as "gone".
/// A pair that throws anything other than a cancellation is reported through
/// <paramref name="onPairFailed"/> and skipped; the walk continues with the next pair. A
/// cancellation stops the walk entirely and propagates to the caller, leaving whatever pairs
/// already completed stamped in the database. A pair that stopped short of the site's results
/// (<see cref="WalkPairOutcome.Capped"/>) gets a partial-coverage token beside its own (see
/// <see cref="RunSources.PartialKey"/>), stamped and rolled back with it, and so does a pair whose later
/// result page failed to load or whose own rendering looked degraded (<see cref="WalkPairOutcome.FailedPage"/>
/// or <see cref="WalkPairOutcome.RenderingDegraded"/>, see <see cref="RunSources.UnreadKey"/>).
/// </summary>
public static class WalkCoverage
{
    public static async Task<List<WalkPairSummary>> RunAsync(
        RunEntity currentRun,
        IReadOnlyList<WalkSite> sites,
        IReadOnlyList<string> models,
        Func<WalkSite, string, CancellationToken, Task<WalkPairOutcome>> walkPairAsync,
        Action<WalkSite, string> announcePair,
        Action<WalkSite, string, Exception> onPairFailed,
        Func<CancellationToken, Task> gapBeforeNextPairAsync,
        Func<CancellationToken, Task> persistCoverageAsync,
        CancellationToken cancellationToken)
    {
        var summaries = new List<WalkPairSummary>();
        List<string> coveredTokens = [.. RunSources.Split(currentRun)];
        bool firstPair = true;

        foreach (WalkSite site in sites)
        {
            foreach (string model in models)
            {
                if (!firstPair)
                {
                    await gapBeforeNextPairAsync(cancellationToken);
                }

                firstPair = false;
                announcePair(site, model);

                try
                {
                    WalkPairOutcome outcome = await walkPairAsync(site, model, cancellationToken);
                    (_, string bareModel) = MakeModel.Split(model);
                    string sourcesBeforePair = currentRun.Sources;
                    int tokensBeforePair = coveredTokens.Count;
                    string coverageKey = RunSources.Key(site.Name, bareModel);
                    coveredTokens.Add(coverageKey);
                    if (outcome.Capped)
                    {
                        coveredTokens.Add(RunSources.PartialKey(coverageKey));
                    }

                    if (outcome.FailedPage is not null || outcome.SearchFellShortOfStatedCount || outcome.RenderingDegraded)
                    {
                        coveredTokens.Add(RunSources.UnreadKey(coverageKey));
                    }

                    if (outcome.AlsoCoveredModel is not null)
                    {
                        string alsoCoverageKey = RunSources.Key(site.Name, outcome.AlsoCoveredModel);
                        coveredTokens.Add(alsoCoverageKey);
                        if (outcome.Capped)
                        {
                            coveredTokens.Add(RunSources.PartialKey(alsoCoverageKey));
                        }

                        if (outcome.FailedPage is not null || outcome.SearchFellShortOfStatedCount || outcome.RenderingDegraded)
                        {
                            coveredTokens.Add(RunSources.UnreadKey(alsoCoverageKey));
                        }
                    }

                    currentRun.Sources = RunSources.Join(coveredTokens);
                    try
                    {
                        await persistCoverageAsync(cancellationToken);
                    }
                    catch
                    {
                        coveredTokens.RemoveRange(tokensBeforePair, coveredTokens.Count - tokensBeforePair);
                        currentRun.Sources = sourcesBeforePair;
                        throw;
                    }

                    summaries.Add(new WalkPairSummary(site.Name, model, outcome.DetailPagesVisited, outcome.Upserted, outcome.Dropped, Completed: true, outcome.KnownFromCards, outcome.Capped, outcome.FailedPage, outcome.SkippedBeyondRadius, outcome.SkippedNoDistance, outcome.SkippedUnrendered, outcome.UnrenderedKnownUrls, outcome.SearchFellShortOfStatedCount, outcome.RenderingDegraded));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    onPairFailed(site, model, ex);
                    summaries.Add(new WalkPairSummary(site.Name, model, 0, 0, new DroppedBreakdown(0, 0, 0, 0), Completed: false));
                }
            }
        }

        return summaries;
    }
}
