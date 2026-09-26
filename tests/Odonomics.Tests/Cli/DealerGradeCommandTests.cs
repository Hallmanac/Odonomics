using Odonomics.CarEdge;
using Odonomics.Cli.Commands;
using Odonomics.Ledger;

namespace Odonomics.Tests.Cli;

public class DealerGradeCommandTests
{
    private const string SearchUrl = "https://caredge.com/dealers?q=x";

    private static readonly DateTimeOffset CheckedAt = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    private static DealerEntity Dealer() => new()
    {
        Name = "Holler Honda",
        NormalizedName = "HOLLER HONDA",
        Location = "Sanford, FL",
        NormalizedLocation = "SANFORD FL",
    };

    [Fact]
    public void ApplyResult_Ambiguous_LeavesTheDealerUngradedAndUnstamped()
    {
        DealerEntity dealer = Dealer();

        DealerGradeOutcome outcome = DealerGradeCommand.ApplyResult(dealer, CarEdgeGradeResult.Ambiguous, CheckedAt, SearchUrl);

        Assert.Null(dealer.GradeCheckedAt);
        Assert.Null(dealer.Grade);
        Assert.False(outcome.Stamped);
        Assert.Equal(DealerGradeTally.Unmatched, outcome.Tally);
    }

    [Fact]
    public void ApplyResult_LocationMismatch_IsReportedAsSuchAndLeavesTheDealerUnstamped()
    {
        DealerEntity dealer = Dealer();

        DealerGradeOutcome outcome = DealerGradeCommand.ApplyResult(dealer, CarEdgeGradeResult.LocationMismatch, CheckedAt, SearchUrl);

        Assert.Null(dealer.GradeCheckedAt);
        Assert.Null(dealer.Grade);
        Assert.False(outcome.Stamped);
        Assert.Equal(DealerGradeTally.Unmatched, outcome.Tally);
        Assert.Equal("Holler Honda: name matched, location did not, will retry later", outcome.Line);
    }

    [Fact]
    public void ApplyResult_NotFound_StampsTheDealerAsCheckedWithNoGrade()
    {
        DealerEntity dealer = Dealer();

        DealerGradeOutcome outcome = DealerGradeCommand.ApplyResult(dealer, CarEdgeGradeResult.NotFound, CheckedAt, SearchUrl);

        Assert.Equal(CheckedAt, dealer.GradeCheckedAt);
        Assert.Null(dealer.Grade);
        Assert.True(outcome.Stamped);
        Assert.Equal(DealerGradeTally.Ungraded, outcome.Tally);
    }

    [Fact]
    public void ApplyResult_Graded_StoresTheGradeAndStampsTheDealer()
    {
        DealerEntity dealer = Dealer();
        var graded = new CarEdgeGradeResult(CarEdgeGradeStatus.Graded, "A", 91, 8, "$600", "No add-ons", Reason: null);

        DealerGradeOutcome outcome = DealerGradeCommand.ApplyResult(dealer, graded, CheckedAt, SearchUrl);

        Assert.Equal("A", dealer.Grade);
        Assert.Equal(CheckedAt, dealer.GradeCheckedAt);
        Assert.True(outcome.Stamped);
        Assert.Equal(DealerGradeTally.Graded, outcome.Tally);
    }

    [Theory]
    [InlineData("dealers-q-daytona-toyota.txt", "Daytona Toyota", 1199, "No add-ons")]
    [InlineData("dealers-q-seminole-toyota.txt", "Seminole Toyota", 999, "$358 add-ons")]
    public void ApplyResult_RecordedGradedPage_StoresTheDocFeeAsADecimalAndTheAddOnsNote(string fixture, string dealerName, int docFee, string addOnsNote)
    {
        string pageText = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "caredge", fixture));
        var dealer = new DealerEntity { Name = dealerName, NormalizedName = dealerName.ToUpperInvariant(), NormalizedLocation = "" };

        DealerGradeOutcome outcome = DealerGradeCommand.ApplyResult(dealer, CarEdgeGradeParser.Parse(pageText, dealerName, null), CheckedAt, SearchUrl);

        Assert.Equal(DealerGradeTally.Graded, outcome.Tally);
        Assert.Equal(docFee, dealer.DocFee);
        Assert.Equal(addOnsNote, dealer.AddOnsNote);
    }

    [Fact]
    public void ApplyResult_DealerAlreadyGradedThenRefreshed_TakesTheNewFeeFieldsAndGrade()
    {
        DealerEntity dealer = Dealer();
        dealer.Grade = "B";
        dealer.GradeCheckedAt = CheckedAt.AddDays(-30);
        var refreshed = new CarEdgeGradeResult(CarEdgeGradeStatus.Graded, "A", 91, 8, "$600", "No add-ons", Reason: null);

        DealerGradeCommand.ApplyResult(dealer, refreshed, CheckedAt, SearchUrl);

        Assert.Equal("A", dealer.Grade);
        Assert.Equal(600m, dealer.DocFee);
        Assert.Equal("No add-ons", dealer.AddOnsNote);
        Assert.Equal(CheckedAt, dealer.GradeCheckedAt);
    }

    [Fact]
    public void ApplyResult_RefreshThatFindsNoCard_KeepsTheFeeFieldsAlreadyStored()
    {
        DealerEntity dealer = Dealer();
        dealer.Grade = "B";
        dealer.DocFee = 1199m;
        dealer.AddOnsNote = "No add-ons";

        DealerGradeCommand.ApplyResult(dealer, CarEdgeGradeResult.Unrecognized, CheckedAt, SearchUrl);

        Assert.Equal(1199m, dealer.DocFee);
        Assert.Equal("No add-ons", dealer.AddOnsNote);
    }

    [Fact]
    public void ApplyResult_RefreshThatFindsNoCardForAGradedDealer_KeepsWhatWasStoredAndSaysSo()
    {
        DealerEntity dealer = Dealer();
        dealer.Grade = "B";
        dealer.DocFee = 1199m;
        dealer.AddOnsNote = "No add-ons";

        DealerGradeOutcome outcome = DealerGradeCommand.ApplyResult(dealer, CarEdgeGradeResult.NotFound, CheckedAt, SearchUrl);

        Assert.Equal("B", dealer.Grade);
        Assert.Equal(1199m, dealer.DocFee);
        Assert.Equal("No add-ons", dealer.AddOnsNote);
        Assert.Equal("Holler Honda: no longer on CarEdge, keeping the stored grade B", outcome.Line);
        Assert.Equal(DealerGradeTally.Kept, outcome.Tally);
        Assert.Equal(CheckedAt, dealer.GradeCheckedAt);
    }

    [Fact]
    public void ApplyResult_NotRated_StampsTheDealerAsCheckedWithNoGrade()
    {
        DealerEntity dealer = Dealer();

        DealerGradeOutcome outcome = DealerGradeCommand.ApplyResult(dealer, CarEdgeGradeResult.NotRated, CheckedAt, SearchUrl);

        Assert.Equal(CheckedAt, dealer.GradeCheckedAt);
        Assert.Null(dealer.Grade);
        Assert.True(outcome.Stamped);
        Assert.Equal(DealerGradeTally.Ungraded, outcome.Tally);
        Assert.Equal("Holler Honda: not rated on CarEdge", outcome.Line);
    }

    [Fact]
    public void ApplyResult_RefreshThatFindsTheCardNowNotRated_ClearsTheGradeAndFees()
    {
        DealerEntity dealer = Dealer();
        dealer.Grade = "F";
        dealer.DocFee = 1199m;
        dealer.AddOnsNote = "$358 add-ons";

        DealerGradeOutcome outcome = DealerGradeCommand.ApplyResult(dealer, CarEdgeGradeResult.NotRated, CheckedAt, SearchUrl);

        Assert.Null(dealer.Grade);
        Assert.Null(dealer.DocFee);
        Assert.Null(dealer.AddOnsNote);
        Assert.Equal(CheckedAt, dealer.GradeCheckedAt);
        Assert.True(outcome.Stamped);
        Assert.Equal(DealerGradeTally.Ungraded, outcome.Tally);
        Assert.Equal("Holler Honda: CarEdge now shows this dealer as not rated, cleared the stored grade F", outcome.Line);
    }

    [Fact]
    public void NeedsCheck_ByDefault_IsOnlyADealerNeverChecked()
    {
        DealerEntity neverChecked = Dealer();
        DealerEntity graded = Dealer();
        graded.Grade = "A";
        graded.GradeCheckedAt = CheckedAt;
        DealerEntity unrated = Dealer();
        unrated.GradeCheckedAt = CheckedAt;

        Assert.True(DealerGradeCommand.NeedsCheck(neverChecked, refresh: false));
        Assert.False(DealerGradeCommand.NeedsCheck(graded, refresh: false));
        Assert.False(DealerGradeCommand.NeedsCheck(unrated, refresh: false));
    }

    [Fact]
    public void NeedsCheck_WithRefresh_AddsGradedDealersButNotOnesCheckedWithNoGrade()
    {
        DealerEntity neverChecked = Dealer();
        DealerEntity graded = Dealer();
        graded.Grade = "A";
        graded.GradeCheckedAt = CheckedAt;
        DealerEntity unrated = Dealer();
        unrated.GradeCheckedAt = CheckedAt;

        Assert.True(DealerGradeCommand.NeedsCheck(neverChecked, refresh: true));
        Assert.True(DealerGradeCommand.NeedsCheck(graded, refresh: true));
        Assert.False(DealerGradeCommand.NeedsCheck(unrated, refresh: true));
    }

    [Fact]
    public void ApplyResult_UnreadablePage_LeavesTheDealerUnstampedAndCountsItFailed()
    {
        foreach (CarEdgeGradeResult result in new[] { CarEdgeGradeResult.Unrecognized, CarEdgeGradeResult.CarEdgeSearchUrlInvalid })
        {
            DealerEntity dealer = Dealer();

            DealerGradeOutcome outcome = DealerGradeCommand.ApplyResult(dealer, result, CheckedAt, SearchUrl);

            Assert.Null(dealer.GradeCheckedAt);
            Assert.False(outcome.Stamped);
            Assert.Equal(DealerGradeTally.Failed, outcome.Tally);
        }
    }

    [Theory]
    [InlineData("Carvana", "", null)]
    [InlineData("Carvana", "ORLANDO FL", "Orlando, FL")]
    public void SkipReason_BareOrLegacyLocatedCarvana_IsSkippedWithAReason(string name, string normalizedLocation, string? location)
    {
        var dealer = new DealerEntity { Name = name, NormalizedName = "CARVANA", NormalizedLocation = normalizedLocation, Location = location };

        string? reason = DealerGradeCommand.SkipReason(dealer);

        Assert.NotNull(reason);
        Assert.Contains("each Carvana hub is graded under its own name", reason);
    }

    [Theory]
    [InlineData("Carvana Winder", "CARVANA WINDER")]
    [InlineData("Holler Honda", "HOLLER HONDA")]
    public void SkipReason_ACarvanaHubOrAnyOtherDealer_IsLookedUp(string name, string normalizedName)
    {
        var dealer = new DealerEntity { Name = name, NormalizedName = normalizedName, NormalizedLocation = "" };

        Assert.Null(DealerGradeCommand.SkipReason(dealer));
    }

    [Fact]
    public void ApplySkip_StampsTheBareCarvanaRowCheckedWithItsReasonAndNoGrade()
    {
        // A row a previous grader run had wrongly graded is cleared too: the bare chain never
        // carries a hub's grade.
        var dealer = new DealerEntity { Name = "Carvana", NormalizedName = "CARVANA", NormalizedLocation = "", Grade = "A", DocFee = 999m, AddOnsNote = "No add-ons" };
        string reason = DealerGradeCommand.SkipReason(dealer) ?? throw new InvalidOperationException("expected a skip reason");

        DealerGradeOutcome outcome = DealerGradeCommand.ApplySkip(dealer, reason, CheckedAt);

        Assert.Null(dealer.Grade);
        Assert.Null(dealer.DocFee);
        Assert.Null(dealer.AddOnsNote);
        Assert.Equal(reason, dealer.GradeReason);
        Assert.Equal(CheckedAt, dealer.GradeCheckedAt);
        Assert.True(outcome.Stamped);
        Assert.Equal(DealerGradeTally.Ungraded, outcome.Tally);
        Assert.StartsWith("Carvana: skipped, ", outcome.Line);
    }

    [Fact]
    public void ApplyResult_CarvanaHubDealerWithNoCardOnThePage_IsStampedCheckedNotCountedFailed()
    {
        // A hub goes through the same parser and outcomes as any other dealer. Here CarEdge's
        // results page (a recorded page for a different dealer) has no card for it, which stamps it
        // checked with no grade rather than counting it as a failed lookup.
        string pageText = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "tests", "Odonomics.Tests", "fixtures", "caredge", "dealers-q-daytona-toyota.txt"));
        var dealer = new DealerEntity { Name = "Carvana Winder", NormalizedName = "CARVANA WINDER", NormalizedLocation = "" };

        CarEdgeGradeResult result = CarEdgeGradeParser.Parse(pageText, dealer.Name, dealer.Location);
        DealerGradeOutcome outcome = DealerGradeCommand.ApplyResult(dealer, result, CheckedAt, SearchUrl);

        Assert.Equal(CarEdgeGradeStatus.NotFound, result.Status);
        Assert.NotEqual(DealerGradeTally.Failed, outcome.Tally);
        Assert.Equal(CheckedAt, dealer.GradeCheckedAt);
    }
}
