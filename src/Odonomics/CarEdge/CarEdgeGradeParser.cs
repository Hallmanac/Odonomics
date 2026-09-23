using System.Text.RegularExpressions;
using Odonomics.Ledger;

namespace Odonomics.CarEdge;

/// <summary>Reads a CarEdge `/dealers?q=` results page's visible text for one dealer, ported to a
/// pure function so it can be proven against recorded page text with no live network in the test
/// run. The page always carries a `Grade:` filter row (`AllABCDFCertified`) and, on a graded page,
/// often a "Top-Rated (Grade A) Dealers" footer link; neither is a dealer's own grade, so the parser
/// only ever reads a letter that sits alone on its own line immediately above a `NN/100` score,
/// which is how the current dealer card renders it. A page CarEdge itself serves as a 404 is read as
/// <see cref="CarEdgeGradeStatus.CarEdgeSearchUrlInvalid"/> before anything else, since that means
/// the search URL is dead rather than this one dealer being unrateable. Anything else unreadable
/// comes back <see cref="CarEdgeGradeStatus.Unrecognized"/> so the caller retries instead of
/// recording a permanent (and possibly wrong) outcome.</summary>
public static partial class CarEdgeGradeParser
{
    public static CarEdgeGradeResult Parse(string pageText, string dealerName)
    {
        if (FourZeroFourLine().IsMatch(pageText) && PageNotFoundText().IsMatch(pageText))
        {
            return CarEdgeGradeResult.CarEdgeSearchUrlInvalid;
        }

        Match resultsFound = ResultsFoundLine().Match(pageText);
        if (!resultsFound.Success)
        {
            return CarEdgeGradeResult.Unrecognized;
        }

        if (resultsFound.Groups["count"].Value == "0")
        {
            return CarEdgeGradeResult.NotFound;
        }

        string normalizedDealerName = DealerNormalizer.Normalize(dealerName);
        if (normalizedDealerName.Length == 0)
        {
            return CarEdgeGradeResult.Unrecognized;
        }

        List<(int Start, int End, Match Match, bool Graded)> signals = [];
        foreach (Match match in GradedCard().Matches(pageText))
        {
            signals.Add((match.Index, match.Index + match.Length, match, true));
        }
        foreach (Match match in NotRatedCard().Matches(pageText))
        {
            signals.Add((match.Index, match.Index + match.Length, match, false));
        }

        if (signals.Count == 0)
        {
            return CarEdgeGradeResult.Unrecognized;
        }

        signals.Sort((a, b) => a.Start.CompareTo(b.Start));

        // Scope the dealer-name check to the text between the previous card's own signal (or the
        // start of the page) and this one, so a multi-result page can't match this card's letter to
        // a dealer named lower down in a different result's card.
        int precedingStart = 0;
        foreach ((int start, int end, Match match, bool graded) in signals)
        {
            string precedingBlock = pageText[precedingStart..start];
            precedingStart = end;
            if (!DealerNormalizer.Normalize(precedingBlock).Contains(normalizedDealerName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!graded)
            {
                return CarEdgeGradeResult.NotFound;
            }

            string grade = match.Groups["grade"].Value.ToUpperInvariant();
            int score = int.Parse(match.Groups["score"].Value);
            int? verifiedQuoteCount = match.Groups["quotes"].Success ? int.Parse(match.Groups["quotes"].Value) : null;
            string docFee = $"${match.Groups["docFee"].Value}";
            string addOnsNote = match.Groups["addonsFlat"].Success
                ? match.Groups["addonsFlat"].Value
                : $"${match.Groups["addonsAmount"].Value} add-ons";

            return new CarEdgeGradeResult(CarEdgeGradeStatus.Graded, grade, score, verifiedQuoteCount, docFee, addOnsNote, Reason: null);
        }

        // The page recognizably rendered CarEdge's results layout, and at least one card on it
        // parsed cleanly, but none of them named this dealer: CarEdge's search just doesn't have it.
        return CarEdgeGradeResult.NotFound;
    }

    [GeneratedRegex(@"^\s*404\s*$", RegexOptions.Multiline)]
    private static partial Regex FourZeroFourLine();

    [GeneratedRegex(@"Page\s+Not\s+Found", RegexOptions.IgnoreCase)]
    private static partial Regex PageNotFoundText();

    [GeneratedRegex(@"(?<count>\d+)\s+dealers?\s+found", RegexOptions.IgnoreCase)]
    private static partial Regex ResultsFoundLine();

    [GeneratedRegex(
        @"(?:·\s*(?<quotes>\d+)\s*verified quotes\s*\r?\n\s*)?" +
        @"\$(?<docFee>[\d,]+)\s*\r?\n\s*doc fee\s*\r?\n\s*" +
        @"(?:(?<addonsFlat>No add-ons)|\$(?<addonsAmount>[\d,]+)\s*\r?\n\s*add-ons)\s*\r?\n\s*" +
        @"(?<grade>[A-F][+-]?)\s*\r?\n\s*(?<score>\d{1,3})/100",
        RegexOptions.IgnoreCase)]
    private static partial Regex GradedCard();

    [GeneratedRegex(@"—\s*\r?\n\s*Not rated", RegexOptions.IgnoreCase)]
    private static partial Regex NotRatedCard();
}
