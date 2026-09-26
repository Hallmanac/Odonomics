using System.Globalization;

namespace Odonomics.CarEdge;

/// <summary>What a CarEdge dealer search page's text resolved to. <see cref="Graded"/> is the only
/// status carrying a grade. <see cref="NotFound"/> means the search returned no card for this dealer
/// at all, and <see cref="NotRated"/> means it returned the dealer's own card marked "Not rated".
/// Both are safe to stamp as checked for good, but only <see cref="NotRated"/> is CarEdge
/// positively saying the dealer has no rating, so it alone clears a grade already on record.
/// <see cref="Unrecognized"/> means the page
/// matched no known shape at all (a slow render, a challenge, a layout change) and should be
/// retried rather than recorded as either outcome. <see cref="CarEdgeSearchUrlInvalid"/> means the
/// page is CarEdge's own 404, which means the search URL itself is dead, not this one dealer.
/// <see cref="Ambiguous"/> means the dealer's location is missing, only a city, or only a state and
/// more than one same-named card on the page fits it, so no card can be picked safely; a later run
/// with a fuller location can. <see cref="LocationMismatch"/> means a card matched the dealer's name but
/// every such card sat in a different city or state; that is not proof CarEdge lacks this dealer
/// (the location on record may be wrong or stale), so it is retried rather than stamped.</summary>
public enum CarEdgeGradeStatus
{
    Graded,
    NotFound,
    NotRated,
    Unrecognized,
    CarEdgeSearchUrlInvalid,
    Ambiguous,
    LocationMismatch,
}

/// <summary><see cref="Score"/>, <see cref="VerifiedQuoteCount"/>, <see cref="DocFee"/> and
/// <see cref="AddOnsNote"/> are only ever populated alongside <see cref="CarEdgeGradeStatus.Graded"/>.
/// <see cref="Reason"/> is carried for schema compatibility with the retired `/dealer-reviews`
/// layout's "Why this grade" text; the current dealer card carries no such reason, so the parser
/// never populates it.</summary>
public sealed record CarEdgeGradeResult(
    CarEdgeGradeStatus Status,
    string? Grade,
    int? Score,
    int? VerifiedQuoteCount,
    string? DocFee,
    string? AddOnsNote,
    string? Reason)
{
    /// <summary><see cref="DocFee"/> as a dollar amount ("$1,199" is 1199), or null when the page
    /// printed none or it does not read as an amount.</summary>
    public decimal? DocFeeAmount =>
        decimal.TryParse(DocFee?.TrimStart('$'), NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out decimal amount)
            ? amount
            : null;

    public static readonly CarEdgeGradeResult NotFound = new(CarEdgeGradeStatus.NotFound, null, null, null, null, null, null);
    public static readonly CarEdgeGradeResult NotRated = new(CarEdgeGradeStatus.NotRated, null, null, null, null, null, null);
    public static readonly CarEdgeGradeResult Unrecognized = new(CarEdgeGradeStatus.Unrecognized, null, null, null, null, null, null);
    public static readonly CarEdgeGradeResult CarEdgeSearchUrlInvalid = new(CarEdgeGradeStatus.CarEdgeSearchUrlInvalid, null, null, null, null, null, null);
    public static readonly CarEdgeGradeResult Ambiguous = new(CarEdgeGradeStatus.Ambiguous, null, null, null, null, null, null);
    public static readonly CarEdgeGradeResult LocationMismatch = new(CarEdgeGradeStatus.LocationMismatch, null, null, null, null, null, null);
}
