using System.Text.Json;
using System.Text.RegularExpressions;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves the render-wait scans a cars.com result page, scrolls a card that hasn't shown its own
/// price yet into view, and keeps scanning, up to a bounded number of times, until a scan comes back with
/// nothing left to render. The page is a fake: a small list of cards standing in for the DOM, answering
/// <see cref="SearchPageCardRenderWait.RenderScanScript"/> the way the browser does, so nothing here opens
/// a browser (see <see cref="SearchPageLinksTests"/> for the sibling pattern this follows).</summary>
public class SearchPageCardRenderWaitTests
{
    /// <summary>A card whose own text is empty until <paramref name="rendersOnScan"/> (1-based): the scan
    /// that number and every one after it sees <paramref name="renderedText"/> instead.</summary>
    private sealed class FakeCard(string href, string renderedText, int rendersOnScan)
    {
        public string Href { get; } = href;
        private readonly string _renderedText = renderedText;
        private readonly int _rendersOnScan = rendersOnScan;
        public bool Scrolled { get; private set; }

        public string TextAsOf(int scanNumber) => scanNumber >= _rendersOnScan ? _renderedText : "";

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

            List<string> hrefs = [];
            foreach (FakeCard card in cards)
            {
                if (!hasAmount.IsMatch(card.TextAsOf(ScanCount)))
                {
                    card.Scroll();
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
    public async Task RunAsync_CancelledDuringThePause_Throws()
    {
        var card = new FakeCard("https://www.cars.com/vehicledetail/a/?sid=1", "$21,202", rendersOnScan: SearchPageCardRenderWait.MaxScans + 10);
        var page = new FakePage(card);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SearchPageCardRenderWait.RunAsync(page.EvaluateAsync, () => TimeSpan.FromSeconds(30), cts.Token));
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
