using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves how a search page's "Show 25 matches" control is pressed: until the page holds the stated
/// count, until a press adds nothing new, or until the control is gone.</summary>
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

        public Task<int> CountAsync(CancellationToken ct) => Task.FromResult(Cards);

        public Task<bool> PressAsync(CancellationToken ct)
        {
            if (!ControlPresent)
            {
                return Task.FromResult(false);
            }

            Cards += _presses < gains.Length ? gains[_presses] : 0;
            _presses++;
            return Task.FromResult(true);
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

        Assert.Equal(new LoadMoreResult(2, 50, EndedOnStalledPress: true), result);
        // Each press pauses once, and the press that added nothing pauses a second time before giving up.
        Assert.Equal(3, page.Pauses);
    }

    [Fact]
    public async Task RunAsync_CountsFewerCardsThanStated_StopsWhenNothingNewAppears()
    {
        var page = new FakePage(25, 20);

        LoadMoreResult result = await RunAsync(page, statedCount: 526);

        Assert.Equal(new LoadMoreResult(2, 45, EndedOnStalledPress: true), result);
    }

    [Fact]
    public async Task RunAsync_ControlIsGone_StopsWithoutCountingAPress()
    {
        var page = new FakePage(25) { ControlPresent = false };

        Assert.Equal(new LoadMoreResult(0, 25), await RunAsync(page, statedCount: 526));
    }

    [Fact]
    public async Task RunAsync_NoStatedCount_PressesUntilNothingNewAppears()
    {
        var page = new FakePage(25, 25, 25);

        Assert.Equal(new LoadMoreResult(3, 75, EndedOnStalledPress: true), await RunAsync(page, statedCount: null));
    }

    [Fact]
    public async Task RunAsync_APageThatKeepsGrowing_StopsAtTheMaximumNumberOfPresses()
    {
        var page = new FakePage(0, [.. Enumerable.Repeat(1, SearchPageLoadMore.MaxPresses + 50)]);

        LoadMoreResult result = await RunAsync(page, statedCount: null);

        Assert.Equal(new LoadMoreResult(SearchPageLoadMore.MaxPresses, SearchPageLoadMore.MaxPresses), result);
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
    [InlineData(3, true)]
    [InlineData(SearchPageLoadMore.MaxPresses, false)]
    public void LoadedAll_WithNoStatedCount_MeansTheRunDidNotHitThePressLimit(int presses, bool expected)
    {
        Assert.Equal(expected, new LoadMoreResult(presses, 75).LoadedAll(null));
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
        var result = new LoadMoreResult(11, cards, EndedOnStalledPress: true);

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
        var result = new LoadMoreResult(SearchPageLoadMore.MaxPresses, 521, EndedOnStalledPress: true);

        Assert.False(result.LoadedAll(522));
    }

    [Fact]
    public void WithinTolerance_CardsReachedTheCount_IsFalse()
    {
        Assert.False(new LoadMoreResult(3, 522, EndedOnStalledPress: true).WithinTolerance(522));
    }

    [Fact]
    public async Task RunAsync_APressAddsNothingOneCardShort_IsLoadedAllWithinTolerance()
    {
        var page = new FakePage(500, 21, 0);

        LoadMoreResult result = await RunAsync(page, statedCount: 522);

        Assert.Equal(new LoadMoreResult(2, 521, EndedOnStalledPress: true), result);
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

    [Fact]
    public async Task RunAsync_ControlGoneOneCardShort_IsNotLoadedAll()
    {
        var page = new FakePage(521) { ControlPresent = false };

        LoadMoreResult result = await RunAsync(page, statedCount: 522);

        Assert.False(result.LoadedAll(522));
    }
}
