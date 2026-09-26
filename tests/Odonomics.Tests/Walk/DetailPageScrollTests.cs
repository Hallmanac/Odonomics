using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

public class DetailPageScrollTests
{
    /// <summary>A page that grows its text as it is scrolled: the text after n scrolls is
    /// <c>texts[n - 1]</c>, or the last entry once the list runs out.</summary>
    private sealed class ScriptedPage(params string[] texts)
    {
        public int Scrolls { get; private set; }

        public Task ScrollDown()
        {
            Scrolls++;
            return Task.CompletedTask;
        }

        public Task<string> ReadText() => Task.FromResult(texts[Math.Min(Math.Max(Scrolls, 1), texts.Length) - 1]);
    }

    private static Task<string> ReadAsync(ScriptedPage page, string? marker, int extraSteps) =>
        DetailPageScroll.ReadAsync(page.ScrollDown, page.ReadText, () => TimeSpan.Zero, marker, extraSteps, CancellationToken.None);

    [Fact]
    public async Task ReadAsync_SiteWithNoMarker_ScrollsOnceAndReadsThatText()
    {
        var page = new ScriptedPage("top", "top and block");

        string text = await ReadAsync(page, marker: null, extraSteps: 3);

        Assert.Equal("top", text);
        Assert.Equal(1, page.Scrolls);
    }

    [Fact]
    public async Task ReadAsync_BlockRenderedAfterTheFirstScroll_ScrollsNoFurther()
    {
        var page = new ScriptedPage("top\nPickup and Delivery");

        string text = await ReadAsync(page, "Pickup and Delivery", extraSteps: 3);

        Assert.Contains("Pickup and Delivery", text);
        Assert.Equal(1, page.Scrolls);
    }

    [Fact]
    public async Task ReadAsync_BlockRenderedOnlyAfterMoreScrolling_KeepsScrollingUntilItAppears()
    {
        var page = new ScriptedPage("top", "top", "top\nPickup and Delivery", "top\nPickup and Delivery\nmore");

        string text = await ReadAsync(page, "Pickup and Delivery", extraSteps: 3);

        Assert.Equal("top\nPickup and Delivery", text);
        Assert.Equal(3, page.Scrolls);
    }

    [Fact]
    public async Task ReadAsync_BlockThatNeverRenders_GivesUpAfterTheExtraStepsAndReturnsWhatItHas()
    {
        var page = new ScriptedPage("top");

        string text = await ReadAsync(page, "Pickup and Delivery", extraSteps: 3);

        Assert.Equal("top", text);
        Assert.Equal(4, page.Scrolls);
    }

    [Fact]
    public async Task ReadAsync_CancelledDuringThePause_Throws()
    {
        var page = new ScriptedPage("top");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DetailPageScroll.ReadAsync(page.ScrollDown, page.ReadText, () => TimeSpan.FromSeconds(30), "x", 3, cts.Token));
    }
}
