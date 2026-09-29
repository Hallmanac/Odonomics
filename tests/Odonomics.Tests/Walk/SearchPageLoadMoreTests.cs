using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves how a search page's "Show 25 matches" control is pressed: until the page holds the stated
/// count, until a press adds nothing new, until the control is gone, or until a click on it fails.</summary>
public class SearchPageLoadMoreTests
{
    /// <summary>A page that starts with <paramref name="initial"/> cards and gains the next entry of
    /// <paramref name="gains"/> at each press; a press past the last entry gains nothing.</summary>
    private sealed class FakePage(int initial, params int[] gains)
    {
        private int _presses;

        public int Cards { get; private set; } = initial;

        public int Pauses { get; private set; }

        public int Presses => _presses;

        public bool ControlPresent { get; set; } = true;

        /// <summary>When set, the control reports gone once this many presses have already succeeded,
        /// instead of being gone (or present) from the start.</summary>
        public int? GoneAfterPresses { get; set; }

        public bool ClickFails { get; set; }

        public Task<int> CountAsync(CancellationToken ct) => Task.FromResult(Cards);

        public Task<LoadMorePress> PressAsync(CancellationToken ct)
        {
            if (!ControlPresent || (GoneAfterPresses is int goneAfter && _presses >= goneAfter))
            {
                return Task.FromResult(LoadMorePress.ControlGone);
            }

            if (ClickFails)
            {
                return Task.FromResult(LoadMorePress.ClickFailed);
            }

            Cards += _presses < gains.Length ? gains[_presses] : 0;
            _presses++;
            return Task.FromResult(LoadMorePress.Pressed);
        }

        public Task PauseAsync(CancellationToken ct)
        {
            Pauses++;
            return Task.CompletedTask;
        }
    }

    private static Task<LoadMoreResult> RunAsync(FakePage page, int? statedCount) =>
        SearchPageLoadMore.RunAsync(statedCount, page.CountAsync, page.PressAsync, page.PauseAsync, CancellationToken.None);

    [Fact]
    public async Task RunAsync_PressesUntilTheStatedCountIsCovered()
    {
        var page = new FakePage(25, 25, 25, 25);

        LoadMoreResult result = await RunAsync(page, statedCount: 100);

        Assert.Equal(new LoadMoreResult(3, 100), result);
    }

    [Fact]
    public async Task RunAsync_CountAlreadyCoveredByTheFirstCards_PressesNothing()
    {
        var page = new FakePage(4);

        Assert.Equal(new LoadMoreResult(0, 4), await RunAsync(page, statedCount: 4));
        Assert.Equal(0, page.Presses);
    }

    [Fact]
    public async Task RunAsync_ZeroMatches_PressesNothing()
    {
        Assert.Equal(new LoadMoreResult(0, 0), await RunAsync(new FakePage(0), statedCount: 0));
    }

    [Fact]
    public async Task RunAsync_PressAddsNothing_StopsAfterAOneMorePauseAndLook()
    {
        var page = new FakePage(25, 25, 0);

        LoadMoreResult result = await RunAsync(page, statedCount: 526);

        Assert.Equal(new LoadMoreResult(2, 50, LoadMoreStopReason.StalledPress), result);
        // Each press pauses once, and the press that added nothing pauses a second time before giving up.
        Assert.Equal(3, page.Pauses);
    }

    [Fact]
    public async Task RunAsync_CountsFewerCardsThanStated_StopsWhenNothingNewAppears()
    {
        var page = new FakePage(25, 20);

        LoadMoreResult result = await RunAsync(page, statedCount: 526);

        Assert.Equal(new LoadMoreResult(2, 45, LoadMoreStopReason.StalledPress), result);
    }

    [Fact]
    public async Task RunAsync_ControlIsGone_StopsWithoutCountingAPress()
    {
        var page = new FakePage(25) { ControlPresent = false };

        Assert.Equal(new LoadMoreResult(0, 25, LoadMoreStopReason.ControlGone), await RunAsync(page, statedCount: 526));
    }

    [Fact]
    public async Task RunAsync_ClickFails_StopsWithoutCountingAPress()
    {
        var page = new FakePage(25) { ClickFails = true };

        Assert.Equal(new LoadMoreResult(0, 25, LoadMoreStopReason.ClickFailed), await RunAsync(page, statedCount: 526));
    }

    [Fact]
    public async Task RunAsync_NoStatedCount_PressesUntilNothingNewAppears()
    {
        var page = new FakePage(25, 25, 25);

        Assert.Equal(new LoadMoreResult(3, 75, LoadMoreStopReason.StalledPress), await RunAsync(page, statedCount: null));
    }

    [Fact]
    public async Task RunAsync_APageThatKeepsGrowing_StopsAtTheMaximumNumberOfPresses()
    {
        var page = new FakePage(0, [.. Enumerable.Repeat(1, SearchPageLoadMore.MaxPresses + 50)]);

        LoadMoreResult result = await RunAsync(page, statedCount: null);

        Assert.Equal(new LoadMoreResult(SearchPageLoadMore.MaxPresses, SearchPageLoadMore.MaxPresses, LoadMoreStopReason.PressLimit), result);
    }

    [Theory]
    [InlineData(50, 526, false)]
    [InlineData(526, 526, true)]
    [InlineData(530, 526, true)]
    public void LoadedAll_WithAStatedCount_MeansTheCardsReachedIt(int cards, int stated, bool expected)
    {
        Assert.Equal(expected, new LoadMoreResult(3, cards).LoadedAll(stated));
    }

    [Theory]
    [InlineData(LoadMoreStopReason.StalledPress, true)]
    [InlineData(LoadMoreStopReason.ControlGone, true)]
    [InlineData(LoadMoreStopReason.ClickFailed, false)]
    [InlineData(LoadMoreStopReason.PressLimit, false)]
    public void LoadedAll_WithNoStatedCount_MeansTheRunDidNotHitThePressLimitOrFailAClick(LoadMoreStopReason stopReason, bool expected)
    {
        Assert.Equal(expected, new LoadMoreResult(75, 75, stopReason).LoadedAll(null));
    }

    [Theory]
    [InlineData(521, 522, true)]
    [InlineData(520, 522, true)]
    [InlineData(517, 522, true)]
    [InlineData(516, 522, false)]
    [InlineData(48, 50, true)]
    [InlineData(47, 50, false)]
    [InlineData(500, 526, false)]
    [InlineData(521, 526, true)]
    [InlineData(520, 526, false)]
    public void LoadedAll_RunEndedOnAStalledPress_AllowsAShortfallOfTwoCardsOrOnePercent(int cards, int stated, bool expected)
    {
        var result = new LoadMoreResult(11, cards, LoadMoreStopReason.StalledPress);

        Assert.Equal(expected, result.LoadedAll(stated));
        Assert.Equal(expected, result.WithinTolerance(stated));
    }

    [Fact]
    public void LoadedAll_ShortByOneWithoutAStalledPress_StaysPartial()
    {
        var result = new LoadMoreResult(0, 521);

        Assert.False(result.LoadedAll(522));
        Assert.False(result.WithinTolerance(522));
    }

    [Fact]
    public void LoadedAll_ShortByOneAtThePressLimit_StaysPartial()
    {
        var result = new LoadMoreResult(SearchPageLoadMore.MaxPresses, 521, LoadMoreStopReason.StalledPress);

        Assert.False(result.LoadedAll(522));
    }

    [Fact]
    public void WithinTolerance_CardsReachedTheCount_IsFalse()
    {
        Assert.False(new LoadMoreResult(3, 522, LoadMoreStopReason.StalledPress).WithinTolerance(522));
    }

    [Fact]
    public async Task RunAsync_APressAddsNothingOneCardShort_IsLoadedAllWithinTolerance()
    {
        var page = new FakePage(500, 21, 0);

        LoadMoreResult result = await RunAsync(page, statedCount: 522);

        Assert.Equal(new LoadMoreResult(2, 521, LoadMoreStopReason.StalledPress), result);
        Assert.True(result.LoadedAll(522));
        Assert.True(result.WithinTolerance(522));
    }

    [Fact]
    public async Task RunAsync_APressAddsNothingFarShort_IsNotLoadedAll()
    {
        var page = new FakePage(25, 25, 0);

        LoadMoreResult result = await RunAsync(page, statedCount: 526);

        Assert.False(result.LoadedAll(526));
    }

    /// <summary>Pins the 2026-09-28 Camry Hybrid recording's shape: 523 stated, the page settling at 522
    /// once the control is gone after 10 presses, a single card short of a 5-card tolerance, so the run
    /// counts as complete the same as one that ended on a stalled press.</summary>
    [Fact]
    public async Task RunAsync_CamryHybrid20260928Shape_ControlGoneWithinTolerance_IsLoadedAll()
    {
        var page = new FakePage(0, 50, 50, 50, 50, 50, 50, 50, 50, 50, 72) { GoneAfterPresses = 10 };

        LoadMoreResult result = await RunAsync(page, statedCount: 523);

        Assert.Equal(new LoadMoreResult(10, 522, LoadMoreStopReason.ControlGone), result);
        Assert.True(result.LoadedAll(523));
        Assert.True(result.WithinTolerance(523));
    }

    [Fact]
    public async Task RunAsync_ControlGoneTwentyShortOfStated_StaysPartial()
    {
        var page = new FakePage(503) { ControlPresent = false };

        LoadMoreResult result = await RunAsync(page, statedCount: 523);

        Assert.Equal(new LoadMoreResult(0, 503, LoadMoreStopReason.ControlGone), result);
        Assert.False(result.LoadedAll(523));
        Assert.False(result.WithinTolerance(523));
    }

    /// <summary>A search small enough that the show-more control was never on the page at all (no press
    /// ever happened) is not the same as one that pressed the control for a while and then found it gone:
    /// reading nothing at all must not be waved through by the tolerance meant for a couple of cards'
    /// render slop.</summary>
    [Fact]
    public async Task RunAsync_ControlNeverPresentAndNoCardsRead_StaysPartialEvenWithinTolerance()
    {
        var page = new FakePage(0) { ControlPresent = false };

        LoadMoreResult result = await RunAsync(page, statedCount: 2);

        Assert.Equal(new LoadMoreResult(0, 0, LoadMoreStopReason.ControlGone), result);
        Assert.False(result.LoadedAll(2));
        Assert.False(result.WithinTolerance(2));
    }

    [Fact]
    public async Task RunAsync_ReachesStatedCountExactlyOnTheLastPress_IsNotTaggedPressLimit()
    {
        var page = new FakePage(0, [.. Enumerable.Repeat(1, SearchPageLoadMore.MaxPresses)]);

        LoadMoreResult result = await RunAsync(page, statedCount: SearchPageLoadMore.MaxPresses);

        Assert.Equal(new LoadMoreResult(SearchPageLoadMore.MaxPresses, SearchPageLoadMore.MaxPresses, LoadMoreStopReason.None), result);
        Assert.True(result.LoadedAll(SearchPageLoadMore.MaxPresses));
    }

    [Fact]
    public async Task RunAsync_ClickFailureOneShortOfStated_StaysPartial()
    {
        var page = new FakePage(522) { ClickFails = true };

        LoadMoreResult result = await RunAsync(page, statedCount: 523);

        Assert.Equal(new LoadMoreResult(0, 522, LoadMoreStopReason.ClickFailed), result);
        Assert.False(result.LoadedAll(523));
        Assert.False(result.WithinTolerance(523));
    }
}
