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

        Assert.Equal(new LoadMoreResult(2, 50), result);
        // Each press pauses once, and the press that added nothing pauses a second time before giving up.
        Assert.Equal(3, page.Pauses);
    }

    [Fact]
    public async Task RunAsync_CountsFewerCardsThanStated_StopsWhenNothingNewAppears()
    {
        var page = new FakePage(25, 20);

        LoadMoreResult result = await RunAsync(page, statedCount: 526);

        Assert.Equal(new LoadMoreResult(2, 45), result);
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

        Assert.Equal(new LoadMoreResult(3, 75), await RunAsync(page, statedCount: null));
    }

    [Fact]
    public async Task RunAsync_APageThatKeepsGrowing_StopsAtTheMaximumNumberOfPresses()
    {
        var page = new FakePage(0, [.. Enumerable.Repeat(1, SearchPageLoadMore.MaxPresses + 50)]);

        LoadMoreResult result = await RunAsync(page, statedCount: null);

        Assert.Equal(new LoadMoreResult(SearchPageLoadMore.MaxPresses, SearchPageLoadMore.MaxPresses), result);
    }
}
