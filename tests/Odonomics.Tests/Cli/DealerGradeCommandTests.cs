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
    public void ChainSkipReason_BareOrLegacyLocatedCarvana_IsSkippedWithAReason(string name, string normalizedLocation, string? location)
    {
        var dealer = new DealerEntity { Name = name, NormalizedName = "CARVANA", NormalizedLocation = normalizedLocation, Location = location };

        string? reason = DealerGradeCommand.ChainSkipReason(dealer);

        Assert.NotNull(reason);
        Assert.Contains("each Carvana hub is graded under its own name", reason);
    }

    [Theory]
    [InlineData("Carvana Winder", "CARVANA WINDER")]
    [InlineData("Holler Honda", "HOLLER HONDA")]
    public void ChainSkipReason_ACarvanaHubOrAnyOtherDealer_IsLookedUp(string name, string normalizedName)
    {
        var dealer = new DealerEntity { Name = name, NormalizedName = normalizedName, NormalizedLocation = "" };

        Assert.Null(DealerGradeCommand.ChainSkipReason(dealer));
    }

    [Fact]
    public void ApplyChainSkip_StampsTheBareCarvanaRowCheckedWithItsReasonAndNoGrade()
    {
        // A row a previous grader run had wrongly graded is cleared too: the bare chain never
        // carries a hub's grade.
        var dealer = new DealerEntity { Name = "Carvana", NormalizedName = "CARVANA", NormalizedLocation = "", Grade = "A" };
        string reason = DealerGradeCommand.ChainSkipReason(dealer) ?? throw new InvalidOperationException("expected a skip reason");

        DealerGradeOutcome outcome = DealerGradeCommand.ApplyChainSkip(dealer, reason, CheckedAt);

        Assert.Null(dealer.Grade);
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
