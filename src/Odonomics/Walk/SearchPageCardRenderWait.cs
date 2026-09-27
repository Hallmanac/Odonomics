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
/// the first such fuse-card whose own text still carries no dollar amount into view (to trigger cars.com's
/// lazy render), and returns every still-unrendered card's anchor href, scrolled one or not. Only the
/// first is ever scrolled in one call: the browser only lays out and checks intersections once the script
/// returns, so a call that scrolled every unrendered card in turn would only ever have the last one's
/// position take effect, leaving every earlier region never actually visited. <see cref="RunAsync"/> runs
/// it, pauses, and runs it again, up to <see cref="MaxScans"/> more times, stopping as soon as a scan comes
/// back empty; since a rendered card drops out of the next scan's still-empty list, each scan's first
/// still-unrendered card advances to a new region of the page. What it returns after the last scan is the
/// hrefs still unrendered: the caller keeps those out of the pool (see
/// <see cref="WalkSearchPages.CollectLinksAsync"/>) rather than letting the ambiguous wrapper-text walk
/// decide their distance for them.
/// </summary>
public static class SearchPageCardRenderWait
{
    /// <summary>How many more render scans <see cref="RunAsync"/> runs, each followed by a pause, after
    /// its own first, unconditional scan: one search page gets at most this many plus that one (six scans
    /// in total, at this constant's own value). A card that still hasn't rendered by then is reported as
    /// unrendered rather than waited on further.</summary>
    public const int MaxScans = 5;

    /// <summary>Scrolls the first not-yet-rendered card into view and returns the hrefs of every card still
    /// without a dollar amount in their own fuse-card text, scrolled or not. Only the first is scrolled: the
    /// browser only realizes one scroll position per call (see the class summary), so scrolling more than
    /// one would waste the call on positions that are immediately overwritten and never actually seen. Takes
    /// the same <c>amount</c> pattern as <see cref="SearchPageLinks.CardScript"/> (see
    /// <see cref="SearchPageLinks.CardAmountPattern"/>), so the two agree on what counts as rendered.</summary>
    public const string RenderScanScript = """
        ({ amount }) => {
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
                    if (!scrolled) {
                        card.scrollIntoView({ block: 'center' });
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
        CancellationToken cancellationToken)
    {
        async Task<IReadOnlyList<string>> ScanAsync() => await evaluateAsync(RenderScanScript, new { amount = SearchPageLinks.CardAmountPattern });

        IReadOnlyList<string> unrendered = await ScanAsync();
        for (int scan = 0; scan < MaxScans && unrendered.Count > 0; scan++)
        {
            await Task.Delay(pause(), cancellationToken);
            unrendered = await ScanAsync();
        }

        return unrendered;
    }

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
