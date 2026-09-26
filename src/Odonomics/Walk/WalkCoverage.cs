using Odonomics.Ledger;

namespace Odonomics.Walk;

/// <summary>What one (site, model) pair's walk actually visited, and how many of the ledger's known
/// links it kept current from their search cards without visiting them.</summary>
public sealed record WalkPairOutcome(int DetailPagesVisited, int Upserted, DroppedBreakdown Dropped, int KnownFromCards = 0);

/// <summary>One (site, model) pair's result, for the end-of-run summary. <see cref="Completed"/>
/// is false when the pair's own walk threw (a page that never loaded, a site that errored); the
/// walk moves on to the next pair rather than aborting the whole run. <see cref="KnownFromCards"/> is
/// how many links the ledger already held that the pair touched from their cards.</summary>
public sealed record WalkPairSummary(string Site, string Model, int DetailPagesVisited, int Upserted, DroppedBreakdown Dropped, bool Completed, int KnownFromCards = 0);

/// <summary>
/// Walks every (site, model) pair in order, stamping the run's coverage token the moment each
/// pair's own walk completes, rather than once at the end of the whole run: a run cancelled or
/// interrupted partway through leaves only the pairs that actually finished looking covered, and
/// the rest untouched, so a later diff never treats an unwalked pair's stale postings as "gone".
/// A pair that throws anything other than a cancellation is reported through
/// <paramref name="onPairFailed"/> and skipped; the walk continues with the next pair. A
/// cancellation stops the walk entirely and propagates to the caller, leaving whatever pairs
/// already completed stamped in the database.
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
                    coveredTokens.Add(RunSources.Key(site.Name, bareModel));
                    currentRun.Sources = RunSources.Join(coveredTokens);
                    try
                    {
                        await persistCoverageAsync(cancellationToken);
                    }
                    catch
                    {
                        coveredTokens.RemoveAt(coveredTokens.Count - 1);
                        currentRun.Sources = sourcesBeforePair;
                        throw;
                    }

                    summaries.Add(new WalkPairSummary(site.Name, model, outcome.DetailPagesVisited, outcome.Upserted, outcome.Dropped, Completed: true, outcome.KnownFromCards));
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
