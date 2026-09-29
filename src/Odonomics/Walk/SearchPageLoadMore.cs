namespace Odonomics.Walk;

/// <summary>Whether pressing the search page's show-more control added new cards, or why it did not:
/// the control is no longer on the page at all (<see cref="ControlGone"/>), or it is still there but
/// clicking it failed (<see cref="ClickFailed"/>).</summary>
public enum LoadMorePress
{
    Pressed,
    ControlGone,
    ClickFailed,
}

/// <summary>Why a load-more run ended before the page reached its stated count: the control disappeared
/// (<see cref="ControlGone"/>), a press could not click it (<see cref="ClickFailed"/>), a press added no
/// new cards even after a second look (<see cref="StalledPress"/>), or the run hit
/// <see cref="SearchPageLoadMore.MaxPresses"/> (<see cref="PressLimit"/>). <see cref="None"/> covers a run
/// whose stated count was reached, so no reason for stopping short applies.</summary>
public enum LoadMoreStopReason
{
    None,
    ControlGone,
    ClickFailed,
    StalledPress,
    PressLimit,
}

/// <summary>How a load-more run ended: how many times the control was pressed, how many distinct
/// detail links the page held at the end, and, when the run ended before the stated count was reached,
/// <see cref="StopReason"/> for why.</summary>
public readonly record struct LoadMoreResult(int Presses, int Cards, LoadMoreStopReason StopReason = LoadMoreStopReason.None)
{
    /// <summary>True when the page holds every card the run set out to load: at least as many as
    /// <paramref name="statedCount"/>, or, when the page states no count, a run that did not end on
    /// <see cref="SearchPageLoadMore.MaxPresses"/>. A run that ends short of that (a control that could not
    /// be clicked, the press limit) leaves results the walk never saw, which must not be read as cars that
    /// left the market. The one exception is a run that ended because the control is gone or because a
    /// press added nothing, with the cards within <see cref="SearchPageLoadMore.ToleranceFor"/> of the
    /// stated count: a site's stated count can run a card or two past what it will render, and that run is
    /// complete.</summary>
    public bool LoadedAll(int? statedCount) =>
        statedCount is int stated
            ? Cards >= stated || WithinTolerance(stated)
            : StopReason != LoadMoreStopReason.PressLimit;

    /// <summary>True when the run ended because the control is gone or a press added nothing, it did not
    /// hit <see cref="SearchPageLoadMore.MaxPresses"/>, and the cards fall short of <paramref name="stated"/>
    /// by no more than <see cref="SearchPageLoadMore.ToleranceFor"/>, so the run counts as complete only
    /// because of the tolerance. A control that could not be clicked, and a run that hit
    /// <see cref="SearchPageLoadMore.MaxPresses"/>, are never within tolerance.</summary>
    public bool WithinTolerance(int stated) =>
        StopReason is LoadMoreStopReason.ControlGone or LoadMoreStopReason.StalledPress
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

    /// <summary>How many cards short of the stated count a run that ended on a stalled press or a
    /// gone control may be and still count as complete: two cards, or one percent of the stated count,
    /// whichever is larger.</summary>
    public static int ToleranceFor(int statedCount) => Math.Max(2, statedCount / 100);

    /// <summary><paramref name="countCardsAsync"/> returns how many distinct detail links the page holds
    /// now. <paramref name="pressControlAsync"/> presses the control and reports whether it did, or why not
    /// (the locator found none, or the click itself failed). <paramref name="pauseAsync"/> waits the walk's
    /// usual pause between page actions, so the cards have arrived and the presses are paced like a
    /// person's.</summary>
    public static async Task<LoadMoreResult> RunAsync(
        int? statedCount,
        Func<CancellationToken, Task<int>> countCardsAsync,
        Func<CancellationToken, Task<LoadMorePress>> pressControlAsync,
        Func<CancellationToken, Task> pauseAsync,
        CancellationToken cancellationToken)
    {
        int cards = await countCardsAsync(cancellationToken);
        int presses = 0;
        LoadMoreStopReason stopReason = LoadMoreStopReason.None;
        while (presses < MaxPresses && (statedCount is null || cards < statedCount))
        {
            LoadMorePress press = await pressControlAsync(cancellationToken);
            if (press == LoadMorePress.ControlGone)
            {
                stopReason = LoadMoreStopReason.ControlGone;
                break;
            }

            if (press == LoadMorePress.ClickFailed)
            {
                stopReason = LoadMoreStopReason.ClickFailed;
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
                stopReason = LoadMoreStopReason.StalledPress;
                break;
            }

            cards = afterPress;
        }

        if (stopReason == LoadMoreStopReason.None && presses >= MaxPresses)
        {
            stopReason = LoadMoreStopReason.PressLimit;
        }

        return new LoadMoreResult(presses, cards, stopReason);
    }
}
