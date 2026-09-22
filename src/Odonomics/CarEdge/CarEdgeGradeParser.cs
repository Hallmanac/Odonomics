using System.Text.RegularExpressions;

namespace Odonomics.CarEdge;

/// <summary>Reads a CarEdge dealer page's visible text into a letter grade, ported to a pure
/// function so it can be proven against recorded page text with no live network in the test run.
/// CarEdge grades a dealer A+ through F on its Dealer Ratings program; only an F grade carries a
/// stated reason.</summary>
public static partial class CarEdgeGradeParser
{
    public static CarEdgeGradeResult Parse(string pageText)
    {
        Match gradeMatch = GradeLine().Match(pageText);
        if (!gradeMatch.Success)
        {
            return CarEdgeGradeResult.NotFound;
        }

        string grade = gradeMatch.Groups[1].Value.ToUpperInvariant();
        if (!grade.StartsWith('F'))
        {
            return new CarEdgeGradeResult(Found: true, Grade: grade, Reason: null);
        }

        Match reasonMatch = ReasonBlock().Match(pageText);
        string? reason = reasonMatch.Success ? reasonMatch.Groups[1].Value.Trim() : null;
        return new CarEdgeGradeResult(Found: true, Grade: grade, Reason: reason);
    }

    [GeneratedRegex(@"Dealer Grade:\s*([A-F][+-]?)(?:\s|$)", RegexOptions.IgnoreCase)]
    private static partial Regex GradeLine();

    [GeneratedRegex(@"Why this grade:\s*(.+?)(?:\r?\n\s*\r?\n|\z)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ReasonBlock();
}
