namespace Odonomics.Walk;

/// <summary>Scrolls a detail page far enough for the text the walk reads to be on it. Every page gets
/// one screen of scrolling and a pause, as it always did. A site that renders a needed block lazily
/// (see <see cref="WalkSite.LazyDetailBlockMarker"/>) then gets up to <c>extraSteps</c> more, stopping as
/// soon as the page text carries the marker, so a page that renders the block early is not scrolled
/// further and a page that never renders it is given up on rather than waited for.</summary>
public static class DetailPageScroll
{
    public static async Task<string> ReadAsync(
        Func<Task> scrollDown,
        Func<Task<string>> readText,
        Func<TimeSpan> pause,
        string? lazyBlockMarker,
        int extraSteps,
        CancellationToken cancellationToken)
    {
        await scrollDown();
        await Task.Delay(pause(), cancellationToken);
        string text = await readText();

        for (int step = 0; lazyBlockMarker is not null && step < extraSteps && !text.Contains(lazyBlockMarker, StringComparison.OrdinalIgnoreCase); step++)
        {
            await scrollDown();
            await Task.Delay(pause(), cancellationToken);
            text = await readText();
        }

        return text;
    }
}
