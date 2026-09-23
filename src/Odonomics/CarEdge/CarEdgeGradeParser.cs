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
    public static CarEdgeGradeResult Parse(string pageText, string dealerName, string? dealerLocation)
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

        string normalizedDealerLocation = DealerNormalizer.Normalize(dealerLocation);

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
        // card's letter to a dealer named lower down in a different result's card. The page's own
        // "Search: "<query>"" echo sits above the results-found line this scope starts from, so it
        // was never in scope to begin with; what the Sort:-line skip below excludes is the filter
        // chrome between the results line and the first card (grade and make filter rows, including
        // the "All Makes..." row, which lists every make CarEdge sells and so can itself contain a
        // dealer's bare name, such as a make like "Tesla") that would otherwise sit inside the first
        // card's own scoped block.
        int precedingStart = resultsFound.Index + resultsFound.Length;
        Match sortOptionsLine = SortOptionsLine().Match(pageText, precedingStart);
        if (sortOptionsLine.Success)
        {
            precedingStart = sortOptionsLine.Index + sortOptionsLine.Length;
        }

        // The card's own dealer name always sits on the line immediately above its "City, ST"
        // location line (itself the last comma-plus-state-shaped line in the block: any make lines a
        // not-rated multi-brand card carries sit below it, not above). Collect every card's name line
        // and that same location line up front, scoped the same way as before (between the previous
        // card's signal and this one), before deciding which card to accept.
        List<(int Start, int End, Match Match, bool Graded, string NameLine, string LocationLine)> cards = [];
        foreach ((int start, int end, Match match, bool graded) in signals)
        {
            string precedingBlock = pageText[precedingStart..start];
            precedingStart = end;
            (string nameLine, string locationLine) = DealerCardIdentity(precedingBlock);
            cards.Add((start, end, match, graded, nameLine, locationLine));
        }

        // A card whose own "City, ST" line doesn't match the searched dealer's known location is
        // never this dealer, even when the name matches exactly: CarEdge's search can return
        // same-named dealers in other cities (a chain, or an unrelated store CarEdge's own matching
        // considered close enough), and taking one of those permanently mislabels the searched
        // dealer's grade. The dealer's own location is often partial (an upstream API can report only
        // a city or only a state), while a card's location line always carries both, so the check
        // requires the dealer's known location text to appear as a whole word within the card's
        // "City, ST" line rather than requiring the two strings to be identical outright: a city-only
        // or state-only dealer location still matches its own card, while a card genuinely in a
        // different city still fails to contain it and is rejected. Skipped entirely when the dealer
        // has no location on record, so that case still falls back to the name-only check below
        // exactly as before.
        Regex? dealerLocationBoundary = normalizedDealerLocation.Length > 0
            ? new Regex($@"\b{Regex.Escape(normalizedDealerLocation)}\b")
            : null;

        bool LocationMatches(string cardLocationLine)
        {
            if (dealerLocationBoundary is null)
            {
                return true;
            }

            string normalizedCardLocation = DealerNormalizer.Normalize(cardLocationLine);
            return normalizedCardLocation.Length == 0 || dealerLocationBoundary.IsMatch(normalizedCardLocation);
        }

        // Prefer a card whose name line is exactly the searched name: a page that returns a fuzzy
        // match like "Toyota of Orlando South" ahead of the searched "Toyota of Orlando" must not
        // have that longer name's card accepted over the dealer's own exact card when both are on
        // the page. Only when no card's name line is an exact match do we fall back to a
        // boundary-anchored containment check on that same name line (not the whole block): CarEdge
        // sometimes renders a dealer's franchised name longer than the ledger's stem (a "Schaller
        // Honda" ledger entry against a "Schaller Honda Subaru Mitsubishi" card), and a card that
        // legitimately extends the searched name is the best available match rather than a
        // permanent, possibly wrong, "not on CarEdge".
        (int Start, int End, Match Match, bool Graded, string NameLine)? chosen = null;
        foreach ((int start, int end, Match match, bool graded, string nameLine, string locationLine) in cards)
        {
            if (string.Equals(DealerNormalizer.Normalize(nameLine), normalizedDealerName, StringComparison.Ordinal)
                && LocationMatches(locationLine))
            {
                chosen = (start, end, match, graded, nameLine);
                break;
            }
        }

        if (chosen is null)
        {
            Regex nameBoundary = new($@"\b{Regex.Escape(normalizedDealerName)}\b");
            foreach ((int start, int end, Match match, bool graded, string nameLine, string locationLine) in cards)
            {
                string normalizedNameLine = DealerNormalizer.Normalize(nameLine);
                if (normalizedNameLine.Length > 0 && nameBoundary.IsMatch(normalizedNameLine) && LocationMatches(locationLine))
                {
                    chosen = (start, end, match, graded, nameLine);
                    break;
                }
            }
        }

        if (chosen is { } chosenCard)
        {
            if (!chosenCard.Graded)
            {
                return CarEdgeGradeResult.NotFound;
            }

            string grade = chosenCard.Match.Groups["grade"].Value.ToUpperInvariant();
            int score = int.Parse(chosenCard.Match.Groups["score"].Value);
            int? verifiedQuoteCount = chosenCard.Match.Groups["quotes"].Success
                ? int.Parse(chosenCard.Match.Groups["quotes"].Value)
                : null;
            string docFee = $"${chosenCard.Match.Groups["docFee"].Value}";
            string addOnsNote = chosenCard.Match.Groups["addonsFlat"].Success
                ? chosenCard.Match.Groups["addonsFlat"].Value
                : $"${chosenCard.Match.Groups["addonsAmount"].Value} add-ons";

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

    private static (string NameLine, string LocationLine) DealerCardIdentity(string block)
    {
        Match? locationLine = CardLocationLine().Matches(block).LastOrDefault();
        if (locationLine is not { Success: true })
        {
            return ("", "");
        }

        string[] linesAboveLocation = block[..locationLine.Index].Split('\n');
        for (int i = linesAboveLocation.Length - 1; i >= 0; i--)
        {
            string line = linesAboveLocation[i].Trim();
            if (line.Length > 0)
            {
                return (line, locationLine.Value);
            }
        }

        return ("", locationLine.Value);
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
