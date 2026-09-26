using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Spike.Sources;

public sealed record PageWalkResult(
    bool Blocked,
    string? BlockReason,
    int? StatusCode,
    string Title,
    string Html,
    string BodyText,
    IReadOnlyList<string> DetailLinks,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> DetailLinkAttributes,
    IReadOnlyList<string> JsonLdBlocks);

/// <summary>
/// Shared plumbing for a headed-Chrome page walk. Deliberately holds no per-site content
/// parsing: it only navigates, detects the common bot-defense challenge pages, harvests
/// hyperlinks by URL shape, and harvests two generic, site-agnostic data carriers that happen to
/// appear on a search page: each link's own <c>data-*</c> attributes, and the page's JSON-LD
/// script blocks. Whether either of those carries a usable VIN (letting a page-walk source skip a
/// detail-page fetch entirely) is a per-site judgment made by the caller in PageWalkSources, not
/// here. Turning page text into vehicle data from a detail page is still always the model
/// extraction step (see ExtractionClient), never a selector written against one site's markup.
///
/// Day one found that Cars.com's and Carvana's Cloudflare defenses let the *first* page load in
/// a persistent profile through cleanly, then challenge every subsequent load in that same
/// profile, regardless of whether it is another search or a detail page. A brand-new profile's
/// first load, by contrast, has succeeded every time we tried it. So instead of one persistent
/// profile reused for a whole run (which would cap a run at a single page), each navigation gets
/// its own fresh, disposable profile directory: the only way found to get more than one page per
/// site per run. This trades away the "looks like a returning user with history" realism a
/// persistent profile is meant to buy; see docs/spike-findings.md for that trade-off.
/// </summary>
public static class PageWalkEngine
{
    private static readonly string[] BlockTitleMarkers =
    [
        "just a moment", "attention required", "access denied", "are you a human", "unusual traffic"
    ];

    /// <summary>Navigates once, in its own fresh browser profile, and returns the rendered
    /// page's raw HTML, visible text, and any hyperlinks matching <paramref name="detailUrlPattern"/>
    /// (pass null to skip link harvest).</summary>
    public static async Task<PageWalkResult> NavigateAsync(string profileRoot, string url, Regex? detailUrlPattern, CancellationToken cancellationToken)
    {
        // Each call gets its own throwaway profile (see the class summary for why), and each one
        // is a full Chromium profile directory: left uncommitted-but-on-disk across a whole run
        // (dozens of pages) that adds up to gigabytes. It is deleted in the finally block below
        // once its one page load is done; nothing after this method needs it again.
        string profileDir = Path.Combine(profileRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profileDir);

        try
        {
            using IPlaywright playwright = await Playwright.CreateAsync();
            await using IBrowserContext context = await playwright.Chromium.LaunchPersistentContextAsync(profileDir, new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = false,
                ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
                Timeout = 20_000,
            });

            IPage page = await context.NewPageAsync();

            IResponse? response;
            try
            {
                response = await page.GotoAsync(url, new PageGotoOptions { Timeout = 25_000, WaitUntil = WaitUntilState.DOMContentLoaded });
            }
            catch (TimeoutException)
            {
                response = null;
            }

            await page.WaitForTimeoutAsync(4_000);

            // A Cloudflare/bot-defense JS challenge often resolves itself within a few extra
            // seconds in a real Chrome tab; give it one more chance before calling it blocked.
            string title = await page.TitleAsync();
            if (BlockTitleMarkers.Any(m => title.ToLowerInvariant().Contains(m)))
            {
                await page.WaitForTimeoutAsync(8_000);
                title = await page.TitleAsync();
            }

            string html = await page.ContentAsync();
            string bodyText = await page.EvaluateAsync<string>("() => document.body.innerText");
            int? statusCode = response?.Status;

            string lowerTitle = title.ToLowerInvariant();
            string? blockMarker = BlockTitleMarkers.FirstOrDefault(m => lowerTitle.Contains(m));
            bool blocked = blockMarker is not null || statusCode is 403 or 429;
            string? reason = blocked
                ? blockMarker is not null ? $"bot-defense challenge page (title contained \"{blockMarker}\")" : $"HTTP {statusCode}"
                : null;

            var links = new List<string>();
            var linkAttributes = new Dictionary<string, IReadOnlyDictionary<string, string>>();
            var jsonLdBlocks = new List<string>();
            if (!blocked && detailUrlPattern is not null)
            {
                string[] hrefs = await page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('a')).map(a => a.href)");
                links = hrefs.Where(h => detailUrlPattern.IsMatch(h)).Distinct().ToList();

                // Generic, site-agnostic data carriers that happen to sit on a search page: an
                // anchor's own data-* attributes, and the page's JSON-LD script blocks. Neither is
                // specific to any one site; whether either one carries a usable VIN for a given
                // link is decided by the caller (see PageWalkSources), not here.
                //
                // Returned as a JSON string and parsed on this side, not deserialized directly by
                // EvaluateAsync<Dictionary<...>>: Playwright's own generic deserialization of a
                // nested JS object into a Dictionary<string, Dictionary<string, string>> was found,
                // by direct testing against these exact recorded fixtures, to silently come back
                // empty even though the browser-side object was populated. A round-trip through
                // System.Text.Json.JsonSerializer does not have that problem.
                string linkAttributesJson = await page.EvaluateAsync<string>(
                    """
                    () => {
                        const result = {};
                        for (const a of document.querySelectorAll('a')) {
                            if (result[a.href]) continue;
                            const attrs = {};
                            for (const attr of a.attributes) {
                                if (attr.name.startsWith('data-')) attrs[attr.name] = attr.value;
                            }
                            result[a.href] = attrs;
                        }
                        return JSON.stringify(result);
                    }
                    """);
                Dictionary<string, Dictionary<string, string>> rawLinkAttributes =
                    JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(linkAttributesJson) ?? [];
                linkAttributes = rawLinkAttributes.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<string, string>)kv.Value);

                string[] jsonLd = await page.EvaluateAsync<string[]>(
                    "() => Array.from(document.querySelectorAll('script[type=\"application/ld+json\"]')).map(s => s.textContent ?? '')");
                jsonLdBlocks = jsonLd.Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
            }

            return new PageWalkResult(blocked, reason, statusCode, title, html, bodyText, links, linkAttributes, jsonLdBlocks);
        }
        finally
        {
            try
            {
                Directory.Delete(profileDir, recursive: true);
            }
            catch (Exception)
            {
                // Best-effort cleanup: a lingering file handle or permissions quirk can stop the
                // delete on some platforms; leaving one stray profile behind is a disk-space nit,
                // not a defect worth failing the whole page walk over.
            }
        }
    }
}
