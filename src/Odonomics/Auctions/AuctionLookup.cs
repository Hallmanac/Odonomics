namespace Odonomics.Auctions;

/// <summary>Decides one VIN's stored result from what the archive pages it opened read as. Any page
/// with an auction record settles it as found. Otherwise a page that could not be read makes the
/// whole lookup could-not-read, so a blocked or changed site is retried on a later run instead of
/// being recorded as a car with no auction history; only when every page read cleanly, or there was
/// nothing to open, is the VIN recorded as not found.</summary>
public static class AuctionLookup
{
    /// <summary>The result a search page settles on its own, or null when it listed archive links that
    /// still need opening: a captcha, block, or unrecognised page is could-not-read, and a search that found nothing, or
    /// nothing on an archive site, is not-found.</summary>
    public static AuctionLookupResult? FromSearch(AuctionSearchPage search) => search switch
    {
        { Status: AuctionSearchStatus.Unreadable } => new AuctionLookupResult(AuctionCheckOutcome.CouldNotRead, null, search.Reason),
        { ArchiveUrls.Count: 0 } => new AuctionLookupResult(AuctionCheckOutcome.NotFound, null, null),
        _ => null,
    };

    public static AuctionLookupResult Decide(IReadOnlyList<AuctionPageReading> readings)
    {
        AuctionPageReading? found = readings.FirstOrDefault(r => r.Status == AuctionPageStatus.Found);
        if (found is not null)
        {
            return new AuctionLookupResult(AuctionCheckOutcome.Found, found.Record, null);
        }

        AuctionPageReading? unread = readings.FirstOrDefault(r => r.Status == AuctionPageStatus.CouldNotRead);
        return unread is not null
            ? new AuctionLookupResult(AuctionCheckOutcome.CouldNotRead, null, unread.Reason)
            : new AuctionLookupResult(AuctionCheckOutcome.NotFound, null, null);
    }
}
