using System.Net;
using System.Text.RegularExpressions;

namespace Odonomics.Auctions;

/// <summary>Reads a html.duckduckgo.com results page: whether it is a captcha or a block, whether it
/// found anything, and which of its results are on a public Copart or IAA archive site, in the order
/// DuckDuckGo ranked them and capped at <see cref="MaxArchiveResults"/>. Every result link on that page is
/// a redirect (<c>//duckduckgo.com/l/?uddg=&lt;the real URL&gt;</c>), so the real URL is read back out
/// of it. Archive sites change and come and go, so <see cref="ArchiveHosts"/> is one short list to
/// edit rather than a rule spread through the code.</summary>
public static partial class AuctionSearchPageReader
{
    public const int MaxArchiveResults = 3;

    /// <summary>The archive sites whose lot pages are read. A result matches when its host is one of
    /// these or a subdomain of one (so "www.bid.cars" matches "bid.cars").</summary>
    public static readonly IReadOnlyList<string> ArchiveHosts =
    [
        "bid.cars",
        "copart.com",
        "iaai.com",
        "poctra.com",
        "stat.vin",
        "bidfax.info",
        "autobidmaster.com",
        "salvagebid.com",
        "carfast.express",
        "autobidcar.com",
        "carsbidshistory.com",
    ];

    private static readonly string[] BlockMarkers =
    [
        "anomaly-modal",
        "bots use duckduckgo too",
        "select all squares containing a duck",
    ];

    private static readonly string[] NoResultsMarkers = ["no-results", "No results"];

    public static AuctionSearchPage Read(string html)
    {
        if (BlockMarkers.Any(m => html.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            return new AuctionSearchPage(AuctionSearchStatus.Unreadable, [], "DuckDuckGo showed a captcha for the search");
        }

        List<string> targets = [.. ResultAnchor().Matches(html)
            .Select(m => HrefAttribute().Match(m.Value))
            .Where(m => m.Success)
            .Select(m => ResolveTarget(WebUtility.HtmlDecode(m.Groups["href"].Value)))
            .OfType<string>()];

        if (targets.Count == 0)
        {
            return NoResultsMarkers.Any(m => html.Contains(m, StringComparison.OrdinalIgnoreCase))
                ? new AuctionSearchPage(AuctionSearchStatus.NoResults, [], null)
                : new AuctionSearchPage(AuctionSearchStatus.Unreadable, [], "the DuckDuckGo page listed no results and did not say none matched, so it may be a block or a changed layout");
        }

        List<string> archive = [.. targets.Where(IsArchiveUrl).Distinct().Take(MaxArchiveResults)];
        return new AuctionSearchPage(AuctionSearchStatus.Results, archive, null);
    }

    public static string BuildSearchUrl(string vin) =>
        $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(vin)}";

    public static bool IsArchiveUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && uri.Scheme is "http" or "https"
        && ArchiveHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

    /// <summary>The real URL behind a DuckDuckGo redirect link, or the link itself when it is already
    /// direct; null when it names no absolute URL.</summary>
    private static string? ResolveTarget(string href)
    {
        string absolute = href.StartsWith("//", StringComparison.Ordinal) ? "https:" + href : href;
        if (!Uri.TryCreate(absolute, UriKind.Absolute, out Uri? uri))
        {
            return null;
        }

        if (!uri.Host.EndsWith("duckduckgo.com", StringComparison.OrdinalIgnoreCase))
        {
            return absolute;
        }

        Match target = RedirectTarget().Match(uri.Query);
        return target.Success
            ? WebUtility.UrlDecode(target.Groups["url"].Value)
            : null;
    }

    [GeneratedRegex(@"<a\b[^>]*\bclass\s*=\s*""[^""]*\bresult__a\b[^""]*""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ResultAnchor();

    [GeneratedRegex(@"\bhref\s*=\s*""(?<href>[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex HrefAttribute();

    [GeneratedRegex(@"[?&]uddg=(?<url>[^&]+)")]
    private static partial Regex RedirectTarget();
}
