using Odonomics.Ledger;
using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves what a CarGurus page or card states about a car's vehicle history. The clean car is the Corolla
/// Hybrid detail page recorded in cargurus-detail-corolla-hybrid-no-dealer-block.txt, the fleet-use case is the
/// recorded Honda Insight page whose History block says "Work fleet vehicle use", and the other two fixtures are that
/// recorded page with its History block and price area edited to the wording a car with accidents and rental use,
/// and a frame-damage car (the #152 shape: "(Frame damage reported)" beside the price), print, since no page with
/// either was recorded.</summary>
public class CarGurusHistoryTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    [Fact]
    public void Read_RecordedCleanCar_StatesACleanTitleZeroAccidentsAndOneOwnerAndNothingElse() =>
        Assert.Equal(
            new PostingHistory("Clean title", 0, 1, null, null),
            CarGurusHistory.Read(Fixture("cargurus-detail-corolla-hybrid-no-dealer-block.txt")));

    [Fact]
    public void Read_CarWithAccidentsAndRentalUse_StatesTheCountsAndTheRentalLine() =>
        Assert.Equal(
            new PostingHistory("Clean title", 2, 2, "Reported as previous rental vehicle", null),
            CarGurusHistory.Read(Fixture("cargurus-detail-history-accidents-rental.txt")));

    [Fact]
    public void Read_FrameDamagePage_StatesFrameDamageBesideTheCleanTitleAndTheRentalLine() =>
        Assert.Equal(
            new PostingHistory("Clean title", 0, 1, "Reported as previous rental vehicle", "Frame damage reported"),
            CarGurusHistory.Read(Fixture("cargurus-detail-history-frame-damage.txt")));

    [Fact]
    public void Read_RecordedFleetVehicleUse_StatesTheReportedAsLine() =>
        Assert.Equal(
            new PostingHistory("Clean title", 0, 2, "Reported as corporate leased vehicle", null),
            CarGurusHistory.Read(Fixture("cargurus-detail-insight-sanford.txt")));

    [Fact]
    public void Read_PageWithNoHistoryBlock_StatesNothingAndNeverZero() =>
        Assert.Equal(PostingHistory.None, CarGurusHistory.Read(Fixture("cars-com-used-detail.txt")));

    [Fact]
    public void Read_EveryRecordedDetailPage_StatesATitleAnAccidentCountAndOwnersFromItsHistoryBlock()
    {
        string walks = Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks");
        string[] recorded = Directory.GetFiles(walks, "cargurus-detail-*.txt")
            .Where(f => !Path.GetFileName(f).StartsWith("cargurus-detail-history-", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(recorded);
        Assert.All(recorded, f =>
        {
            PostingHistory history = CarGurusHistory.Read(File.ReadAllText(f));
            Assert.NotNull(history.TitleWording);
            Assert.NotNull(history.AccidentCount);
            Assert.NotNull(history.PreviousOwnerCount);
        });
    }

    [Fact]
    public void Read_HistoryBlockWithABrandedTitle_ReturnsTheWordingAsPrinted() =>
        Assert.Equal(
            "Salvage title",
            CarGurusHistory.Read("2022 Honda Insight\n\nHistory1\nSalvage title\n\nTitle issue reported.\n\n1 accident reported\n").TitleWording);

    [Fact]
    public void Read_OneAccident_IsOne() =>
        Assert.Equal(1, CarGurusHistory.Read("History1\nClean title\n\n1 accident reported\n\nNo damage reported.\n").AccidentCount);

    [Fact]
    public void Read_HistoryHeadingWithNoTitleLineAfterIt_StatesNoTitleWording() =>
        Assert.Null(CarGurusHistory.Read("History\nSave 20% on the full AutoCheck vehicle history report\n").TitleWording);

    [Fact]
    public void Read_AnEarlierHistoryHeadingThatIsNotTheBlock_IsSkipped() =>
        Assert.Equal("Clean title", CarGurusHistory.Read("Research\nHistory\nFinance\n\nHistory1\nClean title\n").TitleWording);

    [Fact]
    public void Read_DealerDescriptionSayingNoAccidentsOrFrameDamageInASentence_StatesNothing() =>
        Assert.Equal(
            PostingHistory.None,
            CarGurusHistory.Read("Dealer's description\n\nONE OWNER, 2 accidents reported (Frame damage reported) in 2021. Former rental vehicle.\n"));

    [Fact]
    public void Read_FrameDamageMarkerAfterTheDealerDescription_IsNotStated() =>
        Assert.Null(CarGurusHistory.Read("$22,977\n\nDealer's description\n\n(Frame damage reported)\n").FrameDamageStatement);

    [Fact]
    public void Read_SearchCardWithTheFrameDamageMarker_StatesOnlyFrameDamage() =>
        Assert.Equal(
            new PostingHistory(FrameDamageStatement: "Frame damage reported"),
            CarGurusHistory.Read("Save this listing\n\n2025 Toyota Camry Hybrid\n\nLE FWD\n\n49,336 mi\n\nOrlando, FL\n8 mi away\n\n$23,249\n(Frame damage reported)\n$440/mo est.\nCheck availability"));

    [Fact]
    public void Read_RecordedSearchCards_StateNothing()
    {
        string walks = Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks");
        string[] cardFiles = Directory.GetFiles(walks, "cargurus-*cards*.json");

        Assert.NotEmpty(cardFiles);
        Assert.All(cardFiles, f => Assert.Equal(PostingHistory.None, CarGurusHistory.Read(File.ReadAllText(f).Replace("\\n", "\n"))));
    }

    [Fact]
    public void Over_FillsOnlyTheFieldsTheFirstSummaryLeftOut()
    {
        PostingHistory page = new("Clean title", 0, null, null, null);
        PostingHistory card = new(null, 3, 2, null, "Frame damage reported");

        Assert.Equal(new PostingHistory("Clean title", 0, 2, null, "Frame damage reported"), page.Over(card));
    }

    [Fact]
    public void Phrases_StatedFields_ReadAsThePageWordedThem() =>
        Assert.Equal(
            ["Clean title", "2 accidents reported", "1 previous owner", "Reported as previous rental vehicle", "Frame damage reported"],
            new PostingHistory("Clean title", 2, 1, "Reported as previous rental vehicle", "Frame damage reported").Phrases());

    [Fact]
    public void WalkSites_CarGurusHasTheReader_AndEveryOtherSiteStatesNothing()
    {
        string text = Fixture("cargurus-detail-history-frame-damage.txt");

        Assert.Equal("Frame damage reported", WalkSites.CarGurus.ReadHistory(text).FrameDamageStatement);
        Assert.All(new[] { WalkSites.CarsCom, WalkSites.Carvana, WalkSites.Autotrader, WalkSites.CarMax }, s => Assert.Equal(PostingHistory.None, s.ReadHistory(text)));
    }
}
