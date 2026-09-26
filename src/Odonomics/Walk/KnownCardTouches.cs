using Odonomics.Ledger;

namespace Odonomics.Walk;

/// <summary>
/// The links of one site's search pages that the ledger already holds, kept current from the search
/// page alone. A repeat walk opens a detail page only for a listing the ledger has never seen; a link
/// already on the ledger is instead touched from its result card (see
/// <see cref="LedgerUpsertService.TouchAsync"/>), so its LastSeen moves to this run and a lower card
/// price is recorded, and the diff keeps reporting price drops and gone listings from the search pages.
/// The badges the card shows are refreshed the same way, as the posting's display-only attributes.
/// A link the ledger does not hold is not touched, but the badges its card showed are remembered
/// (see <see cref="BadgesOfNewLink"/>) for the detail visit that will record it.
/// <see cref="TryTouchAsync"/> is what <see cref="WalkSearchPages"/> asks of each link before it goes
/// into the pool of detail visits, so a touched link never spends the per-pair cap. It only remembers
/// the touch: <see cref="CommitAsync"/> writes them all once the pair has finished, because a touch
/// stamps LastSeen with this run and the pair's coverage token is only stamped when the pair completes,
/// so a pair that fails or is interrupted must leave its known postings exactly as the last completed
/// run left them. A walk asked to revisit every link gets a set that holds nothing, so every link is
/// visited as before.
/// </summary>
public sealed class KnownCardTouches
{
    private readonly LedgerUpsertService _ledger;
    private readonly string _source;
    private readonly RunEntity _run;
    private readonly HashSet<string> _knownUrls;
    private readonly Dictionary<string, decimal?> _pendingPricesByUrl = [];
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _pendingBadgesByUrl = [];
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _newLinkBadgesByUrl = [];

    private KnownCardTouches(LedgerUpsertService ledger, string source, RunEntity run, HashSet<string> knownUrls)
    {
        _ledger = ledger;
        _source = source;
        _run = run;
        _knownUrls = knownUrls;
    }

    /// <summary>How many distinct known links were seen on cards so far.</summary>
    public int Count => _pendingPricesByUrl.Count;

    /// <summary>The touches for <paramref name="source"/> against what the ledger holds for it now, or
    /// touches that treat every link as new when <paramref name="revisit"/> is set.</summary>
    public static async ValueTask<KnownCardTouches> LoadAsync(LedgerUpsertService ledger, string source, RunEntity run, bool revisit, CancellationToken cancellationToken) =>
        new(ledger, source, run, revisit ? [] : await ledger.KnownUrlsAsync(source, cancellationToken));

    /// <summary>Remembers a touch of the posting at <paramref name="canonicalUrl"/> with the card's price
    /// (<paramref name="cardPrice"/>, null when the card's price could not be read) and its badges
    /// (<paramref name="cardBadges"/>, empty when it shows none) and returns true when the ledger holds
    /// it. A link already seen this run returns true without a second touch (the first card price that
    /// could be read is the one kept, and so is the first set of badges), so a listing that two of a
    /// pair's searches both show is counted once. False means the link is new and belongs in the pool
    /// of detail visits.</summary>
    public ValueTask<bool> TryTouchAsync(string canonicalUrl, decimal? cardPrice, IReadOnlyDictionary<string, string> cardBadges, CancellationToken cancellationToken)
    {
        if (!_knownUrls.Contains(canonicalUrl))
        {
            if (cardBadges.Count > 0)
            {
                _newLinkBadgesByUrl.TryAdd(canonicalUrl, cardBadges);
            }

            return ValueTask.FromResult(false);
        }

        if (!_pendingPricesByUrl.TryGetValue(canonicalUrl, out decimal? seenPrice) || seenPrice is null)
        {
            _pendingPricesByUrl[canonicalUrl] = cardPrice;
        }

        if (cardBadges.Count > 0)
        {
            _pendingBadgesByUrl.TryAdd(canonicalUrl, cardBadges);
        }

        return ValueTask.FromResult(true);
    }

    /// <summary>The badges the card of a link that <see cref="TryTouchAsync"/> reported as new showed, for
    /// the caller to store with the posting its detail visit records (the pool holds only hrefs, and the
    /// card is gone by the time the page opens); empty when the card showed none or the link was not
    /// seen on a card.</summary>
    public IReadOnlyDictionary<string, string> BadgesOfNewLink(string canonicalUrl) =>
        _newLinkBadgesByUrl.GetValueOrDefault(canonicalUrl) ?? new Dictionary<string, string>();

    /// <summary>Writes every touch remembered so far to the ledger, prices and then badges, for the caller to run
    /// when the pair's walk has completed and not before.</summary>
    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        if (_pendingPricesByUrl.Count > 0)
        {
            await _ledger.TouchAsync(_source, _pendingPricesByUrl, _run, cancellationToken);
        }

        if (_pendingBadgesByUrl.Count > 0)
        {
            await _ledger.SetPostingAttributesByUrlAsync(_source, _pendingBadgesByUrl, _run, cancellationToken);
        }
    }
}
