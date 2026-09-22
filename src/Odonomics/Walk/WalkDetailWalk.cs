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

/// <summary>Tally of one (site, model) pair's detail-page walk: how many candidate links were
/// actually opened, how many were upserted, and how many matched but had no VIN.</summary>
public sealed record DetailWalkTally(int Visited, int Upserted, int DroppedNoVin);

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
        int droppedNoVin = 0;
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
            }
        }

        return new DetailWalkTally(visited, upserted, droppedNoVin);
    }
}
