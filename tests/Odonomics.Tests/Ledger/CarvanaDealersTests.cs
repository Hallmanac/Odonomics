using System.Text.Json;
using Odonomics.Ledger;
using Odonomics.Marketcheck;

namespace Odonomics.Tests.Ledger;

public class CarvanaDealersTests
{
    private static readonly DateTimeOffset SeenFrom = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SeenTo = new(2026, 9, 23, 3, 0, 0, TimeSpan.Zero);

    private static VinHistoryListing Listing(string? dealer, DateTimeOffset? firstSeen, DateTimeOffset? lastSeen) =>
        new(dealer, null, null, firstSeen, lastSeen, 20000m, 40000, null);

    private static string History(params VinHistoryListing[] listings) => JsonSerializer.Serialize(listings.ToList());

    [Theory]
    [InlineData("Carvana Winder", true)]
    [InlineData("  CARVANA   University Park ", true)]
    [InlineData("Carvana", false)]
    [InlineData("Carvana, ", false)]
    [InlineData("Carvanaville Motors", false)]
    [InlineData("Holler Honda", false)]
    [InlineData(null, false)]
    public void IsHubName_OnlyCarvanaFollowedBySomethingIsAHub(string? name, bool expected)
    {
        Assert.Equal(expected, CarvanaDealers.IsHubName(name));
    }

    [Fact]
    public void HubNameFromHistory_ListingOverlapsThePostingsWindow_NamesThatHub()
    {
        string history = History(Listing("Carvana Winder", new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 23, 1, 0, 0, TimeSpan.Zero)));

        Assert.Equal("Carvana Winder", CarvanaDealers.HubNameFromHistory(history, SeenFrom, SeenTo));
    }

    [Fact]
    public void HubNameFromHistory_ListingEndedHoursBeforeTheWalkFirstSawIt_StillCountsBecauseMarketcheckTrails()
    {
        string history = History(Listing("Carvana Winder", new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 22, 2, 0, 0, TimeSpan.Zero)));

        Assert.Equal("Carvana Winder", CarvanaDealers.HubNameFromHistory(history, SeenFrom, SeenTo));
    }

    [Fact]
    public void HubNameFromHistory_ListingLastCrawledAboutADayBeforeTheWalk_StillCountsAsTheSameStay()
    {
        string history = History(Listing("Carvana Haines City", new DateTimeOffset(2026, 9, 20, 1, 37, 0, TimeSpan.Zero), SeenFrom.AddHours(-25)));

        Assert.Equal("Carvana Haines City", CarvanaDealers.HubNameFromHistory(history, SeenFrom, SeenTo));
    }

    [Fact]
    public void HubNameFromHistory_ListingEndedMoreThanTwoDaysBeforeTheWalk_IsADifferentStay()
    {
        string history = History(Listing("Carvana Delanco", new DateTimeOffset(2026, 9, 17, 6, 49, 0, TimeSpan.Zero), SeenFrom.AddDays(-2).AddMinutes(-1)));

        Assert.Null(CarvanaDealers.HubNameFromHistory(history, SeenFrom, SeenTo));
    }

    [Fact]
    public void HubNameFromHistory_OnlyAnOlderStayAtAnotherHubOverlapsNothing_NamesNoHub()
    {
        string history = History(Listing("Carvana Fairburn", new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 26, 0, 0, 0, TimeSpan.Zero)));

        Assert.Null(CarvanaDealers.HubNameFromHistory(history, SeenFrom, SeenTo));
    }

    [Fact]
    public void HubNameFromHistory_SeveralHubsOverlap_TheMostRecentListingWins()
    {
        string history = History(
            Listing("Carvana Fairburn", new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 22, 13, 0, 0, TimeSpan.Zero)),
            Listing("Carvana Winder", new DateTimeOffset(2026, 9, 22, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 23, 1, 0, 0, TimeSpan.Zero)));

        Assert.Equal("Carvana Winder", CarvanaDealers.HubNameFromHistory(history, SeenFrom, SeenTo));
    }

    [Fact]
    public void HubNameFromHistory_OnlyTheBareChainNameOrOtherSellersOverlap_NamesNoHub()
    {
        string history = History(
            Listing("Carvana", SeenFrom, SeenTo),
            Listing("Holler Honda", SeenFrom, SeenTo),
            Listing(null, SeenFrom, SeenTo));

        Assert.Null(CarvanaDealers.HubNameFromHistory(history, SeenFrom, SeenTo));
    }

    [Fact]
    public void HubNameFromHistory_ListingWithNoDatesIsOpenOnBothEnds()
    {
        string history = History(Listing("Carvana Winder", null, null));

        Assert.Equal("Carvana Winder", CarvanaDealers.HubNameFromHistory(history, SeenFrom, SeenTo));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    public void HubNameFromHistory_MissingEmptyOrUnreadableHistory_NamesNoHub(string? history)
    {
        Assert.Null(CarvanaDealers.HubNameFromHistory(history, SeenFrom, SeenTo));
    }
}
