using Odonomics.Auctions;

namespace Odonomics.Tests.Auctions;

public class AuctionSearchPageReaderTests
{
    [Fact]
    public void Read_RecordedCorollaHybridResults_KeepsArchiveLinksInRankOrderAndSkipsOtherSites()
    {
        AuctionSearchPage page = AuctionSearchPageReader.Read(AuctionFixtures.Read("ddg-results-JTDBCMFEXS3070309.html"));

        Assert.Equal(AuctionSearchStatus.Results, page.Status);
        Assert.Equal(
            [
                AuctionFixtures.CorollaHybridLotUrl,
                "https://autobidcar.com/car/toyota-corolla-hybrid-xle-2025-jtdbcmfexs3070309",
                "https://carsbidshistory.com/make/360-TOYOTA/97335-COROLLA_HYBRID/2025_TOYOTA_COROLLA_HYBRID_55637026_JTDBCMFEXS3070309",
            ],
            page.ArchiveUrls);
    }

    [Fact]
    public void Read_ResultsPastTheCap_AreNotOpened()
    {
        AuctionSearchPage page = AuctionSearchPageReader.Read(AuctionFixtures.Read("ddg-results-JTDBCMFEXS3070309.html"));

        Assert.Equal(AuctionSearchPageReader.MaxArchiveResults, page.ArchiveUrls.Count);
        Assert.DoesNotContain(page.ArchiveUrls, u => u.Contains("autobidmaster.com", StringComparison.Ordinal));
    }

    [Fact]
    public void Read_RecordedResultsWithNoArchiveSite_IsResultsWithNoArchiveUrls()
    {
        AuctionSearchPage page = AuctionSearchPageReader.Read(AuctionFixtures.Read("ddg-results-no-archive-site-19XZE4F52ME000999.html"));

        Assert.Equal(AuctionSearchStatus.Results, page.Status);
        Assert.Empty(page.ArchiveUrls);
    }

    [Fact]
    public void Read_NoResultsPage_IsNoResults()
    {
        AuctionSearchPage page = AuctionSearchPageReader.Read(AuctionFixtures.Read("ddg-no-results.html"));

        Assert.Equal(AuctionSearchStatus.NoResults, page.Status);
        Assert.Empty(page.ArchiveUrls);
    }

    [Fact]
    public void Read_CaptchaPage_IsUnreadableWithACaptchaReason()
    {
        AuctionSearchPage page = AuctionSearchPageReader.Read(AuctionFixtures.Read("ddg-captcha.html"));

        Assert.Equal(AuctionSearchStatus.Unreadable, page.Status);
        Assert.Equal("DuckDuckGo showed a captcha for the search", page.Reason);
    }

    [Fact]
    public void Read_PageWithNeitherResultsNorANoResultsNote_IsUnreadableNotNoResults()
    {
        AuctionSearchPage page = AuctionSearchPageReader.Read("<html><body><p>Something else entirely</p></body></html>");

        Assert.Equal(AuctionSearchStatus.Unreadable, page.Status);
    }

    [Fact]
    public void Read_ResultsWithNoArchiveSite_IsResultsWithNoArchiveUrls()
    {
        string html = """<a class="result__a" href="//duckduckgo.com/l/?uddg=https%3A%2F%2Fvindecoder.example%2Fx&amp;rut=1">x</a>""";

        AuctionSearchPage page = AuctionSearchPageReader.Read(html);

        Assert.Equal(AuctionSearchStatus.Results, page.Status);
        Assert.Empty(page.ArchiveUrls);
    }

    [Theory]
    [InlineData("https://bid.cars/en/lot/1", true)]
    [InlineData("https://www.bid.cars/en/lot/1", true)]
    [InlineData("https://www.copart.com/lot/1", true)]
    [InlineData("https://www.iaai.com/vehicle/1", true)]
    [InlineData("https://notbid.cars/lot/1", false)]
    [InlineData("https://example.com/bid.cars", false)]
    [InlineData("javascript:alert(1)", false)]
    public void IsArchiveUrl_MatchesTheHostOrASubdomainOfIt(string url, bool expected) =>
        Assert.Equal(expected, AuctionSearchPageReader.IsArchiveUrl(url));

    [Fact]
    public void BuildSearchUrl_SearchesDuckDuckGoHtmlForTheVin() =>
        Assert.Equal("https://html.duckduckgo.com/html/?q=JTDBCMFEXS3070309", AuctionSearchPageReader.BuildSearchUrl("JTDBCMFEXS3070309"));
}
