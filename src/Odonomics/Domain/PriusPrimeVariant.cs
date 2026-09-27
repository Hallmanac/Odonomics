namespace Odonomics.Domain;

/// <summary>Toyota's plug-in hybrid Prius variant, kept as its own scenario model ("Toyota Prius
/// Prime") rather than priced as a plain Prius. A Prius search (the walk's own pair, and auto.dev's
/// and marketcheck's "Toyota Prius" query) mixes Prime candidates in with plain ones, the same way a
/// "Camry Hybrid" search mixes in plain Camry trims, and a Prime's own title or fuel type is what
/// tells the two apart: a title that names "Prime" ("2021 Toyota Prius Prime Limited"), or a fuel
/// type of "Plug-in Hybrid" (Carvana's own facet reads it "Plug-In Hybrid", so the comparison is
/// case-insensitive) for a page whose title reads it differently ("2026 Toyota Prius Plug-in Hybrid
/// SE", whose model text never says "Prime" at all). Both routes require the candidate's own model
/// text to still say "Prius" and its make, when stated, to still say "Toyota", so a candidate that
/// slipped in from some unrelated Toyota plug-in hybrid (the RAV4 Prime, say) is never mistaken for
/// this one.</summary>
public static class PriusPrimeVariant
{
    /// <summary>The scenario's own "Make Model" key for this variant.</summary>
    public const string ScenarioMakeModel = "Toyota Prius Prime";

    /// <summary>The bare model a walked page or a search candidate is stored with.</summary>
    public const string StoredModel = "Prius Prime";

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

        bool namesPrime = model.Contains("Prime", StringComparison.OrdinalIgnoreCase);
        bool fuelIsPlugIn = string.Equals(fuelType, "Plug-in Hybrid", StringComparison.OrdinalIgnoreCase);
        return namesPrime || fuelIsPlugIn;
    }
}
