using System.Text.RegularExpressions;
using Odonomics.Ledger;

namespace Odonomics.CarEdge;

/// <summary>Reads a CarEdge dealer page's visible text into a letter grade, ported to a pure
/// function so it can be proven against recorded page text with no live network in the test run.
/// CarEdge grades a dealer A+ through F on its Dealer Ratings program; only an F grade carries a
/// stated reason. A page is only ever read as <see cref="CarEdgeGradeStatus.NotFound"/> when it
/// carries CarEdge's own no-match wording; anything else unreadable comes back
/// <see cref="CarEdgeGradeStatus.Unrecognized"/> so the caller retries instead of recording a
/// permanent (and possibly wrong) outcome.</summary>
public static partial class CarEdgeGradeParser
{
    public static CarEdgeGradeResult Parse(string pageText, string dealerName)
    {
        Match gradeMatch = GradeLine().Match(pageText);
        if (gradeMatch.Success)
        {
            string normalizedDealerName = DealerNormalizer.Normalize(dealerName);
            string normalizedPage = DealerNormalizer.Normalize(pageText);
            if (normalizedDealerName.Length == 0 || !normalizedPage.Contains(normalizedDealerName, StringComparison.Ordinal))
            {
                // The top grade line on the page isn't for the dealer we searched for (a
                // multi-result search page, most likely) — don't record someone else's grade.
                return CarEdgeGradeResult.Unrecognized;
            }

            string grade = gradeMatch.Groups[1].Value.ToUpperInvariant();
            if (!grade.StartsWith('F'))
            {
                return new CarEdgeGradeResult(CarEdgeGradeStatus.Graded, grade, Reason: null);
            }

            Match reasonMatch = ReasonBlock().Match(pageText);
            string? reason = reasonMatch.Success ? reasonMatch.Groups[1].Value.Trim() : null;
            return new CarEdgeGradeResult(CarEdgeGradeStatus.Graded, grade, reason);
        }

        return NotFoundMessage().IsMatch(pageText) ? CarEdgeGradeResult.NotFound : CarEdgeGradeResult.Unrecognized;
    }

    [GeneratedRegex(@"Dealer Grade:\s*([A-F][+-]?)(?:\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex GradeLine();

    [GeneratedRegex(@"Why this grade:\s*(.+?)(?:\r?\n\s*\r?\n|\z)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ReasonBlock();

    [GeneratedRegex(@"couldn't find a dealer matching", RegexOptions.IgnoreCase)]
    private static partial Regex NotFoundMessage();
}
