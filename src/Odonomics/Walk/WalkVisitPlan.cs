namespace Odonomics.Walk;

/// <summary>
/// The lines that tell the operator how long a walk will run before it has spent the time: one at the
/// start of the run with what is known before any search page loads, and one before each pair's first
/// detail page with how many pages that pair is about to visit. A detail page costs about a minute
/// (the pacing's gaps and pauses added up), so a page count is a length the operator can judge.
/// </summary>
public static class WalkVisitPlan
{
    /// <summary>The rough cost of one detail page, in minutes, the lines quote their estimates from.</summary>
    public const int MinutesPerPage = 1;

    /// <summary>What the ledger already holds for one (site, model) pair.</summary>
    public sealed record PairKnown(string Site, string Model, int KnownPostings);

    /// <summary>The lines printed once at the start of the run. Every pair is listed with how many
    /// postings the ledger already holds for it, which a repeat walk keeps current from cards instead
    /// of visiting (or, with <paramref name="revisit"/>, visits again). How many new cars each pair's
    /// searches turn up is only known once its search pages load, so the header says so and quotes
    /// the per-page cost rather than a total.</summary>
    public static IReadOnlyList<string> RunStartLines(IReadOnlyList<PairKnown> pairs, int? maxDetailPages, bool revisit)
    {
        string capText = maxDetailPages is int cap
            ? $"at most {cap} matching detail page(s) per pair"
            : "no per-pair cap, so every car a search returns is visited";
        string knownText = revisit
            ? "the ledger's postings are visited again (--revisit)"
            : "the ledger's postings are kept current from their search cards, not visited";
        return
        [
            $"walk plan: {pairs.Count} pair(s), {capText}; {knownText}; a detail page takes about {MinutesPerPage} minute(s), and each pair prints its own page count before its first one",
            .. pairs.Select(p => $"  {p.Site} / {p.Model}: the ledger holds {p.KnownPostings} posting(s)"),
        ];
    }

    /// <summary>The line printed before a pair's first detail page: how many pages it is about to visit
    /// (its new links, with the cap applied when there is one) and the rough time that is.
    /// <paramref name="startingWith"/> marks a capped pair whose later searches are not loaded yet, so
    /// the count is only what its first search starts with.</summary>
    public static string PairLine(string site, string make, string model, int pages, bool startingWith)
    {
        string subject = $"{site} / {make} {model}: about to visit";
        string count = startingWith ? $"{pages} detail page(s) to start with, and more from the pair's later searches" : $"{pages} detail page(s)";
        return pages == 0
            ? $"{subject} 0 detail pages, nothing new to open"
            : $"{subject} {count}, roughly {pages * MinutesPerPage} minute(s)";
    }
}
