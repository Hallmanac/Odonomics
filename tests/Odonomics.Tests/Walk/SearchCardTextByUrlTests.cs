using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves <see cref="SearchCardTextByUrl"/> never lets a link's own clean card text lose to an
/// ambiguous multi-card wrapper's, whichever page order the two arrive in, since a wrapper's text can
/// carry a neighboring card's own fee rather than this link's own (see
/// <see cref="WalkSearchPages.CollectLinksAsync"/>'s own touchKnownAsync remarks). The scenario test below
/// pins the real failure this guards against: a cars.com no-distance card whose first sighting is a
/// wrapper naming a neighbor's own "$N delivery" fee must not have that fee read as this posting's own
/// ShippingFee once a later page shows the link's own single-card text.</summary>
public class SearchCardTextByUrlTests
{
    [Fact]
    public void Record_FirstSightingAmbiguous_IsKeptUntilOverwritten()
    {
        var store = new SearchCardTextByUrl();

        store.Record("https://example/1", "wrapper text", textIsAmbiguous: true);

        Assert.True(store.TryGetValue("https://example/1", out string? cardText));
        Assert.Equal("wrapper text", cardText);
    }

    [Fact]
    public void Record_SecondAmbiguousSighting_NeverOverwritesTheFirstAmbiguousOne()
    {
        var store = new SearchCardTextByUrl();

        store.Record("https://example/1", "first wrapper text", textIsAmbiguous: true);
        store.Record("https://example/1", "second wrapper text", textIsAmbiguous: true);

        Assert.True(store.TryGetValue("https://example/1", out string? cardText));
        Assert.Equal("first wrapper text", cardText);
    }

    [Fact]
    public void Record_CleanSightingAfterAmbiguous_ReplacesTheWrappersText()
    {
        var store = new SearchCardTextByUrl();

        store.Record("https://example/1", "wrapper text", textIsAmbiguous: true);
        store.Record("https://example/1", "this link's own clean text", textIsAmbiguous: false);

        Assert.True(store.TryGetValue("https://example/1", out string? cardText));
        Assert.Equal("this link's own clean text", cardText);
    }

    [Fact]
    public void Record_AmbiguousSightingAfterClean_NeverOverwritesTheCleanText()
    {
        var store = new SearchCardTextByUrl();

        store.Record("https://example/1", "this link's own clean text", textIsAmbiguous: false);
        store.Record("https://example/1", "a later padded page's wrapper text", textIsAmbiguous: true);

        Assert.True(store.TryGetValue("https://example/1", out string? cardText));
        Assert.Equal("this link's own clean text", cardText);
    }

    [Fact]
    public void Record_SecondCleanSighting_KeepsTheFirstOne()
    {
        var store = new SearchCardTextByUrl();

        store.Record("https://example/1", "first clean text", textIsAmbiguous: false);
        store.Record("https://example/1", "second clean text", textIsAmbiguous: false);

        Assert.True(store.TryGetValue("https://example/1", out string? cardText));
        Assert.Equal("first clean text", cardText);
    }

    [Fact]
    public void TryGetValue_UnrecordedUrl_ReturnsFalse()
    {
        var store = new SearchCardTextByUrl();

        Assert.False(store.TryGetValue("https://example/never-seen", out _));
    }

    /// <summary>Reproduces the shape a real cars.com run needs: a no-distance card's first sighting is an
    /// ambiguous multi-card wrapper whose own <see cref="CarsComCards.ReadDistanceMiles"/> reads null (it
    /// names two distances, one for a leading CarMax neighbor and one for the second card the wrapper also
    /// covers) and whose leading neighbor states its own "$300 delivery" fee. A later page shows this same
    /// link's own single-card text, naming just its one distance and no delivery fee. Reading the card fee
    /// off whatever <see cref="SearchCardTextByUrl"/> now holds for the link must find the clean text's
    /// answer (no fee), never the wrapper's neighbor's $300.</summary>
    [Fact]
    public void Record_ThenReadCardFee_NeverStoresANeighborsWrapperFeeAsThisLinksOwn()
    {
        const string canonicalUrl = "https://www.cars.com/vehicledetail/aaaaaaaa-1111-2222-3333-444444444444/";
        const string wrapperText =
            "$22,000\n\n30,000 mi.\nUsed 2021 Honda Insight EX\n\nCarMax Some Store\n\n4.1\n$300 delivery to Orlando, FL (5 mi)\nCheck Availability\n\n" +
            "$21,000\n\n20,000 mi.\nUsed 2021 Honda Insight LX\n\nAnother Dealer\n\n4.5\nTampa, FL (95 mi)\nCheck Availability";
        const string ownCleanText =
            "$21,000\n\n20,000 mi.\nUsed 2021 Honda Insight LX\n\nAnother Dealer\n\n4.5\nTampa, FL (95 mi)\nCheck Availability";

        // The wrapper states two distances (5 mi and 95 mi), so it is ambiguous; the clean text states
        // exactly one (95 mi), so it is not. Both match the exact classification WalkCommand itself makes
        // via site.CardDistanceReader before calling Record.
        Assert.Null(CarsComCards.ReadDistanceMiles(wrapperText));
        Assert.Equal(95, CarsComCards.ReadDistanceMiles(ownCleanText));

        // The wrapper's leading neighbor's own $300 fee is exactly the kind of figure this link must never
        // inherit.
        Assert.Equal(300m, CarsComCards.ReadFee(wrapperText)!.Value.ShippingFee);
        Assert.Null(CarsComCards.ReadFee(ownCleanText));

        var store = new SearchCardTextByUrl();

        store.Record(canonicalUrl, wrapperText, textIsAmbiguous: true);
        store.Record(canonicalUrl, ownCleanText, textIsAmbiguous: false);

        Assert.True(store.TryGetValue(canonicalUrl, out string? storedText));
        Assert.Equal(ownCleanText, storedText);
        Assert.Null(WalkSites.CarsCom.ReadCardFee(storedText));
    }
}
