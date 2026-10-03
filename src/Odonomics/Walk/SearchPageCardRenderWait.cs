using System.Text.Encodings.Web;
using System.Text.Json;

namespace Odonomics.Walk;

/// <summary>
/// Waits for a cars.com result page's own cards to render before <see cref="SearchPageLinks"/> reads
/// them. A card whose fuse-card element still holds no dollar amount when the page first loads leaves
/// <see cref="SearchPageLinks.CardScript"/>'s dollar-ancestor walk nothing of its own to stop at, so it
/// climbs past the card to the results list and can read a neighboring card's own stated distance as
/// this card's (walk run 20260927-113258; lessons 9514dea8 and 1364af73). <see cref="RenderScanScript"/>
/// finds every &lt;li&gt; of the results list that holds a fuse-card with a /vehicledetail/ link, scrolls
/// the first such fuse-card that still carries no dollar amount of its own and has not already been
/// scrolled by an earlier scan (to trigger cars.com's lazy render) into view, and returns every
/// still-unrendered card's anchor href, scrolled or not. Only one card is ever scrolled in one call: the
/// browser only lays out and checks intersections once the script returns, so a call that scrolled every
/// unrendered card in turn would only ever have the last one's position take effect, leaving every earlier
/// region never actually visited. Marking a card scrolled (a data attribute on the element itself, which
/// survives between calls because every call runs in the same page) is what lets a card that never renders
/// hand the next scan's single scroll on to the next still-unrendered card instead of holding onto it for
/// every remaining scan. <see cref="RunAsync"/> runs the scan, pauses, and runs it again, up to
/// <see cref="MaxScans"/> more times, stopping as soon as a scan comes back empty. What it returns after
/// the last scan is a snapshot, not a verdict: <see cref="StillUnrenderedAsync"/> is what the caller
/// checks again, right before it actually reads the page's cards, since a card can go on rendering in
/// the time since the scan loop's own last check; only a card still without its own amount at that final
/// check is kept out of the pool (see <see cref="WalkSearchPages.CollectLinksAsync"/>) rather than let
/// the ambiguous wrapper-text walk decide its distance for it.
/// </summary>
public static class SearchPageCardRenderWait
{
    /// <summary>How many more render scans <see cref="RunAsync"/> runs, each followed by a pause, after
    /// its own first, unconditional scan: one search page gets at most this many plus that one (six scans
    /// in total, at this constant's own value). A card that still hasn't rendered by then is reported as
    /// unrendered rather than waited on further.</summary>
    public const int MaxScans = 5;

    /// <summary>Scrolls the first not-yet-rendered, not-yet-scrolled card into view and returns the hrefs of
    /// every card still without a dollar amount in their own fuse-card text, scrolled or not. Only one card
    /// is scrolled per call: the browser only realizes one scroll position per call (see the class summary),
    /// so scrolling more than one would waste the call on positions that are immediately overwritten and
    /// never actually seen. A card already scrolled by an earlier call is skipped when picking which card to
    /// scroll this time (its own <c>data-walk-scrolled</c> attribute says so, and survives between calls
    /// because every call runs against the same live page), so a card that never renders cannot hold onto
    /// the one scroll every scan grants and starve every card after it. Takes the same <c>amount</c> pattern
    /// as <see cref="SearchPageLinks.CardScript"/> (see <see cref="SearchPageLinks.CardAmountPattern"/>), so
    /// the two agree on what counts as rendered (a site's own <see cref="WalkSite.CardAmountPattern"/>, when it has
    /// one, is what <see cref="RunAsync"/> and <see cref="StillUnrenderedAsync"/> pass, so a rendered card whose
    /// price line says "Call for price" and has no dollar sign is not read as one still waiting). <c>scroll</c> defaults true for <see cref="RunAsync"/>'s own
    /// scanning loop; <see cref="StillUnrenderedAsync"/> passes it false for a check that only reads the
    /// page's current state without nudging it further.</summary>
    public const string RenderScanScript = """
        ({ amount, scroll = true }) => {
            const hasAmount = new RegExp(amount);
            const hrefs = [];
            let scrolled = false;
            for (const li of document.querySelectorAll('li')) {
                const card = li.querySelector('fuse-card');
                if (!card) {
                    continue;
                }

                const anchor = card.querySelector('a[href*="/vehicledetail/"]');
                if (!anchor) {
                    continue;
                }

                if (!hasAmount.test(card.innerText || '')) {
                    if (scroll && !scrolled && !card.dataset.walkScrolled) {
                        card.scrollIntoView({ block: 'center' });
                        card.dataset.walkScrolled = '1';
                        scrolled = true;
                    }

                    hrefs.push(anchor.href);
                }
            }

            return hrefs;
        }
        """;

    /// <summary><paramref name="evaluateAsync"/> runs a script in the page with an optional argument and
    /// returns its rows, the same contract <see cref="SearchPageLinks.ReadAsync"/> takes; the walk passes
    /// the browser page's own evaluate. <see cref="RenderScanScript"/> is run through it once immediately,
    /// then, for as long as it keeps coming back non-empty, up to <see cref="MaxScans"/> more times with
    /// <paramref name="pause"/> waited between each. The wait ends early the first time a scan comes back
    /// empty, and otherwise ends with whatever the last scan still found.</summary>
    public static async Task<IReadOnlyList<string>> RunAsync(
        Func<string, object?, Task<string[]>> evaluateAsync,
        Func<TimeSpan> pause,
        CancellationToken cancellationToken,
        string? amountPattern = null)
    {
        async Task<IReadOnlyList<string>> ScanAsync() => await evaluateAsync(RenderScanScript, new { amount = amountPattern ?? SearchPageLinks.CardAmountPattern });

        IReadOnlyList<string> unrendered = await ScanAsync();
        for (int scan = 0; scan < MaxScans && unrendered.Count > 0; scan++)
        {
            await Task.Delay(pause(), cancellationToken);
            unrendered = await ScanAsync();
        }

        return unrendered;
    }

    /// <summary>The same per-card check <see cref="RenderScanScript"/> runs, minus the scroll: every
    /// fuse-card that still carries no dollar amount of its own, right now. A card <see cref="RunAsync"/>
    /// gave up on can still finish rendering in the moments after its last scan (walk run 20260928-134242,
    /// whose cars.com pages were rendering enough slower that many cards <see cref="RunAsync"/> reported
    /// unrendered had already rendered, under their own text rather than a neighbor's, by the time
    /// <see cref="SearchPageLinks.ReadAsync"/> went on to actually read the page): scrolling again would
    /// not make that card render any sooner, since it was never the scroll it was waiting on, only more
    /// real time elapsing. Calling this once more, right before that read, is what tells a card that
    /// genuinely never rendered from one that simply rendered later than <see cref="RunAsync"/> waited for,
    /// without trusting <see cref="SearchPageLinks.ReadAsync"/>'s own wider ancestor climb to make that
    /// call: that climb can still land on a neighboring card's price for a card whose own fuse-card element
    /// is genuinely still blank, which is exactly the misreading <see cref="RunAsync"/> exists to keep out
    /// of the pool (see the class summary), so only this narrower, same-element check may downgrade a
    /// card from unrendered to rendered.</summary>
    public static async Task<IReadOnlyList<string>> StillUnrenderedAsync(Func<string, object?, Task<string[]>> evaluateAsync, string? amountPattern = null) =>
        await evaluateAsync(RenderScanScript, new { amount = amountPattern ?? SearchPageLinks.CardAmountPattern, scroll = false });

    // Relaxed escaping keeps an href's "&" readable, matching SearchPageLinks.CardsJson.
    private static readonly JsonSerializerOptions HrefsJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The still-unrendered hrefs as JSON, for the recorder to write beside a search page's
    /// cards file so a reader can see which car's card never rendered in time.</summary>
    public static string UnrenderedHrefsJson(IEnumerable<string> hrefs) =>
        JsonSerializer.Serialize(hrefs, HrefsJsonOptions);
}
