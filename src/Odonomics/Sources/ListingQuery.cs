using System.Text.RegularExpressions;
using Odonomics.Domain;

namespace Odonomics.Sources;

/// <summary>One target model's search parameters, derived from the scenario rather than
/// hardcoded, unlike the spike's fixed QueryGroup list. <paramref name="HybridOnlyFromModelYear"/>
/// is the scenario's own rule (Scenario.HybridOnlyFromModelYear) for this query's model, when it
/// has one: the model year this base model went hybrid-only, letting a candidate below that year
/// still fail the hybrid check while one at or above it passes without the listing ever saying
/// "Hybrid".</summary>
public sealed record ListingQuery(string Make, string Model, int YearMin, string Zip, int RadiusMiles, int MaxMileage, int? HybridOnlyFromModelYear = null)
{
    public bool MatchesYear(int? year) => year is not null && year >= YearMin;

    public bool MatchesMileage(int? mileage) => mileage is null || mileage <= MaxMileage;

    /// <summary>Whether this query asks for a hybrid variant of its base model ("Corolla Hybrid").</summary>
    public bool IsHybridVariant => Model.Contains("Hybrid", StringComparison.OrdinalIgnoreCase);

    private string BaseModelName => IsHybridVariant
        ? Model[..Model.IndexOf(" Hybrid", StringComparison.OrdinalIgnoreCase)]
        : Model;

    private bool MatchesMakeAndBaseModel(string? make, string? model, string? trim)
    {
        if (make is not null && !make.Contains(Make, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return $"{model} {trim}".Contains(BaseModelName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsHybridText(string? model, string? trim) =>
        $"{model} {trim}".Contains("Hybrid", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a candidate a source (an API response or a walked detail page) actually is
    /// the model this query asked for, rather than generic make inventory the source fell back to
    /// for an unrecognized or compound model facet. The spike found exactly this on cars.com and
    /// Carvana: a "Camry Hybrid" or "Corolla Hybrid" walk came back with gas trims mixed in. The
    /// walk's own search URLs now use each site's real hybrid facet (see WalkSites), but this
    /// check stays in place as a safety net for whatever still slips past that: an API source with
    /// no facet of its own, a compound model facet, or a degraded page. A candidate is never
    /// trusted as this query's own model just because it came back for this query; it has to
    /// actually say so, unless <see cref="HybridOnlyFromModelYear"/> says this base model has no
    /// gas version left to confuse it with at this candidate's year.</summary>
    public bool MatchesExtractedVehicle(string? make, string? model, string? trim, int? year)
    {
        if (!MatchesMakeAndBaseModel(make, model, trim))
        {
            return false;
        }

        if (!IsHybridVariant || ContainsHybridText(model, trim))
        {
            return true;
        }

        return HybridOnlyFromModelYear is int hybridYear && year is int candidateYear && candidateYear >= hybridYear;
    }

    /// <summary>The walk's own version of <see cref="MatchesExtractedVehicle"/>, which also has the
    /// page text to hand. A hybrid query accepts a page whose extraction split the title into a
    /// base model plus trim (model "Corolla", trim "LE") when the page's own title line still reads
    /// "&lt;year&gt; &lt;make&gt; &lt;base model&gt; Hybrid": the extraction model sometimes drops the
    /// variant word even though the title prints it, and the page is a real Corolla Hybrid all the
    /// same. A page also counts when its own spec block carries a fuel spec line that says hybrid
    /// ("Hybrid: Gas/Electric" on Autotrader), which is how a page titled "Certified 2026 Toyota
    /// Corolla SE FWD" says it is a Corolla Hybrid. Only the title line and a spec line above any
    /// similar-vehicles heading count, so a gas page that merely mentions a hybrid elsewhere (a
    /// similar-vehicles card, a review question) is still rejected. The API sources never call
    /// this; they have no page text.</summary>
    public bool MatchesWalkedPage(string? make, string? model, string? trim, int? year, string pageText) =>
        MatchesExtractedVehicle(make, model, trim, year)
        || TitleNamesHybrid(make, model, trim, year, pageText)
        || SpecLineNamesHybrid(make, model, trim, pageText);

    private bool SpecLineNamesHybrid(string? make, string? model, string? trim, string pageText)
    {
        if (!IsHybridVariant || !MatchesMakeAndBaseModel(make, model, trim))
        {
            return false;
        }

        foreach (string line in pageText.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (RelatedVehiclesHeading.IsMatch(line))
            {
                return false;
            }

            if (HybridSpecLine.IsMatch(line))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A fuel spec line that says the car is a hybrid, at the start of a line:
    /// "Hybrid: Gas/Electric" (Autotrader), "Fuel Type: Hybrid", "Fuel: Hybrid", or
    /// "Engine: ... Hybrid". "Plug-in Hybrid" or "Mild Hybrid" values do not match, since the
    /// word after the colon has to be Hybrid itself.</summary>
    private static readonly Regex HybridSpecLine = new(
        @"^(?:Hybrid:\s*Gas/Electric|Fuel(?:\s+Type)?:\s*Hybrid|Engine:.*\bHybrid)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The heading that opens a page's similar-vehicles or recommended-cars block, at the
    /// start of a line so the "View similar vehicles" button near the top of an Autotrader page
    /// does not count. Everything from here down is padding from other listings.</summary>
    private static readonly Regex RelatedVehiclesHeading = new(
        @"^(?:Check out\s+)?(?:Similar|Recommended)\s+(?:Vehicles|Cars|Styles)\b|^You may also like\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private bool TitleNamesHybrid(string? make, string? model, string? trim, int? year, string pageText)
    {
        if (!IsHybridVariant || year is not int titleYear || !MatchesMakeAndBaseModel(make, model, trim))
        {
            return false;
        }

        string? title = pageText
            .Split('\n', StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => TitleLine.IsMatch(line));

        return title is not null
            && Regex.IsMatch(
                title,
                $@"^(?:(?:New|Used|Certified|Price Drop)\s+)*{titleYear}\s+{Regex.Escape(Make)}\s+{Regex.Escape(BaseModelName)}\s+Hybrid\b",
                RegexOptions.IgnoreCase);
    }

    /// <summary>The first line of a page that starts a vehicle title: a year, optionally behind a
    /// condition or price-drop badge. Same idea as <see cref="Walk.NewCarPage"/>'s title line, so a
    /// "Similar vehicles" card further down never decides it.</summary>
    private static readonly Regex TitleLine = new(@"^(?:(?:New|Used|Certified|Price Drop)\s+)*\d{4}\s", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The year this query's hybrid-only rule takes effect, when a candidate otherwise
    /// matches this query's make and base model but falls below it: lets the walk explain a
    /// rejection as a known gas-only-before-year gap (e.g. "2024 Camry SE, gas-only before 2025")
    /// rather than a generic mismatch. Null when this isn't that case, whatever the reason.</summary>
    public int? GasOnlyBeforeHybridYear(string? make, string? model, string? trim, int? year)
    {
        if (!IsHybridVariant || HybridOnlyFromModelYear is not int hybridYear)
        {
            return null;
        }

        if (!MatchesMakeAndBaseModel(make, model, trim) || ContainsHybridText(model, trim))
        {
            return null;
        }

        return year is int candidateYear && candidateYear < hybridYear ? hybridYear : null;
    }

    /// <summary>Builds one query per target model in the scenario's allowed-models list.</summary>
    public static IReadOnlyList<ListingQuery> FromScenario(Scenario scenario) =>
        [.. scenario.Filters.AllowedModels.Select(makeModel => For(scenario, makeModel))];

    /// <summary>Builds the query for one "Make Model" string (e.g. "Toyota Camry Hybrid"); the make
    /// is always the first word, matching how every target model in this scenario is named. The
    /// minimum model year, maximum mileage, and hybrid-only year all come from the scenario, so a
    /// caller that turns the query into a site's search facets applies the same limits the scorer
    /// later enforces.</summary>
    public static ListingQuery For(Scenario scenario, string makeModel)
    {
        int spaceIndex = makeModel.IndexOf(' ');
        string make = spaceIndex < 0 ? makeModel : makeModel[..spaceIndex];
        string model = spaceIndex < 0 ? "" : makeModel[(spaceIndex + 1)..];
        int? hybridOnlyFromModelYear = scenario.HybridOnlyFromModelYear.TryGetValue(makeModel, out int hybridYear) ? hybridYear : null;
        return new ListingQuery(make, model, scenario.Filters.MinYearFor(makeModel), scenario.Zip, scenario.RadiusMiles, scenario.Filters.MaxMileage, hybridOnlyFromModelYear);
    }
}
