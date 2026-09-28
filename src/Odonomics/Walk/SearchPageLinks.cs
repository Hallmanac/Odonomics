using System.Text.Encodings.Web;
using System.Text.Json;

namespace Odonomics.Walk;

/// <summary>
/// Pulls a search page's anchors and, for each detail link, the text of its own result card: the
/// nearest ancestor element of the anchor (the anchor itself counts, since a site may make the whole
/// card one link) whose text matches <see cref="CardAmountPattern"/> or the site's own
/// <see cref="WalkSite.CardAmountPattern"/> override, or the element
/// <see cref="WalkSite.CardContainerSelector"/> names when the site has one. Both cars.com and carvana
/// print a card's price inside the card as a dollar amount, so the nearest ancestor that shows one is
/// the card and not a container of several; autotrader's price is bare digits, so it overrides the
/// pattern instead (see <see cref="WalkSites.Autotrader"/>). A
/// card whose text runs past <see cref="MaxCardTextLength"/> is a container of many cards, not one, so
/// it reads as no card at all. Only links matching the site's detail pattern are asked for a card,
/// since the rest of a page's anchors (navigation, filters) have none.
///
/// <para>For a site whose <see cref="WalkSite.CardAmountMarkerIsInnerToCard"/> is set, the first matching
/// ancestor is only where the climb starts, not where it ends: it keeps widening to each further parent
/// for as long as that parent's own text still carries exactly the one amount match the card itself
/// accounts for, and stops at the last parent before one carries more than one (the results list wrapping
/// several cards, each with its own price line) or before one runs past <see cref="MaxCardTextLength"/>,
/// whichever comes first. The length bound alone is what keeps a page with only one real card (a "1
/// Match" search, with no sponsored card and nothing else on the page carrying the amount marker) from
/// widening past its actual card, all the way up through the results list, the page header, and the
/// footer, to <c>document.body</c> itself: every one of those ancestors still carries exactly the card's
/// own one match, so only the length running past what one card could plausibly hold ever stops it. That
/// is what lets the card include a top-of-card badge and a dealer footer sitting outside the innermost
/// price-bearing element (see <see cref="WalkSites.Autotrader"/>), without also pulling in a neighboring
/// card's own price line, or, on a sparse page, the whole page.
/// </para>
/// </summary>
public static class SearchPageLinks
{
    /// <summary>A dollar sign followed by a digit, as a JavaScript regular expression source; the
    /// default ancestor-climb test, used by a site with no <see cref="WalkSite.CardAmountPattern"/>
    /// override.</summary>
    public const string CardAmountPattern = @"\$\s*\d";

    /// <summary>The longest text a single result card can plausibly have; the recorded cards run to a
    /// few hundred characters.</summary>
    public const int MaxCardTextLength = 3000;

    /// <summary>Reads every anchor as [href, visible text].</summary>
    public const string AnchorScript = "() => Array.from(document.querySelectorAll('a')).map(a => [a.href, a.innerText || ''])";

    /// <summary>Given the positions of anchors in document order, reads each as [href, card text],
    /// where the card text is empty when no card was found.</summary>
    public const string CardScript = """
        ({ indices, amount, container, extendPastAmount, maxLength }) => {
            const anchors = document.querySelectorAll('a');
            const hasAmount = new RegExp(amount);
            const countAmount = new RegExp(amount, 'g');
            return indices.map(i => {
                const anchor = anchors[i];
                if (!anchor) {
                    return ['', ''];
                }

                let card = null;
                if (container) {
                    card = anchor.closest(container);
                } else {
                    for (let el = anchor; el && el !== document.body; el = el.parentElement) {
                        if (hasAmount.test(el.innerText || '')) {
                            card = el;
                            break;
                        }
                    }

                    if (card && extendPastAmount) {
                        for (let el = card.parentElement; el && el !== document.body; el = el.parentElement) {
                            const text = el.innerText || '';
                            const matches = text.match(countAmount);
                            if (!matches || matches.length !== 1 || text.length > maxLength) {
                                break;
                            }

                            card = el;
                        }
                    }
                }

                return [anchor.href, card ? (card.innerText || '') : ''];
            });
        }
        """;

    /// <summary>Every anchor on the page, with the card text filled in for the detail links.
    /// <paramref name="evaluateAsync"/> runs a script in the page with an optional argument and returns
    /// its rows; the walk passes the browser page's own evaluate.</summary>
    public static async Task<IReadOnlyList<PageLink>> ReadAsync(Func<string, object?, Task<string[][]>> evaluateAsync, WalkSite site)
    {
        string[][] anchors = await evaluateAsync(AnchorScript, null);
        List<int> detailIndices =
        [
            .. anchors
                .Index()
                .Where(a => site.DetailUrlPattern.IsMatch(a.Item[0]))
                .Select(a => a.Index)
        ];
        string[][] cards = detailIndices.Count == 0
            ? []
            : await evaluateAsync(CardScript, new { indices = detailIndices, amount = site.CardAmountPattern ?? CardAmountPattern, container = site.CardContainerSelector, extendPastAmount = site.CardAmountMarkerIsInnerToCard, maxLength = MaxCardTextLength });

        var cardTextByIndex = new Dictionary<int, string>();
        foreach ((int anchorIndex, string[] card) in detailIndices.Zip(cards))
        {
            // The page can change between the two reads; a card is only trusted for the same link.
            if (card[0] == anchors[anchorIndex][0] && card[1].Length <= MaxCardTextLength)
            {
                cardTextByIndex[anchorIndex] = card[1];
            }
        }

        return
        [
            .. anchors.Select((a, i) => new PageLink(a[0], a[1], cardTextByIndex.GetValueOrDefault(i, "")))
        ];
    }

    /// <summary>How many distinct candidate detail links <paramref name="anchors"/> (in <see cref="AnchorScript"/>'s
    /// own [href, text] shape) names for <paramref name="site"/>: every canonical URL, once, among the anchors
    /// matching its <see cref="WalkSite.DetailUrlPattern"/>. What a page's own "is there more to load" stall check
    /// watches for a site that shows more cards only by pressing a control (<see cref="WalkSite.LoadMoreControlPattern"/>):
    /// the same count that press works towards, so a page that adds no new canonical link is a page with nothing
    /// left to load.</summary>
    public static int CountDistinctDetailLinks(IEnumerable<string[]> anchors, WalkSite site) =>
        anchors
            .Where(a => site.DetailUrlPattern.IsMatch(a[0]))
            .Select(a => WalkSites.CanonicalDetailUrl(a[0]))
            .Distinct()
            .Count();

    // Relaxed escaping keeps an href's "&" and a card's "…" readable in the file, which a person opens to cut fixtures.
    private static readonly JsonSerializerOptions CardsJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The detail links with their card text as JSON, for the recorder to write beside a
    /// search page's text so fixtures can be cut from it later: one entry per detail link, in page
    /// order, each with its href, its own text, and its card's text.</summary>
    public static string CardsJson(WalkSite site, IEnumerable<PageLink> links) =>
        JsonSerializer.Serialize(
            links
                .Where(l => site.DetailUrlPattern.IsMatch(l.Href))
                .Select(l => new { href = l.Href, text = l.Text, card = l.CardText }),
            CardsJsonOptions);
}
