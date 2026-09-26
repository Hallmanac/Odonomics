using System.Text.Encodings.Web;
using System.Text.Json;

namespace Odonomics.Walk;

/// <summary>
/// Pulls a search page's anchors and, for each detail link, the text of its own result card: the
/// nearest ancestor element of the anchor (the anchor itself counts, since a site may make the whole
/// card one link) whose text contains a dollar amount, or the element <see cref="WalkSite.CardContainerSelector"/>
/// names when the site has one. Both cars.com and carvana print a card's price inside the card, so
/// the nearest ancestor that shows a dollar amount is the card and not a container of several. A
/// card whose text runs past <see cref="MaxCardTextLength"/> is a container of many cards, not one, so
/// it reads as no card at all. Only links matching the site's detail pattern are asked for a card,
/// since the rest of a page's anchors (navigation, filters) have none.
/// </summary>
public static class SearchPageLinks
{
    /// <summary>A dollar sign followed by a digit, as a JavaScript regular expression source.</summary>
    public const string CardAmountPattern = @"\$\s*\d";

    /// <summary>The longest text a single result card can plausibly have; the recorded cards run to a
    /// few hundred characters.</summary>
    public const int MaxCardTextLength = 3000;

    /// <summary>Reads every anchor as [href, visible text].</summary>
    public const string AnchorScript = "() => Array.from(document.querySelectorAll('a')).map(a => [a.href, a.innerText || ''])";

    /// <summary>Given the positions of anchors in document order, reads each as [href, card text],
    /// where the card text is empty when no card was found.</summary>
    public const string CardScript = """
        ({ indices, amount, container }) => {
            const anchors = document.querySelectorAll('a');
            const hasAmount = new RegExp(amount);
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
            : await evaluateAsync(CardScript, new { indices = detailIndices, amount = CardAmountPattern, container = site.CardContainerSelector });

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
