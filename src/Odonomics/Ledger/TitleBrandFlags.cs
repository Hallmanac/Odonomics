using Odonomics.Domain;

namespace Odonomics.Ledger;

/// <summary>The title-brand red flag a vehicle's own postings raise: a posting whose detail text stated
/// a title brand (see <see cref="PostingEntity.TitleBrandPhrase"/>). It comes from the ledger and needs
/// no research fetch, so `odo rank` can show it for a vehicle nobody has researched yet.</summary>
public static class TitleBrandFlags
{
    /// <summary>The title-brand-listed flag (see <see cref="RedFlagsEvaluator.TitleBrandListed"/>) for
    /// every posting of <paramref name="vehicle"/> that holds a phrase, gone ones included: a listing
    /// that stated the brand is evidence about the car even after that listing is gone, and a later
    /// detail visit that finds the wording dropped replaces the phrase on its own posting. A list of
    /// zero or one so a caller can append it to the flags it already has.</summary>
    public static IReadOnlyList<RedFlag> For(VehicleEntity vehicle)
    {
        RedFlag? flag = RedFlagsEvaluator.TitleBrandListed(vehicle.Postings
            .Where(p => !string.IsNullOrWhiteSpace(p.TitleBrandPhrase))
            .OrderByDescending(p => p.LastSeen)
            .Select(p => (p.Source, p.TitleBrandPhrase!.Trim())));

        return flag is null
            ? []
            : [flag];
    }
}
