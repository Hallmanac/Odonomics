using System.Diagnostics;
using System.Text.RegularExpressions;
using Spike.Models;

namespace Spike.Sources;

/// <summary>
/// Craigslist by plain HTML fetch (no browser): one search request per query group, honoring
/// robots.txt, then a capped number of posting detail pages run through the model extraction
/// step to check for a VIN. Craigslist's own robots.txt (checked at spike build time, day one)
/// disallows /reply, /fb/, /suggest, /flag, /mf, /mailflag, /eaf, and /sitemap/; search pages and
/// posting detail pages are not disallowed.
/// </summary>
public sealed class CraigslistSource(RecordedResponses recorded, ExtractionClient extraction, int detailPageCapPerGroup = 4) : IListingSource
{
    public string Name => "craigslist";

    private const string CitySubdomain = "daytona"; // covers zip 32114 (Daytona Beach / Port Orange, FL)
    private static readonly Regex DetailLinkPattern = new(@"https://www\.craigslist\.org/view/d/[^""'\s]+", RegexOptions.IgnoreCase);

    public async Task<SourceRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var result = new SourceRunResult { Source = Name };
        Stopwatch stopwatch = Stopwatch.StartNew();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");

        foreach (QueryGroup group in QueryGroup.All)
        {
            try
            {
                string query = Uri.EscapeDataString($"{group.Make} {group.Model}");
                string searchUrl = $"https://{CitySubdomain}.craigslist.org/search/cta?query={query}" +
                                 $"&postal={group.Zip}&search_distance={group.RadiusMiles}&max_auto_miles={group.MaxMileage}";

                string searchHtml = await http.GetStringAsync(searchUrl, cancellationToken);
                string fileBase = $"{group.Make}-{group.Model}".Replace(" ", "_");
                await recorded.WriteAsync(Name, $"{fileBase}-search.html", searchHtml, cancellationToken);

                List<string> detailUrls = DetailLinkPattern.Matches(searchHtml)
                    .Select(m => m.Value)
                    .Distinct()
                    .Take(detailPageCapPerGroup)
                    .ToList();

                if (detailUrls.Count == 0)
                {
                    result.Failures.Add($"{group.Make} {group.Model}: no matching postings found on the search page");
                    continue;
                }

                int index = 0;
                foreach (string detailUrl in detailUrls)
                {
                    index++;
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                    string detailHtml;
                    try
                    {
                        detailHtml = await http.GetStringAsync(detailUrl, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        result.Failures.Add($"{group.Make} {group.Model} posting {index}: fetch failed: {ex.Message}");
                        continue;
                    }

                    string rawPath = await recorded.WriteAsync(Name, $"{fileBase}-detail-{index}.html", detailHtml, cancellationToken);
                    string bodyText = StripHtml(detailHtml);

                    ExtractionOutcome outcome = await extraction.ExtractAsync(bodyText, cancellationToken);
                    result.DollarsSpent += outcome.CostUsd;

                    if (outcome.Error is not null)
                    {
                        result.Failures.Add($"{group.Make} {group.Model} posting {index}: extraction failed: {outcome.Error}");
                        continue;
                    }

                    ExtractionResult extracted = outcome.Result!;
                    bool matchesQuery = group.MatchesExtractedVehicle(extracted.Make, extracted.Model, extracted.Trim, extracted.Year, extracted.Mileage);
                    if (!matchesQuery)
                    {
                        result.Failures.Add(
                            $"{group.Make} {group.Model} posting {index}: search match did not match the query ({extracted.Year} {extracted.Make} {extracted.Model} {extracted.Trim}), excluded from counts but kept for the extraction hand-check");
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
            }
            catch (Exception ex)
            {
                result.Failures.Add($"{group.Make} {group.Model}: {ex.Message}");
            }
        }

        stopwatch.Stop();
        result.WallTime = stopwatch.Elapsed;
        return result;
    }

    private static string StripHtml(string html)
    {
        string noScripts = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
        string noStyles = Regex.Replace(noScripts, @"<style[^>]*>[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
        string noTags = Regex.Replace(noStyles, "<[^>]+>", " ");
        string decoded = System.Net.WebUtility.HtmlDecode(noTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }
}
