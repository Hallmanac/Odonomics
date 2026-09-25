namespace Odonomics.Walk;

/// <summary>What happened when the walk actually opened one candidate detail link.</summary>
public enum DetailPageOutcome
{
    /// <summary>The page itself never yielded usable text: a load error, a challenge, or some
    /// other exception opening or reading the tab. See <see cref="ExtractionFailed"/> for the
    /// page loading fine but the extraction step then failing.</summary>
    Failed,

    /// <summary>The page loaded and its text was captured, but the extraction API call over that
    /// text failed or returned no result (a rate limit, a bad API key, a malformed response).
    /// Kept distinct from <see cref="Failed"/> so an operator isn't sent looking at the browser
    /// or network for what's actually an extraction-service problem.</summary>
    ExtractionFailed,

    /// <summary>The page is some other vehicle than the one this pair asked for. Like
    /// <see cref="Repeat"/>, this never spends any of the per-pair cap: a search page that still
    /// mixes in other models, despite the walk's own search URL now asking each site for the
    /// exact model in the common case (one deliberate exception: cars.com queries both the hybrid
    /// facet and the base model for a hybrid-only-from-year model, see
    /// <see cref="Odonomics.Walk.WalkSites"/>), still gets every real candidate visited, up to the
    /// pool <see cref="WalkDetailWalk.RunAsync"/> was handed.</summary>
    NotMatching,

    /// <summary>The page had no VIN to key a ledger row on. Checked before the model-match check
    /// below, so this can be recorded even for a page that is also some other model; that page
    /// still spends a slot of the per-pair cap, since it never got far enough to tell.</summary>
    NoVin,

    /// <summary>The page's VIN was already saved earlier in this same (site, model) pair's walk,
    /// via a different detail link. Like <see cref="NotMatching"/>, this never spends the per-pair
    /// cap: the pool's own links, not the model's real inventory, produced the duplicate, so a
    /// search page that links one listing twice still lets the walk reach as many distinct
    /// candidates as the cap allows.</summary>
    Repeat,

    /// <summary>The page is a new-car listing (see <see cref="NewCarPage"/>), which the scenario can
    /// never rank. Like <see cref="NotMatching"/> and <see cref="Repeat"/>, this never spends the
    /// per-pair cap: the search page mixing new cars into a used search, not the model's real used
    /// inventory, produced the visit, so it must not shorten the pair. Checked on the page text
    /// before extraction, so no extraction call is spent on it either.</summary>
    NewCar,

    /// <summary>The page matched and had a VIN but was missing year, price, or mileage. Only a used
    /// car's page lands here; a new-car page is <see cref="NewCar"/>.</summary>
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
        DetailPageOutcome.ExtractionFailed => "extraction failed",
        DetailPageOutcome.Repeat => "repeat",
        DetailPageOutcome.NewCar => "new-car listing",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "not a dropped outcome"),
    };
}

/// <summary>How many candidate detail pages were dropped for each reason. Total, plus whatever
/// was saved, always equals the number of pages visited: NotMatching, Repeat and NewCar are all
/// included here even though none spends the per-pair cap, since the page was still visited.</summary>
public sealed record DroppedBreakdown(int MissingFields, int NoVin, int NotMatching, int Failed, int ExtractionFailed = 0, int Repeat = 0, int NewCar = 0)
{
    public int Total => MissingFields + NoVin + NotMatching + Failed + ExtractionFailed + Repeat + NewCar;

    public int this[DetailPageOutcome outcome] => outcome switch
    {
        DetailPageOutcome.MissingFields => MissingFields,
        DetailPageOutcome.NoVin => NoVin,
        DetailPageOutcome.NotMatching => NotMatching,
        DetailPageOutcome.Failed => Failed,
        DetailPageOutcome.ExtractionFailed => ExtractionFailed,
        DetailPageOutcome.Repeat => Repeat,
        DetailPageOutcome.NewCar => NewCar,
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
/// requested model, for repeating a VIN this pair already saved, or for being a new car, or the
/// pool runs out, whichever comes first. A page dropped as some other model never spends a slot of the cap, so a search page
/// that returns other models mixed in with the one asked for still gets a real chance to fill the
/// cap with actual candidates, up to
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
        int droppedExtractionFailed = 0;
        int droppedRepeat = 0;
        int droppedNewCar = 0;
        int spentOnCap = 0;

        for (int i = 0; i < candidateLinks.Count && spentOnCap < maxDetailPages; i++)
        {
            if (i > 0)
            {
                await gapBeforeNextLinkAsync(cancellationToken);
            }

            visited++;
            DetailPageOutcome outcome = await visitLinkAsync(candidateLinks[i], i, cancellationToken);
            if (outcome is not (DetailPageOutcome.NotMatching or DetailPageOutcome.Repeat or DetailPageOutcome.NewCar))
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
                case DetailPageOutcome.ExtractionFailed:
                    droppedExtractionFailed++;
                    break;
                case DetailPageOutcome.Repeat:
                    droppedRepeat++;
                    break;
                case DetailPageOutcome.NewCar:
                    droppedNewCar++;
                    break;
            }
        }

        var dropped = new DroppedBreakdown(droppedMissingFields, droppedNoVin, droppedNotMatching, droppedFailed, droppedExtractionFailed, droppedRepeat, droppedNewCar);
        return new DetailWalkTally(visited, upserted, dropped);
    }
}
