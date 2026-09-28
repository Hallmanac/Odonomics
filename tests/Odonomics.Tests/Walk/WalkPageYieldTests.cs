using Odonomics.Walk;

namespace Odonomics.Tests.Walk;

/// <summary>Pins the page-yield numbers actually recorded from walk run 20260928-134242: cars.com's
/// Prius search opened at 37 raw cards on its first page, then settled at 14-15 a page (about half its
/// typical 30) once the run's rendering degraded, while its Camry Hybrid hybrid facet's own first page
/// held only 11 to begin with and later pages held at 6, never actually dropping to half of that.</summary>
public class WalkPageYieldTests
{
    [Theory]
    [InlineData(37, 37, 30, false)] // the recorded, healthy first page
    [InlineData(25, 37, 30, false)] // a transitional page, not yet down to half
    [InlineData(15, 37, 30, true)] // the recorded degraded count: exactly half of the typical 30
    [InlineData(14, 37, 30, true)] // the recorded degraded count a little later in the same run
    [InlineData(6, 11, 30, false)] // the hybrid facet's own recorded later-page count, held to its thinner first page (11) rather than the site's 30
    [InlineData(5, 11, 30, true)] // a genuine drop below half of that same thinner first page
    public void FellFar_MatchesTheRecordedWalkNumbers(int rawCardCount, int firstPageCardCount, int typicalResultsPerPage, bool expected)
    {
        Assert.Equal(expected, WalkPageYield.FellFar(rawCardCount, firstPageCardCount, typicalResultsPerPage));
    }

    [Fact]
    public void FellFar_AFirstPageThinnerThanTheSitesTypicalSize_IsCheckedAgainstItsOwnFirstPageNotTheSitesSize()
    {
        // Half of the site's typical 30 is 15, which would wrongly flag every page of an 11-card facet as
        // degraded even when it never shrinks at all; held to its own 11-card first page instead, only a
        // real drop below half of that (5 or fewer) counts.
        Assert.False(WalkPageYield.FellFar(rawCardCount: 10, firstPageCardCount: 11, typicalResultsPerPage: 30));
        Assert.True(WalkPageYield.FellFar(rawCardCount: 5, firstPageCardCount: 11, typicalResultsPerPage: 30));
    }
}
