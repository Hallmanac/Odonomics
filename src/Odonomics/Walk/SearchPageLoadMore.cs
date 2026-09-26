namespace Odonomics.Walk;

/// <summary>How a load-more run ended: how many times the control was pressed and how many distinct
/// detail links the page held at the end.</summary>
public readonly record struct LoadMoreResult(int Presses, int Cards);

/// <summary>
/// Loads the rest of a search page's cards for a site that hides them behind a control instead of a
/// page number (CarMax's "Show 25 matches"; see <see cref="WalkSite.LoadMoreControlPattern"/>). The
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
                break;
            }

            cards = afterPress;
        }

        return new LoadMoreResult(presses, cards);
    }
}
