using System.Text.Json;
using System.Text.RegularExpressions;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the render-wait scans a cars.com result page, scrolls a card that hasn't shown its own
/// price yet into view, and keeps scanning, up to a bounded number of times, until a scan comes back with
/// nothing left to render; and that its final, no-scroll recheck (<see cref="SearchPageCardRenderWait.StillUnrenderedAsync"/>)
/// gives a card that goes on rendering after the scan loop gives up one more chance to prove it before the
/// walk drops it (walk run 20260928-134242). The page is a fake: a small list of cards standing in for the
/// DOM, answering <see cref="SearchPageCardRenderWait.RenderScanScript"/> the way the browser does, so
/// nothing here opens a browser (see <see cref="SearchPageLinksTests"/> for the sibling pattern this follows).</summary>
public class SearchPageCardRenderWaitTests
{
    /// <summary>A card whose own text is empty until <paramref name="rendersOnScan"/> (1-based): the scan
    /// that number and every one after it sees <paramref name="renderedText"/> instead. When
    /// <paramref name="requiresScroll"/> is set, the card renders no earlier than that scan and only once
    /// it has actually been scrolled, the same as a lazily hydrated cars.com card.</summary>
    private sealed class FakeCard(string href, string renderedText, int rendersOnScan, bool requiresScroll = false)
    {
        public string Href { get; } = href;
        private readonly string _renderedText = renderedText;
        private readonly int _rendersOnScan = rendersOnScan;
        private readonly bool _requiresScroll = requiresScroll;
        public bool Scrolled { get; private set; }

        public string TextAsOf(int scanNumber) =>
            scanNumber >= _rendersOnScan && (!_requiresScroll || Scrolled) ? _renderedText : "";

        public void Scroll() => Scrolled = true;
    }

    private sealed class FakePage(params FakeCard[] cards)
    {
        public int ScanCount { get; private set; }

        public Task<string[]> EvaluateAsync(string script, object? arg)
        {
            ScanCount++;
            Assert.Equal(SearchPageCardRenderWait.RenderScanScript, script);
            JsonElement options = JsonSerializer.SerializeToElement(arg);
            var hasAmount = new Regex(options.GetProperty("amount").GetString()!);
            bool scroll = !options.TryGetProperty("scroll", out JsonElement scrollProperty) || scrollProperty.GetBoolean();

            // Mirrors RenderScanScript's own guard: only the first still-unrendered, not-yet-scrolled
            // card of the scan is scrolled, since the browser only ever realizes one scroll position per
            // script call, and a card's own Scrolled flag persists between calls the same way the script's
            // data attribute survives between calls against the same live page. StillUnrenderedAsync passes
            // scroll: false, the same as the real script does, for a check that nudges nothing.
            List<string> hrefs = [];
            bool scrolled = false;
            foreach (FakeCard card in cards)
            {
                if (!hasAmount.IsMatch(card.TextAsOf(ScanCount)))
                {
                    if (scroll && !scrolled && !card.Scrolled)
                    {
                        card.Scroll();
                        scrolled = true;
                    }

                    hrefs.Add(card.Href);
                }
            }

            return Task.FromResult(hrefs.ToArray());
        }
    }

    private static Task<IReadOnlyList<string>> RunAsync(FakePage page) =>
        SearchPageCardRenderWait.RunAsync(page.EvaluateAsync, () => TimeSpan.Zero, CancellationToken.None);

    [Fact]
    public async Task RunAsync_EveryCardAlreadyRendered_ScansOnceAndScrollsNothing()
    {
        var card = new FakeCard("https://www.cars.com/vehicledetail/a/?sid=1", "$21,202 Used 2023 Honda Insight EX", rendersOnScan: 1);
        var page = new FakePage(card);

        IReadOnlyList<string> unrendered = await RunAsync(page);

        Assert.Empty(unrendered);
        Assert.Equal(1, page.ScanCount);
        Assert.False(card.Scrolled);
    }

    [Fact]
    public async Task RunAsync_ACardThatRendersAfterAPause_EndsAsSoonAsItDoesAndHasScrolledIt()
    {
        var card = new FakeCard("https://www.cars.com/vehicledetail/a/?sid=1", "$21,202 Used 2023 Honda Insight EX", rendersOnScan: 3);
        var page = new FakePage(card);

        IReadOnlyList<string> unrendered = await RunAsync(page);

        Assert.Empty(unrendered);
        Assert.Equal(3, page.ScanCount);
        Assert.True(card.Scrolled);
    }

    [Fact]
    public async Task RunAsync_ACardThatNeverRenders_StopsAfterMaxScansAndReportsIt()
    {
        const string href = "https://www.cars.com/vehicledetail/a/?sid=1";
        var card = new FakeCard(href, "$21,202 Used 2023 Honda Insight EX", rendersOnScan: SearchPageCardRenderWait.MaxScans + 10);
        var page = new FakePage(card);

        IReadOnlyList<string> unrendered = await RunAsync(page);

        Assert.Equal([href], unrendered);
        Assert.Equal(SearchPageCardRenderWait.MaxScans + 1, page.ScanCount);
        Assert.True(card.Scrolled);
    }

    [Fact]
    public async Task RunAsync_OneCardNeverRendersAmongOthersThatDo_ReturnsOnlyTheOneStillUnrendered()
    {
        const string neverRenders = "https://www.cars.com/vehicledetail/never/?sid=1";
        var rendersFirstScan = new FakeCard("https://www.cars.com/vehicledetail/already/?sid=1", "$19,393 Used 2021 Honda Insight EX", rendersOnScan: 1);
        var rendersLater = new FakeCard("https://www.cars.com/vehicledetail/later/?sid=1", "$19,201 Used 2019 Honda Insight Touring", rendersOnScan: 2);
        var neverCard = new FakeCard(neverRenders, "$20,000 Used 2020 Honda Insight EX", rendersOnScan: SearchPageCardRenderWait.MaxScans + 10);
        var page = new FakePage(rendersFirstScan, rendersLater, neverCard);

        IReadOnlyList<string> unrendered = await RunAsync(page);

        Assert.Equal([neverRenders], unrendered);
        Assert.False(rendersFirstScan.Scrolled);
        Assert.True(rendersLater.Scrolled);
        Assert.True(neverCard.Scrolled);
    }

    [Fact]
    public async Task RunAsync_ACardThatNeverRendersComesFirst_StillScrollsAndRendersTheCardAfterIt()
    {
        const string neverRenders = "https://www.cars.com/vehicledetail/never/?sid=1";
        var neverCard = new FakeCard(neverRenders, "$20,000 Used 2020 Honda Insight EX", rendersOnScan: SearchPageCardRenderWait.MaxScans + 10);
        var rendersOnceScrolled = new FakeCard("https://www.cars.com/vehicledetail/later/?sid=1", "$19,201 Used 2019 Honda Insight Touring", rendersOnScan: 1, requiresScroll: true);
        var page = new FakePage(neverCard, rendersOnceScrolled);

        IReadOnlyList<string> unrendered = await RunAsync(page);

        Assert.Equal([neverRenders], unrendered);
        Assert.True(neverCard.Scrolled);
        Assert.True(rendersOnceScrolled.Scrolled);
    }

    [Fact]
    public async Task RunAsync_TwoCardsUnrenderedOnTheSameScan_ScrollsOnlyTheFirstThatScanAndTheSecondOnceItBecomesFirst()
    {
        var first = new FakeCard("https://www.cars.com/vehicledetail/first/?sid=1", "$21,202 Used 2023 Honda Insight EX", rendersOnScan: 2);
        var second = new FakeCard("https://www.cars.com/vehicledetail/second/?sid=1", "$19,393 Used 2021 Honda Insight EX", rendersOnScan: 3);
        var page = new FakePage(first, second);

        IReadOnlyList<string> unrendered = await RunAsync(page);

        Assert.Empty(unrendered);
        Assert.Equal(3, page.ScanCount);
        Assert.True(first.Scrolled);
        Assert.True(second.Scrolled);
    }

    [Fact]
    public void RenderScanScript_ScrollsAtMostOneCardPerScan()
    {
        string script = SearchPageCardRenderWait.RenderScanScript;

        Assert.Single(Regex.Matches(script, "scrollIntoView"));
        Assert.Contains("scrolled", script);
    }

    [Fact]
    public async Task RunAsync_CancelledDuringThePause_Throws()
    {
        var card = new FakeCard("https://www.cars.com/vehicledetail/a/?sid=1", "$21,202", rendersOnScan: SearchPageCardRenderWait.MaxScans + 10);
        var page = new FakePage(card);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SearchPageCardRenderWait.RunAsync(page.EvaluateAsync, () => TimeSpan.FromSeconds(30), cts.Token));
    }

    /// <summary>Reproduces walk run 20260928-134242: cars.com rendered slowly enough that a card
    /// <see cref="RunAsync"/> gave up on, within its own bounded scans, had already rendered under its own
    /// text by the time the walk went on to actually read the page's cards a moment later. Modelled here
    /// as the same card rendering on the very next scan after <see cref="RunAsync"/>'s own last one: without
    /// <see cref="SearchPageCardRenderWait.StillUnrenderedAsync"/>, WalkCommand would have dropped it from
    /// the pool on <see cref="RunAsync"/>'s stale say-so alone.</summary>
    [Fact]
    public async Task StillUnrenderedAsync_ACardThatFinishedRenderingSinceRunAsyncGaveUpOnIt_ReportsNothing()
    {
        var card = new FakeCard("https://www.cars.com/vehicledetail/a/?sid=1", "$21,202 Used 2023 Honda Insight EX", rendersOnScan: SearchPageCardRenderWait.MaxScans + 2);
        var page = new FakePage(card);
        IReadOnlyList<string> unrenderedAfterTheLoop = await RunAsync(page);
        Assert.Equal([card.Href], unrenderedAfterTheLoop);

        IReadOnlyList<string> stillUnrendered = await SearchPageCardRenderWait.StillUnrenderedAsync(page.EvaluateAsync);

        Assert.Empty(stillUnrendered);
    }

    [Fact]
    public async Task StillUnrenderedAsync_ACardThatNeverRenders_IsStillReportedUnrendered()
    {
        var card = new FakeCard("https://www.cars.com/vehicledetail/a/?sid=1", "$21,202 Used 2023 Honda Insight EX", rendersOnScan: SearchPageCardRenderWait.MaxScans + 100);
        var page = new FakePage(card);
        await RunAsync(page);

        IReadOnlyList<string> stillUnrendered = await SearchPageCardRenderWait.StillUnrenderedAsync(page.EvaluateAsync);

        Assert.Equal([card.Href], stillUnrendered);
    }

    [Fact]
    public async Task StillUnrenderedAsync_NeverScrollsTheCardItChecks()
    {
        var card = new FakeCard("https://www.cars.com/vehicledetail/a/?sid=1", "$21,202 Used 2023 Honda Insight EX", rendersOnScan: 1, requiresScroll: true);
        var page = new FakePage(card);

        IReadOnlyList<string> stillUnrendered = await SearchPageCardRenderWait.StillUnrenderedAsync(page.EvaluateAsync);

        // The card would render on the very first scan once scrolled, but StillUnrenderedAsync never
        // scrolls anything, so a card only lazy-rendering trigger away from showing its price still
        // reports as unrendered rather than being nudged into rendering by the check itself.
        Assert.Equal([card.Href], stillUnrendered);
        Assert.False(card.Scrolled);
    }

    [Fact]
    public void UnrenderedHrefsJson_ListsEveryHref()
    {
        string json = SearchPageCardRenderWait.UnrenderedHrefsJson(
        [
            "https://www.cars.com/vehicledetail/a/?sid=1",
            "https://www.cars.com/vehicledetail/b/?sid=1",
        ]);

        using JsonDocument doc = JsonDocument.Parse(json);
        string[] hrefs = [.. doc.RootElement.EnumerateArray().Select(e => e.GetString()!)];
        Assert.Equal(["https://www.cars.com/vehicledetail/a/?sid=1", "https://www.cars.com/vehicledetail/b/?sid=1"], hrefs);
    }

    [Fact]
    public void UnrenderedHrefsJson_KeepsAmpersandsReadable()
    {
        string json = SearchPageCardRenderWait.UnrenderedHrefsJson(["https://www.cars.com/vehicledetail/x/?a=1&sid=2"]);

        Assert.Contains("a=1&sid=2", json);
    }
}
