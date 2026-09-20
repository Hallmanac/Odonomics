using System.Text.Json;
using System.Text.RegularExpressions;
using Spike.Models;

namespace Spike.Sources;

/// <summary>A vehicle record read directly off a search page, without a detail-page fetch or a model
/// extraction call. Same shape as the fields extraction would have produced, minus the cost.</summary>
public sealed record SearchPageCandidate(
    string? Vin,
    int? Year,
    string? Make,
    string? Model,
    string? Trim,
    decimal? Price,
    int? Mileage);

public static class PageWalkSources
{
    private static string Slugify(string value) => value.ToLowerInvariant().Replace(" ", "-");

    /// <summary>
    /// Day one found that guessing a hybrid-specific model slug (hyphenated, e.g. "corolla-hybrid",
    /// or underscored, e.g. "corolla_hybrid" as advertised by Cars.com's own model-facet JSON) makes
    /// these sites silently drop the model filter and show generic inventory for the make instead of
    /// erroring or 404ing. Cars.com's own recorded response for a "toyota-corolla_hybrid" query
    /// resolved <c>selected_search_filters</c> back to plain "toyota-corolla", proving the underscore
    /// slug does not actually apply despite appearing as a distinct, seemingly valid facet value.
    /// So every caller here asks for the base model only and leans on
    /// <see cref="QueryGroup.MatchesExtractedVehicle"/> to enforce the hybrid distinction afterward.
    /// </summary>
    private static string BaseModelSlug(string model)
    {
        int hybridIndex = model.IndexOf(" Hybrid", StringComparison.OrdinalIgnoreCase);
        return Slugify(hybridIndex >= 0 ? model[..hybridIndex] : model);
    }

    /// <summary>
    /// Reads a Cars.com search-result card's own VIN, year, make, model, trim, price, and mileage
    /// directly off the anchor's <c>data-*</c> attributes (e.g. <c>data-vin</c>, <c>data-year</c>):
    /// generic DOM data harvested by PageWalkEngine for every link, interpreted here per site. No
    /// candidate without a VIN in these attributes is returned, since the caller's whole reason for
    /// calling this is to decide whether a detail-page fetch can be skipped.
    /// </summary>
    private static SearchPageCandidate? FromCarsComAttributes(IReadOnlyDictionary<string, string> attrs)
    {
        if (!attrs.TryGetValue("data-vin", out string? vin) || string.IsNullOrWhiteSpace(vin))
        {
            return null;
        }

        int? year = attrs.TryGetValue("data-year", out string? y) && int.TryParse(y, out int yi) ? yi : null;
        decimal? price = attrs.TryGetValue("data-price", out string? p) && decimal.TryParse(p, out decimal pd) ? pd : null;
        int? mileage = attrs.TryGetValue("data-mileage", out string? m) && int.TryParse(m, out int mi) ? mi : null;

        return new SearchPageCandidate(
            Vin: vin,
            Year: year,
            Make: attrs.GetValueOrDefault("data-make"),
            Model: attrs.GetValueOrDefault("data-model"),
            Trim: attrs.GetValueOrDefault("data-trim"),
            Price: price,
            Mileage: mileage);
    }

    /// <summary>Extracts the numeric vehicle id Carvana uses in both its detail-page URLs
    /// (".../vehicle/4754913") and its JSON-LD offer URLs, so the two can be matched even though
    /// the href on the card is relative and carries a query string while the JSON-LD url is not.</summary>
    private static string? CarvanaVehicleId(string url)
    {
        Match match = Regex.Match(url, @"/vehicle/(\d+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Reads Carvana's own schema.org Vehicle JSON-LD blocks, one per search-result card, keyed by
    /// vehicle id so a detail link's href (which carries a different, relative form of the same URL)
    /// can be matched back to the right block. Every field the pass mark cares about (VIN, year,
    /// make, model, mileage, price) is present on the block; only trim is not, so it is left null.
    /// </summary>
    private static Dictionary<string, SearchPageCandidate> ParseCarvanaJsonLd(IReadOnlyList<string> jsonLdBlocks)
    {
        var byId = new Dictionary<string, SearchPageCandidate>();
        foreach (string block in jsonLdBlocks)
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(block);
            }
            catch (JsonException)
            {
                continue; // not every ld+json block on the page is a Vehicle record (breadcrumbs, org info, ...)
            }

            using (doc)
            {
                JsonElement root = doc.RootElement;
                if (!root.TryGetProperty("vehicleIdentificationNumber", out JsonElement vinEl) || vinEl.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                if (!root.TryGetProperty("offers", out JsonElement offers) || !offers.TryGetProperty("url", out JsonElement urlEl))
                {
                    continue;
                }

                string? id = CarvanaVehicleId(urlEl.GetString() ?? "");
                if (id is null)
                {
                    continue;
                }

                int? year = root.TryGetProperty("modelDate", out JsonElement yearEl) && yearEl.ValueKind == JsonValueKind.Number
                    ? yearEl.GetInt32() : null;
                string? make = root.TryGetProperty("manufacturer", out JsonElement makeEl) ? makeEl.GetString() : null;
                string? model = root.TryGetProperty("model", out JsonElement modelEl) ? modelEl.GetString() : null;
                int? mileage = root.TryGetProperty("mileageFromOdometer", out JsonElement mileageEl) && mileageEl.ValueKind == JsonValueKind.Number
                    ? mileageEl.GetInt32() : null;
                decimal? price = offers.TryGetProperty("price", out JsonElement priceEl) && priceEl.ValueKind == JsonValueKind.Number
                    ? priceEl.GetDecimal() : null;

                byId[id] = new SearchPageCandidate(vinEl.GetString(), year, make, model, Trim: null, price, mileage);
            }
        }

        return byId;
    }

    public static PageWalkListingSource CreateCarsCom(string profileDir, RecordedResponses recorded, ExtractionClient extraction) =>
        new(
            name: "cars.com",
            profileRoot: profileDir,
            buildSearchUrl: group =>
            {
                string makeSlug = Slugify(group.Make);
                string modelSlug = $"{makeSlug}-{BaseModelSlug(group.Model)}";
                return $"https://www.cars.com/shopping/results/?makes[]={makeSlug}&models[]={modelSlug}" +
                       $"&maximum_distance={group.RadiusMiles}&zip={group.Zip}&stock_type=used";
            },
            detailUrlPattern: new Regex("/vehicledetail/", RegexOptions.IgnoreCase),
            recorded: recorded,
            extraction: extraction,
            resolveFromSearchPage: search => search.DetailLinkAttributes
                .Select(kv => (url: kv.Key, candidate: FromCarsComAttributes(kv.Value)))
                .Where(x => x.candidate is not null)
                .ToDictionary(x => x.url, x => x.candidate!));

    // No resolveFromSearchPage for Autotrader: its search-page markup has never been observed
    // (see SPIKE-FINDINGS.md), so there is no verified data carrier to read a VIN from here.
    // Every candidate falls back to a detail-page fetch, the same as before this fix.
    public static PageWalkListingSource CreateAutotrader(string profileDir, RecordedResponses recorded, ExtractionClient extraction) =>
        new(
            name: "autotrader",
            profileRoot: profileDir,
            buildSearchUrl: group =>
            {
                string makeSlug = Slugify(group.Make);
                string modelSlug = BaseModelSlug(group.Model);
                return $"https://www.autotrader.com/cars-for-sale/all-cars/{makeSlug}/{modelSlug}/daytona-beach-fl-{group.Zip}" +
                       $"?searchRadius={group.RadiusMiles}&maxMileage={group.MaxMileage}";
            },
            detailUrlPattern: new Regex("/cars-for-sale/vehicledetails", RegexOptions.IgnoreCase),
            recorded: recorded,
            extraction: extraction);

    public static PageWalkListingSource CreateCarvana(string profileDir, RecordedResponses recorded, ExtractionClient extraction) =>
        new(
            name: "carvana",
            profileRoot: profileDir,
            buildSearchUrl: group =>
            {
                string modelSlug = $"{Slugify(group.Make)}-{BaseModelSlug(group.Model)}";
                return $"https://www.carvana.com/cars/{modelSlug}?zip={group.Zip}";
            },
            detailUrlPattern: new Regex("/vehicle/", RegexOptions.IgnoreCase),
            recorded: recorded,
            extraction: extraction,
            resolveFromSearchPage: search =>
            {
                Dictionary<string, SearchPageCandidate> byId = ParseCarvanaJsonLd(search.JsonLdBlocks);
                return search.DetailLinks
                    .Select(url => (url, id: CarvanaVehicleId(url)))
                    .Where(x => x.id is not null && byId.ContainsKey(x.id!))
                    .ToDictionary(x => x.url, x => byId[x.id!]);
            });

    /// <summary>Runs Cars.com; if it never turns up a usable, in-scope VIN and it was blocked at
    /// some point, falls back to Autotrader per the brief. Gated on matching candidates, not raw
    /// candidate count: a page-walk source can end a run with candidates on hand that are all
    /// wrong-model listings the site returned because it silently ignored the model filter (see
    /// QueryGroup.MatchesExtractedVehicle), which is not evidence the fallback is unneeded.</summary>
    public static async Task<SourceRunResult> RunAggregatorAsync(
        string profileRoot, RecordedResponses recorded, ExtractionClient extraction, CancellationToken cancellationToken)
    {
        PageWalkListingSource carsCom = CreateCarsCom(Path.Combine(profileRoot, "cars.com"), recorded, extraction);
        SourceRunResult primary = await carsCom.RunAsync(cancellationToken);
        bool hasUsableCandidates = primary.Candidates.Any(c => c.MatchesQuery && !string.IsNullOrWhiteSpace(c.Vin));
        if (hasUsableCandidates || !primary.WasBlocked)
        {
            return primary;
        }

        PageWalkListingSource autotrader = CreateAutotrader(Path.Combine(profileRoot, "autotrader"), recorded, extraction);
        SourceRunResult fallback = await autotrader.RunAsync(cancellationToken);
        fallback.Failures.Insert(0,
            $"cars.com found {primary.CandidatesFound} candidates on search pages but none were usable, in-scope VINs, and bot defenses blocked part of the walk ({string.Join("; ", primary.Failures)}); fell back to autotrader for VINs");
        return fallback;
    }
}
