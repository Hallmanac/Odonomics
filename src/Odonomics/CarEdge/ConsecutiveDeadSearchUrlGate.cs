namespace Odonomics.CarEdge;

/// <summary>Tracks consecutive CarEdge search-page 404s for `odo dealer grade`, so three dead pages
/// in a row (the search URL itself is dead) are told apart from three dealers that merely failed to
/// parse or weren't rated. Any other outcome resets the count, since only a run of dead pages, not
/// an occasional one mixed with real results, means the URL itself is the problem.</summary>
public sealed class ConsecutiveDeadSearchUrlGate
{
    private const int StopThreshold = 3;

    public int Count { get; private set; }
    public bool ShouldStop => Count >= StopThreshold;

    public void RecordDeadSearchUrl() => Count++;
    public void RecordOtherOutcome() => Count = 0;
}
