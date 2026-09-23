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
}
