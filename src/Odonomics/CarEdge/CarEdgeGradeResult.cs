namespace Odonomics.CarEdge;

/// <summary>What a CarEdge dealer page's text resolved to. <see cref="Graded"/> is the only status
/// carrying a grade. <see cref="NotFound"/> means the page positively said CarEdge has no rating
/// for this dealer (safe to stamp as checked for good). <see cref="Unrecognized"/> means the page
/// matched neither shape — a slow render, a challenge, a layout change, or a search page whose top
/// result wasn't this dealer — and should be retried rather than recorded as either outcome.</summary>
public enum CarEdgeGradeStatus
{
    Graded,
    NotFound,
    Unrecognized,
}

/// <summary><see cref="Reason"/> is only ever populated alongside an F <see cref="Grade"/>.</summary>
public sealed record CarEdgeGradeResult(CarEdgeGradeStatus Status, string? Grade, string? Reason)
{
    public static readonly CarEdgeGradeResult NotFound = new(CarEdgeGradeStatus.NotFound, null, null);
    public static readonly CarEdgeGradeResult Unrecognized = new(CarEdgeGradeStatus.Unrecognized, null, null);
}
