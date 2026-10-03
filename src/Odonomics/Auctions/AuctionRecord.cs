namespace Odonomics.Auctions;

/// <summary>What a public salvage-auction archive page says about one VIN's sale at a Copart or IAA
/// auction. Every field is optional because archive sites differ in what they print: only a lot
/// number and at least one other field are needed for a page to count as an auction record (see
/// <see cref="AuctionPageParser"/>).</summary>
public sealed record AuctionRecord(
    string? Auction,
    string? LotNumber,
    DateOnly? SaleDate,
    string? SaleDocument,
    string? PrimaryDamage,
    string? SecondaryDamage,
    decimal? Acv,
    decimal? RepairEstimate,
    int? Odometer,
    string? SourceUrl);

/// <summary>How one archive page read: it carried an auction record for the VIN
/// (<see cref="AuctionPageStatus.Found"/>), it was readable and had nothing for the VIN
/// (<see cref="AuctionPageStatus.NoRecord"/>), or it could not be read, with a reason such as a
/// captcha or a layout the parser does not recognise (<see cref="AuctionPageStatus.CouldNotRead"/>).</summary>
public enum AuctionPageStatus
{
    Found,
    NoRecord,
    CouldNotRead,
}

public sealed record AuctionPageReading(AuctionPageStatus Status, AuctionRecord? Record, string? Reason);

/// <summary>How a DuckDuckGo results page read: it listed results (<see cref="AuctionSearchStatus.Results"/>,
/// possibly none of them on an archive site), it said nothing matched the VIN, or it could not be
/// read because it was a captcha or block page or matched no layout the reader knows.</summary>
public enum AuctionSearchStatus
{
    Results,
    NoResults,
    Unreadable,
}

public sealed record AuctionSearchPage(AuctionSearchStatus Status, IReadOnlyList<string> ArchiveUrls, string? Reason);

/// <summary>The outcome of looking one VIN up in the archives, as stored on the vehicle.</summary>
public enum AuctionCheckOutcome
{
    Found,
    NotFound,
    CouldNotRead,
}

public sealed record AuctionLookupResult(AuctionCheckOutcome Outcome, AuctionRecord? Record, string? Reason);
