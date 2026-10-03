using Odonomics.Auctions;

namespace Odonomics.Tests.Auctions;

public class AuctionLookupTests
{
    private static readonly AuctionRecord Record = new("Copart", "1-55637026", new DateOnly(2026, 7, 16), "Salvage certificate (CA)", "Side", "Front end", 23937m, 22579m, 83630, null);

    [Fact]
    public void FromSearch_CaptchaOnTheSearch_IsCouldNotReadAndOpensNothing()
    {
        AuctionLookupResult? result = AuctionLookup.FromSearch(AuctionSearchPageReader.Read(AuctionFixtures.Read("ddg-captcha.html")));

        Assert.Equal(AuctionCheckOutcome.CouldNotRead, result?.Outcome);
        Assert.Equal("DuckDuckGo showed a captcha for the search", result?.Reason);
    }

    [Fact]
    public void FromSearch_NotFoundSearch_IsNotFound()
    {
        AuctionLookupResult? result = AuctionLookup.FromSearch(AuctionSearchPageReader.Read(AuctionFixtures.Read("ddg-no-results.html")));

        Assert.Equal(AuctionCheckOutcome.NotFound, result?.Outcome);
        Assert.Null(result?.Record);
        Assert.Null(result?.Reason);
    }

    [Fact]
    public void FromSearch_RecordedSearchWithNoArchiveSite_IsNotFound() =>
        Assert.Equal(
            AuctionCheckOutcome.NotFound,
            AuctionLookup.FromSearch(AuctionSearchPageReader.Read(AuctionFixtures.Read("ddg-results-no-archive-site-19XZE4F52ME000999.html")))?.Outcome);

    [Fact]
    public void FromSearch_ArchiveLinksListed_LeavesThePagesToBeOpened() =>
        Assert.Null(AuctionLookup.FromSearch(AuctionSearchPageReader.Read(AuctionFixtures.Read("ddg-results-JTDBCMFEXS3070309.html"))));

    [Fact]
    public void Decide_AnyFoundPage_IsFoundEvenWhenAnotherPageWasBlocked()
    {
        AuctionLookupResult result = AuctionLookup.Decide(
        [
            new AuctionPageReading(AuctionPageStatus.CouldNotRead, null, "the site showed a captcha or block page"),
            new AuctionPageReading(AuctionPageStatus.Found, Record, null),
        ]);

        Assert.Equal(AuctionCheckOutcome.Found, result.Outcome);
        Assert.Equal(Record, result.Record);
    }

    [Fact]
    public void Decide_BlockedPageAndNoRecordPage_IsCouldNotReadSoItIsRetriedLater()
    {
        AuctionLookupResult result = AuctionLookup.Decide(
        [
            new AuctionPageReading(AuctionPageStatus.NoRecord, null, null),
            new AuctionPageReading(AuctionPageStatus.CouldNotRead, null, "the site showed a captcha or block page"),
        ]);

        Assert.Equal(AuctionCheckOutcome.CouldNotRead, result.Outcome);
        Assert.Equal("the site showed a captcha or block page", result.Reason);
    }

    [Fact]
    public void Decide_EveryPageReadableWithNoRecord_IsNotFound() =>
        Assert.Equal(AuctionCheckOutcome.NotFound, AuctionLookup.Decide([new AuctionPageReading(AuctionPageStatus.NoRecord, null, null)]).Outcome);
}
