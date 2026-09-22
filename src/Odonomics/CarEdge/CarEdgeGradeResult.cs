namespace Odonomics.CarEdge;

/// <summary>The outcome of reading one CarEdge dealer page's text. <see cref="Found"/> is false
/// when CarEdge has no rating for the dealer at all (still a successful check, just nothing to
/// record beyond "checked"); <see cref="Reason"/> is only ever populated alongside an F
/// <see cref="Grade"/>.</summary>
public sealed record CarEdgeGradeResult(bool Found, string? Grade, string? Reason)
{
    public static readonly CarEdgeGradeResult NotFound = new(Found: false, Grade: null, Reason: null);
}
