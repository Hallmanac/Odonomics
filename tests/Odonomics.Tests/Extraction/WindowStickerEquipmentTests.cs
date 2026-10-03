using System.Reflection;
using Odonomics.Domain;
using Odonomics.Extraction;

namespace Odonomics.Tests.Extraction;

public class WindowStickerEquipmentTests
{
    /// <summary>The shape of the JTDBCMFE7P3014805 listing: the dealer's description claims push-button start
    /// and leather, while the same page's window sticker lists only Keyless Entry and Power Door Locks.</summary>
    private const string DealerClaimsPushButtonStickerDoesNot = """
        2023 Toyota Corolla Hybrid LE Sedan 4D
        $19,990  41,200 miles
        VIN: JTDBCMFE7P3014805
        Dealer description
        One owner hybrid with push button start, leather seats, and a great warranty. Come see it today!
        Features
        Bluetooth, Backup Camera, Alloy Wheels
        Window Sticker
        Standard Equipment
        Keyless Entry, Power Door Locks
        Cloth Seat Trim
        """;

    private const string StickerWithSmartKeySystem = """
        2023 Toyota Corolla Hybrid LE
        Window Sticker
        Standard Equipment
        Smart Key System with Push Button Start
        Power Door Locks
        """;

    private const string DealerTextOnly = """
        2023 Toyota Corolla Hybrid LE Sedan 4D
        Dealer description
        One owner hybrid with push button start and smart key entry. Come see it today!
        """;

    private static readonly MethodInfo GroundInPageTextMethod = typeof(ExtractionClient)
        .GetMethod("GroundInPageText", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("ExtractionClient.GroundInPageText not found");

    [Fact]
    public void Ground_StickerListsOnlyKeylessEntry_KeepsAbsentForBothFeatures()
    {
        Assert.Equal("absent", WindowStickerEquipment.Ground("absent", EquipmentFeatures.SmartKeyEntry, DealerClaimsPushButtonStickerDoesNot));
        Assert.Equal("absent", WindowStickerEquipment.Ground("absent", EquipmentFeatures.PushButtonStart, DealerClaimsPushButtonStickerDoesNot));
    }

    [Fact]
    public void Ground_DealerTextClaimsPushButtonStartButStickerDoesNot_DropsAPresentClaim()
    {
        string? grounded = WindowStickerEquipment.Ground("present", EquipmentFeatures.PushButtonStart, DealerClaimsPushButtonStickerDoesNot);

        Assert.Null(grounded);
    }

    [Fact]
    public void Ground_StickerListsASmartKeySystemWithPushButtonStart_KeepsPresentForBothFeatures()
    {
        Assert.Equal("present", WindowStickerEquipment.Ground("present", EquipmentFeatures.SmartKeyEntry, StickerWithSmartKeySystem));
        Assert.Equal("present", WindowStickerEquipment.Ground("present", EquipmentFeatures.PushButtonStart, StickerWithSmartKeySystem));
    }

    [Theory]
    [InlineData("present")]
    [InlineData("absent")]
    public void Ground_PageWithNoWindowSticker_DropsEitherClaim(string claimed)
    {
        Assert.Null(WindowStickerEquipment.Ground(claimed, EquipmentFeatures.PushButtonStart, DealerTextOnly));
        Assert.Null(WindowStickerEquipment.Ground(claimed, EquipmentFeatures.SmartKeyEntry, DealerTextOnly));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("maybe")]
    [InlineData("unknown")]
    public void Ground_ClaimThatIsNotPresentOrAbsent_IsUnknown(string? claimed)
    {
        Assert.Null(WindowStickerEquipment.Ground(claimed, EquipmentFeatures.SmartKeyEntry, StickerWithSmartKeySystem));
    }

    [Fact]
    public void Ground_AbsentClaimOnAStickerThatNamesNoFob_IsDropped()
    {
        const string stickerWithoutKeyItems = "Window Sticker\nStandard Equipment\nPower Door Locks\nCloth Seat Trim";

        Assert.Null(WindowStickerEquipment.Ground("absent", EquipmentFeatures.PushButtonStart, stickerWithoutKeyItems));
    }

    [Theory]
    [InlineData("present", EquipmentStatus.Present)]
    [InlineData("Absent", EquipmentStatus.Absent)]
    [InlineData(null, EquipmentStatus.Unknown)]
    [InlineData("nonsense", EquipmentStatus.Unknown)]
    public void StatusOf_ReadsTheExtractionsWording(string? claimed, EquipmentStatus expected)
    {
        Assert.Equal(expected, WindowStickerEquipment.StatusOf(claimed));
    }

    [Fact]
    public void GroundInPageText_StickerShapeFromTheJtdListing_ReturnsAbsentEvenWhenTheModelClaimedPresent()
    {
        var extracted = new ExtractionResult("JTDBCMFE7P3014805", 2023, "Toyota", "Corolla Hybrid", "LE", 19990m, 41200, null, null, "Hybrid", SmartKeyEntry: "absent", PushButtonStart: "present");

        ExtractionResult grounded = (ExtractionResult)GroundInPageTextMethod.Invoke(null, [extracted, DealerClaimsPushButtonStickerDoesNot])!;

        Assert.Equal("absent", grounded.SmartKeyEntry);
        Assert.Null(grounded.PushButtonStart);
    }

    [Fact]
    public void TextForExtraction_ShortPage_IsReturnedWhole()
    {
        Assert.Equal(StickerWithSmartKeySystem, WindowStickerEquipment.TextForExtraction(StickerWithSmartKeySystem, 12_000));
    }

    [Fact]
    public void TextForExtraction_StickerBeyondTheCut_AppendsAnExcerptAroundIt()
    {
        string page = new string('x', 500) + "\nWindow Sticker\nStandard Equipment\nKeyless Entry\n" + new string('y', 500);

        string text = WindowStickerEquipment.TextForExtraction(page, 100);

        Assert.StartsWith(new string('x', 100), text);
        Assert.Contains("Window Sticker", text);
        Assert.Contains("Keyless Entry", text);
    }

    [Fact]
    public void TextForExtraction_LongPageWithNoStickerBeyondTheCut_IsJustTruncated()
    {
        string page = new string('x', 500);

        Assert.Equal(new string('x', 100), WindowStickerEquipment.TextForExtraction(page, 100));
    }
}
