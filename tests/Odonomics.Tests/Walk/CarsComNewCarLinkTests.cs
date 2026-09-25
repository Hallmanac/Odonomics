using System.Text.RegularExpressions;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves cars.com's link collection leaves new-car cards out of the candidate pool, over
/// the title lines of a real recorded search page (a used search that mixed in eleven compact
/// "New 2026/2027 Toyota Corolla Hybrid" cards). The recording is page text, which carries no
/// hrefs, so each title line becomes the two anchors a live card has: a photo link with no text
/// and a title link, both to the card's own listing.</summary>
public partial class CarsComNewCarLinkTests
{
    [GeneratedRegex(@"^(New|Used|Certified) \d{4} .+$")]
    private static partial Regex CardTitleLine();

    private static List<string> RecordedTitles() =>
        [.. File.ReadAllLines(FixturePath("cars-com-search-with-new-cards.txt")).Where(l => CardTitleLine().IsMatch(l))];

    private static string FixturePath(string name) =>
        Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name);

    private static string ListingUrl(int card) => $"https://www.cars.com/vehicledetail/card-{card}/";

    private static List<PageLink> AnchorsFor(IReadOnlyList<string> titles)
    {
        List<PageLink> anchors = [new PageLink("https://www.cars.com/shopping/results/?page=2", "Next page")];
        for (int i = 0; i < titles.Count; i++)
        {
            anchors.Add(new PageLink($"{ListingUrl(i)}?sid=abc", ""));
            anchors.Add(new PageLink($"{ListingUrl(i)}?openLeadForm=true&sid=abc", titles[i]));
        }

        return anchors;
    }

    [Fact]
    public void CollectDetailLinks_RecordedSearchPage_YieldsEveryUsedCardAndNoNewCard()
    {
        List<string> titles = RecordedTitles();
        List<int> usedCards = [.. Enumerable.Range(0, titles.Count).Where(i => titles[i].StartsWith("Used ", StringComparison.Ordinal))];
        Assert.Equal(11, titles.Count(t => t.StartsWith("New ", StringComparison.Ordinal)));
        Assert.Equal(19, usedCards.Count);

        IReadOnlyList<string> links = WalkSites.CarsCom.CollectDetailLinks(AnchorsFor(titles), poolSize: 60);

        Assert.Equal(usedCards.Select(i => $"{ListingUrl(i)}?sid=abc"), links);
    }

    [Fact]
    public void CollectDetailLinks_NewCardsNeverCountAgainstThePoolSize()
    {
        List<string> titles = RecordedTitles();

        IReadOnlyList<string> links = WalkSites.CarsCom.CollectDetailLinks(AnchorsFor(titles), poolSize: 5);

        // Every one of the five links returned is a used card's, so no new card took a slot.
        HashSet<string> usedUrls = [.. Enumerable.Range(0, titles.Count)
            .Where(i => titles[i].StartsWith("Used ", StringComparison.Ordinal))
            .Select(i => $"{ListingUrl(i)}?sid=abc")];
        Assert.Equal(5, links.Count);
        Assert.All(links, l => Assert.Contains(l, usedUrls));
    }

    [Theory]
    [InlineData("New 2027 Toyota Corolla Hybrid LE")]
    [InlineData("new 2027 Toyota Corolla Hybrid LE")]
    [InlineData("  NEW 2026 Toyota Corolla Hybrid SE")]
    public void CollectDetailLinks_TitleBeginningWithNewAnyCase_IsSkipped(string title)
    {
        IReadOnlyList<string> links = WalkSites.CarsCom.CollectDetailLinks(
            [new PageLink("https://www.cars.com/vehicledetail/x/?sid=1", title)],
            poolSize: 10);

        Assert.Empty(links);
    }

    [Theory]
    [InlineData("Used 2024 Toyota Corolla Hybrid LE")]
    [InlineData("Certified 2024 Toyota Corolla Hybrid LE")]
    [InlineData("Newport Toyota 2024 Corolla")]
    [InlineData("")]
    public void CollectDetailLinks_OtherTitles_AreKept(string title)
    {
        IReadOnlyList<string> links = WalkSites.CarsCom.CollectDetailLinks(
            [new PageLink("https://www.cars.com/vehicledetail/x/?sid=1", title)],
            poolSize: 10);

        Assert.Single(links);
    }

    [Fact]
    public void CollectDetailLinks_Carvana_SkipsNothingByTitle()
    {
        IReadOnlyList<string> links = WalkSites.Carvana.CollectDetailLinks(
            [new PageLink("https://www.carvana.com/vehicle/123", "New 2027 Toyota Corolla Hybrid LE")],
            poolSize: 10);

        Assert.Single(links);
    }
}
