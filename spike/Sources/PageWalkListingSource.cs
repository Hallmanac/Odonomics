using System.Diagnostics;
using System.Text.RegularExpressions;
using Spike.Models;

namespace Spike.Sources;

/// <summary>
/// Generic headed-Chrome page walk. Phase one visits one search page per query group and harvests
/// detail links; for each link, <paramref name="resolveFromSearchPage"/> (when the site has one; see
/// PageWalkSources) is checked first for a VIN already sitting on the search page. Phase two visits
/// a capped number of only the remaining detail pages, i.e. those the search page didn't already
/// resolve, through the model extraction step: "detail pages only where the VIN is not on the search
/// page," per the brief. Each navigation gets its own fresh browser profile (see PageWalkEngine for
/// why). Shared by every Playwright-based source (Cars.com, Autotrader, Carvana); only the URL
/// shapes, the detail-link pattern, and the search-page resolver differ.
/// </summary>
public sealed class PageWalkListingSource(
    string name,
    string profileRoot,
    Func<QueryGroup, string> buildSearchUrl,
    Regex detailUrlPattern,
    RecordedResponses recorded,
    ExtractionClient extraction,
    Func<PageWalkResult, IReadOnlyDictionary<string, SearchPageCandidate>>? resolveFromSearchPage = null,
    int detailPageCapPerGroup = 6) : IListingSource
{
    public string Name => name;

    public async Task<SourceRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var result = new SourceRunResult { Source = Name };
        Stopwatch stopwatch = Stopwatch.StartNew();

        // Phase 1: one search page per query group. Any link the search page's own resolver
        // already ties to a VIN becomes a candidate immediately, at zero extraction cost; only the
        // rest are queued for a detail-page fetch.
        var pendingDetailUrls = new List<(QueryGroup Group, string Url)>();
        bool searchBlocked = false;
        int totalCandidatesSeen = 0;

        foreach (QueryGroup group in QueryGroup.All)
        {
            string fileBase = $"{group.Make}-{group.Model}".Replace(" ", "_");

            if (searchBlocked)
            {
                result.Failures.Add($"{group.Make} {group.Model}: skipped, {Name} search already blocked earlier in this run");
                continue;
            }

            PageWalkResult search;
            try
            {
                search = await PageWalkEngine.NavigateAsync(profileRoot, buildSearchUrl(group), detailUrlPattern, cancellationToken);
            }
            catch (Exception ex)
            {
                result.Failures.Add($"{group.Make} {group.Model}: search page navigation failed: {ex.Message}");
                continue;
            }

            string searchRawPath = await recorded.WriteAsync(Name, $"{fileBase}-search.html", search.Html, cancellationToken);

            if (search.Blocked)
            {
                result.Failures.Add($"{group.Make} {group.Model}: blocked on search page ({search.BlockReason})");
                searchBlocked = true;
                continue;
            }

            if (search.DetailLinks.Count == 0)
            {
                result.Failures.Add($"{group.Make} {group.Model}: no detail links found on search page (0 candidates)");
                continue;
            }

            totalCandidatesSeen += search.DetailLinks.Count;

            IReadOnlyDictionary<string, SearchPageCandidate> knownFromSearch =
                resolveFromSearchPage?.Invoke(search) ?? new Dictionary<string, SearchPageCandidate>();

            var needsDetailFetch = new List<string>();
            foreach (string url in search.DetailLinks)
            {
                if (!knownFromSearch.TryGetValue(url, out SearchPageCandidate? known) || string.IsNullOrWhiteSpace(known.Vin))
                {
                    needsDetailFetch.Add(url);
                    continue;
                }

                bool matchesQuery = group.MatchesExtractedVehicle(known.Make, known.Model, known.Trim, known.Year, known.Mileage);
                if (!matchesQuery)
                {
                    result.Failures.Add(
                        $"{group.Make} {group.Model}: search page listing ({known.Year} {known.Make} {known.Model} {known.Trim}) did not match the query, excluded from counts but kept for the extraction hand-check");
                }

                result.Candidates.Add(new Candidate
                {
                    Source = Name,
                    Vin = known.Vin,
                    Year = known.Year,
                    Make = known.Make ?? group.Make,
                    Model = known.Model ?? group.Model,
                    Trim = known.Trim,
                    Price = known.Price,
                    Mileage = known.Mileage,
                    Url = url,
                    RawRecordPath = searchRawPath,
                    WasExtracted = false,
                    MatchesQuery = matchesQuery,
                });
            }

            foreach (string url in needsDetailFetch.Take(detailPageCapPerGroup))
            {
                pendingDetailUrls.Add((group, url));
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        result.SearchOnlyCandidatesFound = totalCandidatesSeen;

        // Phase 2: detail pages, up to the cap, only for links the search page didn't already
        // resolve. Each gets its own fresh profile (see PageWalkEngine), so one blocked detail page
        // does not doom the rest.
        int index = 0;
        bool anyDetailBlocked = false;
        foreach ((QueryGroup group, string detailUrl) in pendingDetailUrls)
        {
            index++;
            string fileBase = $"{group.Make}-{group.Model}".Replace(" ", "_");

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

            PageWalkResult detail;
            try
            {
                detail = await PageWalkEngine.NavigateAsync(profileRoot, detailUrl, null, cancellationToken);
            }
            catch (Exception ex)
            {
                result.Failures.Add($"{group.Make} {group.Model} detail {index}: navigation failed: {ex.Message}");
                continue;
            }

            string rawPath = await recorded.WriteAsync(Name, $"{fileBase}-detail-{index}.html", detail.Html, cancellationToken);

            if (detail.Blocked)
            {
                result.Failures.Add($"{group.Make} {group.Model} detail {index}: blocked ({detail.BlockReason})");
                anyDetailBlocked = true;
                continue;
            }

            ExtractionOutcome outcome = await extraction.ExtractAsync(detail.BodyText, cancellationToken);
            result.DollarsSpent += outcome.CostUsd;

            if (outcome.Error is not null)
            {
                result.Failures.Add($"{group.Make} {group.Model} detail {index}: extraction failed: {outcome.Error}");
                continue;
            }

            ExtractionResult extracted = outcome.Result!;
            bool matchesQuery = group.MatchesExtractedVehicle(extracted.Make, extracted.Model, extracted.Trim, extracted.Year, extracted.Mileage);
            if (!matchesQuery)
            {
                result.Failures.Add(
                    $"{group.Make} {group.Model} detail {index}: site returned a non-matching vehicle ({extracted.Year} {extracted.Make} {extracted.Model} {extracted.Trim}), excluded from counts but kept for the extraction hand-check");
            }

            result.Candidates.Add(new Candidate
            {
                Source = Name,
                Vin = extracted.Vin,
                Year = extracted.Year,
                Make = extracted.Make ?? group.Make,
                Model = extracted.Model ?? group.Model,
                Trim = extracted.Trim,
                Price = extracted.Price,
                Mileage = extracted.Mileage,
                Url = detailUrl,
                RawRecordPath = rawPath,
                WasExtracted = true,
                MatchesQuery = matchesQuery,
            });
        }

        stopwatch.Stop();
        result.WallTime = stopwatch.Elapsed;

        result.WasBlocked = searchBlocked || anyDetailBlocked;

        if (result.Candidates.Count == 0 && pendingDetailUrls.Count == 0 && searchBlocked)
        {
            result.CouldNotRun = true;
            result.CouldNotRunReason = "blocked: bot defenses stopped every search in this run";
        }

        return result;
    }
}
