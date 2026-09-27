namespace Odonomics.Walk;

/// <summary>How a load-more run ended: how many times the control was pressed, how many distinct
/// detail links the page held at the end, and whether the run ended because a press added nothing
/// new even after a second look (as opposed to a control that could not be pressed, or the press
/// limit).</summary>
public readonly record struct LoadMoreResult(int Presses, int Cards, bool EndedOnStalledPress = false)
{
    /// <summary>True when the page holds every card the run set out to load: at least as many as
    /// <paramref name="statedCount"/>, or, when the page states no count, a run that ended because
    /// nothing new appeared and not because it hit <see cref="SearchPageLoadMore.MaxPresses"/>. A run
    /// that ends short of that (a control that could not be pressed, a press that added nothing, the
    /// press limit) leaves results the walk never saw, which must not be read as cars that left the
    /// market. The one exception is a run that ended because a press added nothing with the cards
    /// within <see cref="SearchPageLoadMore.ToleranceFor"/> of the stated count: a site's stated count
    /// can run a card or two past what it will render, and that run is complete.</summary>
    public bool LoadedAll(int? statedCount) =>
        statedCount is int stated
            ? Cards >= stated || WithinTolerance(stated)
            : Presses < SearchPageLoadMore.MaxPresses;

    /// <summary>True when the run ended because a press added nothing and the cards fall short of
    /// <paramref name="stated"/> by no more than <see cref="SearchPageLoadMore.ToleranceFor"/>, so the
    /// run counts as complete only because of the tolerance. A run that hit
    /// <see cref="SearchPageLoadMore.MaxPresses"/> is never within tolerance.</summary>
    public bool WithinTolerance(int stated) =>
        EndedOnStalledPress
        && Presses < SearchPageLoadMore.MaxPresses
        && Cards < stated
        && stated - Cards <= SearchPageLoadMore.ToleranceFor(stated);
}

/// <summary>
/// Loads the rest of a search page's cards for a site that hides them behind a control instead of a
/// page number (CarMax's "Show 25 matches" or "Load more"; see <see cref="WalkSite.LoadMoreControlPattern"/>). The
/// control is pressed until the page holds as many cards as it states matches, or until pressing
/// adds nothing new. A press that adds nothing is given one more pause and a second look before it
/// ends the run, since the cards arrive a moment after the click. The run also ends when the control
/// is gone (nothing to press) and after <see cref="MaxPresses"/> presses, so a page that keeps
/// offering more never keeps the walk on one search page forever. With no stated count the run goes on
/// until nothing new appears.
/// </summary>
public static class SearchPageLoadMore
{
    /// <summary>The most presses one search page gets: a stated 526 matches take about 22 at 25 a
    /// press, so this leaves room for several thousand.</summary>
    public const int MaxPresses = 200;

    /// <summary>How many cards short of the stated count a run that ended on a stalled press may be and
    /// still count as complete: two cards, or one percent of the stated count, whichever is larger.</summary>
    public static int ToleranceFor(int statedCount) => Math.Max(2, statedCount / 100);

    /// <summary><paramref name="countCardsAsync"/> returns how many distinct detail links the page holds
    /// now. <paramref name="pressControlAsync"/> presses the control and returns false when there is none
    /// to press. <paramref name="pauseAsync"/> waits the walk's usual pause between page actions, so the
    /// cards have arrived and the presses are paced like a person's.</summary>
    public static async Task<LoadMoreResult> RunAsync(
        int? statedCount,
        Func<CancellationToken, Task<int>> countCardsAsync,
        Func<CancellationToken, Task<bool>> pressControlAsync,
        Func<CancellationToken, Task> pauseAsync,
        CancellationToken cancellationToken)
    {
        int cards = await countCardsAsync(cancellationToken);
        int presses = 0;
        bool endedOnStalledPress = false;
        while (presses < MaxPresses && (statedCount is null || cards < statedCount))
        {
            if (!await pressControlAsync(cancellationToken))
            {
                break;
            }

            presses++;
            await pauseAsync(cancellationToken);
            int afterPress = await countCardsAsync(cancellationToken);
            if (afterPress <= cards)
            {
                await pauseAsync(cancellationToken);
                afterPress = await countCardsAsync(cancellationToken);
            }

            if (afterPress <= cards)
            {
                endedOnStalledPress = true;
                break;
            }

            cards = afterPress;
        }

        return new LoadMoreResult(presses, cards, endedOnStalledPress);
    }
}
