namespace Odonomics.CarEdge;

/// <summary>What a CarEdge dealer search page's text resolved to. <see cref="Graded"/> is the only
/// status carrying a grade. <see cref="NotFound"/> covers CarEdge positively having nothing for this
/// dealer, whether the search returned no card for it at all or returned a card for it marked "Not
/// rated" (safe to stamp as checked for good either way). <see cref="Unrecognized"/> means the page
/// matched no known shape at all (a slow render, a challenge, a layout change) and should be
/// retried rather than recorded as either outcome. <see cref="CarEdgeSearchUrlInvalid"/> means the
/// page is CarEdge's own 404, which means the search URL itself is dead, not this one dealer.</summary>
public enum CarEdgeGradeStatus
{
    Graded,
    NotFound,
    Unrecognized,
    CarEdgeSearchUrlInvalid,
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
    public static readonly CarEdgeGradeResult NotFound = new(CarEdgeGradeStatus.NotFound, null, null, null, null, null, null);
    public static readonly CarEdgeGradeResult Unrecognized = new(CarEdgeGradeStatus.Unrecognized, null, null, null, null, null, null);
    public static readonly CarEdgeGradeResult CarEdgeSearchUrlInvalid = new(CarEdgeGradeStatus.CarEdgeSearchUrlInvalid, null, null, null, null, null, null);
}
