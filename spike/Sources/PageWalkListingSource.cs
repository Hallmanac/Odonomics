using System.Diagnostics;
using System.Text.RegularExpressions;
using Spike.Models;

namespace Spike.Sources;

/// <summary>
/// Generic headed-Chrome page walk. Phase one visits one search page per query group and
/// harvests detail links; phase two visits a capped number of those detail pages (the VIN is not
/// visible on the search page on the sites this spike targets) through the model extraction
/// step. Each navigation gets its own fresh browser profile (see PageWalkEngine for why). Shared
/// by every Playwright-based source (Cars.com, Autotrader, Carvana); only the URL shapes and the
/// detail-link pattern differ.
/// </summary>
public sealed class PageWalkListingSource(
    string name,
    string profileRoot,
    Func<QueryGroup, string> buildSearchUrl,
    Regex detailUrlPattern,
    RecordedResponses recorded,
    ExtractionClient extraction,
    int detailPageCapPerGroup = 6) : IListingSource
{
    public string Name => name;

    public async Task<SourceRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var result = new SourceRunResult { Source = Name };
        var stopwatch = Stopwatch.StartNew();

        // Phase 1: one search page per query group.
        var pendingDetailUrls = new List<(QueryGroup Group, string Url)>();
        var searchBlocked = false;
        var totalCandidatesSeen = 0;

        foreach (var group in QueryGroup.All)
        {
            var fileBase = $"{group.Make}-{group.Model}".Replace(" ", "_");

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

            await recorded.WriteAsync(Name, $"{fileBase}-search.html", search.Html, cancellationToken);

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
            foreach (var url in search.DetailLinks.Take(detailPageCapPerGroup))
            {
                pendingDetailUrls.Add((group, url));
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        result.SearchOnlyCandidatesFound = totalCandidatesSeen;

        // Phase 2: detail pages, up to the cap. Each gets its own fresh profile (see
        // PageWalkEngine), so one blocked detail page does not doom the rest.
        var index = 0;
        var anyDetailBlocked = false;
        foreach (var (group, detailUrl) in pendingDetailUrls)
        {
            index++;
            var fileBase = $"{group.Make}-{group.Model}".Replace(" ", "_");

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

            var rawPath = await recorded.WriteAsync(Name, $"{fileBase}-detail-{index}.html", detail.Html, cancellationToken);

            if (detail.Blocked)
            {
                result.Failures.Add($"{group.Make} {group.Model} detail {index}: blocked ({detail.BlockReason})");
                anyDetailBlocked = true;
                continue;
            }

            var outcome = await extraction.ExtractAsync(detail.BodyText, cancellationToken);
            result.DollarsSpent += outcome.CostUsd;

            if (outcome.Error is not null)
            {
                result.Failures.Add($"{group.Make} {group.Model} detail {index}: extraction failed: {outcome.Error}");
                continue;
            }

            var extracted = outcome.Result!;
            var matchesQuery = group.MatchesExtractedVehicle(extracted.Make, extracted.Model, extracted.Trim, extracted.Year, extracted.Mileage);
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
