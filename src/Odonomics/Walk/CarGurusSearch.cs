using System.Text.RegularExpressions;
using Odonomics.Sources;

namespace Odonomics.Walk;

/// <summary>What the CarGurus walk target knows about the site's search: which id CarGurus gives each scenario
/// model, the search URL those ids go into, and how its result pages are followed.
/// CarGurus names a search's model by two numbers it assigns, the make (<c>m7</c> for Toyota) and the model
/// (<c>d15</c> for Prius), and nothing derives them from the model's name, so they are held here, one row per
/// scenario model ("Make Model", the way the scenario's allowed-models list writes it). A scenario model with no
/// row is an error that names it, and never a search for the wrong car: the fix is to read the two ids off a
/// search for that model in a browser (its <c>makeModelTrimPaths</c> parameter) and add the row.</summary>
public static class CarGurusSearch
{
    private static readonly IReadOnlyDictionary<string, (int MakeId, int ModelId)> ModelIds =
        new Dictionary<string, (int MakeId, int ModelId)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Toyota Prius"] = (7, 15),
            ["Toyota Corolla Hybrid"] = (7, 2840),
            ["Toyota Camry Hybrid"] = (7, 2908),
            // The plain (non-hybrid-named) Camry, for the base-model search a hybrid-only-from-year
            // rule adds (see WalkSites.CarGurus): CarGurus files the 2025+ Camry Hybrid here rather
            // than under d2908, since Toyota dropped the gas Camry that year. Verified live rather than
            // guessed: a probe browser (port 9223, profile edge-probe-profile, kept apart from the
            // walk's own port 9222) loaded the d2908 search above and its embedded facet data listed
            // this model's sibling entry as {"label":"Camry","value":"m7/d292"}; loading
            // m7/d292 with startYear=2025 directly then showed the site's own applied-years chip as
            // "2025 - 2026" and 394 vehicles found, confirming the id.
            ["Toyota Camry"] = (7, 292),
            ["Honda Insight"] = (6, 591),
        };

    /// <summary>The value of a search's <c>makeModelTrimPaths</c> parameter for <paramref name="make"/> and
    /// <paramref name="model"/>: the make id, a comma, the make id again, a slash, and the model id, with the
    /// comma and the slash percent-encoded the way the site writes them ("m7%2Cm7%2Fd15").</summary>
    public static string ModelPath(string make, string model) =>
        ModelIds.TryGetValue($"{make} {model}", out (int MakeId, int ModelId) ids)
            ? $"m{ids.MakeId}%2Cm{ids.MakeId}%2Fd{ids.ModelId}"
            : throw new InvalidOperationException(
                $"CarGurus has no id for {make} {model}: add its make and model ids to CarGurusSearch (read them off the makeModelTrimPaths of a CarGurus search for it)");

    /// <summary>CarGurus's search URL for <paramref name="query"/>: the zip and radius, the model, the minimum model year
    /// (<c>startYear</c>; the site ignores <c>minYear</c>, <c>minModelYear</c> and <c>yearMin</c>) and the mileage ceiling,
    /// best match first. The site prints the years it applied as a chip ("2019 - 2026") when it honors them.</summary>
    public static string SearchUrl(ListingQuery query) => SearchUrl(query, query.Model, query.YearMin);

    /// <summary>The same URL <see cref="SearchUrl(ListingQuery)"/> builds, but for <paramref name="model"/> and
    /// <paramref name="yearMin"/> rather than the query's own: for a hybrid-only-from-year model, whose base-model
    /// search (see <see cref="WalkSites.CarGurus"/>) needs the base model's own id and the hybrid-only year rather
    /// than the query's hybrid model and minimum year.</summary>
    public static string SearchUrl(ListingQuery query, string model, int yearMin) =>
        $"https://www.cargurus.com/search?zip={query.Zip}&distance={query.RadiusMiles}&makeModelTrimPaths={ModelPath(query.Make, model)}" +
        $"&startYear={yearMin}&maxMileage={query.MaxMileage}&sortDirection=ASC&sortType=BEST_MATCH";

    /// <summary>The URL of result page <paramref name="pageNumber"/> of <paramref name="searchUrl"/>: <c>page=N</c>, and
    /// the page's <c>pageAlignment</c> when the first page gave one. The alignment is how many cards the first page held and
    /// how many each later page does; without it a later page is cut from a different offset, so it repeats some
    /// cards and skips others (a 96-car Corolla Hybrid search read through plain <c>page=N</c> URLs held 78 distinct cars), and
    /// a page past the fourth sends the browser back to the first.</summary>
    public static string PagedSearchUrl(string searchUrl, int pageNumber, string? pagingToken) =>
        pagingToken is null
            ? $"{searchUrl}&page={pageNumber}"
            : $"{searchUrl}&page={pageNumber}&pageAlignment={Uri.EscapeDataString(pagingToken)}";

    private static readonly Regex PageAlignment = new(@"""pageAlignment"":""(?<token>[^""]+)""", RegexOptions.Compiled);

    /// <summary>The <c>pageAlignment</c> value a search page's HTML carries in its embedded data (a short base64
    /// string of the two counts), or null when it carries none.</summary>
    public static string? ReadPagingToken(string html)
    {
        Match match = PageAlignment.Match(html);
        return match.Success ? match.Groups["token"].Value : null;
    }
}
