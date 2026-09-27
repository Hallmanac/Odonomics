using System.Text.RegularExpressions;

namespace Odonomics.Domain;

/// <summary>Toyota's plug-in hybrid Prius variant: not a candidate the scenario ranks, so a Prius
/// Prime is recognized only to be excluded, never priced as a plain Prius or as a target model of
/// its own. A Prius search (the walk's own pair, and auto.dev's and marketcheck's "Toyota Prius"
/// query) mixes Prime candidates in with plain ones, the same way a "Camry Hybrid" search mixes in
/// plain Camry trims, and a Prime's own title or fuel type is what tells the two apart: a title that
/// names "Prime" ("2021 Toyota Prius Prime Limited"), a title that instead names "Plug-in Hybrid"
/// ("2026 Toyota Prius Plug-in Hybrid SE", Toyota's own name for the car from model year 2025 on,
/// whose model text never says "Prime" at all), or a fuel type of "Plug-in Hybrid" for a page whose
/// model text names neither (either spelling of "in" matches: Carvana's own facet reads it
/// "Plug-In Hybrid", and marketcheck's recorded heading reads it "Plug In Hybrid" with no hyphen at
/// all). Every route still requires the candidate's own model text to say "Prius" and its make, when
/// stated, to say "Toyota", so a candidate that slipped in from some unrelated Toyota plug-in hybrid
/// (the RAV4 Prime, say) is never mistaken for this one. A row already on the ledger under the plain
/// "Prius" model is still relabelled "Prius Prime" (see <see cref="StoredModel"/>) when a walked page
/// reads as one, so it excludes by name rather than continuing to pass as a plain Prius; nothing on
/// the ledger is ever deleted for reading this way.</summary>
public static class PriusPrimeVariant
{
    /// <summary>The scenario's own "Make Model" key for this variant.</summary>
    public const string ScenarioMakeModel = "Toyota Prius Prime";

    /// <summary>The bare model a walked page or a search candidate is stored with.</summary>
    public const string StoredModel = "Prius Prime";

    /// <summary>The wording of "Plug-in Hybrid" as a regex fragment, matching either spelling of
    /// "in" ("Plug-in Hybrid" or "Plug In Hybrid") case-insensitively. Shared with
    /// <see cref="Sources.ListingQuery"/>'s own base-model rejection so the two rules that decide
    /// whether a candidate names a plug-in variant never drift apart from each other again.</summary>
    public const string PlugInHybridPattern = @"Plug[\s-]?in\s+Hybrid";

    private static readonly Regex PlugInHybridText = new(PlugInHybridPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool ReadsAsPrime(string? make, string? model, string? fuelType = null)
    {
        if (make is not null && !make.Contains("Toyota", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (model is null || !model.Contains("Prius", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool namesPrime = model.Contains("Prime", StringComparison.OrdinalIgnoreCase) || PlugInHybridText.IsMatch(model);
        bool fuelIsPlugIn = fuelType is not null && PlugInHybridText.IsMatch(fuelType);
        return namesPrime || fuelIsPlugIn;
    }

    /// <summary>What the walk's Prius pair does with a detail page once it has read the page's own
    /// make, model, and fuel type: <see cref="PrimeOutcome.NotAPrime"/> when <see cref="ReadsAsPrime"/>
    /// says it isn't one at all, so the pair's ordinary model-match check decides it instead;
    /// <see cref="PrimeOutcome.DropAsNotACandidate"/> for a Prime the ledger has never held, since the
    /// Prime is not a candidate the scenario ranks and a brand-new one is dropped rather than saved;
    /// <see cref="PrimeOutcome.RelabelKnown"/> for a Prime the ledger already holds under the plain
    /// "Prius" model (reached again only under a walk's own <c>--revisit</c>, since an unvisited known
    /// link is otherwise kept current from its search card), which is relabelled <see cref="StoredModel"/>
    /// and upserted instead of dropped, so it stays on the ledger and excludes by name rather than
    /// continuing to pass as a plain Prius.</summary>
    public static PrimeOutcome DecideOutcome(string? make, string? model, string? fuelType, bool isKnownOnLedger) =>
        !ReadsAsPrime(make, model, fuelType) ? PrimeOutcome.NotAPrime
        : isKnownOnLedger ? PrimeOutcome.RelabelKnown
        : PrimeOutcome.DropAsNotACandidate;
}

/// <summary>See <see cref="PriusPrimeVariant.DecideOutcome"/>.</summary>
public enum PrimeOutcome
{
    NotAPrime,
    DropAsNotACandidate,
    RelabelKnown,
}
