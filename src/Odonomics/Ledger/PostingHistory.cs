using System.Globalization;

namespace Odonomics.Ledger;

/// <summary>The vehicle-history summary a listing states about its car (CarGurus prints one from its AutoCheck
/// data: the title wording, accidents reported, previous owners, rental or fleet use, and frame damage). Every
/// field is null when the listing did not state it, and a null is never a claim: no accident count is not zero
/// accidents, and no use statement is not personal use. <see cref="FrameDamageStatement"/> and
/// <see cref="UseStatement"/> are only ever printed when they are true, so they are text as read or null.</summary>
public sealed record PostingHistory(
    string? TitleWording = null,
    int? AccidentCount = null,
    int? PreviousOwnerCount = null,
    string? UseStatement = null,
    string? FrameDamageStatement = null)
{
    /// <summary>A summary that states nothing.</summary>
    public static readonly PostingHistory None = new();

    /// <summary>Whether no field was stated.</summary>
    public bool IsEmpty => this == None;

    /// <summary>This summary with each field it does not state filled from <paramref name="fallback"/>, so a
    /// reading of a detail page wins field by field over the card that linked to it, and a card fills only what
    /// the page left out.</summary>
    public PostingHistory Over(PostingHistory fallback) => new(
        TitleWording ?? fallback.TitleWording,
        AccidentCount ?? fallback.AccidentCount,
        PreviousOwnerCount ?? fallback.PreviousOwnerCount,
        UseStatement ?? fallback.UseStatement,
        FrameDamageStatement ?? fallback.FrameDamageStatement);

    /// <summary>The stated fields as display phrases, in a fixed order ("Clean title", "2 accidents reported",
    /// "1 previous owner", "Reported as previous rental vehicle", "Frame damage reported"); empty when none was
    /// stated.</summary>
    public IReadOnlyList<string> Phrases()
    {
        List<string> phrases = [];
        if (!string.IsNullOrWhiteSpace(TitleWording))
        {
            phrases.Add(TitleWording.Trim());
        }

        if (AccidentCount is int accidents)
        {
            phrases.Add($"{accidents.ToString(CultureInfo.InvariantCulture)} {(accidents == 1 ? "accident" : "accidents")} reported");
        }

        if (PreviousOwnerCount is int owners)
        {
            phrases.Add($"{owners.ToString(CultureInfo.InvariantCulture)} previous {(owners == 1 ? "owner" : "owners")}");
        }

        if (!string.IsNullOrWhiteSpace(UseStatement))
        {
            phrases.Add(UseStatement.Trim());
        }

        if (!string.IsNullOrWhiteSpace(FrameDamageStatement))
        {
            phrases.Add(FrameDamageStatement.Trim());
        }

        return phrases;
    }
}
