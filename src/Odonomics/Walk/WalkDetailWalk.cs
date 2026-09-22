namespace Odonomics.Walk;

/// <summary>What happened when the walk actually opened one candidate detail link.</summary>
public enum DetailPageOutcome
{
    /// <summary>The page never yielded a usable extraction (a load error, a challenge, an
    /// extraction API failure).</summary>
    Failed,

    /// <summary>The page is some other vehicle than the one this pair asked for. This is the one
    /// outcome that never spends any of the per-pair cap: a search page that mixes in other
    /// models (neither cars.com's nor Carvana's own model filter separates a hybrid or plug-in
    /// variant from its base model) still gets every real candidate visited, up to the pool
    /// <see cref="WalkDetailWalk.RunAsync"/> was handed.</summary>
    NotMatching,

    /// <summary>The page matched but had no VIN to key a ledger row on.</summary>
    NoVin,

    /// <summary>The page matched and had a VIN but was missing year, price, or mileage.</summary>
    MissingFields,

    /// <summary>The page matched, had a VIN, and had every required field; it was upserted.</summary>
    Upserted,
}

/// <summary>The reason wording an operator sees for a dropped outcome, shared between the
/// per-page detail lines and the pair/run summaries so the two always agree by eye.</summary>
public static class WalkOutcomeWording
{
    public static string DroppedReason(DetailPageOutcome outcome) => outcome switch
    {
        DetailPageOutcome.MissingFields => "missing fields",
        DetailPageOutcome.NoVin => "no VIN",
        DetailPageOutcome.NotMatching => "wrong model",
        DetailPageOutcome.Failed => "failed to load",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "not a dropped outcome"),
    };
}

/// <summary>How many candidate detail pages were dropped for each reason. Total, plus whatever
/// was saved, always equals the number of pages visited: NotMatching is included here (as "wrong
/// model") even though it never spends the per-pair cap, since the page was still visited.</summary>
public sealed record DroppedBreakdown(int MissingFields, int NoVin, int NotMatching, int Failed)
{
    public int Total => MissingFields + NoVin + NotMatching + Failed;

    public int this[DetailPageOutcome outcome] => outcome switch
    {
        DetailPageOutcome.MissingFields => MissingFields,
        DetailPageOutcome.NoVin => NoVin,
        DetailPageOutcome.NotMatching => NotMatching,
        DetailPageOutcome.Failed => Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "not a dropped outcome"),
    };
}

/// <summary>Tally of one (site, model) pair's detail-page walk: how many candidate links were
/// actually visited, how many were upserted, and how many were dropped for each reason. Visited
/// always equals Upserted plus Dropped.Total, so the pair and run summaries can report "pages,
/// saved, dropped" without the arithmetic ever looking contradictory.</summary>
public sealed record DetailWalkTally(int Visited, int Upserted, DroppedBreakdown Dropped);

/// <summary>
/// Walks a pool of candidate detail links for one (site, model) pair, stopping once
/// <paramref name="maxDetailPages"/> of them have not been rejected for not matching the
/// requested model, or the pool runs out, whichever comes first. A page dropped as some other
/// model never spends a slot of the cap, so a search page that returns other models mixed in with
/// the one asked for still gets a real chance to fill the cap with actual candidates, up to
/// however large a pool the caller handed in.
/// </summary>
public static class WalkDetailWalk
{
    public static async Task<DetailWalkTally> RunAsync(
        IReadOnlyList<string> candidateLinks,
        int maxDetailPages,
        Func<string, int, CancellationToken, Task<DetailPageOutcome>> visitLinkAsync,
        Func<CancellationToken, Task> gapBeforeNextLinkAsync,
        CancellationToken cancellationToken)
    {
        int visited = 0;
        int upserted = 0;
        int droppedMissingFields = 0;
        int droppedNoVin = 0;
        int droppedNotMatching = 0;
        int droppedFailed = 0;
        int spentOnCap = 0;

        for (int i = 0; i < candidateLinks.Count && spentOnCap < maxDetailPages; i++)
        {
            if (i > 0)
            {
                await gapBeforeNextLinkAsync(cancellationToken);
            }

            visited++;
            DetailPageOutcome outcome = await visitLinkAsync(candidateLinks[i], i, cancellationToken);
            if (outcome != DetailPageOutcome.NotMatching)
            {
                spentOnCap++;
            }

            switch (outcome)
            {
                case DetailPageOutcome.Upserted:
                    upserted++;
                    break;
                case DetailPageOutcome.NoVin:
                    droppedNoVin++;
                    break;
                case DetailPageOutcome.MissingFields:
                    droppedMissingFields++;
                    break;
                case DetailPageOutcome.NotMatching:
                    droppedNotMatching++;
                    break;
                case DetailPageOutcome.Failed:
                    droppedFailed++;
                    break;
            }
        }

        var dropped = new DroppedBreakdown(droppedMissingFields, droppedNoVin, droppedNotMatching, droppedFailed);
        return new DetailWalkTally(visited, upserted, dropped);
    }
}
