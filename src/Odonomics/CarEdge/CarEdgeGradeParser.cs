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
        string normalizedDealerName = DealerNormalizer.Normalize(dealerName);
        MatchCollection gradeMatches = GradeLine().Matches(pageText);

        if (gradeMatches.Count > 0 && normalizedDealerName.Length == 0)
        {
            return CarEdgeGradeResult.Unrecognized;
        }

        for (int i = 0; i < gradeMatches.Count; i++)
        {
            Match gradeMatch = gradeMatches[i];

            // Scope the dealer-name check to the text between the previous grade line (or the
            // start of the page) and this one, so a multi-result search page can't match this
            // grade to a dealer named lower down in a different result's block.
            int precedingStart = i == 0 ? 0 : gradeMatches[i - 1].Index;
            string precedingBlock = pageText[precedingStart..gradeMatch.Index];
            if (!DealerNormalizer.Normalize(precedingBlock).Contains(normalizedDealerName, StringComparison.Ordinal))
            {
                continue;
            }

            string grade = gradeMatch.Groups[1].Value.ToUpperInvariant();
            if (!grade.StartsWith('F'))
            {
                return new CarEdgeGradeResult(CarEdgeGradeStatus.Graded, grade, Reason: null);
            }

            int sectionEnd = i + 1 < gradeMatches.Count ? gradeMatches[i + 1].Index : pageText.Length;
            string dealerSection = pageText[gradeMatch.Index..sectionEnd];
            Match reasonMatch = ReasonBlock().Match(dealerSection);
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
