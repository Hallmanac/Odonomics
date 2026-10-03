using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Proves which title-brand phrase a detail page's own text states. The fixtures are the cars.com
/// detail page recorded in cars-com-used-detail.txt (which states "Title" then "Clean" in its history
/// block, and says nothing branded anywhere) with one statement edited in, the way
/// <see cref="FeeStatementsTests"/> edits a recorded page for a case nothing recorded.</summary>
public class TitleBrandStatementsTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks", name));

    [Fact]
    public void Read_RecordedPageWithACleanTitleHeading_StatesNoBrand() =>
        Assert.Null(TitleBrandStatements.Read(Fixture("cars-com-used-detail.txt")));

    [Fact]
    public void Read_PageSayingCleanTitleAndNoBrandedTitle_StatesNoBrand() =>
        Assert.Null(TitleBrandStatements.Read(Fixture("carscom-detail-clean-title-statement.txt")));

    [Fact]
    public void Read_EveryOtherRecordedDetailPage_StatesNoBrand()
    {
        string[] edited = ["carscom-detail-title-heading-rebuilt.txt", "carscom-detail-description-salvage-title.txt"];
        string walks = Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "walks");
        string[] recorded = [.. Directory.GetFiles(walks, "*detail*.txt").Where(f => !edited.Contains(Path.GetFileName(f)))];

        Assert.NotEmpty(recorded);
        Assert.All(recorded, f => Assert.Null(TitleBrandStatements.Read(File.ReadAllText(f))));
    }

    [Fact]
    public void Read_HistoryBlockWhoseTitleValueIsRebuilt_ReturnsThePhraseAsRead() =>
        Assert.Equal("Title Rebuilt", TitleBrandStatements.Read(Fixture("carscom-detail-title-heading-rebuilt.txt")));

    [Fact]
    public void Read_DealerDescriptionStatingASalvageTitle_ReturnsThePhraseAsRead() =>
        Assert.Equal("Salvage Title", TitleBrandStatements.Read(Fixture("carscom-detail-description-salvage-title.txt")));

    [Theory]
    [InlineData("This one has a rebuilt title.", "rebuilt title")]
    [InlineData("REBUILT TITLE, runs great", "REBUILT TITLE")]
    [InlineData("Reconstructed title vehicle", "Reconstructed title")]
    [InlineData("Sold with a salvaged title", "salvaged title")]
    [InlineData("branded title, see seller", "branded title")]
    [InlineData("Flood title", "Flood title")]
    [InlineData("Lemon title", "Lemon title")]
    [InlineData("Lemon law buyback title", "buyback title")]
    [InlineData("Manufacturer buy-back title", "buy-back title")]
    [InlineData("Title status: Salvage", "Title status: Salvage")]
    [InlineData("Title: Rebuilt", "Title: Rebuilt")]
    [InlineData("Title brand: Flood", "Title brand: Flood")]
    [InlineData("Clean title.\nSeller notes: rebuilt title.", "rebuilt title")]
    public void Read_TitleStatusPhrase_ReturnsItAsRead(string page, string expected) =>
        Assert.Equal(expected, TitleBrandStatements.Read(page));

    [Theory]
    [InlineData("Clean title in hand")]
    [InlineData("Title: Clean")]
    [InlineData("Title\n\nClean")]
    [InlineData("A salvage yard find, flood zone resident, lemon-yellow paint, branded floor mats")]
    [InlineData("Title and registration fees are extra")]
    [InlineData("No salvage, flood or rebuilt title")]
    [InlineData("This is not a salvage title vehicle")]
    [InlineData("Never had a branded title")]
    [InlineData("Non-salvage title")]
    [InlineData("It doesn't carry a rebuilt title")]
    [InlineData("This car doesn\u2019t have a salvage title")]
    [InlineData("No accidents or rebuilt title")]
    [InlineData("")]
    public void Read_NoTitleStatusPhrase_ReturnsNull(string page) =>
        Assert.Null(TitleBrandStatements.Read(page));

    [Theory]
    [InlineData("One owner, non-smoker, rebuilt title, runs great.", "rebuilt title")]
    [InlineData("No haggle pricing, rebuilt title", "rebuilt title")]
    [InlineData("Don't miss this salvage title deal!", "salvage title")]
    [InlineData("Arctic cold a/c, No pets, Must test drive, Rebuilt title, Mint Condition", "Rebuilt title")]
    [InlineData("Non smoker, salvage title", "salvage title")]
    public void Read_UnrelatedNegationEarlierInTheSentence_DoesNotHideTheStatement(string page, string expected) =>
        Assert.Equal(expected, TitleBrandStatements.Read(page));

    [Fact]
    public void Read_NegationInAnEarlierSentence_DoesNotHideALaterStatement() =>
        Assert.Equal("rebuilt title", TitleBrandStatements.Read("No accidents reported. Seller states rebuilt title."));
}
