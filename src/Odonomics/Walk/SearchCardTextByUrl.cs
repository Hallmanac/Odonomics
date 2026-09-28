using System.Diagnostics.CodeAnalysis;

namespace Odonomics.Walk;

/// <summary>
/// The result card text of every detail link a pair's search pages showed, one entry per canonical URL
/// across that pair's whole walk (see <see cref="WalkSite.CardFeeReader"/>): a link visited later has
/// only its own URL to look up, and the card its own search page carried is where a fee, if the site
/// prints one, is read from. A card whose <see cref="WalkSite.CardDistanceReader"/> reads no distance
/// from its text is an ambiguous multi-card wrapper that can carry a neighboring card's own text instead
/// of this link's own (see <see cref="WalkSearchPages.CollectLinksAsync"/>'s own touchKnownAsync
/// remarks), so <see cref="Record"/> keeps that text only until a later page shows this same link's own
/// single-card text (one whose reader finds a real distance): the clean text then replaces the
/// wrapper's, rather than losing to it under a plain first-seen rule, so a fee later read off it is
/// never a neighbor's. A site with no <see cref="WalkSite.CardDistanceReader"/> (every site but
/// cars.com) never marks an entry ambiguous, so its first-seen text is kept exactly as a plain
/// first-seen store would.
/// </summary>
public sealed class SearchCardTextByUrl
{
    private readonly Dictionary<string, string> _textByUrl = [];
    private readonly HashSet<string> _ambiguousUrls = [];

    /// <summary>Records <paramref name="cardText"/> for <paramref name="canonicalUrl"/>, unless it loses to
    /// what is already stored: an ambiguous sighting (<paramref name="textIsAmbiguous"/>) never overwrites
    /// anything, and is itself stored only when nothing is stored yet; a non-ambiguous sighting always wins
    /// over an ambiguous one already stored, but never overwrites a non-ambiguous one already stored (the
    /// first clean text seen is the one kept).</summary>
    public void Record(string canonicalUrl, string cardText, bool textIsAmbiguous)
    {
        if (textIsAmbiguous)
        {
            if (_textByUrl.TryAdd(canonicalUrl, cardText))
            {
                _ambiguousUrls.Add(canonicalUrl);
            }

            return;
        }

        if (!_textByUrl.ContainsKey(canonicalUrl) || _ambiguousUrls.Remove(canonicalUrl))
        {
            _textByUrl[canonicalUrl] = cardText;
        }
    }

    public bool TryGetValue(string canonicalUrl, [MaybeNullWhen(false)] out string cardText) => _textByUrl.TryGetValue(canonicalUrl, out cardText);
}
