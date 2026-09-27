using System.Text.RegularExpressions;

namespace Odonomics.Domain;

/// <summary>Toyota's plug-in hybrid Prius variant, kept as its own scenario model ("Toyota Prius
/// Prime") rather than priced as a plain Prius. A Prius search (the walk's own pair, and auto.dev's
/// and marketcheck's "Toyota Prius" query) mixes Prime candidates in with plain ones, the same way a
/// "Camry Hybrid" search mixes in plain Camry trims, and a Prime's own title or fuel type is what
/// tells the two apart: a title that names "Prime" ("2021 Toyota Prius Prime Limited"), a title that
/// instead names "Plug-in Hybrid" ("2026 Toyota Prius Plug-in Hybrid SE", Toyota's own name for the
/// car from model year 2025 on, whose model text never says "Prime" at all), or a fuel type of
/// "Plug-in Hybrid" for a page whose model text names neither (either spelling of "in" matches:
/// Carvana's own facet reads it "Plug-In Hybrid", and marketcheck's recorded heading reads it
/// "Plug In Hybrid" with no hyphen at all). Every route still requires the candidate's own model
/// text to say "Prius" and its make, when stated, to say "Toyota", so a candidate that slipped in
/// from some unrelated Toyota plug-in hybrid (the RAV4 Prime, say) is never mistaken for this
/// one.</summary>
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
}
