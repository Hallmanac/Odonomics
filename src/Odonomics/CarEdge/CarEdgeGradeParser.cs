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

        int declaredCount = int.Parse(resultsFound.Groups["count"].Value);
        if (declaredCount == 0)
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
        // page chrome above the first card) and this one, so a multi-result page can't match this
        // card's letter to a dealer named lower down in a different result's card, and so the
        // first card's own name line isn't preceded only by the page's own "Search: "<query>""
        // echo, disclaimer, grade/make filter rows, or sort options, all of which sit between the
        // results-found line and the first card and can themselves contain a dealer's bare name (a
        // make like "Tesla").
        int precedingStart = resultsFound.Index + resultsFound.Length;
        Match sortOptionsLine = SortOptionsLine().Match(pageText, precedingStart);
        if (sortOptionsLine.Success)
        {
            precedingStart = sortOptionsLine.Index + sortOptionsLine.Length;
        }

        foreach ((int start, int end, Match match, bool graded) in signals)
        {
            string precedingBlock = pageText[precedingStart..start];
            precedingStart = end;

            // The card's own dealer name always sits on the line immediately above its "City, ST"
            // location line (itself the last comma-plus-state-shaped line in the block: any make
            // lines a not-rated multi-brand card carries sit below it, not above). Anchoring on that
            // location line and comparing the name line to the searched name with exact equality,
            // rather than testing whether the whole block merely contains the searched name, is
            // deliberate: a page that returns a fuzzy match like "Toyota of Orlando South" ahead of
            // the searched "Toyota of Orlando" must not have that longer name's card accepted, since
            // normalization collapses newlines to spaces and would otherwise make "Toyota of
            // Orlando" a boundary-clean substring of "Toyota of Orlando South".
            string dealerNameLine = DealerNameLineAboveLocation(precedingBlock);
            if (!string.Equals(DealerNormalizer.Normalize(dealerNameLine), normalizedDealerName, StringComparison.Ordinal))
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

        // The page recognizably rendered CarEdge's results layout and none of the cards that parsed
        // named this dealer. That's only trustworthy as "CarEdge's search doesn't have it" when every
        // card the page itself declares actually parsed; if fewer cards parsed than the page's own
        // count, the searched dealer's own card may be a render variant the regexes above don't cover,
        // so come back Unrecognized and let the caller retry instead of recording a permanent (and
        // possibly wrong) "not on CarEdge".
        return signals.Count >= declaredCount
            ? CarEdgeGradeResult.NotFound
            : CarEdgeGradeResult.Unrecognized;
    }

    private static string DealerNameLineAboveLocation(string block)
    {
        Match? locationLine = CardLocationLine().Matches(block).LastOrDefault();
        if (locationLine is not { Success: true })
        {
            return "";
        }

        string[] linesAboveLocation = block[..locationLine.Index].Split('\n');
        for (int i = linesAboveLocation.Length - 1; i >= 0; i--)
        {
            string line = linesAboveLocation[i].Trim();
            if (line.Length > 0)
            {
                return line;
            }
        }

        return "";
    }

    [GeneratedRegex(@"^\s*404\s*$", RegexOptions.Multiline)]
    private static partial Regex FourZeroFourLine();

    [GeneratedRegex(@"Page\s+Not\s+Found", RegexOptions.IgnoreCase)]
    private static partial Regex PageNotFoundText();

    [GeneratedRegex(@"(?<count>\d+)\s+dealers?\s+found", RegexOptions.IgnoreCase)]
    private static partial Regex ResultsFoundLine();

    [GeneratedRegex(@"Sort:\s*\r?\n[^\r\n]*\r?\n")]
    private static partial Regex SortOptionsLine();

    [GeneratedRegex(@"^[^\r\n,]*,\s*[A-Z]{2}\b", RegexOptions.Multiline)]
    private static partial Regex CardLocationLine();

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
